using System.Text;
using Newtonsoft.Json.Linq;
using ReporterTests.Standard;

namespace ReporterTests.Native;

public static class NativeControls
{
    private static void WithUnit(Action<Context> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "ctrf-dotnet-controls-" + Guid.NewGuid());
        try { action(Fixtures.Unit(path)); } finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    public static List<TestCase> Cases(string artifacts) =>
    [
        new("native/existing-regression-runner", () =>
        {
            var result = Fixtures.Execute(Contract.Root, "node", ["tests/verify.mjs"]);
            var path = Path.Combine(artifacts, "diagnostics"); Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "existing-regressions.log"), result.Stdout + result.Stderr);
            Checks.Need(System.Text.RegularExpressions.Regex.IsMatch(result.Stdout, @"Passed: [1-9]\d* reports accepted .*; [1-9]\d* malformed inputs rejected; legacy suite rejected\."), "Existing regression runner did not report a completed nonempty run");
        }),
        new("native/json/timestamps-retain-string-and-precision", () =>
        {
            var report = Checks.Decode(Encoding.UTF8.GetBytes("{\"timestamp\":\"2026-10-05T10:00:00.1234567Z\"}"));
            Checks.Equal(report["timestamp"]!.Type, JTokenType.String, "Timestamp JSON type");
            Checks.Equal(report["timestamp"], "2026-10-05T10:00:00.1234567Z", "Timestamp precision");
        }),
        new("native/capabilities/complete-inventory", () =>
        {
            var manifest = Contract.Manifest(); Checks.Equal(manifest.Count, Checks.Entries.Count, "Native capability inventory");
            foreach (var entry in Checks.Entries) _ = Checks.Disposition(entry, manifest);
        }),
        new("native/fixtures/serialized-producer-evidence", () => WithUnit(context =>
        {
            Checks.Equal(context.Samples.Count, 7, "Unit scenarios"); Checks.Equal(context.Repeated.Count, 2, "Repeated runs"); Checks.Equal(context.Shards.Count, 2, "Shards");
            foreach (var sample in context.Samples) Checks.Validate(sample.Bytes);
        })),
        new("native/oracle/fixed-mixed-values", () =>
        {
            var observed = Fixtures.Observe(File.ReadAllText(Path.Combine(Contract.Root, "tests/fixtures/results.trx")));
            Checks.Equal(Checks.Tests(observed).Select(t => (long)t["duration"]!).ToArray(), new long[] {100,123,10,3723456,0,0,0,2592000000,0}, "Independent durations");
            Checks.Equal(Checks.Tests(observed).Select(t => (string)t["status"]!).ToArray(), new[] {"passed","failed","failed","failed","failed","skipped","pending","other","other"}, "Independent statuses");
            Checks.Equal(Checks.Tests(observed)[0]["stdout"], new JArray("first","","third","fourth","fifth"), "Independent output lines");
            Checks.Equal(Checks.Tests(observed)[1]["filePath"], "C:\\src\\UnitTests.cs", "Independent failure source");
        }),
        new("native/identity/omitted-run-remains-absent", () => WithUnit(context => Checks.Need(context.Samples.Single(s => s.Name == "empty").Report["runId"] == null, "Run ID invented"))),
        new("native/identity/source-run-and-shard-propagate", () => WithUnit(context => { foreach (var sample in context.Shards) { Checks.Equal(sample.Report["runId"], "coordinated", "Source run"); Checks.Need(Checks.Results(sample.Report)["environment"]!["shardId"] != null, "Missing shard"); } })),
        new("native/capabilities/unsupported-emission-still-validates", () => WithUnit(context => { var o = (JObject)context.Samples[0].Report.DeepClone(); o["insights"] = true; CheckerControls.Reject(() => Checks.Validate(Encoding.UTF8.GetBytes(o.ToString()))); })),
        new("native/defects/no-unreproduced-baselines", () => { foreach (var entry in Checks.Entries) Checks.Need(Contract.KnownIssue(entry) == null, "Review newly introduced defect records and add native exact matcher controls"); }),
        new("native/reporting/expected-failure-is-other", () =>
        {
            var issue = new Issue("local-control", "Native reporting control", "pending", new() { ["unit"] = ["control"] });
            var report = Checks.Decode(Runner.Report("unit", [new Completed { Id = "conformance/property/reportFormat", Status = "other", ExpectedFailure = issue }]));
            var test = Checks.Tests(report)[0]; Checks.Equal(test["status"], "other", "Expected failure status"); Checks.Equal(test["rawStatus"], "expected-failure", "Native marker"); Checks.Need((string?)test["extra"]?["expectedFailure"]?["issue"] == "local-control", "Issue metadata missing");
        }),
        new("native/reporting/failure-not-hidden-by-marker", () =>
        {
            var report = Checks.Decode(Runner.Report("unit", [new Completed { Id = "control", Status = "failed", ExpectedFailure = new Issue("control", "control", "pending", []) }])); Checks.Equal(Checks.Summary(report)["failed"], 1, "Unexpected failure remains failed"); Checks.Equal(Checks.Summary(report)["other"], 0, "No false expected failure");
        }),
        new("native/reporting/checker-id-preserved", () =>
        {
            var id = Checks.CheckerIds[0];
            var report = Checks.Decode(Runner.Report("checker", [new Completed { Id = id, Status = "passed" }]));
            Checks.Equal(Checks.Tests(report)[0]["testId"], id, "Checker identity");
        }),
        new("native/json/numeric-source-equality", () =>
        {
            var context = CheckerControls.Valid(); Checks.Tests(context.Samples[0].Report)[0]["duration"] = 12.0;
            Checks.Check(CheckerControls.Property("results.tests[].duration"), context);
            CheckerControls.Reject(() => Checks.Equal(12.5, 12, "Unequal numeric values"));
            Checks.Tests(context.Samples[0].Report)[0]["retries"] = 1.0;
            Checks.Check(CheckerControls.Rule("history-requires-retry-count"), context);
        }),
        new("native/reporting/duplicate-completed-ids-rejected", () => CheckerControls.Reject(() => Runner.Report("unit", [new Completed { Id = "same", Status = "passed" }, new Completed { Id = "same", Status = "passed" }]))),
        new("native/reporting/skip-reason-retained", () =>
        {
            var report = Checks.Decode(Runner.Report("unit", [new Completed { Id = "control", Status = "skipped", Message = "Unsupported optional feature" }])); Checks.Equal(Checks.Tests(report)[0]["message"], "Unsupported optional feature", "Skip reason");
        }),
        new("native/reporting/level-identities-distinct", () =>
        {
            var records = new List<Completed> {new() { Id = "conformance/property/reportFormat", Status = "passed" }};
            var unit = Checks.Tests(Checks.Decode(Runner.Report("unit", records)))[0]; var integration = Checks.Tests(Checks.Decode(Runner.Report("integration", records)))[0]; Checks.Need(!JToken.DeepEquals(unit["testId"], integration["testId"]), "Levels share test identity");
        }),
        new("native/reporting/recommendations-retained", () =>
        {
            var record = new Completed { Id = "control", Status = "passed" }; record.Warnings.Add("SHOULD control"); var report = Checks.Decode(Runner.Report("unit", [record])); Checks.Equal(Checks.Tests(report)[0]["extra"]!["warnings"], new JArray("SHOULD control"), "Warnings retained");
        }),
        new("native/runner/failure-does-not-stop-later-tests", () =>
        {
            var ran = false; var completed = Runner.Execute([new TestCase("forced failure", () => throw new Exception("independent native negative control")), new TestCase("later", () => ran = true)], logFailures: false);
            Checks.Need(ran && completed[0].Status == "failed" && completed[1].Status == "passed", "Runner did not preserve failure and continue");
        })
    ];
}
