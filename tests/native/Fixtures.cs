using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotnetCtrfJsonReporter;
using Newtonsoft.Json.Linq;
using ReporterTests.Standard;

namespace ReporterTests.Native;

public static class Fixtures
{
    public static string Dotnet => Environment.GetEnvironmentVariable("DOTNET_COMMAND") ?? "dotnet";
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public sealed record ProcessResult(int Exit, string Stdout, string Stderr);
    public static ProcessResult Execute(string cwd, string command, IEnumerable<string> args, int expected = 0, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(command) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in args) start.ArgumentList.Add(argument);
        if (environment != null) foreach (var (key, value) in environment) start.Environment[key] = value;
        using var process = Process.Start(start) ?? throw new Exception("Cannot start " + command);
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(180000)) { process.Kill(true); throw new Exception("Process timed out: " + command); }
        Task.WaitAll(stdout, stderr);
        var result = new ProcessResult(process.ExitCode, stdout.Result, stderr.Result);
        Checks.Need(result.Exit == expected, $"{command} {string.Join(' ', args)}: exit {result.Exit}, expected {expected}\n{result.Stdout}\n{result.Stderr}");
        return result;
    }
    public static string Controlled(string name, string? run = null)
    {
        var root = new XElement("TestRun", new XElement("Times", new XAttribute("start", "2026-10-05T10:00:00Z"), new XAttribute("finish", "2026-10-05T10:00:00.123Z")),
            new XElement("Results", new XElement("UnitTestResult", new XAttribute("testId", "case"), new XAttribute("executionId", name + "-execution"), new XAttribute("testName", "Controlled"), new XAttribute("outcome", "Passed"), new XAttribute("duration", "00:00:00.1234567"), new XAttribute("startTime", "2026-10-05T10:00:00Z"), new XAttribute("endTime", "2026-10-05T10:00:00.123Z"))),
            new XElement("TestDefinitions", new XElement("UnitTest", new XAttribute("id", "case"), new XElement("TestMethod", new XAttribute("className", "Fixture.Parent.Cases, Fixture, Version=1.0.0.0"), new XAttribute("name", "Controlled"), new XAttribute("codeBase", "/src/Cases.cs")))));
        if (run != null) root.SetAttributeValue("id", run);
        return root.ToString();
    }
    // Independent source projection. Native fixture inventories/outcomes are checked
    // before projection; no expected value is read from CTRF output or subject APIs.
    public static JObject Observe(string xml, string tool = "dotnet", string? run = null, string? shard = null)
    {
        var doc = XDocument.Parse(xml); var cases = new JArray(); var times = new List<long>();
        long? Epoch(string? text) => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value) ? value.ToUnixTimeMilliseconds() : null;
        foreach (var result in doc.Descendants().Where(e => e.Name.LocalName == "UnitTestResult"))
        {
            string? Attr(XElement? e, string key) => (string?)e?.Attribute(key);
            var definition = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "UnitTest" && Attr(e, "id") == Attr(result, "testId"));
            var method = definition?.Descendants().FirstOrDefault(e => e.Name.LocalName == "TestMethod");
            var name = new[] { Attr(result, "testName"), Attr(definition, "name"), Attr(method, "name"), Attr(result, "testId") }.First(n => !string.IsNullOrWhiteSpace(n));
            var raw = Attr(result, "outcome"); var status = raw == "Passed" ? "passed" : new[] {"Failed", "Error", "Timeout", "Aborted"}.Contains(raw) ? "failed" : raw == "NotExecuted" ? "skipped" : raw == "Pending" ? "pending" : "other";
            var start = Epoch(Attr(result, "startTime")); var stop = Epoch(Attr(result, "endTime"));
            var duration = Attr(result, "duration");
            var milliseconds = string.IsNullOrWhiteSpace(duration) ? start.HasValue && stop.HasValue ? stop.Value - start.Value : 0 : (long)decimal.Truncate((decimal)TimeSpan.Parse(duration, CultureInfo.InvariantCulture).Ticks / TimeSpan.TicksPerMillisecond);
            var test = new JObject { ["name"] = name, ["status"] = status, ["duration"] = milliseconds };
            if (raw != null) test["rawStatus"] = raw;
            if (start != null) { test["start"] = start; times.Add(start.Value); }
            if (stop != null) { test["stop"] = stop; times.Add(stop.Value); }
            if (Attr(result, "executionId") is string execution) test["executionId"] = execution;
            var classname = Attr(method, "className")?.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(classname)) { var hierarchy = classname.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); if (hierarchy.Length > 0) test["suite"] = new JArray(hierarchy); }
            if (Attr(method, "codeBase") is string file && new[] {".cs", ".vb", ".fs", ".fsx"}.Contains(Path.GetExtension(file).ToLowerInvariant())) test["filePath"] = file;
            foreach (var (native, ctrf) in new[] {("Message", "message"), ("StackTrace", "trace")})
            {
                var error = result.Descendants().FirstOrDefault(e => e.Name.LocalName == "ErrorInfo")?.Descendants().FirstOrDefault(e => e.Name.LocalName == native);
                if (error != null) test[ctrf] = error.Value;
            }
            if (test["trace"] is JValue trace)
            {
                var match = Regex.Match((string)trace!, @" in (.+):line (\d+)");
                if (match.Success) { test["filePath"] = match.Groups[1].Value; test["line"] = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture); }
            }
            var stdout = result.Descendants().FirstOrDefault(e => e.Name.LocalName == "StdOut");
            if (stdout != null) test["stdout"] = new JArray(stdout.Value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'));
            cases.Add(test);
        }
        var runTimes = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Times");
        var summary = new JObject { ["tests"] = cases.Count, ["start"] = Epoch((string?)runTimes?.Attribute("start")) ?? (times.Count > 0 ? times.Min() : 0) };
        summary["stop"] = Epoch((string?)runTimes?.Attribute("finish")) ?? (times.Count > 0 ? times.Max() : (long)summary["start"]!);
        foreach (var status in new[] {"passed", "failed", "pending", "skipped", "other"}) summary[status] = cases.Count(t => (string?)t["status"] == status);
        var expected = new JObject { ["reportFormat"] = "CTRF", ["specVersion"] = "0.2.0", ["generatedBy"] = "dotnet-ctrf-json-reporter", ["results"] = new JObject { ["tool"] = new JObject { ["name"] = tool }, ["summary"] = summary, ["tests"] = cases } };
        var runId = run ?? (string?)doc.Root?.Attribute("id"); if (!string.IsNullOrWhiteSpace(runId)) expected["runId"] = runId;
        if (shard != null) Checks.Results(expected)["environment"] = new JObject { ["shardId"] = shard };
        return expected;
    }
    private static Sample Capture(string level, string name, string xml, byte[] bytes, string artifacts, JObject expected, long before, long after)
    {
        var report = Checks.Decode(bytes);
        var timestamp = DateTimeOffset.Parse((string)report["timestamp"]!, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
        Checks.Need(timestamp >= before && timestamp <= after, "Generated timestamp outside independent conversion window");
        var dir = Path.Combine(artifacts, "fixtures", level, name); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "source.trx"), xml); File.WriteAllBytes(Path.Combine(dir, "report.json"), bytes); File.WriteAllText(Path.Combine(dir, "source.json"), new JObject { ["expected"] = expected, ["generationWindow"] = new JArray(before, after) }.ToString());
        return new Sample(name, bytes, report, expected);
    }
    public static Context Unit(string artifacts)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "ctrf-dotnet-unit-" + Guid.NewGuid()); Directory.CreateDirectory(temporary);
        var context = new Context();
        try
        {
            var mixed = XDocument.Load(Path.Combine(Contract.Root, "tests/fixtures/results.trx")); var count = 0;
            foreach (var test in mixed.Descendants().Where(e => e.Name.LocalName == "UnitTestResult")) test.SetAttributeValue("executionId", "mixed-" + ++count);
            var scenarios = new[] { ("mixed", mixed.ToString(), (string?)null), ("repeat-a", Controlled("repeat-a"), null), ("repeat-b", Controlled("repeat-b"), null), ("shard-a", Controlled("shard-a", "coordinated"), "a"), ("shard-b", Controlled("shard-b", "coordinated"), "b"), ("empty", "<TestRun><Results/></TestRun>", null), ("fallback", "<TestRun><Results><UnitTestResult testId='fallback' startTime='2026-10-05T10:00:00Z' endTime='2026-10-05T10:00:01.234Z'/></Results></TestRun>", null) };
            foreach (var (name, xml, shard) in scenarios)
            {
                var path = Path.Combine(temporary, name + ".trx"); File.WriteAllText(path, xml); var before = Now - 1;
                var model = new TrxProcessor().ProcessTrxFile(path);
                if (shard != null) model.Results.Environment = new EnvironmentModel { ShardId = shard };
                var bytes = Encoding.UTF8.GetBytes(new DotnetCtrfJsonReporter.JsonConverter().ConvertToCrtfJson(model));
                var sample = Capture("unit", name, xml, bytes, artifacts, Observe(xml, shard: shard), before, Now + 1);
                context.Samples.Add(sample); if (name.StartsWith("repeat-", StringComparison.Ordinal)) context.Repeated.Add(sample); if (name.StartsWith("shard-", StringComparison.Ordinal)) context.Shards.Add(sample);
            }
            return context;
        }
        finally { Directory.Delete(temporary, true); }
    }
    private static void VerifyNative(string xml, string mode)
    {
        var observed = XDocument.Parse(xml).Descendants().Where(e => e.Name.LocalName == "UnitTestResult").ToList();
        var expectedCounts = mode == "mixed" ? new Dictionary<string, int> { ["Passing"] = 1, ["Failing"] = 1, ["Skipped"] = 1, ["Data"] = 2 } : new Dictionary<string, int> { [mode == "shard-b" ? "Skipped" : "Passing"] = 1 };
        var counts = new Dictionary<string, int>();
        foreach (var result in observed)
        {
            var name = (string?)result.Attribute("testName") ?? ""; var match = Regex.Match(name, @"(?:^|\.)(Passing|Failing|Skipped|Data)\b");
            Checks.Need(match.Success, "Unexpected native fixture name: " + name); var method = match.Groups[1].Value;
            counts[method] = counts.GetValueOrDefault(method) + 1;
            var outcome = method == "Failing" ? "Failed" : method == "Skipped" ? "NotExecuted" : "Passed";
            Checks.Equal((string?)result.Attribute("outcome"), outcome, "native-outcome:" + name);
        }
        Checks.Equal(JObject.FromObject(counts), JObject.FromObject(expectedCounts), "native-inventory");
    }
    public static Context Integration(string artifacts)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "ctrf-dotnet-installed-" + Guid.NewGuid()); Directory.CreateDirectory(temporary);
        var context = new Context();
        try
        {
            var packages = Path.Combine(temporary, "packages"); var tool = Path.Combine(temporary, "tool");
            Execute(Contract.Root, Dotnet, ["pack", "DotnetCtrfJsonReporter.csproj", "-c", "Release", "--no-restore", "-o", packages]);
            var config = Path.Combine(temporary, "tool-source.config"); File.WriteAllText(config, new XElement("configuration", new XElement("packageSources", new XElement("clear"), new XElement("add", new XAttribute("key", "local"), new XAttribute("value", packages)))).ToString());
            var package = Directory.GetFiles(packages, "*.nupkg").Single();
            string version;
            byte[] packagedBinary;
            using (var archive = ZipFile.OpenRead(package))
            {
                using var nuspec = archive.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
                version = XDocument.Load(nuspec).Descendants().Single(e => e.Name.LocalName == "version").Value;
                Checks.Need(!archive.Entries.Any(e => e.FullName.Contains("ConformanceHarness", StringComparison.Ordinal) || e.FullName.Contains("JsonSchema", StringComparison.Ordinal) || e.FullName.Contains("/tests/", StringComparison.Ordinal)), "Test tooling entered the public package");
                using var binary = archive.Entries.Single(e => e.FullName.EndsWith("/DotnetCtrfJsonReporter.dll", StringComparison.Ordinal)).Open();
                using var buffer = new MemoryStream(); binary.CopyTo(buffer); packagedBinary = buffer.ToArray();
            }
            Execute(temporary, Dotnet, ["tool", "install", "DotnetCtrfJsonReporter", "--version", version, "--tool-path", tool, "--configfile", config], environment: new Dictionary<string,string> { ["NUGET_PACKAGES"] = Path.Combine(temporary,"tool-package-cache") });
            var installedBinary = Directory.GetFiles(tool, "DotnetCtrfJsonReporter.dll", SearchOption.AllDirectories).Single();
            Checks.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installedBinary))), Convert.ToHexString(SHA256.HashData(packagedBinary)), "Installed tool matches freshly packed bytes");
            var command = Path.Combine(tool, OperatingSystem.IsWindows() ? "DotnetCtrfJsonReporter.exe" : "DotnetCtrfJsonReporter");
            foreach (var framework in new[] {"mstest", "nunit", "xunit"})
            {
                var consumer = Path.Combine(temporary, framework); Directory.CreateDirectory(consumer);
                foreach (var file in new[] {"Fixture.csproj", "Cases.cs"}) File.Copy(Path.Combine(Contract.Root, "tests/native/fixtures", framework, file), Path.Combine(consumer, file));
                foreach (var mode in new[] {"mixed", "repeat-a", "repeat-b", "shard-a", "shard-b"})
                {
                    var name = framework + "-" + mode; var results = Path.Combine(consumer, name); Directory.CreateDirectory(results);
                    var filter = mode == "mixed" ? "FullyQualifiedName~Cases" : mode == "shard-b" ? "FullyQualifiedName~Skipped" : "FullyQualifiedName~Passing";
                    var native = Execute(consumer, Dotnet, ["test", "Fixture.csproj", "-c", "Release", "--logger", "trx;LogFileName=source.trx", "--results-directory", results, "--filter", filter, "--nologo"], mode == "mixed" ? 1 : 0);
                    File.WriteAllText(Path.Combine(results, "native.log"), native.Stdout + native.Stderr);
                    var trx = Path.Combine(results, "source.trx"); var xml = File.ReadAllText(trx);
                    var retained = Path.Combine(artifacts, "fixtures/integration", name); Directory.CreateDirectory(retained);
                    File.WriteAllText(Path.Combine(retained, "native.log"), native.Stdout + native.Stderr); File.WriteAllText(Path.Combine(retained, "source.trx"), xml);
                    VerifyNative(xml, mode);
                    var output = Path.Combine(results, "report"); var before = Now - 1;
                    var arguments = new List<string> {"-p", trx, "-d", output, "-f", "report.json", "-t", framework};
                    string? shard = null; string? run = null;
                    if (mode.StartsWith("shard-", StringComparison.Ordinal)) { shard = framework + "-" + mode[^1]; run = "coordinated"; arguments.AddRange(["--run-id", run, "--shard-id", shard]); }
                    var conversion = Execute(consumer, command, arguments);
                    var bytes = File.ReadAllBytes(Path.Combine(output, "report.json"));
                    var sample = Capture("integration", name, xml, bytes, artifacts, Observe(xml, framework, run, shard), before, Now + 1);
                    File.WriteAllText(Path.Combine(retained, "converter.log"), conversion.Stdout + conversion.Stderr);
                    context.Samples.Add(sample); if (framework == "mstest" && mode.StartsWith("repeat-", StringComparison.Ordinal)) context.Repeated.Add(sample); if (mode.StartsWith("shard-", StringComparison.Ordinal)) context.Shards.Add(sample);
                }
            }
            // Real converter empty-input case complements native framework evidence.
            var empty = Path.Combine(temporary, "empty.trx"); File.WriteAllText(empty, "<TestRun><Results/></TestRun>");
            var start = Now - 1; Execute(temporary, command, ["-p", empty, "-d", temporary, "-f", "empty.json"]);
            context.Samples.Add(Capture("integration", "empty", File.ReadAllText(empty), File.ReadAllBytes(Path.Combine(temporary, "empty.json")), artifacts, Observe(File.ReadAllText(empty)), start, Now + 1));
            return context;
        }
        finally { Directory.Delete(temporary, true); }
    }
}
