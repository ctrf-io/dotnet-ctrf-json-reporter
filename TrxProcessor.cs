using System.Xml.Linq;

namespace DotnetCtrfJsonReporter
{
    public class TrxProcessor
    {
        public TestResultsModel ProcessTrxFile(string filePath)
        {
            var trxReport = XDocument.Load(filePath);
            var tests = new TestProcessor().ProcessTests(trxReport);
            var timesElement = trxReport.Descendants().FirstOrDefault(d => d.Name.LocalName == "Times");
            var timestamps = tests.SelectMany(test => new[] { test.Start, test.Stop })
                .Where(time => time.HasValue).Select(time => time!.Value).ToList();
            var start = TestProcessor.ParseEpochMilliseconds(timesElement?.Attribute("start")?.Value)
                ?? (timestamps.Count > 0 ? timestamps.Min() : 0);
            var stop = TestProcessor.ParseEpochMilliseconds(timesElement?.Attribute("finish")?.Value)
                ?? (timestamps.Count > 0 ? timestamps.Max() : start);
            if (stop < start)
                throw new FormatException("TRX run ends before it starts.");

            // Count the normalized results, so totals always match the emitted tests.
            var totals = new SummaryModel
            {
                Tests = tests.Count,
                Passed = tests.Count(test => test.Status == "passed"),
                Failed = tests.Count(test => test.Status == "failed"),
                Skipped = tests.Count(test => test.Status == "skipped"),
                Pending = tests.Count(test => test.Status == "pending"),
                Other = tests.Count(test => test.Status == "other"),
                Start = start,
                Stop = stop
            };

            return new TestResultsModel
            {
                RunId = trxReport.Root?.Attribute("id")?.Value is string sourceRun && !string.IsNullOrWhiteSpace(sourceRun) ? sourceRun : Guid.NewGuid().ToString(),
                Results = new ResultsModel
                {
                    Tool = new ToolModel { Name = "dotnet" },
                    Summary = totals,
                    Tests = tests
                }
            };
        }
    }
}
