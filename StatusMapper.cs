namespace DotnetCtrfJsonReporter
{
    public static class StatusMapper
    {
        public static string MapToCtrfStatus(string? trxStatus)
        {
            return trxStatus switch
            {
                "Passed" => "passed",
                "Failed" or "Error" or "Timeout" or "Aborted" => "failed",
                "NotExecuted" => "skipped",
                "Pending" => "pending",
                _ => "other"
            };
        }
    }
}
