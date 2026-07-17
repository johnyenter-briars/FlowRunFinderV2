using FlowRunFinderV2.Core.Auth;
using FlowRunFinderV2.Core.Configuration;

namespace FlowRunFinderV2.UI.Model;

public sealed record SettingsDialogResult(
    int DefaultRunCount,
    int MaxRunsToQuery,
    bool UseFlowRunHistoryTable,
    AuthenticationFlow AuthenticationFlow,
    string DataverseClientId,
    string PowerAutomateClientId,
    LogVerbosity LogVerbosity);
