
## NuGet

| Package | Description | Link |
|---------|-------------|------|
|**SpawnDev.UnitTesting**| Cross-platform unit testing framework | [![NuGet version](https://badge.fury.io/nu/SpawnDev.UnitTesting.svg)](https://www.nuget.org/packages/SpawnDev.UnitTesting) |
|**SpawnDev.UnitTesting.Blazor**| Blazor UI components for SpawnDev.UnitTesting | [![NuGet version](https://badge.fury.io/nu/SpawnDev.UnitTesting.Blazor.svg)](https://www.nuget.org/packages/SpawnDev.UnitTesting.Blazor) |
|**SpawnDev.UnitTesting.Desktop**| WPF UI components for SpawnDev.UnitTesting | [![NuGet version](https://badge.fury.io/nu/SpawnDev.UnitTesting.Desktop.svg)](https://www.nuget.org/packages/SpawnDev.UnitTesting.Desktop) |

# SpawnDev.UnitTesting
[![NuGet](https://img.shields.io/nuget/dt/SpawnDev.UnitTesting.svg?label=SpawnDev.UnitTesting)](https://www.nuget.org/packages/SpawnDev.UnitTesting)

A lightweight, cross-platform unit testing framework for .NET. Works everywhere - Blazor WASM, WPF, Console, and more.

Supersedes [SpawnDev.Blazor.UnitTesting](https://www.nuget.org/packages/SpawnDev.Blazor.UnitTesting) with a platform-agnostic core.

## Features

- **Cross-platform** - no Blazor, WPF, or browser dependencies in the core library
- **Simple API** - `[TestMethod]` attribute and `UnitTestRunner` for reflection-based discovery
- **Async support** - full `async Task` test methods with configurable timeouts
- **Dependency injection** - test classes resolved via `IServiceProvider` and `ActivatorUtilities`
- **Unsupported tests** - throw `UnsupportedTestException` to skip tests gracefully
- **Blazor UI** - `SpawnDev.UnitTesting.Blazor` provides `UnitTestsView` Razor component
- **WPF Desktop UI** - `SpawnDev.UnitTesting.Desktop` provides `UnitTestsControl` and `UnitTestsWindow`
- **Console runner** - `ConsoleRunner` for subprocess-based test execution (used by PlaywrightMultiTest)
- **Lazy test resolution** - single-test mode avoids loading all method metadata, preventing .NET 10 JIT crashes with heavy generic types

## Quick Start

### 1. Create test classes

```csharp
using SpawnDev.UnitTesting;

public class MyTests
{
    [TestMethod]
    public async Task AdditionTest()
    {
        int result = 1 + 1;
        if (result != 2) throw new Exception($"Expected 2, got {result}");
    }

    [TestMethod]
    public async Task UnsupportedFeatureTest()
    {
        throw new UnsupportedTestException("Feature not available on this platform");
    }
}
```

### 2. Run tests programmatically

```csharp
var runner = new UnitTestRunner();
runner.SetTestTypes(new[] { typeof(MyTests) });
runner.TestStatusChanged += () => Console.WriteLine($"Progress: {runner.Tests.Count(t => t.State == TestState.Done)}/{runner.Tests.Count}");
await runner.RunTests();
```

### 3. Dependency Injection

Test constructors are injected via `ActivatorUtilities.CreateInstance`. Register services and test classes in the service container:

```csharp
var services = new ServiceCollection();
services.AddSingleton<IMyService, MyService>();
services.AddSingleton<MyTests>();
var sp = services.BuildServiceProvider();

var runner = new UnitTestRunner(sp, true); // true = auto-discover all tests
await runner.RunTests();
```

Resolution order for test class instances:
1. Custom resolver via `OnUnitTestResolverEvent`
2. `IServiceProvider.GetService(type)` - for registered services
3. `ActivatorUtilities.CreateInstance(sp, type)` - for DI-injected constructors
4. `Activator.CreateInstance(type)` - parameterless constructor fallback

One instance per test class, reused across all `[TestMethod]` calls. Implement `IDisposable` for cleanup.

### 4. Console Runner

`ConsoleRunner` enables subprocess-based test execution. Each test runs in its own process, isolating failures and enabling parallel test harnesses like PlaywrightMultiTest.

```csharp
// Program.cs
using SpawnDev.UnitTesting;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddSingleton<MyTests>();
var sp = services.BuildServiceProvider();

// Skip full discovery when running a single test for better performance
// and to avoid .NET 10 JIT issues with heavy generic types
var runner = new UnitTestRunner(sp, false);
if (args.Length == 0)
{
    // List mode: discover all tests
    runner.SetTestTypes(new[] { typeof(MyTests) });
}
else
{
    // Single test mode: register types without method reflection
    runner.RegisterTestTypes(new[] { typeof(MyTests) });
}
await ConsoleRunner.Run(args, runner);
```

**Usage:**
- `myapp.exe` - lists all test names (one per line)
- `myapp.exe MyTests.AdditionTest` - runs that test, outputs `TEST: {json}` to stdout

**How it works:**
- No args: calls `SetTestTypes` for full method discovery, prints `ClassName.MethodName` for each test
- With test name arg: uses `ResolveSingleTest` to find and run only the requested method, then serializes the `UnitTest` result as JSON

### 5. Lazy Test Resolution (v2.5.1+)

When running a single test via ConsoleRunner, `ResolveSingleTest` resolves the method by name using `Type.GetMethod()` instead of loading all method metadata via `GetMethods()`. This is critical for test classes that reference heavy generic types (e.g., ILGPU kernels, `ArrayView<T>`) in non-test methods.

```csharp
// Register types without method discovery
runner.RegisterTestTypes(new[] { typeof(MyTests) });

// Resolve and run a single test by name
var test = runner.ResolveSingleTest("MyTests.AdditionTest");
if (test != null)
{
    await runner.RunTest(test);
}
```

**Why this matters:** On .NET 10, `GetMethods()` forces the JIT to compile type metadata for all method signatures. When test classes contain methods referencing complex generic types, this can race with COM interop threads and cause intermittent `Internal CLR error (0x80131506)` crashes. Lazy resolution avoids this by only loading the specific method being tested.

### 6. Test Timeouts

Default timeout is 30 seconds per test. Override per-test or globally:

```csharp
// Per-test timeout
[TestMethod(Timeout = 120000)] // 2 minutes
public async Task SlowTest() { /* ... */ }

// Global default
runner.DefaultTimeoutMs = 60000; // 1 minute
runner.DefaultTimeoutMs = 0;     // disable timeout
```

### 7. Retry on Failure (v2.5.2+)

For tests whose failure mode is genuine external-infrastructure flake (network hops, signaling servers, STUN/TURN, DNS), set `RetryCount` on the attribute. The test re-runs up to N times on `Error`; `Success` and `Unsupported` short-circuit the loop.

```csharp
[TestMethod(Timeout = 180000, RetryCount = 3)]
public async Task SignalingPathTest() { /* ... */ }
```

Per-attempt state (`Error`, `ResultText`, `StackTrace`) is reset between tries so a passing retry doesn't carry over the prior attempt's error. `UnitTest.AttemptsConsumed` is populated so you can see "passed on attempt 2 of 4" in the results. Duration is cumulative across all attempts.

**Don't use this to mask library races.** If a test fails because of a genuine concurrency bug, fix the bug. `RetryCount` is for infrastructure hiccups outside your code's control.

### 8. Test Categories (v2.5.2+)

Tag tests with a `Category` string for grouping or filtering. Common uses: opt-in stress tests, smoke suites, integration-only subsets.

```csharp
[TestMethod(Category = "Stress")]
public async Task HundredMBTensorTransfer() { /* ... */ }

[TestMethod(Category = "Smoke")]
public async Task CriticalPathTest() { /* ... */ }
```

The category flows through to `UnitTest.Category` for downstream filtering; the runner itself does not pre-filter by category - consuming harnesses decide which categories to include/exclude at run time.

### 9. Test Result Text

Tests can return a string for additional result information:

```csharp
[TestMethod]
public async Task<string> InfoTest()
{
    int count = ProcessItems();
    return $"Processed {count} items";
}
```

### 10. Blazor UI (SpawnDev.UnitTesting.Blazor)

```razor
@using SpawnDev.UnitTesting.Blazor

<UnitTestsView TestAssemblies="_assemblies"></UnitTestsView>

@code {
    IEnumerable<Assembly>? _assemblies = new List<Assembly> {
        typeof(MyTests).Assembly
    };
}
```

Supports live `latest.json` result output via `ResultsDirectory` (OPFS `FileSystemDirectoryHandle` in browser).

### 11. WPF Desktop UI (SpawnDev.UnitTesting.Desktop)

**Option A - Standalone Window:**
```csharp
var testWindow = new SpawnDev.UnitTesting.Desktop.UnitTestsWindow
{
    TestTypes = new[] { typeof(MyTests) },
    AutoRun = true,
    ResultsDirectory = "path/to/results",
    CloseOnComplete = true,
};
testWindow.Show();
```

**Option B - Embeddable UserControl:**
```xml
<local:UnitTestsControl x:Name="TestControl" />
```
```csharp
TestControl.TestTypes = new[] { typeof(MyTests) };
TestControl.AutoRun = true;
```

## API Reference

### UnitTestRunner

| Method | Description |
|--------|-------------|
| `SetTestTypes(IEnumerable<Type>)` | Registers test types and discovers all `[TestMethod]` methods via reflection |
| `SetTestAssemblies(IEnumerable<Assembly>)` | Scans assemblies for types with `[TestMethod]` methods |
| `RegisterTestTypes(IEnumerable<Type>)` | Registers types without method discovery (for use with `ResolveSingleTest`) |
| `ResolveSingleTest(string)` | Resolves one test by `ClassName.MethodName` without loading all method metadata |
| `FindAllTests()` | Discovers tests in all loaded assemblies |
| `RunTest(UnitTest)` | Runs a single test |
| `RunTests()` | Runs all discovered tests |
| `RunTestsByClass(string)` | Runs all tests in a class |
| `RunTestByName(string, string)` | Runs a test by class and method name |
| `ResetTests()` | Resets all tests to initial state and disposes cached instances |
| `CancelTests()` | Cancels the current test run |

### Test Result Types

| Outcome | How to signal |
|---------|---------------|
| **Pass** | Return normally (no exception) |
| **Fail** | Throw any `Exception` |
| **Skip** | Throw `UnsupportedTestException("reason")` |
| **Timeout** | Exceeds `TestMethodAttribute.Timeout` or `DefaultTimeoutMs` |

### UnitTest Properties

| Property | Type | Description |
|----------|------|-------------|
| `TestName` | `string` | `ClassName.MethodName` |
| `TestTypeName` | `string` | Short class name |
| `TestMethodName` | `string` | Method name |
| `Result` | `TestResult` | `None`, `Error`, `Success`, `Unsupported` |
| `State` | `TestState` | `None`, `Running`, `Done` |
| `Duration` | `double` | Execution time in milliseconds |
| `Error` | `string` | Error message if failed |
| `StackTrace` | `string` | Stack trace if failed |
| `ResultText` | `string` | Success message or skip reason |
| `AttemptsConsumed` | `int` | Retries used before final result (0 = passed on first attempt) |
| `Category` | `string` | Category tag from `TestMethodAttribute.Category` (empty when unset) |

## Changelog

### v2.5.2
- **Added `TestMethodAttribute.RetryCount`** - retry the test up to N times on `TestResult.Error`. `Success` and `Unsupported` short-circuit the loop. Per-attempt state (`Error`, `ResultText`, `StackTrace`) is reset between tries; cumulative `Duration` across all attempts.
- **Added `TestMethodAttribute.Category`** - string tag for grouping/filtering tests (e.g. `"Stress"`, `"Smoke"`). Propagated to `UnitTest.Category` for downstream filtering by consuming harnesses.
- **Added `UnitTest.AttemptsConsumed`** - populated by `UnitTestRunner.RunTest` so results can report "passed on attempt 2 of 4".
- **Added `UnitTest.Category`** - mirrors the attribute value for easy access without re-reflecting.

### v2.5.1
- **Fixed .NET 10 JIT crash** - eager method reflection via `GetMethods()` forced the CLR to compile type metadata for all method signatures at process startup, causing intermittent `Internal CLR error (0x80131506)` when test classes referenced heavy generic types (ILGPU kernels, etc.)
- Added `RegisterTestTypes()` - registers types without method discovery
- Added `ResolveSingleTest()` - resolves a single test by name without loading all methods
- Added `UnitTestJsonContext` - source-generated JSON serialization for enum safety
- Improved `SetTestAssemblies()` with `ReflectionTypeLoadException` handling
- `ConsoleRunner` uses lazy resolution as fallback for single-test subprocess mode

### v2.5.0
- Added `SpawnDev.UnitTesting.Desktop` - WPF test runner library
- `UnitTestsWindow` and `UnitTestsControl` for WPF applications

### v2.4.2
- Added `JsonNumberHandling.AllowNamedFloatingPointLiterals` for float serialization safety
- Live `latest.json` result output for real-time test monitoring
