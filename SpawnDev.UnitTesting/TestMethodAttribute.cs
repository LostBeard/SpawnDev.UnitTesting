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
        /// Number of times to retry on failure. 0 (default) means no retry.
        /// Retries apply only to <see cref="TestResult.Error"/> outcomes - Success and
        /// Unsupported (skipped) tests are never retried. Useful for tests whose known
        /// failure mode is external-infrastructure flake (tracker, STUN, DNS) rather than
        /// a real bug. Do NOT use to mask library races - fix those at the source.
        /// </summary>
        public int RetryCount { get; set; }
        /// <summary>
        /// Optional category tag for grouping/filtering tests (e.g. "Stress", "Integration",
        /// "Smoke"). Empty by default. Runners can use this to include or exclude groups
        /// of tests from a run.
        /// </summary>
        public string Category { get; set; } = "";
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
