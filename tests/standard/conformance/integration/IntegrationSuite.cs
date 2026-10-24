using ReporterTests.Native;
namespace ReporterTests.Standard;
public static class IntegrationSuite
{
    public static List<TestCase> Cases(string artifacts) => Contract.Cases("integration", artifacts);
}
