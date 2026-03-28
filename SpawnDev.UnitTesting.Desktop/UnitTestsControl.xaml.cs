using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SpawnDev.UnitTesting.Desktop
{
    /// <summary>
    /// WPF UserControl that displays and runs unit tests.
    /// Embeddable counterpart to SpawnDev.UnitTesting.Blazor's UnitTestsView.
    /// Can be hosted in any WPF window or panel.
    /// </summary>
    public partial class UnitTestsControl : UserControl, IDisposable
    {
        private UnitTestRunner _runner = default!;
        private bool _initialized;
        private bool _disposed;

        /// <summary>
        /// Types to scan for test methods.
        /// </summary>
        public IEnumerable<Type>? TestTypes { get; set; }

        /// <summary>
        /// Assemblies to scan for test classes.
        /// </summary>
        public IEnumerable<Assembly>? TestAssemblies { get; set; }

        /// <summary>
        /// Optional custom resolver for test type instances (DI support).
        /// </summary>
        public Func<Type, object?>? TypeInstanceResolver { get; set; }

        /// <summary>
        /// Optional service provider for dependency injection.
        /// </summary>
        public IServiceProvider? ServiceProvider { get; set; }

        /// <summary>
        /// Optional directory path for writing live test results (latest.json).
        /// </summary>
        public string? ResultsDirectory { get; set; }

        /// <summary>
        /// If true, automatically starts running all tests when initialized.
        /// </summary>
        public bool AutoRun { get; set; }

        /// <summary>
        /// Fired when all tests have completed.
        /// </summary>
        public event Action? OnTestsComplete;

        /// <summary>
        /// Observable collection bound to the ListView.
        /// </summary>
        public ObservableCollection<TestItemViewModel> TestItems { get; } = new();

        // Results file writing state
        private bool _writeInProgress;
        private bool _writeQueued;
        private string? _runTimestamp;
        private int _lastWrittenCompleteCount;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        public UnitTestsControl()
        {
            InitializeComponent();
            TestList.ItemsSource = TestItems;
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!_initialized)
            {
                _initialized = true;
                InitializeRunner();
                if (AutoRun)
                {
                    _ = RunAllAsync();
                }
            }
        }

        /// <summary>
        /// Initializes the test runner and discovers tests.
        /// Called automatically on Loaded, or can be called manually.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            InitializeRunner();
        }

        private void InitializeRunner()
        {
            _runner = ServiceProvider != null
                ? new UnitTestRunner(ServiceProvider, false)
                : new UnitTestRunner(false);

            _runner.OnUnitTestResolverEvent += OnResolverEvent;
            _runner.TestStatusChanged += OnTestStatusChanged;

            LoadFromParams();
        }

        private void OnResolverEvent(UnitTestResolverEvent resolverEvent)
        {
            resolverEvent.TypeInstance = TypeInstanceResolver?.Invoke(resolverEvent.TestType);
            if (resolverEvent.TypeInstance == null && ServiceProvider != null)
            {
                resolverEvent.TypeInstance = ServiceProvider.GetService(resolverEvent.TestType);
            }
        }

        private void LoadFromParams()
        {
            var types = new List<Type>();
            if (TestTypes != null) types.AddRange(TestTypes);
            if (TestAssemblies != null)
            {
                var testClassTypes = TestAssemblies
                    .SelectMany(a => a.GetTypes())
                    .Where(t => !t.IsAbstract && t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Any(m => m.GetCustomAttribute<TestMethodAttribute>() != null))
                    .ToList();
                types.AddRange(testClassTypes);
            }
            if (types.Count == 0)
            {
                _runner.FindAllTests();
            }
            else
            {
                _runner.SetTestTypes(types.Distinct());
            }
            RebuildTestItems();
            UpdateStatus();
        }

        private void OnTestStatusChanged()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(OnTestStatusChanged);
                return;
            }
            SyncTestItems();
            UpdateStatus();
            TryWriteResultsAsync();
            if (_runner.State == TestState.Done)
            {
                OnTestsComplete?.Invoke();
            }
        }

        private void RebuildTestItems()
        {
            TestItems.Clear();
            for (int i = 0; i < _runner.Tests.Count; i++)
            {
                TestItems.Add(TestItemViewModel.FromUnitTest(_runner.Tests[i], i + 1));
            }
        }

        private void SyncTestItems()
        {
            for (int i = 0; i < _runner.Tests.Count; i++)
            {
                if (i < TestItems.Count)
                {
                    TestItems[i].UpdateFrom(_runner.Tests[i]);
                }
                else
                {
                    TestItems.Add(TestItemViewModel.FromUnitTest(_runner.Tests[i], i + 1));
                }
            }
            while (TestItems.Count > _runner.Tests.Count)
            {
                TestItems.RemoveAt(TestItems.Count - 1);
            }
        }

        private void UpdateStatus()
        {
            var tests = _runner.Tests;
            var completed = tests.Where(t => t.State == TestState.Done).ToList();
            var passed = completed.Count(t => t.Result == TestResult.Success);
            var failed = completed.Count(t => t.Result == TestResult.Error);
            var skipped = completed.Count(t => t.Result == TestResult.Unsupported);
            var pending = tests.Count - completed.Count;
            var totalDuration = completed.Sum(t => t.Duration);

            var stateText = _runner.State switch
            {
                TestState.Running => "Running...",
                TestState.Done => failed == 0 ? "Done" : "Done (failures)",
                _ => "Ready",
            };

            StatusText.Text = $"{stateText}  |  {tests.Count} tests  |  {passed} passed  |  {failed} failed  |  {skipped} skipped  |  {pending} pending";
            SummaryText.Text = $"{passed}/{completed.Count} passed, {failed} failed, {skipped} skipped  ({totalDuration:N0} ms)";

            RunAllBtn.IsEnabled = _runner.State != TestState.Running;
        }

        private async void RunAll_Click(object sender, RoutedEventArgs e)
        {
            await RunAllAsync();
        }

        /// <summary>
        /// Runs all discovered tests.
        /// </summary>
        public async Task RunAllAsync()
        {
            if (!_initialized) Initialize();
            RunAllBtn.IsEnabled = false;
            await _runner.RunTests();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _runner.CancelTests();
        }

        #region Results File Writing

        private async void TryWriteResultsAsync()
        {
            if (string.IsNullOrEmpty(ResultsDirectory)) return;
            var completedCount = _runner.Tests.Count(t => t.State == TestState.Done);
            var runDone = _runner.State == TestState.Done;
            if (completedCount == _lastWrittenCompleteCount && !runDone) return;
            if (_writeInProgress) { _writeQueued = true; return; }
            _writeInProgress = true;
            do
            {
                _writeQueued = false;
                await WriteCurrentStateAsync();
            } while (_writeQueued);
            _writeInProgress = false;
        }

        private async Task WriteCurrentStateAsync()
        {
            try
            {
                _runTimestamp ??= DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
                var results = BuildResults();
                var json = JsonSerializer.Serialize(results, _jsonOptions);
                _lastWrittenCompleteCount = _runner.Tests.Count(t => t.State == TestState.Done);

                Directory.CreateDirectory(ResultsDirectory!);

                var latestPath = Path.Combine(ResultsDirectory!, "latest.json");
                await File.WriteAllTextAsync(latestPath, json);

                var runDir = Path.Combine(ResultsDirectory!, _runTimestamp);
                Directory.CreateDirectory(runDir);
                var resultsPath = Path.Combine(runDir, "results.json");
                await File.WriteAllTextAsync(resultsPath, json);

                if (_runner.State == TestState.Done)
                {
                    _runTimestamp = null;
                    _lastWrittenCompleteCount = 0;
                }
            }
            catch { }
        }

        private object BuildResults()
        {
            var tests = _runner.Tests;
            var completedTests = tests.Where(t => t.State == TestState.Done).ToList();
            return new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                runState = _runner.State.ToString(),
                total = tests.Count,
                passed = completedTests.Count(t => t.Result == TestResult.Success),
                failed = completedTests.Count(t => t.Result == TestResult.Error),
                skipped = completedTests.Count(t => t.Result == TestResult.Unsupported),
                pending = tests.Count - completedTests.Count,
                totalDurationMs = completedTests.Sum(t => t.Duration),
                tests = tests.Select((t, i) => new
                {
                    index = i + 1,
                    className = t.TestTypeName,
                    method = t.TestMethodName,
                    state = t.State.ToString(),
                    result = t.Result.ToString(),
                    durationMs = t.Duration,
                    error = t.Result == TestResult.Error ? t.Error : null,
                    stackTrace = t.Result == TestResult.Error && !string.IsNullOrEmpty(t.StackTrace) ? t.StackTrace : null,
                    resultText = !string.IsNullOrEmpty(t.ResultText) ? t.ResultText : null,
                }).ToArray()
            };
        }

        #endregion

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_runner != null)
            {
                _runner.CancelTests();
                _runner.TestStatusChanged -= OnTestStatusChanged;
                _runner.OnUnitTestResolverEvent -= OnResolverEvent;
            }
        }
    }
}
