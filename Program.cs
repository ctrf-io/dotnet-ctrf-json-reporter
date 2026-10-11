using CommandLine;

namespace DotnetCtrfJsonReporter
{
    public class CommandLineOptions
    {
        [Option('p', "trx-path", Required = true, HelpText = "Path to the TRX file.")]
        public required string TrxPath { get; set; }

        [Option("run-id", HelpText = "Logical run ID shared by coordinated reports.")]
        public string? RunId { get; set; }
        [Option("shard-id", HelpText = "Partition producing this report.")]
        public string? ShardId { get; set; }

        [Option('f', "output-filename", Required = false, HelpText = "Name of the CTRF report.", Default = "ctrf-report.json")]
        public string? CtrfReportName { get; set; }

        [Option('d', "output-directory", Required = false, HelpText = "Output directory for the report.", Default = "ctrf")]
        public string? Output { get; set; }

        [Option('t', "test-tool", Required = false, HelpText = "Name of the test tool (nunit, mstest, xunit, etc.).", Default = "dotnet")]
        public string ToolName { get; set; } = "dotnet";
    }

    class Program
    {
        static int Main(string[] args)
        {
            return Parser.Default.ParseArguments<CommandLineOptions>(args)
                .MapResult(RunWithOptions, errors => errors.All(error => error is HelpRequestedError or VersionRequestedError) ? 0 : 1);
        }

        static int RunWithOptions(CommandLineOptions opts)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(opts.ToolName))
                    throw new ArgumentException("Test tool name must not be empty.");

                var outputFilename = EnsureJsonExtension(opts.CtrfReportName ?? "ctrf-report.json");
                var outputDirectory = opts.Output ?? "ctrf";
                var outputFilePath = Path.Combine(outputDirectory, outputFilename);
                var testResultsModel = new TrxProcessor().ProcessTrxFile(opts.TrxPath);
                testResultsModel.Results.Tool.Name = opts.ToolName;
                if (opts.RunId != null)
                {
                    if (string.IsNullOrWhiteSpace(opts.RunId)) throw new ArgumentException("run-id must be non-empty.");
                    testResultsModel.RunId = opts.RunId;
                }
                if (opts.ShardId != null)
                {
                    if (string.IsNullOrWhiteSpace(opts.ShardId)) throw new ArgumentException("shard-id must be non-empty.");
                    testResultsModel.Results.Environment = new EnvironmentModel { ShardId = opts.ShardId };
                }
                var jsonContent = new JsonConverter().ConvertToCrtfJson(testResultsModel);

                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(outputFilePath, jsonContent);
                Console.WriteLine($"ctrf-json-reporter successfully written ctrf json to {outputFilePath}");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"ctrf-json-reporter: {error.Message}");
                return 1;
            }
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
    }
}
