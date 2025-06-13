using System.Xml.Linq;

namespace DotnetCtrfJsonReporter
{
    public class TestProcessor
    {
        public List<TestModel> ProcessTests(XDocument trxDocument)
        {
            var testResults = trxDocument.Descendants().Where(d => d.Name.LocalName == "UnitTestResult");
            var testModels = new List<TestModel>();

            foreach (var testResult in testResults)
            {
                var durationString = testResult.Attribute("duration")?.Value ?? "00:00:00";
                var totalMilliseconds = ParseTotalMilliseconds(durationString);
                var errorInfo = GetErrorInfo(testResult);

                var testModel = new TestModel
                {
                    Name = testResult.Attribute("testName")?.Value,
                    Status = StatusMapper.MapToCtrfStatus(testResult.Attribute("outcome")?.Value),
                    Duration = totalMilliseconds,

                    Message = errorInfo?.Message,
                    Trace = errorInfo?.Trace,
                };

                testModels.Add(testModel);
            }

            return testModels;
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

        private static ErrorInfo? GetErrorInfo(XElement testResult)
        {
            var errorInfo = testResult.Descendants().Where(x => x.Name.LocalName == "ErrorInfo").FirstOrDefault();
            if (errorInfo is null)
            {
                return null;
            }

            var message = errorInfo.Descendants().Where(x => x.Name.LocalName == "Message").FirstOrDefault();
            var trace = errorInfo.Descendants().Where(x => x.Name.LocalName == "StackTrace").FirstOrDefault();

            return new(message?.Value, trace?.Value);
        }

        private record ErrorInfo(string? Message, string? Trace);
    }
}
