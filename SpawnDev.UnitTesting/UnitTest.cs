using System.Reflection;
using System.Text.Json.Serialization;

namespace SpawnDev.UnitTesting
{
    /// <summary>
    /// Represents a single unit test (a method decorated with [TestMethod])
    /// </summary>
    public class UnitTest
    {
        /// <summary>
        /// The test class type
        /// </summary>
        [JsonIgnore]
        public Type? TestType { get; }
        /// <summary>
        /// The test method info
        /// </summary>
        [JsonIgnore]
        public MethodInfo? TestMethod { get; }
        /// <summary>
        /// The test name
        /// </summary>
        public string TestName => $"{TestTypeName}.{TestMethodName}";
        /// <summary>
        /// The test class name
        /// </summary>
        public string TestTypeName { get; set; }
        /// <summary>
        /// The test method name
        /// </summary>
        public string TestMethodName { get; set; }
        /// <summary>
        /// Result text (success message or unsupported reason)
        /// </summary>
        public string ResultText { get; set; } = "";
        /// <summary>
        /// Test result
        /// </summary>
        public TestResult Result { get; set; } = TestResult.None;
        /// <summary>
        /// Test state
        /// </summary>
        public TestState State { get; set; } = TestState.None;
        /// <summary>
        /// Duration in milliseconds. NaN/Infinity sanitized to 0 to prevent JSON serialization errors.
        /// </summary>
        private double _duration;
        public double Duration
        {
            get => _duration;
            set => _duration = double.IsFinite(value) ? value : 0;
        }
        /// <summary>
        /// Error message if the test failed
        /// </summary>
        public string Error { get; set; } = "";
        /// <summary>
        /// Stack trace if the test failed
        /// </summary>
        public string StackTrace { get; set; } = "";
        /// <summary>
        /// Number of retries consumed before the final result was recorded. Populated by
        /// <see cref="UnitTestRunner.RunTest"/> when <see cref="TestMethodAttribute.RetryCount"/>
        /// is non-zero. 0 means the test passed (or was skipped) on its first attempt.
        /// </summary>
        public int AttemptsConsumed { get; set; }
        /// <summary>
        /// Category tag propagated from <see cref="TestMethodAttribute.Category"/>. Empty
        /// when no category was specified.
        /// </summary>
        public string Category { get; set; } = "";
        /// <summary>
        /// Creates a new UnitTest
        /// </summary>
        public UnitTest(Type testClass, MethodInfo methodInfo)
        {
            TestType = testClass;
            TestTypeName = TestType.Name;
            TestMethod = methodInfo;
            TestMethodName = TestMethod.Name;
            var attr = methodInfo.GetCustomAttribute<TestMethodAttribute>();
            if (attr != null) Category = attr.Category ?? "";
        }
        public UnitTest() { }
        /// <summary>
        /// Resets the test to its initial state
        /// </summary>
        public void Reset()
        {
            Error = "";
            ResultText = "";
            Result = TestResult.None;
            Duration = 0;
            State = TestState.None;
            StackTrace = "";
            AttemptsConsumed = 0;
        }
        /// <summary>
        /// Returns ClassType.MethodName
        /// </summary>
        public override string ToString()
        {
            return $"{TestTypeName}.{TestMethodName}";
        }
    }
}
