using CommandLine;

namespace DotnetCtrfJsonReporter
{
    public class CommandLineOptions
    {
        [Option('p', "trx-path", Required = true, HelpText = "Path to the TRX file.")]
        public required string TrxPath { get; set; }

        [Option('f', "output-filename", Required = false, HelpText = "Name of the CTRF report.", Default = "ctrf-report.json")]
        public string? CtrfReportName { get; set; }

        [Option('d', "output-directory", Required = false, HelpText = "Output directory for the report.", Default = "ctrf")]
        public string? Output { get; set; }

        [Option('t', "test-tool", Required = false, HelpText = "Name of the test tool (nunit, mstest, xunit, etc.).", Default = "dotnet")]
        public string ToolName { get; set; }
    }




    class Program
    {
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<CommandLineOptions>(args)
                  .WithParsed(RunWithOptions)
                  .WithNotParsed(HandleParseError);
        }

        static void RunWithOptions(CommandLineOptions opts)
        {
            string trxFilePath = opts.TrxPath;
            string outputFilename = EnsureJsonExtension(opts.CtrfReportName ?? "ctrf-report.json");
            string outputDirectory = opts.Output ?? "ctrf";

            string outputFilePath = Path.Combine(outputDirectory, outputFilename);

            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var trxProcessor = new TrxProcessor();
            var testResultsModel = trxProcessor.ProcessTrxFile(trxFilePath);

            testResultsModel.Results.Tool.Name = opts.ToolName;

            var jsonConverter = new JsonConverter();
            string jsonContent = jsonConverter.ConvertToCrtfJson(testResultsModel);

            File.WriteAllText(outputFilePath, jsonContent);

            Console.WriteLine($"ctrf-json-reporter successfully written ctrf json to {outputFilePath}");
        }

        static string EnsureJsonExtension(string filename)
        {
            if (Path.HasExtension(filename))
            {
                if (Path.GetExtension(filename).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    return filename;
                }
                return filename + ".json";
            }
            else
            {
                return filename + ".json";
            }
        }

        static void HandleParseError(IEnumerable<Error> errs)
        {
            Console.WriteLine("error parsing ctrf-json-reporter command line arguments");
        }
    }
}
