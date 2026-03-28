using System.Reflection;
using System.Windows;

namespace SpawnDev.UnitTesting.Desktop
{
    /// <summary>
    /// WPF Window that hosts a UnitTestsControl.
    /// Convenience wrapper — for embedding tests in an existing window, use UnitTestsControl directly.
    /// </summary>
    public partial class UnitTestsWindow : Window, IDisposable
    {
        /// <summary>
        /// Types to scan for test methods.
        /// </summary>
        public IEnumerable<Type>? TestTypes
        {
            get => TestControl.TestTypes;
            set => TestControl.TestTypes = value;
        }

        /// <summary>
        /// Assemblies to scan for test classes.
        /// </summary>
        public IEnumerable<Assembly>? TestAssemblies
        {
            get => TestControl.TestAssemblies;
            set => TestControl.TestAssemblies = value;
        }

        /// <summary>
        /// Optional custom resolver for test type instances (DI support).
        /// </summary>
        public Func<Type, object?>? TypeInstanceResolver
        {
            get => TestControl.TypeInstanceResolver;
            set => TestControl.TypeInstanceResolver = value;
        }

        /// <summary>
        /// Optional service provider for dependency injection.
        /// </summary>
        public IServiceProvider? ServiceProvider
        {
            get => TestControl.ServiceProvider;
            set => TestControl.ServiceProvider = value;
        }

        /// <summary>
        /// Optional directory path for writing live test results (latest.json).
        /// </summary>
        public string? ResultsDirectory
        {
            get => TestControl.ResultsDirectory;
            set => TestControl.ResultsDirectory = value;
        }

        /// <summary>
        /// If true, automatically starts running all tests when the window loads.
        /// </summary>
        public bool AutoRun
        {
            get => TestControl.AutoRun;
            set => TestControl.AutoRun = value;
        }

        /// <summary>
        /// If true, closes the window automatically when all tests complete.
        /// </summary>
        public bool CloseOnComplete { get; set; }

        public UnitTestsWindow()
        {
            InitializeComponent();
            TestControl.OnTestsComplete += () =>
            {
                if (CloseOnComplete) Close();
            };
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            TestControl.Dispose();
        }

        /// <inheritdoc/>
        protected override void OnClosed(EventArgs e)
        {
            Dispose();
            base.OnClosed(e);
        }
    }
}
