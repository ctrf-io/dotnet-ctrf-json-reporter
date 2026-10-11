using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using System.Xml.Linq;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DotnetCtrfJsonReporter
{
    public class TestProcessor
    {
        public List<TestModel> ProcessTests(XDocument trxDocument)
        {
            var testResults = trxDocument.Descendants().Where(d => d.Name.LocalName == "UnitTestResult");
            var testDefinitions = trxDocument.Descendants().Where(d => d.Name.LocalName == "UnitTest");
            var testModels = new List<TestModel>();

            foreach (var testResult in testResults)
            {
                var testId = testResult.Attribute("testId")?.Value;
                var testDefinition = testDefinitions.FirstOrDefault(td => td.Attribute("id")?.Value == testId);

                var testMethod = testDefinition?.Descendants().FirstOrDefault(d => d.Name.LocalName == "TestMethod");
                var name = new[] {
                    testResult.Attribute("testName")?.Value,
                    testDefinition?.Attribute("name")?.Value,
                    testMethod?.Attribute("name")?.Value,
                    testId
                }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (name == null)
                    throw new FormatException("TRX test result has no test name, definition name or testId.");

                // Assembly qualifiers include volatile version metadata and are not part of the test class identity.
                var className = NormalizeClassName(testMethod?.Attribute("className")?.Value ?? "");
                var identityTuple = new[] { className, testMethod?.Attribute("name")?.Value ?? "", Path.GetFileName((testMethod?.Attribute("codeBase")?.Value ?? "").Replace('\\', '/')), name };
                var logicalIdentity = "ctrf:dotnet:v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(identityTuple)))).ToLowerInvariant();
                var testModel = new TestModel
                {
                    TestId = logicalIdentity,
                    ExecutionId = testResult.Attribute("executionId")?.Value is string sourceExecution && !string.IsNullOrWhiteSpace(sourceExecution) ? sourceExecution : Guid.NewGuid().ToString(),
                    Name = name,
                    Status = StatusMapper.MapToCtrfStatus(testResult.Attribute("outcome")?.Value ?? string.Empty),
                    RawStatus = testResult.Attribute("outcome")?.Value,
                    Stdout = testResult.Descendants().FirstOrDefault(d => d.Name.LocalName == "StdOut")?.Value.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None)
                };

                testModel.Start = ParseEpochMilliseconds(testResult.Attribute("startTime")?.Value);
                testModel.Stop = ParseEpochMilliseconds(testResult.Attribute("endTime")?.Value);
                if (testModel.Start.HasValue && testModel.Stop.HasValue && testModel.Stop < testModel.Start)
                    throw new FormatException($"TRX test '{name}' ends before it starts.");

                var durationString = testResult.Attribute("duration")?.Value;
                testModel.Duration = string.IsNullOrWhiteSpace(durationString)
                    ? (testModel.Start.HasValue && testModel.Stop.HasValue ? testModel.Stop.Value - testModel.Start.Value : 0)
                    : ParseTotalMilliseconds(durationString);

                if (!string.IsNullOrWhiteSpace(className))
                {
                    // Ignore the assembly qualifier; namespace components precede the class.
                    var hierarchy = className.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (hierarchy.Length > 0)
                        testModel.Suite = hierarchy;
                }

                // TRX codeBase normally names the compiled assembly, not the source file.
                var codeBase = testMethod?.Attribute("codeBase")?.Value;
                if (codeBase != null && new[] { ".cs", ".vb", ".fs", ".fsx" }.Contains(Path.GetExtension(codeBase), StringComparer.OrdinalIgnoreCase))
                    testModel.FilePath = codeBase;

                // Extract error information for failed tests
                var errorInfo = testResult.Descendants().FirstOrDefault(d => d.Name.LocalName == "ErrorInfo");
                if (errorInfo != null)
                {
                    var messageElement = errorInfo.Descendants().FirstOrDefault(d => d.Name.LocalName == "Message");
                    var stackTraceElement = errorInfo.Descendants().FirstOrDefault(d => d.Name.LocalName == "StackTrace");

                    testModel.Message = messageElement?.Value;
                    testModel.Trace = stackTraceElement?.Value;

                    // Extract line number and file path from stack trace
                    if (!string.IsNullOrEmpty(testModel.Trace))
                    {
                        var lineInfo = ExtractLineAndFileFromStackTrace(testModel.Trace);
                        if (lineInfo.Line.HasValue)
                        {
                            testModel.Line = lineInfo.Line;
                        }
                        if (!string.IsNullOrEmpty(lineInfo.FilePath))
                        {
                            // Prefer source file path from stack trace over DLL path
                            testModel.FilePath = lineInfo.FilePath;
                        }
                    }
                }

                testModels.Add(testModel);
            }

            return testModels;
        }

        private (int? Line, string? FilePath) ExtractLineAndFileFromStackTrace(string stackTrace)
        {
            // Pattern to match: "at MethodName() in /path/to/file.cs:line 42"
            var pattern = @"at .+ in (.+):line (\d+)";
            var match = Regex.Match(stackTrace, pattern);

            if (match.Success)
            {
                var filePath = match.Groups[1].Value;
                if (int.TryParse(match.Groups[2].Value, out int lineNumber))
                {
                    return (lineNumber, filePath);
                }
                return (null, filePath);
            }

            return (null, null);
        }

        private static string NormalizeClassName(string value)
        {
            // A qualifier starts at a comma outside brackets. Generic arguments can
            // contain their own qualified types, so normalize those recursively.
            var typeName = SplitTypeComponents(value).First().Trim();
            var normalized = new StringBuilder();
            for (var i = 0; i < typeName.Length; i++)
            {
                if (typeName[i] == '\\' && i + 1 < typeName.Length)
                {
                    normalized.Append(typeName[i]).Append(typeName[++i]);
                    continue;
                }
                if (typeName[i] != '[' || !Regex.IsMatch(typeName[..i], @"`\d+$"))
                {
                    normalized.Append(typeName[i]);
                    continue;
                }

                var end = i + 1;
                var depth = 1;
                for (; end < typeName.Length; end++)
                {
                    if (typeName[end] == '\\') { end++; continue; }
                    if (typeName[end] == '[') depth++;
                    if (typeName[end] == ']' && --depth == 0) break;
                }
                if (end == typeName.Length)
                {
                    normalized.Append(typeName[i..]);
                    break;
                }

                var arguments = SplitTypeComponents(typeName[(i + 1)..end]).Select(argument =>
                {
                    var type = argument.Trim();
                    if (type.StartsWith('[') && type.EndsWith(']')) type = type[1..^1];
                    return NormalizeClassName(type);
                });
                normalized.Append('[').AppendJoin(",", arguments).Append(']');
                i = end;
            }
            return normalized.ToString();
        }

        private static IEnumerable<string> SplitTypeComponents(string value)
        {
            var depth = 0;
            var start = 0;
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\') { i++; continue; }
                if (value[i] == '[') depth++;
                if (value[i] == ']') depth--;
                if (value[i] == ',' && depth == 0)
                {
                    yield return value[start..i];
                    start = i + 1;
                }
            }
            yield return value[start..];
        }

        internal static long? ParseEpochMilliseconds(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dateTime))
                return null;
            return dateTime.ToUnixTimeMilliseconds();
        }

        private static long ParseTotalMilliseconds(string value)
        {
            if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var duration) || duration < TimeSpan.Zero)
                throw new FormatException($"Invalid TRX duration '{value}'. Expected a non-negative TimeSpan.");
            // CTRF uses integer milliseconds; discard sub-millisecond precision.
            return duration.Ticks / TimeSpan.TicksPerMillisecond;
        }
    }
}
