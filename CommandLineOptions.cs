using CommandLine;

public class CommandLineOptions
{
    [Option('t', "trx-path", Required = false, HelpText = "Path to the TRX file.")]
    public string TrxPath { get; set; } = "default.trx";

    [Option('c', "ctrf-report-name", Required = false, HelpText = "Name of the CTRF report.")]
    public string CtrfReportName { get; set; } = "ctrf-report.json";

    [Option('o', "output-path", Required = false, HelpText = "CTRF report output path.")]
    public string Output { get; set; } = "output";
}
