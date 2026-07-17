namespace FlowRunFinderV2.Core.Query;

public sealed class FlowRunQueryProgress
{
    public FlowRunQueryProgress(
        int candidateRecordCount,
        int scannedRecordCount,
        int matchCount,
        int percentScanned)
    {
        CandidateRecordCount = candidateRecordCount;
        ScannedRecordCount = scannedRecordCount;
        MatchCount = matchCount;
        PercentScanned = Math.Max(0, Math.Min(100, percentScanned));
    }

    public int CandidateRecordCount { get; }
    public int ScannedRecordCount { get; }
    public int MatchCount { get; }
    public int PercentScanned { get; }
}
