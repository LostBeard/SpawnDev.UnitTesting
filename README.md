
## NuGet

| Package | Description | Link |
|---------|-------------|------|
|**SpawnDev.UnitTesting**| Cross-platform unit testing framework | [![NuGet version](https://badge.fury.io/nu/SpawnDev.UnitTesting.svg)](https://www.nuget.org/packages/SpawnDev.UnitTesting) |
|**SpawnDev.UnitTesting.Blazor**| Blazor UI components for SpawnDev.UnitTesting | [![NuGet version](https://badge.fury.io/nu/SpawnDev.UnitTesting.Blazor.svg)](https://www.nuget.org/packages/SpawnDev.UnitTesting.Blazor) |
|**SpawnDev.UnitTesting.Desktop**| WPF UI components for SpawnDev.UnitTesting | [![NuGet version](https://badge.fury.io/nu/SpawnDev.UnitTesting.Desktop.svg)](https://www.nuget.org/packages/SpawnDev.UnitTesting.Desktop) |

# SpawnDev.UnitTesting
[![NuGet](https://img.shields.io/nuget/dt/SpawnDev.UnitTesting.svg?label=SpawnDev.UnitTesting)](https://www.nuget.org/packages/SpawnDev.UnitTesting)

A lightweight, cross-platform unit testing framework for .NET. Works everywhere — Blazor WASM, WPF, Console, and more.

Supersedes [SpawnDev.Blazor.UnitTesting](https://www.nuget.org/packages/SpawnDev.Blazor.UnitTesting) with a platform-agnostic core.

## Features

- **Cross-platform** — no Blazor, WPF, or browser dependencies in the core library
- **Simple API** — `[TestMethod]` attribute and `UnitTestRunner` for reflection-based discovery
- **Async support** — full `async Task` test methods with configurable timeouts
- **Unsupported tests** — throw `UnsupportedTestException` to skip tests gracefully
- **Blazor UI** — `SpawnDev.UnitTesting.Blazor` provides `UnitTestsView` Razor component
- **WPF Desktop UI** — `SpawnDev.UnitTesting.Desktop` provides `UnitTestsControl` and `UnitTestsWindow`

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

### 3. Blazor UI (SpawnDev.UnitTesting.Blazor)

```razor
@using SpawnDev.UnitTesting.Blazor

<UnitTestsView TestAssemblies="_assemblies"></UnitTestsView>

@code {
    IEnumerable<Assembly>? _assemblies = new List<Assembly> {
        typeof(MyTests).Assembly
    };
}
```

### 4. WPF Desktop UI (SpawnDev.UnitTesting.Desktop)

**Option A — Standalone Window:**
```csharp
var testWindow = new SpawnDev.UnitTesting.Desktop.UnitTestsWindow
{
    TestTypes = new[] { typeof(MyTests) },
    AutoRun = true,
};
testWindow.Show();
```

**Option B — Embeddable UserControl:**
```xml
<local:UnitTestsControl x:Name="TestControl" />
```
```csharp
TestControl.TestTypes = new[] { typeof(MyTests) };
TestControl.AutoRun = true;
```
