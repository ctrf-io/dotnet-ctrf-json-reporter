using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using ReporterTests.Native;

namespace ReporterTests.Standard;

public static class CheckerControls
{
    public static Entry Property(string path) => Checks.Entries.Single(e => e.Path == path);
    public static Entry Rule(string rule) => Checks.Entries.Single(e => e.Rule == rule);
    public static Context Valid()
    {
        var report = JObject.Parse("""{"reportFormat":"CTRF","specVersion":"0.2.0","results":{"tool":{"name":"independent-control"},"summary":{"tests":1,"passed":1,"failed":0,"pending":0,"skipped":0,"other":0,"start":100,"stop":120},"tests":[{"name":"case","status":"passed","duration":12,"retries":1,"flaky":true,"attemptId":"final","retryAttempts":[{"attempt":1,"attemptId":"first","status":"failed"}]}]}}""");
        var expected = JObject.Parse("""{"results":{"summary":{"tests":1},"tests":[{"duration":12,"sourceAttempts":[{"status":"failed"},{"status":"passed","duration":12}]}]}}""");
        return new Context { Samples = [new Sample("valid", Encoding.UTF8.GetBytes(report.ToString()), report, expected)] };
    }
    public static void Reject(Action action)
    {
        var rejected = false; try { action(); } catch { rejected = true; } Checks.Need(rejected, "Mutation unexpectedly accepted");
    }
    public static List<TestCase> Cases() => Checks.CheckerIds.Select(id => new TestCase(id, () => Run(id))).ToList();
    private static void Run(string id)
    {
        if (id.StartsWith("checker/expected-failure/", StringComparison.Ordinal)) { Defect(id.Split('/')[^1]); return; }
        var context = Valid(); var sample = context.Samples[0]; var test = Checks.Tests(sample.Report)[0]; var history = Checks.History(test)[0];
        void CheckRule(string name) => Checks.Check(Rule(name), context);
        void CheckProperty(string path) => Checks.Check(Property(path), context);
        switch (id)
        {
            case "checker/schema/catalogue-covers-expanded-properties-and-relationships":
                var paths = new List<string>();
                void Walk(JObject input, string prefix)
                {
                    var node = Checks.Resolve(input);
                    if (node["properties"] is JObject properties) foreach (var property in properties.Properties()) { var path = prefix.Length == 0 ? property.Name : prefix + "." + property.Name; paths.Add(path); Walk((JObject)property.Value, path); }
                    if (node["items"] is JObject items) Walk(items, prefix + "[]");
                }
                Walk(Checks.Schema, ""); Checks.Equal(Checks.Entries.Where(e => e.Kind == "property").Select(e => e.Path).ToArray(), paths.ToArray(), "Expanded catalogue");
                Checks.Equal(Checks.Entries.Where(e => e.Kind == "report").Select(e => e.Rule).Order().ToArray(), Checks.Rules.Order().ToArray(), "Rule inventory");
                Checks.Equal(Checks.Entries.Select(e => e.Id).Distinct().Count(), Checks.Entries.Count, "Unique IDs"); break;
            case "checker/schema/accepts-independent-valid-document": CheckRule("single-json-object"); break;
            case "checker/schema/rejects-unknown-property": sample.Report["unknown"] = true; InvalidDocument(); break;
            case "checker/schema/rejects-wrong-type": test["duration"] = "12"; InvalidDocument(); break;
            case "checker/schema/rejects-missing-required-field": test.Remove("status"); InvalidDocument(); break;
            case "checker/schema/requires-positive-evidence": Reject(() => CheckProperty("results.tests[].screenshot")); break;
            case "checker/schema/required-child-is-checked-when-parent-exists": test.Remove("status"); Reject(() => CheckProperty("results.tests[].status")); break;
            case "checker/schema/capabilities-require-entry-and-reason": Reject(() => Checks.Disposition(Property("reportFormat"), new Dictionary<string, Capability>())); Reject(() => Checks.Disposition(Property("reportFormat"), new Dictionary<string, Capability> { [Property("reportFormat").Id] = new Capability(true, "") })); break;
            case "checker/schema/unsupported-declaration-cannot-hide-emitted-invalid-field": test["extra"] = "invalid"; InvalidDocument(); break;
            case "checker/specification/accepts-independent-retry-history": foreach (var rule in new[] {"history-requires-retry-count", "positive-retries-require-history", "retry-count-matches-history-length", "history-orders-contiguous-attempts", "history-excludes-final-attempt", "flaky-matches-attempt-outcomes"}) CheckRule(rule); break;
            case "checker/specification/rejects-source-duration-unit-mismatch": test["duration"] = 12000; Reject(() => CheckProperty("results.tests[].duration")); break;
            case "checker/specification/missing-contextual-evidence-fails": foreach (var rule in new[] {"case-identity-stable-across-runs", "coordinates-run-and-shard-identity"}) Reject(() => CheckRule(rule)); break;
            case "checker/specification/timing-recommendation-warns": Checks.Summary(sample.Report)["stop"] = 50; CheckRule("orders-start-and-stop"); Checks.Equal(context.Warnings.Count, 1, "Warning count"); break;
            case "checker/specification/total-count-recommendation-warns-but-bounds-fail": Checks.Summary(sample.Report)["tests"] = 2; CheckProperty("results.summary.tests"); CheckRule("summary-matches-final-results"); Checks.Equal(context.Warnings.Count, 1, "Warning count"); Checks.Summary(sample.Report)["tests"] = -1; Reject(() => CheckProperty("results.summary.tests")); break;
            case "checker/schema/shared-snapshot-matches-declared-files":
                var root = Path.Combine(Contract.Root, "tests/standard"); var manifest = JObject.Parse(File.ReadAllText(Path.Combine(root, "shared-files.json")));
                Checks.Equal(manifest["revision"], "standard-checker-controls-v1", "Source revision"); Checks.Need(manifest["files"]!.Count() >= 5, "Missing native snapshot");
                foreach (var file in ((JObject)manifest["files"]!).Properties()) Checks.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, file.Name)))).ToLowerInvariant(), (string)file.Value!, "Shared file drift: " + file.Name); break;
            default:
                var name = id.Replace("checker/specification/rejects-", "", StringComparison.Ordinal).Replace("-mutation", "", StringComparison.Ordinal);
                switch (name)
                {
                    case "utf8-without-bom": context.Samples[0] = sample with { Bytes = [239,187,191,123,125] }; break;
                    case "summary-matches-final-results": Checks.Summary(sample.Report)["passed"] = 2; break;
                    case "history-orders-contiguous-attempts": history["attempt"] = 2; break;
                    case "retry-count-matches-history-length": test["retries"] = 2; break;
                    case "history-excludes-final-attempt": ((JArray)test["retryAttempts"]!).Add(new JObject { ["attempt"] = 2, ["status"] = "passed" }); break;
                    case "flaky-matches-attempt-outcomes": test["flaky"] = false; break;
                    case "attempt-identity-scoped-to-execution": test["attemptId"] = "first"; break;
                    case "respects-reserved-extension-ownership": test["extra"] = new JObject { ["ctrf.not-owned"] = true }; break;
                    default: throw new Exception("Unimplemented shared control: " + id);
                }
                Reject(() => Checks.Rule(name, context)); break;
        }
        void InvalidDocument() { context.Samples[0] = sample with { Bytes = Encoding.UTF8.GetBytes(sample.Report.ToString()) }; Reject(() => CheckRule("single-json-object")); }
    }
    private static void Defect(string name)
    {
        Sample Broken(string label) => new(label, [], JObject.Parse("""{"results":{"environment":{"buildNumber":1.5}}}"""), new JObject());
        var context = new Context { Samples = [Broken("first"), Broken("second")] };
        var issue = new Issue("control-defect", "Synthetic checker control", "https://example.test/control", new() { ["unit"] = ["first", "second"] });
        switch (name)
        {
            case "rejects-unexpected-pass": foreach (var sample in context.Samples) sample.Report["results"]!["environment"]!["buildNumber"] = 0; break;
            case "rejects-partial-fix": context.Samples[1].Report["results"]!["environment"]!["buildNumber"] = 0; break;
            case "rejects-different-value": context.Samples[0].Report["results"]!["environment"]!["buildNumber"] = "wrong"; break;
            case "rejects-missing-sample": context.Samples.RemoveAt(1); break;
            case "rejects-duplicate-sample": context.Samples.Add(context.Samples[0]); break;
            case "rejects-unrecorded-broken-sample": context.Samples.Add(Broken("third")); break;
            case "allows-optional-absence-outside-defect-samples": context.Samples.Add(new Sample("absent", [], new JObject { ["results"] = new JObject() }, new JObject())); break;
        }
        void Check(Entry _, Context current)
        {
            if (name == "rejects-infrastructure-error") throw new Exception("infrastructure");
            foreach (var sample in current.Samples) { if (name == "rejects-unrelated-error-in-later-sample" && sample.Name == "second") throw new Exception("different error"); Checks.Equal(sample.Report["results"]!["environment"]?["buildNumber"], 0, "control mismatch"); }
        }
        bool Matches(Entry _, Sample __, Issue ___, Exception error) => error is Mismatch mismatch && mismatch.Code == "control mismatch" && JToken.DeepEquals(mismatch.Actual as JToken, new JValue(1.5)) && Equals(mismatch.Expected, 0);
        bool Run() => ExpectedFailures.Run(Property("results.environment.buildNumber"), context, name == "does-not-apply-at-unrecorded-level" ? "integration" : "unit", issue, Check, Matches);
        if (name is "accepts-only-recorded-mismatch" or "allows-optional-absence-outside-defect-samples") Checks.Need(Run(), "Missing expected-failure record");
        else Reject(() => Run());
    }
}
