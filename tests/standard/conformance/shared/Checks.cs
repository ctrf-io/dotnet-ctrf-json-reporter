using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Json.Schema;
using Newtonsoft.Json.Linq;

namespace ReporterTests.Standard;

public sealed record Entry(string Id, string Kind, string? Path, string? Rule, bool Required);
public sealed record Capability(bool Supported, string Reason);
public sealed record Sample(string Name, byte[] Bytes, JObject Report, JObject Expected, long[]? RunWindow = null);
public sealed class Context
{
    public List<Sample> Samples { get; init; } = [];
    public List<Sample> Repeated { get; init; } = [];
    public List<Sample> Shards { get; init; } = [];
    public List<string> Warnings { get; } = [];
    public HashSet<string> CtrfDefinedExtensions { get; init; } = [];
    public Context With(Sample sample) => new() { Samples = [sample], Repeated = Repeated, Shards = Shards, CtrfDefinedExtensions = CtrfDefinedExtensions };
}
public sealed class Mismatch(string code, object? actual, object? expected) : Exception($"{code}: got {actual ?? "<absent>"}, want {expected ?? "<absent>"}")
{
    public string Code { get; } = code;
    public object? Actual { get; } = actual;
    public object? Expected { get; } = expected;
}
public static class Checks
{
    public static byte[] Resource(string suffix)
    {
        var assembly = typeof(Checks).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal)))!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); return buffer.ToArray();
    }
    public static readonly JObject Schema = JObject.Parse(Encoding.UTF8.GetString(Resource("ctrf.schema.json")));
    public static readonly JObject Catalogue = JObject.Parse(Encoding.UTF8.GetString(Resource("catalogue.json")));
    public static readonly List<Entry> Entries = Catalogue["entries"]!.Select(x => new Entry((string)x["id"]!, (string)x["kind"]!, (string?)x["path"], (string?)x["rule"], (bool?)x["required"] ?? false)).ToList();
    public static readonly string[] CheckerIds = JArray.Parse(Encoding.UTF8.GetString(Resource("checker-ids.json"))).Values<string>().Select(x => x!).ToArray();
    private static readonly JsonSchema Document = JsonSchema.FromText(Schema.ToString());
    private static readonly Dictionary<string, JsonSchema> Validators = [];
    private static readonly HashSet<string> Recommended = ["orders-start-and-stop", "case-identity-stable-across-runs", "execution-identity-distinguishes-executions", "attempt-identity-scoped-to-execution", "attachment-identity-scoped-to-parent", "document-identity-follows-emission", "coordinates-run-and-shard-identity"];
    public static readonly string[] Rules = ["utf8-without-bom", "single-json-object", "declares-supported-contract", "summary-matches-final-results", "summary-matches-flaky-results", "orders-start-and-stop", "history-requires-retry-count", "positive-retries-require-history", "zero-retries-omit-history", "retry-count-matches-history-length", "history-orders-contiguous-attempts", "history-excludes-final-attempt", "flaky-matches-attempt-outcomes", "case-identity-stable-across-runs", "execution-identity-distinguishes-executions", "attempt-identity-scoped-to-execution", "attachment-identity-scoped-to-parent", "document-identity-follows-emission", "coordinates-run-and-shard-identity", "respects-reserved-extension-ownership"];
    public static void Need(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    public static void Equal(object? actual, object? expected, string code)
    {
        var a = actual as JToken ?? (actual == null ? null : JToken.FromObject(actual));
        var b = expected as JToken ?? (expected == null ? null : JToken.FromObject(expected));
        if (!Equivalent(a, b)) throw new Mismatch(code, actual, expected);
    }
    private static bool Equivalent(JToken? a, JToken? b)
    {
        if (a == null || b == null) return a == null && b == null;
        if (a.Type is JTokenType.Integer or JTokenType.Float && b.Type is JTokenType.Integer or JTokenType.Float) return a.Value<double>().Equals(b.Value<double>());
        if (a is JArray leftArray && b is JArray rightArray) return leftArray.Count == rightArray.Count && leftArray.Zip(rightArray).All(pair => Equivalent(pair.First, pair.Second));
        if (a is JObject leftObject && b is JObject rightObject) return leftObject.Count == rightObject.Count && leftObject.Properties().All(p => rightObject.ContainsKey(p.Name) && Equivalent(p.Value, rightObject[p.Name]));
        return JToken.DeepEquals(a, b);
    }
    public static List<JToken> Values(JToken? value, string path)
    {
        var values = value == null ? new List<JToken>() : [value];
        foreach (var part in path.Split('.'))
        {
            var key = part.Replace("[]", "", StringComparison.Ordinal); var next = new List<JToken>();
            foreach (var parent in values.OfType<JObject>())
            {
                if (!parent.TryGetValue(key, out var child)) continue;
                if (part.EndsWith("[]", StringComparison.Ordinal)) { if (child is JArray array) next.AddRange(array); }
                else next.Add(child);
            }
            values = next;
        }
        return values;
    }
    public static JObject Results(JObject report) => (JObject)report["results"]!;
    public static JObject Summary(JObject report) => (JObject)Results(report)["summary"]!;
    public static List<JObject> Tests(JObject report) => Results(report)["tests"]!.Children<JObject>().ToList();
    public static List<JObject> History(JObject test) => test["retryAttempts"]?.Children<JObject>().ToList() ?? [];
    public static double Number(JToken? value) => value == null ? 0 : (double)value;
    public static JObject Decode(byte[] bytes)
    {
        // Parse disallows multiple top-level JSON values; strict UTF-8 is checked separately.
        var node = JsonNode.Parse(bytes); Need(node is JsonObject, "Expected one JSON object");
        using var reader = new Newtonsoft.Json.JsonTextReader(new StringReader(Encoding.UTF8.GetString(bytes))) { DateParseHandling = Newtonsoft.Json.DateParseHandling.None };
        return JObject.Load(reader);
    }
    public static void Validate(byte[] bytes) => ValidateSchema(Document, Decode(bytes));
    private static void ValidateSchema(JsonSchema schema, JToken value)
    {
        var result = schema.Evaluate(JsonNode.Parse(value.ToString(Newtonsoft.Json.Formatting.None)), new EvaluationOptions { OutputFormat = OutputFormat.List, RequireFormatValidation = true });
        Need(result.IsValid, "Schema validation failed: " + System.Text.Json.JsonSerializer.Serialize(result));
    }
    public static JObject Resolve(JObject node)
    {
        if (node["$ref"] is not JValue reference) return node;
        JToken current = Schema;
        foreach (var key in ((string)reference!).Replace("#/", "", StringComparison.Ordinal).Split('/')) current = current[key]!;
        return (JObject)current;
    }
    private static JsonSchema Local(string path)
    {
        if (Validators.TryGetValue(path, out var cached)) return cached;
        var node = Schema;
        foreach (var part in path.Split('.'))
        {
            node = (JObject)Resolve(node)["properties"]![part.Replace("[]", "", StringComparison.Ordinal)]!;
            if (part.EndsWith("[]", StringComparison.Ordinal)) node = (JObject)Resolve(node)["items"]!;
        }
        var local = (JObject)Resolve(node).DeepClone();
        if (local["properties"] is JObject properties) foreach (var property in properties.Properties().ToList()) property.Value = new JObject();
        if (local["items"]?["properties"] is JObject) local["items"] = new JObject { ["type"] = "object" };
        var validator = JsonSchema.FromText(local.ToString()); Validators[path] = validator; return validator;
    }
    private static bool Primitive(JToken token) => token is JValue || token is JArray a && a.All(v => v is JValue);
    public static void Property(Entry entry, Context context)
    {
        var path = entry.Path!; var validator = Local(path); var witnessed = 0;
        foreach (var sample in context.Samples)
        {
            var actual = Values(sample.Report, path); witnessed += actual.Count;
            var parts = path.Split('.'); var parents = parts.Length == 1 ? new List<JToken> { sample.Report } : Values(sample.Report, string.Join('.', parts[..^1]));
            if (entry.Required) foreach (var parent in parents) Need(parent is JObject o && o.ContainsKey(parts[^1]), $"{sample.Name}: missing {path}");
            foreach (var value in actual)
            {
                if (Regex.IsMatch(path, @"^results\.summary\.(tests|passed|failed|skipped|pending|other|flaky|suites|duration)$") || path.EndsWith(".duration", StringComparison.Ordinal) || path.EndsWith(".retries", StringComparison.Ordinal)) Need(Number(value) >= 0, $"{path} must be nonnegative");
                ValidateSchema(validator, value);
                if (path.EndsWith(".suite", StringComparison.Ordinal)) Need(value.Any(), "Empty suite");
                if (path.EndsWith(".screenshot", StringComparison.Ordinal))
                {
                    var b = Convert.FromBase64String((string)value!);
                    Need(b.AsSpan().StartsWith(new byte[] {137,80,78,71,13,10,26,10}) || b.AsSpan().StartsWith(new byte[] {255,216,255}), "Screenshot must contain PNG or JPEG bytes");
                }
                if (sample.RunWindow != null && path is "timestamp" or "results.summary.start" or "results.summary.stop")
                {
                    var milliseconds = path == "timestamp" ? DateTimeOffset.Parse((string)value!, System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeMilliseconds() : Number(value);
                    if (milliseconds == 0 && Tests(sample.Report).Count == 0) continue;
                    Need(milliseconds >= sample.RunWindow[0] && milliseconds <= sample.RunWindow[1], $"{path} outside independently observed window");
                }
            }
            var expected = Values(sample.Expected, path);
            if (path != "results.summary.tests" && expected.Count > 0 && expected.All(Primitive)) Equal(new JArray(actual), new JArray(expected), "mapping:" + path);
        }
        Need(witnessed > 0, "No positive evidence for " + path);
    }
    public static void Check(Entry entry, Context context)
    {
        Need(context.Samples.Count > 0, "Missing samples");
        if (entry.Kind == "property") { Property(entry, context); return; }
        if (entry.Rule == "case-identity-stable-across-runs") Need(context.Repeated.Count >= 2, "Missing repeated-run evidence");
        if (entry.Rule == "coordinates-run-and-shard-identity") Need(context.Shards.Count >= 2, "Missing shard evidence");
        Need(Rules.Contains(entry.Rule), "Unimplemented rule: " + entry.Rule);
        try { Rule(entry.Rule!, context); }
        catch (Exception error) when (Recommended.Contains(entry.Rule!)) { context.Warnings.Add($"{entry.Id} (SHOULD): {error.Message}"); }
    }
    private static JArray Pair(JToken? first, JToken? second) => new(first?.DeepClone() ?? JValue.CreateNull(), second?.DeepClone() ?? JValue.CreateNull());
    private static void Unique(IEnumerable<JToken?> values, string code)
    {
        var present = values.Where(v => v != null).Select(v => v!.ToString()).ToList();
        Equal(present.Distinct().Count(), present.Count, code);
    }
    public static void Rule(string rule, Context context)
    {
        Need(Rules.Contains(rule), "Unimplemented rule: " + rule);
        if (rule == "case-identity-stable-across-runs")
        {
            Need(context.Repeated.Count >= 2, "Missing independent repeated runs");
            JArray Keys(Sample s) => new(Tests(s.Report).Select(t => Pair(t["name"], t["testId"])));
            foreach (var sample in context.Repeated.Skip(1)) Equal(Keys(sample), Keys(context.Repeated[0]), rule); return;
        }
        if (rule == "coordinates-run-and-shard-identity")
        {
            Need(context.Shards.Count >= 2, "Missing coordinated shards");
            foreach (var sample in context.Shards) Equal(sample.Report["runId"], context.Shards[0].Report["runId"], rule);
            Unique(context.Shards.Select(s => Results(s.Report)["environment"]!["shardId"]), rule); return;
        }
        var witness = false; var executionIds = new List<JToken?>(); var documentIds = new List<JToken?>();
        foreach (var sample in context.Samples)
        {
            var tests = Tests(sample.Report); var summary = Summary(sample.Report);
            switch (rule)
            {
                case "utf8-without-bom": Need(!sample.Bytes.AsSpan().StartsWith(new byte[] {239,187,191}), "UTF-8 BOM"); _ = new UTF8Encoding(false, true).GetString(sample.Bytes); break;
                case "single-json-object": Validate(sample.Bytes); break;
                case "declares-supported-contract": Equal(sample.Report["reportFormat"], "CTRF", rule); Equal(sample.Report["specVersion"], Catalogue["specVersion"], rule); break;
                case "summary-matches-final-results":
                    if (Number(summary["tests"]) != tests.Count) context.Warnings.Add("summary.tests differs from test array length (SHOULD, Core §8.1)");
                    foreach (var status in new[] {"passed", "failed", "pending", "skipped", "other"}) { Equal(summary[status], tests.Count(t => (string?)t["status"] == status), "summary:" + status); Need(Number(summary[status]) >= 0, "Negative counter"); } break;
                case "summary-matches-flaky-results": if (summary["flaky"] != null) { witness = true; Equal(summary["flaky"], tests.Count(t => (bool?)t["flaky"] == true), rule); } break;
                case "orders-start-and-stop":
                    foreach (var value in new[] {summary}.Concat(tests).Concat(tests.SelectMany(History))) if (value["start"] != null && value["stop"] != null && Number(value["stop"]) < Number(value["start"])) context.Warnings.Add("stop precedes start (SHOULD)"); break;
                case "document-identity-follows-emission": documentIds.Add(sample.Report["reportId"]); break;
            }
            var expected = sample.Expected["results"]?["tests"]?.Children<JObject>().ToList() ?? [];
            for (var index = 0; index < tests.Count; index++)
            {
                var test = tests[index]; var history = History(test); var source = index < expected.Count ? expected[index] : new JObject();
                switch (rule)
                {
                    case "history-requires-retry-count": if (test["retryAttempts"] != null) { witness = true; Need(test["retries"]?.Type is JTokenType.Integer or JTokenType.Float && double.IsInteger(Number(test["retries"])), "History requires integer retries"); } break;
                    case "positive-retries-require-history": if (Number(test["retries"]) > 0) Need(history.Count > 0, "Positive retries require history"); break;
                    case "zero-retries-omit-history": if (test["retries"] != null && Number(test["retries"]) == 0) Need(test["retryAttempts"] == null, "Zero retries must omit history"); break;
                    case "retry-count-matches-history-length": if (test["retryAttempts"] != null) Equal(test["retries"], history.Count, rule); break;
                    case "history-orders-contiguous-attempts": if (test["retryAttempts"] != null) Equal(new JArray(history.Select(h => h["attempt"])), new JArray(Enumerable.Range(1, (int)Number(test["retries"]))), rule); break;
                    case "history-excludes-final-attempt":
                        if (test["retryAttempts"] != null && source["sourceAttempts"] is JArray attempts) { witness = true; Equal(history.Count, attempts.Count - 1, "history-length"); Equal(new JArray(history.Select(h => Pair(h["status"], h["duration"]))), new JArray(attempts.SkipLast(1).Select(a => Pair(a["status"], a["duration"]))), "history-values"); Equal(test["duration"], attempts.Last!["duration"], "final-duration"); } break;
                    case "flaky-matches-attempt-outcomes":
                        if (test["flaky"] != null) { var earlier = source["sourceAttempts"] is JArray flakyAttempts ? flakyAttempts.SkipLast(1) : history.Cast<JToken>(); Equal(test["flaky"], (string?)test["status"] == "passed" && earlier.Any(a => (string?)a["status"] == "failed"), rule); } break;
                    case "execution-identity-distinguishes-executions": if (test["executionId"] != null) executionIds.Add(test["executionId"]); break;
                    case "attempt-identity-scoped-to-execution": Unique(new[] {test["attemptId"]}.Concat(history.Select(h => h["attemptId"])), rule); break;
                    case "attachment-identity-scoped-to-parent": foreach (var parent in new[] {test}.Concat(history)) Unique(parent["attachments"]?.Select(a => a["attachmentId"]) ?? [], rule); break;
                }
            }
            if (rule == "respects-reserved-extension-ownership") foreach (var entry in Entries.Where(e => e.Kind == "property" && (e.Path == "extra" || e.Path!.EndsWith(".extra", StringComparison.Ordinal)))) foreach (var extra in Values(sample.Report, entry.Path!).OfType<JObject>()) foreach (var p in extra.Properties()) if (p.Name.StartsWith("ctrf.", StringComparison.Ordinal) || p.Name.StartsWith("ctrf/", StringComparison.Ordinal)) Need(context.CtrfDefinedExtensions.Contains(p.Name), "Reserved extension: " + p.Name);
        }
        if (rule is "history-requires-retry-count" or "history-excludes-final-attempt" or "summary-matches-flaky-results") Need(witness, "Missing positive relationship evidence");
        if (rule == "execution-identity-distinguishes-executions") { Need(executionIds.Count > 0, "Missing execution IDs"); Unique(executionIds, rule); }
        if (rule == "document-identity-follows-emission") Unique(documentIds, rule);
    }
    public static Capability Disposition(Entry entry, IReadOnlyDictionary<string, Capability> manifest)
    {
        Need(manifest.TryGetValue(entry.Id, out var capability) && !string.IsNullOrWhiteSpace(capability.Reason), "Capability requires entry and reason: " + entry.Id);
        return capability!;
    }
}
