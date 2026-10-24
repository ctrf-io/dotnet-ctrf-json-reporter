using ReporterTests.Native;
namespace ReporterTests.Standard;
public static class UnitSuite
{
    public static List<TestCase> Cases(string artifacts) => Contract.Cases("unit", artifacts);
}
