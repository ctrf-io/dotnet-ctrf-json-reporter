using Newtonsoft.Json.Linq;

namespace ReporterTests.Standard;

public static partial class Checks
{
    private static void CheckProvenanceIdentities(JObject report)
    {
        if (report["provenance"]?["inputs"] is not JArray inputs) return;
        var ids = inputs.Select(i => ((string)i["reportId"]!).ToLowerInvariant()).ToList();
        Need(ids.Distinct().Count() == ids.Count, "Duplicate provenance reportId");
        var self = ((string?)report["reportId"])?.ToLowerInvariant();
        Need(self == null || !ids.Contains(self), "Provenance references its own reportId");
    }
    // In the independent oracle, an explicit null records required omission.
    private static bool MatchSourceValue(JObject actual, JObject? expected, string key, string label)
    {
        if (expected == null || !expected.TryGetValue(key, out var value)) return false;
        if (value.Type == JTokenType.Null) { Need(!actual.ContainsKey(key), label + ": expected omission"); return false; }
        Equal(actual[key], value, label); return true;
    }
    private static void CheckAttemptEvidence(Context context, string key)
    {
        var witnessed = false;
        foreach (var sample in context.Samples)
        {
            var expected = sample.Expected["results"]?["tests"]?.Children<JObject>().ToList() ?? [];
            var tests = Tests(sample.Report);
            for (var index = 0; index < tests.Count; index++)
            {
                var test = tests[index]; var native = index < expected.Count ? expected[index] : null;
                var label = $"{sample.Name}: {test["name"]} {key}";
                witnessed = MatchSourceValue(test, native, key, label) || witnessed;
                if (native?["retryAttempts"] is not JArray attempts) continue;
                var history = History(test);
                for (var i = 0; i < attempts.Count; i++)
                {
                    if (attempts[i] is not JObject attempt || !attempt.ContainsKey(key)) continue;
                    Need(i < history.Count, label + ": missing history attempt");
                    witnessed = MatchSourceValue(history[i], attempt, key, $"{label} (attempt {i + 1})") || witnessed;
                }
            }
        }
        Need(witnessed, "Missing positive native attempt evidence for " + key);
    }
    private static bool NewRule(string rule, Context context)
    {
        if (rule == "assertion-failures-belong-to-attempt") { CheckAttemptEvidence(context, "assertionFailures"); return true; }
        if (rule == "thread-ids-belong-to-attempt") { CheckAttemptEvidence(context, "threadId"); return true; }
        if (rule is not ("provenance-identities-are-distinct" or "provenance-records-direct-sources" or "global-errors-belong-to-run" or "global-error-merge-preserves-inputs")) return false;
        var witnessed = false;
        foreach (var sample in context.Samples)
        {
            var report = sample.Report;
            switch (rule)
            {
                case "provenance-identities-are-distinct":
                    witnessed = report["provenance"] != null || witnessed; CheckProvenanceIdentities(report); break;
                case "provenance-records-direct-sources":
                    if (sample.DirectSources == null) continue;
                    Need(sample.DirectSources.Count > 0, "Missing direct source evidence");
                    var ids = sample.DirectSources.Select(s => ((string?)s["reportId"])?.ToLowerInvariant()).ToList();
                    if (ids.Contains(null)) { Need(!report.ContainsKey("provenance"), "Omit lineage when a source lacks an ID"); continue; }
                    Need(report["provenance"] != null, "Missing provenance for implemented scenario");
                    CheckProvenanceIdentities(report);
                    Equal(new JArray(report["provenance"]!["inputs"]!.Select(i => ((string)i["reportId"]!).ToLowerInvariant()).Order(StringComparer.Ordinal)), new JArray(ids.Distinct().Order(StringComparer.Ordinal)), "Record all and only direct source IDs");
                    if (string.IsNullOrEmpty((string?)report["reportId"])) context.Warnings.Add("A derived report should have a new reportId (SHOULD, Core §20.1)");
                    witnessed = true; break;
                case "global-errors-belong-to-run":
                    var native = sample.Expected["results"] as JObject;
                    witnessed = MatchSourceValue(Results(report), native, "errors", sample.Name + ": run error mapping") || witnessed;
                    if (Results(report)["errors"] != null && native?.ContainsKey("errors") == true)
                    {
                        Need(native["tests"] is JArray, "Missing independent test case evidence");
                        Equal(new JArray(Tests(report).Select(t => t["name"])), new JArray(native["tests"]!.Select(t => t["name"])), "Global errors must not become test results");
                    }
                    break;
                case "global-error-merge-preserves-inputs":
                    if (sample.DirectSources == null || sample.DirectSources.Count < 2) continue;
                    var errors = sample.DirectSources.SelectMany(s => s["results"]?["errors"] as JArray ?? []).ToList();
                    if (errors.Count == 0) continue;
                    witnessed = true;
                    var extra = (Results(report)["errors"] as JArray ?? []).ToList(); var missing = false;
                    foreach (var error in errors)
                    {
                        var index = extra.FindIndex(candidate => Equivalent(candidate, error));
                        if (index < 0) missing = true; else extra.RemoveAt(index);
                    }
                    if (missing || extra.Count > 0) context.Warnings.Add("Merge should retain all input errors without deduplicating (SHOULD, Core §6.5)");
                    break;
            }
        }
        Need(witnessed, "Missing positive evidence for " + rule); return true;
    }
}
