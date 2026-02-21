namespace SpawnDev.UnitTesting
{
    /// <summary>
    /// Used to mark test methods for unit testing
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class TestMethodAttribute : Attribute
    {
        /// <summary>
        /// Test method name override. If empty, the method name is used.
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Maximum time in milliseconds to wait for the test to complete.
        /// A value of 0 (default) uses the runner's DefaultTimeout.
        /// </summary>
        public int Timeout { get; set; }
        /// <summary>
        /// New instance
        /// </summary>
        /// <param name="name"></param>
        public TestMethodAttribute(string name = "")
        {
            Name = name;
        }
    }
}
