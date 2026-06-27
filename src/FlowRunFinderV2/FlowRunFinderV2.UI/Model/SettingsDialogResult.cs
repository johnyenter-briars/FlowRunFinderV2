using FlowRunFinderV2.Core.Configuration;

namespace FlowRunFinderV2.UI.Model;

public sealed record SettingsDialogResult(
    int DefaultRunCount,
    int MaxRunsToQuery,
    bool UseFlowRunHistoryTable,
    string DataverseClientId,
    string PowerAutomateClientId,
    LogVerbosity LogVerbosity);
