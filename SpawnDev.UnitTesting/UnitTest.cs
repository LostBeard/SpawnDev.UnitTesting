using System.Reflection;

namespace SpawnDev.UnitTesting
{
    /// <summary>
    /// Represents a single unit test (a method decorated with [TestMethod])
    /// </summary>
    public class UnitTest
    {
        /// <summary>
        /// The test class name
        /// </summary>
        public string TestTypeName { get; }
        /// <summary>
        /// The test class type
        /// </summary>
        public Type TestType { get; }
        /// <summary>
        /// The test method name
        /// </summary>
        public string TestMethodName { get; }
        /// <summary>
        /// The test method info
        /// </summary>
        public MethodInfo TestMethod { get; }
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
        /// Duration in milliseconds
        /// </summary>
        public double Duration { get; set; }
        /// <summary>
        /// Error message if the test failed
        /// </summary>
        public string Error { get; set; } = "";
        /// <summary>
        /// Stack trace if the test failed
        /// </summary>
        public string StackTrace { get; set; } = "";
        /// <summary>
        /// Creates a new UnitTest
        /// </summary>
        public UnitTest(Type testClass, MethodInfo methodInfo)
        {
            TestType = testClass;
            TestTypeName = TestType.Name;
            TestMethod = methodInfo;
            TestMethodName = TestMethod.Name;
        }
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
