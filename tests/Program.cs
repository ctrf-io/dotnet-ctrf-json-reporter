using ReporterTests.Native;
using ReporterTests.Standard;

var selected = args.FirstOrDefault() ?? "standard";
var scopes = selected switch
{
    "all" => new[] { "reporter-tests", "checker", "unit", "integration" },
    "standard" => new[] { "checker", "unit", "integration" },
    "conformance" => new[] { "unit", "integration" },
    "ordinary" => new[] { "reporter-tests" },
    "checker" or "unit" or "integration" => new[] { selected },
    _ => throw new ArgumentException("Unknown suite: " + selected)
};
var artifacts = Environment.GetEnvironmentVariable("CTRF_STANDARD_ARTIFACTS");
var temporary = artifacts == null;
artifacts ??= Path.Combine(Path.GetTempPath(), "ctrf-dotnet-results-" + Guid.NewGuid());
Checks.Need(Path.IsPathFullyQualified(artifacts), "CTRF_STANDARD_ARTIFACTS must be absolute");
var failed = false;
try
{
    foreach (var scope in scopes)
    {
        var folder = scope is "unit" or "integration" ? "reports" : scope == "checker" ? "checker-reports" : "reporter-reports";
        var output = Path.Combine(artifacts, folder, scope + ".json");
        if (File.Exists(output)) File.Delete(output);
        List<Completed> completed;
        try
        {
            var cases = scope == "checker" ? CheckerControls.Cases() : scope == "reporter-tests" ? NativeControls.Cases(artifacts) : scope == "unit" ? UnitSuite.Cases(artifacts) : IntegrationSuite.Cases(artifacts);
            completed = Runner.Execute(cases);
            var wanted = scope == "checker" ? Checks.CheckerIds : scope is "unit" or "integration" ? Checks.Entries.Select(e => e.Id).ToArray() : cases.Select(c => c.Id).ToArray();
            Checks.Equal(completed.Select(r => r.Id).Order().ToArray(), wanted.Order().ToArray(), "Completed inventory");
        }
        catch (Exception error) { completed = [new Completed { Id = "SETUP: " + scope, Status = "failed", Message = error.ToString() }]; Console.Error.WriteLine(error); }
        try { Runner.Write(scope, completed, artifacts); } catch (Exception error) { Console.Error.WriteLine(error); failed = true; }
        failed |= completed.Any(c => c.Status == "failed");
    }
}
finally { if (temporary && Directory.Exists(artifacts)) Directory.Delete(artifacts, true); }
return failed ? 1 : 0;
