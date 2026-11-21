using System.Text;
using Newtonsoft.Json.Linq;

namespace ReporterTests.Standard;

public static partial class CheckerControls
{
    private const string SourceA = "abcdefab-e29b-41d4-a716-446655440001";
    private const string SourceB = "abcdefab-e29b-41d4-a716-446655440002";
    private const string Derived = "abcdefab-e29b-41d4-a716-446655440003";
    private static JObject Inputs(params string[] ids) => new() { ["inputs"] = new JArray(ids.Select(id => new JObject { ["reportId"] = id })) };
    private static void Run030(string id)
    {
        var context = Valid(); var sample = context.Samples[0]; var report = sample.Report;
        var test = Checks.Tests(report)[0]; var history = Checks.History(test)[0];
        void CheckRule(string name) => Checks.Check(Rule(name), context);
        void Validate() { context.Samples[0] = context.Samples[0] with { Bytes = Encoding.UTF8.GetBytes(report.ToString()) }; CheckRule("single-json-object"); }
        switch (id)
        {
            case "optional-fields-accept-omission-and-valid-emission":
                Validate(); CheckRule("declares-supported-contract");
                test["description"] = ""; test["threadId"] = "host-b/worker-2"; history["threadId"] = "host-a/worker-1";
                history["assertionFailures"] = new JArray(new JObject { ["expected"] = "", ["extra"] = new JObject() });
                Checks.Results(report)["errors"] = new JArray(new JObject { ["extra"] = new JObject() });
                report["provenance"] = Inputs(SourceA); report["provenance"]!["extra"] = new JObject(); Validate();
                foreach (var path in new[] {"results.tests[].description", "results.tests[].retryAttempts[].threadId", "results.tests[].retryAttempts[].assertionFailures[].expected", "results.errors[].extra", "provenance.inputs[].reportId"}) Checks.Check(Property(path), context);
                break;
            case "rejects-description-even-without-capability": test["description"] = JValue.CreateNull(); Reject(Validate); break;
            case "rejects-retry-thread-even-without-capability": history["threadId"] = 1; Reject(Validate); break;
            case "rejects-empty-assertions-even-without-capability": test["assertionFailures"] = new JArray(); Reject(Validate); break;
            case "rejects-unformatted-comparison-even-without-capability": history["assertionFailures"] = new JArray(new JObject { ["actual"] = new JObject { ["unformatted"] = true } }); Reject(Validate); break;
            case "rejects-empty-global-error-even-without-capability": Checks.Results(report)["errors"] = new JArray(new JObject()); Reject(Validate); break;
            case "rejects-missing-provenance-inputs-even-without-capability": report["provenance"] = new JObject { ["extra"] = new JObject() }; Reject(Validate); break;
            case "rejects-bad-source-uuid-even-without-capability": report["provenance"] = Inputs("not-a-uuid"); Reject(Validate); break;
            case "duplicate-identity-ignores-case-and-input-metadata":
                report["provenance"] = Inputs(SourceA, SourceA.ToUpperInvariant()); report["provenance"]!["inputs"]![1]!["extra"] = new JObject { ["shard"] = 2 }; Reject(Validate); break;
            case "no-self-reference-even-with-different-case": report["reportId"] = SourceA; report["provenance"] = Inputs(SourceA.ToUpperInvariant()); Reject(Validate); break;
            case "direct-lineage-excludes-flattened-ancestors-and-omits-unidentified-sources":
                report["reportId"] = Derived;
                context.Samples[0] = sample with { DirectSources = [new JObject { ["reportId"] = SourceA, ["provenance"] = Inputs("ancestor") }, new JObject { ["reportId"] = SourceB }] };
                report["provenance"] = Inputs(SourceB, SourceA.ToUpperInvariant());
                var unidentified = Valid().Samples[0] with { DirectSources = [new JObject { ["reportId"] = SourceA }, new JObject()] }; context.Samples.Add(unidentified);
                CheckRule("provenance-records-direct-sources"); report.Remove("reportId"); CheckRule("provenance-records-direct-sources");
                Checks.Need(context.Warnings.Last().Contains("SHOULD", StringComparison.Ordinal), "Missing recommendation warning");
                report["provenance"]!["inputs"]![0] = new JObject { ["reportId"] = "ancestor" }; Reject(() => CheckRule("provenance-records-direct-sources"));
                report["provenance"]!["inputs"]![0] = new JObject { ["reportId"] = SourceB }; unidentified.Report["provenance"] = Inputs(SourceA); Reject(() => CheckRule("provenance-records-direct-sources")); break;
            case "assertion-and-thread-mapping-uses-independent-attempt-evidence":
                Reject(() => CheckRule("assertion-failures-belong-to-attempt"));
                var native = (JObject)sample.Expected["results"]!["tests"]![0]!;
                native["assertionFailures"] = JValue.CreateNull(); native["threadId"] = "host-b/worker-2";
                native["retryAttempts"] = new JArray(new JObject { ["threadId"] = "host-a/worker-1", ["assertionFailures"] = new JArray(new JObject { ["message"] = "comparison", ["actual"] = "" }) });
                test["threadId"] = "host-b/worker-2"; history["threadId"] = "host-a/worker-1";
                history["assertionFailures"] = native["retryAttempts"]![0]!["assertionFailures"]!.DeepClone();
                CheckRule("assertion-failures-belong-to-attempt"); CheckRule("thread-ids-belong-to-attempt");
                history["threadId"] = (string)test["threadId"]!; Reject(() => CheckRule("thread-ids-belong-to-attempt"));
                var assertion = (JObject)history["assertionFailures"]![0]!; assertion["expected"] = "inferred"; Reject(() => CheckRule("assertion-failures-belong-to-attempt"));
                assertion.Remove("expected"); test["assertionFailures"] = history["assertionFailures"]!.DeepClone(); Reject(() => CheckRule("assertion-failures-belong-to-attempt")); break;
            case "global-errors-do-not-become-tests-even-if-counts-are-consistent":
                sample.Expected["results"]!["errors"] = new JArray(new JObject { ["message"] = "worker setup" }); sample.Expected["results"]!["tests"]![0]!["name"] = "case";
                Checks.Results(report)["errors"] = sample.Expected["results"]!["errors"]!.DeepClone(); CheckRule("global-errors-belong-to-run");
                ((JArray)Checks.Results(report)["tests"]!).Add(new JObject { ["name"] = "worker setup", ["status"] = "failed", ["duration"] = 0 });
                Checks.Summary(report)["tests"] = 2; Checks.Summary(report)["failed"] = 1; CheckRule("summary-matches-final-results"); Reject(() => CheckRule("global-errors-belong-to-run")); break;
            case "error-merge-multiplicity-is-advisory-but-evidence-is-required":
                Reject(() => CheckRule("global-error-merge-preserves-inputs"));
                var error = new JObject { ["message"] = "setup", ["extra"] = new JObject { ["retryable"] = true } };
                context.Samples[0] = sample with { DirectSources = [new JObject { ["results"] = new JObject() }, new JObject { ["results"] = new JObject { ["errors"] = new JArray(error.DeepClone(), error.DeepClone()) } }] };
                Checks.Results(report)["errors"] = new JArray(error.DeepClone(), error.DeepClone()); CheckRule("global-error-merge-preserves-inputs"); Checks.Equal(context.Warnings.Count, 0, "No merge warning");
                ((JArray)Checks.Results(report)["errors"]!).RemoveAt(1); CheckRule("global-error-merge-preserves-inputs"); Checks.Equal(context.Warnings.Count, 1, "Advisory merge warning"); break;
            case "core-accepts-opaque-extension-values-without-extension-validation":
                report["extra"] = new JObject { ["example/private"] = JValue.CreateNull(), ["ctrf.future"] = new JObject { ["extensionVersion"] = 42 } };
                test["extra"] = new JObject { ["ctrf.future"] = new JArray("opaque", false) }; Validate(); break;
            default: throw new Exception("Unimplemented 0.3.0 control: " + id);
        }
    }
}
