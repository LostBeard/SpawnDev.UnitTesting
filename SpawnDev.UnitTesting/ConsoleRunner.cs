using System.Text.Json;

namespace SpawnDev.UnitTesting
{
    public static class ConsoleRunner
    {
        public static async Task<int> Run(string[] args, UnitTestRunner? runner = null)
        {
            runner ??= new UnitTestRunner(true);

            // default return list of tests
            if (args.Length == 0)
            {
                foreach(var test in runner.Tests)
                {
                    Console.WriteLine($"{test.TestTypeName}.{test.TestMethodName}");
                }
                return 0;
            } 
            // if args has a test name, run that test
            var testName = args[0];
            
            var selectedtest = runner.Tests.FirstOrDefault(t => $"{t.TestTypeName}.{t.TestMethodName}" == testName);
            if (selectedtest != null)
            {
                await runner.RunTest(selectedtest);
                Console.WriteLine($"TEST: {JsonSerializer.Serialize(selectedtest)}");
                return 0;
            }
            // test not found
            Console.WriteLine("ERROR: Test not found: " + testName);
            return 1;
        }
    }
}
