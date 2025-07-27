using Newtonsoft.Json;
using System.Reflection;

namespace DotnetCtrfJsonReporter
{
    public class TestResultsModel
    {
        public string SpecVersion => "0.0.0";
        public string ReportId => Guid.NewGuid().ToString();
        public string Timestamp => $"{DateTime.UtcNow:O}";
        public string ReportFormat => "CTRF";
        public string GeneratedBy => "dotnet-ctrf-json-reporter";
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
        public string Name { get; set; }
        public string Status { get; set; }
        public long Duration { get; set; }
        
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public long? Start { get; set; }
        
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public long? Stop { get; set; }
        
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Suite { get; set; }
        
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Message { get; set; }
        
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? Trace { get; set; }
        
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? Line { get; set; }
        
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? RawStatus { get; set; }
        
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? FilePath { get; set; }
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
