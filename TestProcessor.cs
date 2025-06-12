using System.Xml.Linq;
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
                var durationString = testResult.Attribute("duration")?.Value ?? "00:00:00";
                var totalMilliseconds = ParseTotalMilliseconds(durationString);
                
                var testId = testResult.Attribute("testId")?.Value;
                var testDefinition = testDefinitions.FirstOrDefault(td => td.Attribute("id")?.Value == testId);

                var testModel = new TestModel
                {
                    Name = testResult.Attribute("testName")?.Value ?? string.Empty,
                    Status = StatusMapper.MapToCtrfStatus(testResult.Attribute("outcome")?.Value ?? string.Empty),
                    Duration = totalMilliseconds,
                    RawStatus = testResult.Attribute("outcome")?.Value
                };

                // Extract timing information
                if (DateTime.TryParse(testResult.Attribute("startTime")?.Value, out DateTime startTime))
                {
                    testModel.Start = ConvertToEpochMilliseconds(startTime);
                }
                
                if (DateTime.TryParse(testResult.Attribute("endTime")?.Value, out DateTime endTime))
                {
                    testModel.Stop = ConvertToEpochMilliseconds(endTime);
                }

                // Extract suite information from test definition
                if (testDefinition != null)
                {
                    var testMethod = testDefinition.Descendants().FirstOrDefault(d => d.Name.LocalName == "TestMethod");
                    var className = testMethod?.Attribute("className")?.Value;
                    if (!string.IsNullOrEmpty(className))
                    {
                        // Extract just the class name without namespace
                        var classNameParts = className.Split('.');
                        testModel.Suite = classNameParts.Length > 0 ? classNameParts.Last() : className;
                    }

                    // Extract file path from codeBase
                    var codeBase = testMethod?.Attribute("codeBase")?.Value;
                    if (!string.IsNullOrEmpty(codeBase))
                    {
                        testModel.FilePath = codeBase;
                    }
                }

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

        private long ConvertToEpochMilliseconds(DateTime dateTime)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(dateTime.ToUniversalTime() - epoch).TotalMilliseconds;
        }

        private int ParseTotalMilliseconds(string durationString)
        {
            var trimmedDuration = durationString.Length > 12 ? durationString.Substring(0, 12) : durationString;

            var parts = trimmedDuration.Split(':');
            var secondsAndMilliseconds = parts[2].Split('.');

            var hours = int.Parse(parts[0]);
            var minutes = int.Parse(parts[1]);
            var seconds = int.Parse(secondsAndMilliseconds[0]);
            var milliseconds = secondsAndMilliseconds.Length > 1 ? int.Parse(secondsAndMilliseconds[1]) : 0;

            return (hours * 3600000) + (minutes * 60000) + (seconds * 1000) + milliseconds;
        }
    }
}
