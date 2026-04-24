using Microsoft.Extensions.DependencyInjection;
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
        IServiceProvider? _serviceProvider;
        /// <summary>
        /// Initializes a new instance of the UnitTestRunner class with default settings. Use SetTestTypes or SetTestAssemblies to discover tests before running.
        /// </summary>
        public UnitTestRunner()
        {
            FindAllTests();
        }
        /// <summary>
        /// Initializes a new instance of the UnitTestRunner class. If findAll is true, discovers all tests in the current assembly and its references.
        /// </summary>
        public UnitTestRunner(bool findAll)
        {
            if (findAll) FindAllTests();
        }
        /// <summary>
        /// Initializes a new instance of the UnitTestRunner class using the specified service provider to resolve
        /// dependencies.
        /// </summary>
        /// <param name="serviceProvider">The service provider used to obtain required services and dependencies for the UnitTestRunner instance.
        /// Cannot be null.</param>
        public UnitTestRunner(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            FindAllTests();
        }
        /// <summary>
        /// Initializes a new instance of the UnitTestRunner class using the specified service provider, and optionally
        /// discovers all available tests.
        /// </summary>
        /// <remarks>Set findAll to true to automatically discover and load all tests when the
        /// UnitTestRunner is created. This can be useful for scenarios where immediate test discovery is
        /// required.</remarks>
        /// <param name="serviceProvider">The service provider used to resolve dependencies required by the UnitTestRunner.</param>
        /// <param name="findAll">true to discover and load all available tests during initialization; otherwise, false.</param>
        public UnitTestRunner(IServiceProvider serviceProvider, bool findAll)
        {
            _serviceProvider = serviceProvider;
            if (findAll) FindAllTests();
        }

        /// <summary>
        /// Finds all tests in the running assembly and its referenced assemblies. This is a convenience method that calls SetTestAssemblies with the current assembly and its references.
        /// </summary>
        public void FindAllTests()
        {
            // get a list of assemblies to scan: the current assembly and all referenced assemblies
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

            //foreach (var assembly in loadedAssemblies)
            //{
            //    Console.WriteLine($"{assembly.GetName().Name} - {assembly.Location}");
            //}
            //var currentAssembly = Assembly.GetExecutingAssembly();
            //var referencedAssemblies = currentAssembly.GetReferencedAssemblies()
            //    .Select(Assembly.Load)
            //    .ToList();
            //referencedAssemblies.Add(currentAssembly);
            SetTestAssemblies(loadedAssemblies);
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
        /// Fired when test status changes (for UI updates)
        /// </summary>
        public event Action? TestStatusChanged;
        /// <summary>
        /// Optional callback invoked before each test to check for and dismiss
        /// framework-level error UI (e.g. Blazor's #blazor-error-ui).
        /// Should return true if an error was visible and dismissed.
        /// </summary>
        public Func<Task<bool>>? DismissErrorUI { get; set; }
        /// <summary>
        /// Optional callback invoked after each test to check if framework-level
        /// error UI appeared during the test.
        /// Should return true if an error is currently visible.
        /// </summary>
        public Func<Task<bool>>? CheckErrorUI { get; set; }
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
            if (!unitTestTypes.Any())
            {
                return;
            }
            if (State == TestState.Running)
            {
                throw new Exception("Unit test types cannot be set while tests are running");
            }
            UnitTestTypes = unitTestTypes.Distinct().ToList();
            Tests.Clear();
            foreach (Type unitTestType in UnitTestTypes)
            {
                // Discover test methods using metadata-only reflection to avoid forcing
                // the CLR to JIT-compile type signatures for non-test methods.
                // This prevents .NET 10 JIT crashes when test classes contain methods that
                // reference heavy generic types (ILGPU kernels, ArrayView<T>, etc.).
                //
                // Strategy: use CustomAttributeData (metadata-only, no type loading) to find
                // method names with [TestMethod], then resolve only those specific methods.
                var testMethodNames = new HashSet<string>();
                var testMethodAttrName = typeof(TestMethodAttribute).FullName;

                // Walk the type hierarchy to find all [TestMethod]-decorated methods
                var currentType = unitTestType;
                while (currentType != null && currentType != typeof(object))
                {
                    foreach (var method in currentType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        try
                        {
                            // Use CustomAttributeData for metadata-only check - does NOT
                            // force resolution of the method's parameter/return types
                            var hasAttr = CustomAttributeData.GetCustomAttributes(method)
                                .Any(a => a.AttributeType == typeof(TestMethodAttribute));
                            if (!hasAttr) continue;
                            if (method.GetParameters().Length != 0) continue;
                            // Only add if not already found in a more-derived type
                            testMethodNames.Add(method.Name);
                        }
                        catch
                        {
                            // Skip methods that can't be reflected (e.g., broken generic instantiations)
                        }
                    }
                    currentType = currentType.BaseType;
                }

                // Now resolve only the test methods by name on the concrete type
                foreach (var methodName in testMethodNames)
                {
                    try
                    {
                        var method = unitTestType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                        if (method != null)
                        {
                            Tests.Add(new UnitTest(unitTestType, method));
                        }
                    }
                    catch
                    {
                        // Skip methods that fail to resolve
                    }
                }
            }
            State = TestState.None;
            FireStateChangeEvent();
        }

        /// <summary>
        /// Registers test types without performing method discovery.
        /// Use this when you only need ResolveSingleTest and want to avoid loading
        /// all method signatures upfront.
        /// </summary>
        public void RegisterTestTypes(IEnumerable<Type> types)
        {
            UnitTestTypes = types.Distinct().ToList();
        }

        /// <summary>
        /// Discovers test types from the given assemblies (types with [TestMethod] methods).
        /// Uses IsDefined for attribute checks to avoid eager type loading of method signatures
        /// which can trigger .NET 10 JIT crashes with heavy generic types (e.g., ILGPU kernels).
        /// </summary>
        public void SetTestAssemblies(IEnumerable<Assembly> assemblies)
        {
            var types = new List<Type>();
            foreach (var assembly in assemblies)
            {
                Type[] assemblyTypes;
                try { assemblyTypes = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { assemblyTypes = ex.Types.Where(t => t != null).ToArray()!; }
                catch { continue; }

                foreach (var type in assemblyTypes)
                {
                    if (type.IsAbstract) continue;
                    try
                    {
                        // Use IsDefined for a lightweight attribute check instead of
                        // GetCustomAttribute which forces full method signature resolution.
                        var hasTestMethod = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .Any(m => m.GetParameters().Length == 0 && m.IsDefined(typeof(TestMethodAttribute), true));
                        if (hasTestMethod) types.Add(type);
                    }
                    catch { }
                }
            }
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
        /// Resolves a single test by "ClassName.MethodName" without loading all method metadata
        /// on the type. This avoids forcing the CLR to JIT-compile type signatures for every
        /// method, which prevents .NET 10 JIT crashes when test classes contain methods that
        /// reference heavy generic types (ILGPU kernels, ArrayView, etc.).
        /// </summary>
        public UnitTest? ResolveSingleTest(string fullTestName)
        {
            var parts = fullTestName.Split('.', 2);
            if (parts.Length != 2) return null;
            var className = parts[0];
            var methodName = parts[1];

            // Find the test type by class name from registered types,
            // or scan loaded assemblies if no types are registered yet
            Type? testType = UnitTestTypes.FirstOrDefault(t => t.Name == className);
            if (testType == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        testType = assembly.GetTypes().FirstOrDefault(t => !t.IsAbstract && t.Name == className);
                        if (testType != null) break;
                    }
                    catch { }
                }
            }
            if (testType == null) return null;

            // Resolve ONLY the specific method by name - does NOT load all methods
            var method = testType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (method == null) return null;

            // Verify it has [TestMethod]
            if (!method.IsDefined(typeof(TestMethodAttribute), true)) return null;

            var test = new UnitTest(testType, method);
            Tests.Add(test);
            return test;
        }

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
            ret = ev.TypeInstance;
            // check if the test type is a service
            if (ret == null && _serviceProvider != null)
            {
                try
                {
                    ret = _serviceProvider.GetService(testType);
                }
                catch { }

            }
            if (ret == null)
            {
                // if not, try to create an instance (will work if it has a parameterless constructor or if dependencies can be resolved from the service provider)
                if (_serviceProvider != null)
                {
                    // the type may use services, try to create it with the service provider
                    try
                    {
                        ret = ActivatorUtilities.CreateInstance(_serviceProvider, testType);
                    }
                    catch { }
                }
                else
                {
                    try
                    {
                        ret = Activator.CreateInstance(testType);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[UnitTest] Failed to create instance of {testType.FullName}: {ex.Message}");
                    }
                }
            }
            if (ret != null) _instances[testType] = ret;
            return ret;
        }

        /// <summary>
        /// Runs a single test. If <see cref="TestMethodAttribute.RetryCount"/> is non-zero and
        /// the test result is <see cref="TestResult.Error"/>, the test is re-invoked up to that
        /// many additional times. Success and Unsupported outcomes short-circuit the retry loop.
        /// The final <see cref="UnitTest.Duration"/> is cumulative across all attempts.
        /// </summary>
        public async Task RunTest(UnitTest test)
        {
            var method = test.TestMethod;
            var testInstance = GetTestTypeInstance(test.TestType);
            test.Reset();
            test.State = TestState.Running;
            FireStateChangeEvent();
            // Determine timeout + retry count from the test method attribute
            var testMethodAttr = method.GetCustomAttribute<TestMethodAttribute>();
            var timeoutMs = testMethodAttr?.Timeout > 0 ? testMethodAttr.Timeout : DefaultTimeoutMs;
            var maxAttempts = Math.Max(1, (testMethodAttr?.RetryCount ?? 0) + 1);
            var sw = new Stopwatch();
            sw.Start();
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                // Fresh per-attempt state so a passing retry doesn't leak the prior attempt's error
                test.Error = "";
                test.ResultText = "";
                test.StackTrace = "";
                test.Result = TestResult.None;
                // Dismiss any pre-existing error UI before the attempt
                if (DismissErrorUI != null)
                {
                    try { await DismissErrorUI(); } catch { }
                }
                try
                {
                    var ret = method!.Invoke(testInstance, null);
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
                // Check if framework error UI appeared during the test (only on otherwise-success path)
                if (test.Result == TestResult.Success && CheckErrorUI != null)
                {
                    try
                    {
                        if (await CheckErrorUI())
                        {
                            test.Result = TestResult.Error;
                            test.Error = "Framework error UI appeared during test execution";
                        }
                    }
                    catch { }
                }
                // Stop retrying once we have a non-error outcome (Success or Unsupported)
                if (test.Result != TestResult.Error)
                {
                    test.AttemptsConsumed = attempt - 1;
                    break;
                }
                // Error and more attempts remain — loop
                if (attempt < maxAttempts) continue;
                test.AttemptsConsumed = attempt - 1;
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
