using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;
using System.Reflection;
using System.Text.Json;

namespace SpawnDev.UnitTesting.Browser
{
    /// <summary>
    /// Blazor component that displays and runs unit tests.
    /// Uses the cross-platform UnitTestRunner from SpawnDev.UnitTesting.
    /// </summary>
    public partial class UnitTestsView : IDisposable
    {
        UnitTestRunner unitTestService { get; set; } = default!;

        /// <summary>
        /// Types to scan for test methods
        /// </summary>
        [Parameter]
        public IEnumerable<Type>? TestTypes { get; set; }

        /// <summary>
        /// Assemblies to scan for test classes
        /// </summary>
        [Parameter]
        public IEnumerable<Assembly>? TestAssemblies { get; set; }

        /// <summary>
        /// Optional custom resolver for test type instances
        /// </summary>
        [Parameter]
        public Func<Type, object?>? TypeInstanceResolver { get; set; }

        /// <summary>
        /// Optional root directory handle for writing live test results (results-{timestamp}.json, one per app load).
        /// </summary>
        [Parameter]
        public FileSystemDirectoryHandle? ResultsDirectory { get; set; }

        /// <summary>
        /// Optional run-specific directory for writing results.json into a timestamped folder.
        /// When set, results are written to both ResultsDirectory/latest.json (live)
        /// and RunDirectory/results.json (permanent per-run record).
        /// </summary>
        [Parameter]
        public FileSystemDirectoryHandle? RunDirectory { get; set; }

        /// <summary>
        /// Optional externally-supplied timestamp for the per-session results file (results-{this}.json). When set
        /// (e.g. to ShaderDebugService.AppLoadTimestamp), the results file shares ONE timestamp with the shader-dump
        /// folder for the same app load. When null, an internal app-load timestamp is used. Format: "yyyy-MM-dd_HH-mm-ss".
        /// </summary>
        [Parameter]
        public string? SessionTimestamp { get; set; }

        [Inject]
        IServiceProvider ServiceProvider { get; set; } = default!;

        [Inject]
        SpawnJSRuntime JS { get; set; } = default!;

        bool _beenInit = false;

        // Results file writing state
        private bool _writeInProgress;
        private bool _writeQueued;
        // Timestamp captured when this view (the page/app) LOADED — fixed for the whole session. The timestamped
        // results file (results-{this}.json) is therefore stable per app load and NEVER overwritten by a later run:
        // PlaywrightMultiTest runs tests one at a time by clicking each Run button (no batch "done" signal), and each
        // backend lane reloads the page (= a new component instance = a new timestamp = a new file), so prior lanes'
        // results survive for comparison. Every write refreshes this file with the full current state.
        private readonly string _sessionTimestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        private int _lastWrittenCompleteCount;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        /// <inheritdoc/>
        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            if (_beenInit) LoadFromParams();
        }

        private void UnitTestService_OnUnitTestResolverEvent(UnitTestResolverEvent resolverEvent)
        {
            resolverEvent.TypeInstance = TypeInstanceResolver?.Invoke(resolverEvent.TestType);
            resolverEvent.TypeInstance ??= ServiceProvider.GetService(resolverEvent.TestType);
        }

        void LoadFromParams()
        {
            var types = new List<Type>();
            if (TestTypes != null) types.AddRange(TestTypes);
            if (TestAssemblies != null)
            {
                // Find types that have methods with [TestMethod] attribute
                var testClassTypes = TestAssemblies
                    .SelectMany(o => o.GetTypes())
                    .Where(o => !o.IsAbstract && o.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .Any(m => m.GetCustomAttribute<TestMethodAttribute>() != null))
                    .ToList();
                types.AddRange(testClassTypes);
            }
            unitTestService.SetTestTypes(types.Distinct());
        }

        /// <summary>
        /// Dismisses the Blazor error UI if visible. Returns true if it was visible.
        /// </summary>
        private async Task<bool> DismissBlazorErrorUI()
        {
            return JS.Call<string, bool>(
                "eval",
                "(() => { var el = document.getElementById('blazor-error-ui'); if (el && getComputedStyle(el).display !== 'none') { el.style.display = 'none'; return true; } return false; })()");
        }

