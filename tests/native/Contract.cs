using Newtonsoft.Json.Linq;
using ReporterTests.Standard;

namespace ReporterTests.Native;

public static class Contract
{
    public static string Root
    {
        get
        {
            var path = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (path != null && !File.Exists(Path.Combine(path.FullName, "DotnetCtrfJsonReporter.csproj"))) path = path.Parent;
            return path?.FullName ?? throw new Exception("Reporter repository root not found");
        }
    }
    public static Dictionary<string, Capability> Manifest() => JObject.Parse(File.ReadAllText(Path.Combine(Root, "tests/standard/conformance/dotnet.json"))).Properties().ToDictionary(p => p.Name, p => new Capability((bool)p.Value["supported"]!, (string)p.Value["reason"]!));
    public static List<TestCase> Cases(string level, string artifacts)
    {
        var manifest = Manifest(); var context = level == "unit" ? Fixtures.Unit(artifacts) : Fixtures.Integration(artifacts);
        return Checks.Entries.Select(entry => new TestCase(entry.Id, () =>
        {
            var capability = Checks.Disposition(entry, manifest);
            if (!capability.Supported) throw new Skip(capability.Reason);
            var run = new Context { Samples = context.Samples, Repeated = context.Repeated, Shards = context.Shards };
            var issue = KnownIssue(entry);
            if (ExpectedFailures.Run(entry, run, level, issue, Checks.Check, Matches)) Runner.Current!.ExpectedFailure = issue;
            Runner.Current!.Warnings.AddRange(run.Warnings);
        })).ToList();
    }
    public static Issue? KnownIssue(Entry entry) => null;
    public static bool Matches(Entry entry, Sample sample, Issue issue, Exception error) => false;
}
