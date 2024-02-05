using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace DotnetCtrfJsonReporter
{
    public class JsonConverter
    {
        public string ConvertToCrtfJson(TestResultsModel testResults)
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.Indented
            };

            return JsonConvert.SerializeObject(testResults, settings);
        }
    }
}
