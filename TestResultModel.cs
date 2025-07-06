using Newtonsoft.Json;
using System.Reflection;

namespace DotnetCtrfJsonReporter
{
    public class TestResultsModel
    {
        public static string SpecVersion => Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString() ?? "0.0.0";
        public static string ReportId => Guid.NewGuid().ToString();
        public static string Timestamp => DateTime.UtcNow.ToString("O");
        public static string ReportFormat => "CTRF";
        public static string GeneratedBy => "ctrf-json-reporter";
        public ResultsModel Results { get; set; } = new ResultsModel();
    }

    public class ResultsModel
    {
        public ToolModel Tool { get; set; } = new ToolModel();
        public SummaryModel Summary { get; set; } = new SummaryModel();
        public List<TestModel> Tests { get; set; } = new List<TestModel>();
    }

    public class TestModel
    {
        public required string Name { get; set; }
        public required string Status { get; set; }
        public required long Duration { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Message { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Trace { get; set; }
    }

    public class ToolModel
    {
        public string Name { get; set; }
    }

    public class SummaryModel
    {
        public int Tests { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Pending { get; set; }
        public int Skipped { get; set; }
        public int Other { get; set; }
        public long Start { get; set; }
        public long Stop { get; set; }
    }
}
