using System.Diagnostics;
using System.Reflection;

namespace SpawnDev.UnitTesting
{
    /// <summary>
    /// Cross-platform unit test runner. Discovers and executes tests using reflection.
    /// No platform-specific dependencies — works in Blazor, WPF, Console, and more.
    /// </summary>
    public class UnitTestRunner
    {
        /// <summary>
        /// Fired when test status changes (for UI updates)
        /// </summary>
        public event Action? TestStatusChanged;
        /// <summary>
        /// Current runner state
        /// </summary>
        public TestState State { get; private set; } = TestState.None;
        /// <summary>
        /// Default timeout in milliseconds for each test. Default is 30000 (30 seconds).
        /// Set to 0 to disable timeout.
        /// Can also be set per-test via TestMethodAttribute.Timeout.
        /// </summary>
        public int DefaultTimeoutMs { get; set; } = 30000;
        /// <summary>
        /// All discovered tests
        /// </summary>
        public List<UnitTest> Tests { get; private set; } = new List<UnitTest>();
        /// <summary>
        /// Registered test types
        /// </summary>
        public IEnumerable<Type> UnitTestTypes { get; private set; } = Enumerable.Empty<Type>();

        /// <summary>
        /// Sets the test types and discovers all test methods.
        /// </summary>
        public void SetTestTypes(IEnumerable<Type> unitTestTypes)
        {
            if (State == TestState.Running)
            {
                throw new Exception("Unit test types cannot be set while tests are running");
            }
            UnitTestTypes = unitTestTypes.Distinct().ToList();
            Tests.Clear();
            foreach (Type unitTestType in UnitTestTypes)
            {
                var methods = unitTestType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(o => o.GetParameters().Length == 0)
                    // When a derived class hides a base method with 'new',
                    // GetMethods returns both. Group by name and keep only the
                    // most-derived version (DeclaringType closest to unitTestType).
                    .GroupBy(m => m.Name)
                    .Select(g => g.OrderByDescending(m =>
                        GetTypeDepth(m.DeclaringType!, unitTestType)).First())
                    .ToList();
                foreach (var method in methods)
                {
                    var testMethodAttr = method.GetCustomAttribute<TestMethodAttribute>();
                    if (testMethodAttr == null) continue;
                    Tests.Add(new UnitTest(unitTestType, method));
                }
            }
            State = TestState.None;
            FireStateChangeEvent();
        }

        /// <summary>
        /// Discovers test types from the given assemblies (types with [TestMethod] methods)
        /// </summary>
        public void SetTestAssemblies(IEnumerable<Assembly> assemblies)
        {
            var types = assemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => !t.IsAbstract && t.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Any(m => m.GetCustomAttribute<TestMethodAttribute>() != null))
                .ToList();
            SetTestTypes(types);
        }

        /// <summary>
        /// Returns how many levels deep <paramref name="type"/> is in the
        /// inheritance chain rooted at <paramref name="root"/>.
        /// Used to prefer most-derived method when 'new' hides a base method.
        /// </summary>
        private static int GetTypeDepth(Type type, Type root)
        {
            int depth = 0;
            var t = root;
            while (t != null)
            {
                if (t == type) return depth;
                t = t.BaseType;
                depth--;
            }
            return depth;
        }

        /// <summary>
        /// Delegate for resolving test type instances
        /// </summary>
        public delegate void UnitTestResolverEventDelegate(UnitTestResolverEvent resolverEvent);
        /// <summary>
        /// Event for custom test type instance resolution (e.g. DI)
        /// </summary>
        public event UnitTestResolverEventDelegate? OnUnitTestResolverEvent;

