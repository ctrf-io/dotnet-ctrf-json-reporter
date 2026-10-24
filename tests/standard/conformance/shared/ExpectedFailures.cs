using Newtonsoft.Json.Linq;

namespace ReporterTests.Standard;

public sealed record Issue(string Key, string Reason, string FollowUp, Dictionary<string, string[]> Samples);
public static class ExpectedFailures
{
    public static bool Run(Entry entry, Context context, string level, Issue? issue,
        Action<Entry, Context> check, Func<Entry, Sample, Issue, Exception, bool> matches)
    {
        if (issue == null || !issue.Samples.TryGetValue(level, out var names)) { check(entry, context); return false; }
        var expected = names.ToHashSet(StringComparer.Ordinal);
        foreach (var name in expected) Checks.Need(context.Samples.Count(s => s.Name == name) == 1, "Expected-failure fixture missing or duplicated: " + name);
        foreach (var sample in context.Samples)
        {
            var parts = entry.Path?.Split('.');
            var hasParent = parts == null || parts.Length == 1 || Checks.Values(sample.Report, string.Join('.', parts[..^1])).Count > 0;
            if (entry.Kind == "property" && (!entry.Required || !hasParent) && !expected.Contains(sample.Name) && Checks.Values(sample.Report, entry.Path!).Count == 0) continue;
            Exception? failure = null;
            var one = context.With(sample);
            try { check(entry, one); } catch (Exception error) { failure = error; }
            context.Warnings.AddRange(one.Warnings);
            if (!expected.Contains(sample.Name)) { if (failure != null) throw failure; continue; }
            Checks.Need(failure != null, "Unexpected pass: " + entry.Id + " / " + sample.Name);
            if (!matches(entry, sample, issue, failure!)) throw failure!;
        }
        return true;
    }
}
