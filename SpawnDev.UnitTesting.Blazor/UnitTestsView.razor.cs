using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using System.Reflection;

namespace SpawnDev.UnitTesting.Blazor
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

        [Inject]
        IServiceProvider ServiceProvider { get; set; } = default!;

        [Inject]
        IJSRuntime JS { get; set; } = default!;

        bool _beenInit = false;

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
            return await JS.InvokeAsync<bool>(
                "eval",
                "(() => { var el = document.getElementById('blazor-error-ui'); if (el && getComputedStyle(el).display !== 'none') { el.style.display = 'none'; return true; } return false; })()");
        }

        /// <summary>
        /// Checks if the Blazor error UI is currently visible.
        /// </summary>
        private async Task<bool> IsBlazorErrorUIVisible()
        {
            return await JS.InvokeAsync<bool>(
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