        /// <summary>
        /// Checks if the Blazor error UI is currently visible.
        /// </summary>
        private async Task<bool> IsBlazorErrorUIVisible()
        {
            return JS.Call<string, bool>(
                "eval",
                "(() => { var el = document.getElementById('blazor-error-ui'); return el != null && getComputedStyle(el).display !== 'none'; })()");
        }

        /// <inheritdoc/>
        protected override void OnInitialized()
        {
            base.OnInitialized();
            if (!_beenInit)
            {
                _beenInit = true;
                unitTestService = new UnitTestRunner(false);
                unitTestService.OnUnitTestResolverEvent += UnitTestService_OnUnitTestResolverEvent;
                unitTestService.TestStatusChanged += UnitTestSet_TestStatusChanged;
                unitTestService.DismissErrorUI = DismissBlazorErrorUI;
                unitTestService.CheckErrorUI = IsBlazorErrorUIVisible;
                LoadFromParams();
            }
        }

        private void UnitTestSet_TestStatusChanged()
        {
            StateHasChanged();
            TryWriteResultsAsync();
        }

        /// <summary>
        /// Coalescing write guard — ensures writes don't overlap on the single-threaded WASM runtime.
        /// If a write is already in progress, queues another write for when it finishes.
        /// </summary>
        private async void TryWriteResultsAsync()
        {
            if (ResultsDirectory == null) return;
            // Only write when new tests have completed
            var completedCount = unitTestService.Tests.Count(t => t.State == TestState.Done);
            var runDone = unitTestService.State == TestState.Done;
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
                var results = BuildResults();
                var json = JsonSerializer.Serialize(results, _jsonOptions);
                _lastWrittenCompleteCount = unitTestService.Tests.Count(t => t.State == TestState.Done);

                // Write the SINGLE per-session results file, keyed on the app-LOAD timestamp. There is no separate
                // "latest.json" — the latest run is simply the newest results-{ts}.json (by name or mtime), so a
                // fixed-name duplicate would just double the I/O (a full serialize + OPFS write every test). This
                // file is unique to this app load, so each PlaywrightMultiTest backend lane (each a page reload)
                // leaves its own permanent, comparable record. Written every (coalesced) update with the full
                // current state — including which tests have a last result — so it's correct after each
                // one-at-a-time Run click, with no dependence on a batch "done" signal.
                if (ResultsDirectory != null)
                {
                    var ts = SessionTimestamp ?? _sessionTimestamp;
                    using var fhTs = await ResultsDirectory.GetFileHandle($"results-{ts}.json", create: true);
                    using var wsTs = await fhTs.CreateWritable();
                    await wsTs.Write(json);
                    await wsTs.Close();
                }

                // Write results.json into the run folder (updated live + final)
                if (RunDirectory != null)
                {
                    using var fh = await RunDirectory.GetFileHandle("results.json", create: true);
                    using var ws = await fh.CreateWritable();
                    await ws.Write(json);
                    await ws.Close();
                }

                // When the run completes, allow re-detection of new writes if tests are re-run in this same page
                // session. The session timestamp is NOT reset — the results file stays keyed to this app load.
                if (unitTestService.State == TestState.Done)
                {
                    _lastWrittenCompleteCount = 0;
                }
            }
            catch { }
        }

        private object BuildResults()
        {
            var tests = unitTestService.Tests;
            var completedTests = tests.Where(t => t.State == TestState.Done).ToList();
            return new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                runState = unitTestService.State.ToString(),
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

        /// <inheritdoc/>
        protected override void OnAfterRender(bool firstRender)
        {
            if (!Rendered)
            {
                Rendered = true;
                StateHasChanged();
            }
        }

        bool Rendered = false;

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_beenInit)
            {
                _beenInit = false;
                unitTestService.CancelTests();
                unitTestService.TestStatusChanged -= UnitTestSet_TestStatusChanged;
            }
        }
    }
}