        /// <summary>
        /// Resets all tests to initial state
        /// </summary>
        public void ResetTests()
        {
            DisposeInstances();
            Tests.ForEach(o => o.Reset());
            State = TestState.None;
        }

        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>
        /// Cancels the current test run
        /// </summary>
        public void CancelTests()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = null;
        }

        private readonly Dictionary<Type, object> _instances = new Dictionary<Type, object>();

        /// <summary>
        /// Disposes all cached test class instances that implement IDisposable.
        /// This releases cached resources like GPU contexts/accelerators.
        /// </summary>
        private void DisposeInstances()
        {
            foreach (var instance in _instances.Values)
            {
                (instance as IDisposable)?.Dispose();
            }
            _instances.Clear();
        }

        private object? GetTestTypeInstance(Type testType)
        {
            if (_instances.TryGetValue(testType, out var instance)) return instance;
            object? ret = null;
            var ev = new UnitTestResolverEvent(testType);
            OnUnitTestResolverEvent?.Invoke(ev);
            ret = ev.TypeInstance ?? Activator.CreateInstance(testType);
            if (ret != null) _instances[testType] = ret;
            return ret;
        }

        /// <summary>
        /// Runs a single test
        /// </summary>
        public async Task RunTest(UnitTest test)
        {
            var method = test.TestMethod;
            var testInstance = GetTestTypeInstance(test.TestType);
            test.Reset();
            test.State = TestState.Running;
            FireStateChangeEvent();
            var sw = new Stopwatch();
            sw.Start();
            // Determine timeout: per-test attribute overrides default
            var testMethodAttr = method.GetCustomAttribute<TestMethodAttribute>();
            var timeoutMs = testMethodAttr?.Timeout > 0 ? testMethodAttr.Timeout : DefaultTimeoutMs;
            try
            {
                var ret = method.Invoke(testInstance, null);
                if (ret is Task task)
                {
                    if (timeoutMs > 0)
                    {
                        var timeoutTask = Task.Delay(timeoutMs);
                        var completed = await Task.WhenAny(task, timeoutTask);
                        if (completed == timeoutTask)
                        {
                            throw new TimeoutException($"Test exceeded timeout of {timeoutMs}ms");
                        }
                        await task;
                    }
                    else
                    {
                        await task;
                    }
                    // If the task has a result, try to get it
                    if (ret.GetType().IsGenericType)
                    {
                        var resultProp = ret.GetType().GetProperty("Result");
                        if (resultProp != null)
                        {
                            var resultVal = resultProp.GetValue(ret);
                            if (resultVal is string retStr && !string.IsNullOrEmpty(retStr))
                            {
                                test.ResultText = retStr;
                            }
                        }
                    }
                }
                else if (ret is string retStr && !string.IsNullOrEmpty(retStr))
                {
                    test.ResultText = retStr;
                }
                test.Result = TestResult.Success;
            }
            catch (UnsupportedTestException ex)
            {
                test.StackTrace = "";
                test.ResultText = ex.Message ?? "";
                test.Result = TestResult.Unsupported;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is UnsupportedTestException unsupported)
            {
                test.StackTrace = "";
                test.ResultText = unsupported.Message ?? "";
                test.Result = TestResult.Unsupported;
            }
            catch (TimeoutException ex)
            {
                test.StackTrace = "";
                test.Error = ex.Message;
                test.Result = TestResult.Error;
            }
            catch (Exception ex)
            {
                test.StackTrace = ex.StackTrace ?? "";
                test.Error = ex.InnerException != null ? ex.InnerException.ToString() : ex.ToString();
                test.Result = TestResult.Error;
            }
            if (string.IsNullOrEmpty(test.ResultText)) test.ResultText = test.Result.ToString();
            test.State = TestState.Done;
            test.Duration = Math.Round(sw.Elapsed.TotalMilliseconds);
            FireStateChangeEvent();
        }

        /// <summary>
        /// Runs all pending tests
        /// </summary>
        public async Task RunTests()
        {
            if (State == TestState.Done)
            {
                ResetTests();
            }
            if (State != TestState.None) return;
            using var tokenSource = new CancellationTokenSource();
            _cancellationTokenSource = tokenSource;
            var token = _cancellationTokenSource.Token;
            State = TestState.Running;
            FireStateChangeEvent();
            foreach (var test in Tests)
            {
                if (token.IsCancellationRequested) break;
                if (test.State != TestState.None) continue;
                await RunTest(test);
            }
            _cancellationTokenSource = null;
            // Dispose cached test instances to release GPU resources
            DisposeInstances();
            State = TestState.Done;
            FireStateChangeEvent();
            LogResults();
        }

        /// <summary>
        /// Runs all tests matching a test class name
        /// </summary>
        public async Task RunTestsByClass(string className)
        {
            if (State == TestState.Running) return;
            if (State == TestState.Done) ResetTests();
            var matchingTests = Tests.Where(t => t.TestTypeName.Equals(className, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matchingTests.Count == 0)
            {
                Console.WriteLine($"[UnitTest] No tests found for class: {className}");
                return;
            }
            using var tokenSource = new CancellationTokenSource();
            _cancellationTokenSource = tokenSource;
            var token = _cancellationTokenSource.Token;
            State = TestState.Running;
            FireStateChangeEvent();
            foreach (var test in matchingTests)
            {
                if (token.IsCancellationRequested) break;
                await RunTest(test);
            }
            _cancellationTokenSource = null;
            State = TestState.Done;
            FireStateChangeEvent();
            LogResults();
        }

        /// <summary>
        /// Runs a single test by class and method name
        /// </summary>
        public async Task RunTestByName(string className, string methodName)
        {
            if (State == TestState.Running) return;
            var test = Tests.FirstOrDefault(t =>
                t.TestTypeName.Equals(className, StringComparison.OrdinalIgnoreCase) &&
                t.TestMethodName.Equals(methodName, StringComparison.OrdinalIgnoreCase));
            if (test == null)
            {
                Console.WriteLine($"[UnitTest] Test not found: {className}.{methodName}");
                return;
            }
            State = TestState.Running;
            FireStateChangeEvent();
            await RunTest(test);
            State = TestState.Done;
            FireStateChangeEvent();
            LogResults();
        }

        /// <summary>
        /// Gets structured test results
        /// </summary>
        public object GetResults()
        {
            var completedTests = Tests.Where(t => t.State == TestState.Done).ToList();
            return new
            {
                state = State.ToString(),
                total = Tests.Count,
                passed = completedTests.Count(t => t.Result == TestResult.Success),
                failed = completedTests.Count(t => t.Result == TestResult.Error),
                skipped = completedTests.Count(t => t.Result == TestResult.Unsupported),
                pending = Tests.Count(t => t.State == TestState.None),
                totalDuration = completedTests.Sum(t => t.Duration),
                tests = completedTests.Select(t => new
                {
                    className = t.TestTypeName,
                    method = t.TestMethodName,
                    result = t.Result.ToString(),
                    duration = t.Duration,
                    error = t.Result == TestResult.Error ? t.Error : null,
                    resultText = t.ResultText
                }).ToArray()
            };
        }

        /// <summary>
        /// Logs a summary of test results to the console
        /// </summary>
        public void LogResults()
        {
            var completedTests = Tests.Where(t => t.State == TestState.Done).ToList();
            var passed = completedTests.Count(t => t.Result == TestResult.Success);
            var failed = completedTests.Count(t => t.Result == TestResult.Error);
            var skipped = completedTests.Count(t => t.Result == TestResult.Unsupported);
            var totalDuration = completedTests.Sum(t => t.Duration);
            Console.WriteLine($"[UnitTest] DONE: {passed}/{completedTests.Count} passed, {failed} failed, {skipped} skipped ({totalDuration}ms)");
            foreach (var test in completedTests.Where(t => t.Result == TestResult.Error))
            {
                Console.WriteLine($"[UnitTest] FAIL: {test.TestTypeName}.{test.TestMethodName} - {test.Error}");
            }
        }

        /// <summary>
        /// Fires the state change event
        /// </summary>
        protected void FireStateChangeEvent()
        {
            TestStatusChanged?.Invoke();
        }
    }
}
