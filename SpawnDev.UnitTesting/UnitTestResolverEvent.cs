namespace SpawnDev.UnitTesting
{
    /// <summary>
    /// Event args for resolving test type instances
    /// </summary>
    public class UnitTestResolverEvent
    {
        /// <summary>
        /// The test type being resolved
        /// </summary>
        public Type TestType { get; }
        /// <summary>
        /// Set this to provide a custom instance for the test type
        /// </summary>
        public object? TypeInstance { get; set; }
        /// <summary>
        /// Creates a new resolver event
        /// </summary>
        public UnitTestResolverEvent(Type testType)
        {
            TestType = testType;
        }
    }
}
