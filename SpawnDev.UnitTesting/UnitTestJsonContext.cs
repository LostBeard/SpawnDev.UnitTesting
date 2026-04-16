using System.Text.Json.Serialization;

namespace SpawnDev.UnitTesting
{
    /// <summary>
    /// Source-generated JSON serialization context for UnitTest.
    /// Avoids reflection-based enum converter initialization which triggers
    /// an intermittent CLR internal error (0x80131506) on .NET 10 with
    /// System.Text.Json's EnumConverter constructor.
    /// </summary>
    [JsonSerializable(typeof(UnitTest))]
    [JsonSerializable(typeof(TestResult))]
    [JsonSerializable(typeof(TestState))]
    internal partial class UnitTestJsonContext : JsonSerializerContext
    {
    }
}
