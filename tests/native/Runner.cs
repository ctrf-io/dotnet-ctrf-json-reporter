using System.Diagnostics;
using System.Text;
using Newtonsoft.Json.Linq;
using ReporterTests.Standard;

namespace ReporterTests.Native;

public sealed class Skip(string reason) : Exception(reason);
public sealed record TestCase(string Id, Action Run);
public sealed class Completed
{
    public required string Id { get; init; }
    public required string Status { get; set; }
    public long Duration { get; set; }
    public string? Message { get; set; }
    public Issue? ExpectedFailure { get; set; }
    public List<string> Warnings { get; } = [];
}
public static class Runner
{
    public static Completed? Current { get; private set; }
    public static List<Completed> Execute(IEnumerable<TestCase> cases, bool logFailures = true)
    {
        var records = new List<Completed>(); var ids = new HashSet<string>();
        foreach (var test in cases)
        {
            Checks.Need(ids.Add(test.Id), "Duplicate registered test: " + test.Id);
            var previous = Current;
            var record = new Completed { Id = test.Id, Status = "passed" }; Current = record; var watch = Stopwatch.StartNew();
            try { test.Run(); }
            catch (Skip skip) { record.Status = "skipped"; record.Message = skip.Message; }
            catch (Exception error) { record.Status = "failed"; record.Message = error.ToString(); }
            finally { record.Duration = watch.ElapsedMilliseconds; Current = previous; }
            if (record.ExpectedFailure != null && record.Status == "passed") record.Status = "other";
            records.Add(record);
            if (logFailures && record.Status == "failed") Console.Error.WriteLine($"{test.Id}: {record.Message}");
        }
        return records;
    }
    public static byte[] Report(string scope, List<Completed> records)
    {
        Checks.Need(records.Select(r => r.Id).Distinct().Count() == records.Count, "Duplicate completed records");
        var tests = new JArray();
        foreach (var record in records)
        {
            var level = scope is "unit" or "integration" ? char.ToUpperInvariant(scope[0]) + scope[1..] + " conformance" : "";
            var group = record.Id.StartsWith("conformance/property/", StringComparison.Ordinal) ? "Schema properties" : "Specification relationships";
            var suite = scope == "checker" ? new JArray("Shared checker controls", record.Id.Split('/').ElementAtOrDefault(1) ?? "setup") : scope == "reporter-tests" ? new JArray("Reporter tests", "Native controls") : new JArray(level, group);
            if (record.Id.StartsWith("SETUP:", StringComparison.Ordinal)) suite = new JArray(level.Length > 0 ? level : scope, "Setup failures");
            var test = new JObject { ["name"] = record.Id, ["testId"] = scope == "checker" ? record.Id : scope + "/" + record.Id, ["status"] = record.Status, ["duration"] = record.Duration, ["suite"] = suite };
            if (record.Message != null) test["message"] = record.Message;
            var extra = new JObject();
            if (record.ExpectedFailure != null && record.Status == "other")
            {
                var issue = record.ExpectedFailure;
                test["name"] = "EXPECTED FAILURE: " + record.Id; test["rawStatus"] = "expected-failure";
                test["suite"] = new JArray(level, "Expected failures (tracked defects)");
                extra["expectedFailure"] = new JObject { ["issue"] = issue.Key, ["reason"] = issue.Reason, ["followUp"] = issue.FollowUp, ["samples"] = JObject.FromObject(issue.Samples) };
            }
            if (record.Warnings.Count > 0) extra["warnings"] = JArray.FromObject(record.Warnings);
            if (extra.Count > 0) test["extra"] = extra;
            tests.Add(test);
        }
        var summary = new JObject { ["tests"] = tests.Count, ["start"] = 0, ["stop"] = 0 };
        foreach (var status in new[] {"passed", "failed", "pending", "skipped", "other"}) summary[status] = records.Count(r => r.Status == status);
        var report = new JObject { ["reportFormat"] = "CTRF", ["specVersion"] = "0.2.0", ["results"] = new JObject { ["tool"] = new JObject { ["name"] = "Native .NET assessment" }, ["summary"] = summary, ["tests"] = tests } };
        var bytes = Encoding.UTF8.GetBytes(report.ToString()); Checks.Validate(bytes); return bytes;
    }
    public static void Write(string scope, List<Completed> records, string artifacts)
    {
        var folder = scope is "unit" or "integration" ? "reports" : scope == "checker" ? "checker-reports" : "reporter-reports";
        var path = Path.Combine(artifacts, folder, scope + ".json"); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Report(scope, records));
        var diagnostics = Path.Combine(artifacts, "diagnostics"); Directory.CreateDirectory(diagnostics);
        File.WriteAllText(Path.Combine(diagnostics, scope + ".json"), System.Text.Json.JsonSerializer.Serialize(records));
        Console.WriteLine($"{scope}: {records.Count(r => r.Status == "passed")} passed, {records.Count(r => r.Status == "failed")} failed, {records.Count(r => r.Status == "skipped")} skipped, {records.Count(r => r.Status == "other")} expected failures/other");
    }
}
