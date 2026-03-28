using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SpawnDev.UnitTesting.Desktop
{
    /// <summary>
    /// ViewModel for a single test row in the ListView.
    /// Implements INotifyPropertyChanged for live WPF binding updates.
    /// </summary>
    public class TestItemViewModel : INotifyPropertyChanged
    {
        private int _index;
        private string _className = "";
        private string _methodName = "";
        private string _resultText = "";
        private string _durationText = "";
        private string _error = "";

        public int Index
        {
            get => _index;
            set { if (_index != value) { _index = value; OnPropertyChanged(); } }
        }

        public string ClassName
        {
            get => _className;
            set { if (_className != value) { _className = value; OnPropertyChanged(); } }
        }

        public string MethodName
        {
            get => _methodName;
            set { if (_methodName != value) { _methodName = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Display text for the Result column.
        /// Values: "Pass", "Fail", "Skip", "Running", "" (pending).
        /// These match the XAML DataTrigger values for color coding.
        /// </summary>
        public string ResultText
        {
            get => _resultText;
            set { if (_resultText != value) { _resultText = value; OnPropertyChanged(); } }
        }

        public string DurationText
        {
            get => _durationText;
            set { if (_durationText != value) { _durationText = value; OnPropertyChanged(); } }
        }

        public string Error
        {
            get => _error;
            set { if (_error != value) { _error = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>
        /// Maps UnitTest result values to friendly display strings that match XAML DataTriggers.
        /// </summary>
        private static string MapResultText(UnitTest test)
        {
            return test.State switch
            {
                TestState.Running => "Running",
                TestState.Done => test.Result switch
                {
                    TestResult.Success => "Pass",
                    TestResult.Error => "Fail",
                    TestResult.Unsupported => "Skip",
                    _ => "",
                },
                _ => "",
            };
        }

        public static TestItemViewModel FromUnitTest(UnitTest test, int index)
        {
            return new TestItemViewModel
            {
                Index = index,
                ClassName = test.TestTypeName,
                MethodName = test.TestMethodName,
                ResultText = MapResultText(test),
                DurationText = test.State == TestState.Done ? $"{test.Duration:N0} ms" : "-",
                Error = test.Result == TestResult.Error ? test.Error : "",
            };
        }

        public void UpdateFrom(UnitTest test)
        {
            ClassName = test.TestTypeName;
            MethodName = test.TestMethodName;
            ResultText = MapResultText(test);
            DurationText = test.State == TestState.Done ? $"{test.Duration:N0} ms" : "-";
            Error = test.Result == TestResult.Error ? test.Error : "";
        }
    }
}
