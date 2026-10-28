namespace SpawnDev.UnitTesting
{
    /// <summary>
    /// Test discovery leaves out this class's [TestMethod]s whose <see cref="TestMethodAttribute.Category"/> is one of
    /// these categories: they are never listed, scheduled or run for this class. Use it when a whole category cannot
    /// apply to a class (e.g. CPU-only tests on a GPU backend's class) instead of skipping each test at run time, which
    /// still costs a test launch per test. A test's Category may list several categories separated by commas
    /// (<c>Category = "BrowserOnly,NotOnWebGL"</c>); it is left out when any of them is excluded. Matching is
    /// case-insensitive. The most-derived declaration wins.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public class ExcludeCategoriesAttribute : Attribute
    {
        /// <summary>The excluded categories.</summary>
        public string[] Categories { get; }

        /// <summary>New instance.</summary>
        public ExcludeCategoriesAttribute(params string[] categories)
        {
            Categories = categories ?? Array.Empty<string>();
        }
    }
}
