using System.Xml.Linq;

namespace DotnetCtrfJsonReporter
{
    public class TrxProcessor
    {
        public TestResultsModel ProcessTrxFile(string filePath)
        {
            var trxReport = XDocument.Load(filePath);
            var testResults = trxReport.Descendants().Where(d => d.Name.LocalName == "UnitTestResult");
            var timesElement = trxReport.Descendants().FirstOrDefault(d => d.Name.LocalName == "Times");

            var totals = new SummaryModel();

            if (timesElement != null)
            {
                totals.Start = ConvertToEpochMilliseconds(ParseDateTime(timesElement.Attribute("start")?.Value));
                totals.Stop = ConvertToEpochMilliseconds(ParseDateTime(timesElement.Attribute("finish")?.Value));
            }

            foreach (var testResult in testResults)
            {
                totals.Tests++;

                var nunitOutcome = testResult.Attribute("outcome")?.Value;
                var ctrfOutcome = StatusMapper.MapToCtrfStatus(nunitOutcome);
                switch (ctrfOutcome)
                {
                    case "passed":
                        totals.Passed++;
                        break;
                    case "failed":
                        totals.Failed++;
                        break;
                    case "skipped":
                        totals.Skipped++;
                        break;
                    case "other":
                        totals.Other++;
                        break;
                }
            }

            var testProcessor = new TestProcessor();
            var tests = testProcessor.ProcessTests(trxReport);

            return new TestResultsModel
            {
                Results = new ResultsModel
                {
                    Tool = new ToolModel { Name = "nunit" },
                    Summary = totals,
                    Tests = tests
                }
            };
        }

        private DateTime ParseDateTime(string dateTimeStr)
        {
            return DateTime.TryParse(dateTimeStr, out DateTime dateTime) 
                ? dateTime 
                : DateTime.MinValue;
        }

        private long ConvertToEpochMilliseconds(DateTime dateTime)
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(dateTime.ToUniversalTime() - epoch).TotalMilliseconds;
        }
    }
}