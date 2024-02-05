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

                var testModel = new TestModel
                {
                    Name = testResult.Attribute("testName")?.Value,
                    Status = StatusMapper.MapToCtrfStatus(testResult.Attribute("outcome")?.Value),
                    Duration = totalMilliseconds
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
    }
}
