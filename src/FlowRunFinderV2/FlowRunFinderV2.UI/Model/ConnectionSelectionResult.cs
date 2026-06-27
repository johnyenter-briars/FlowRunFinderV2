using FlowRunFinderV2.Core.Configuration;

namespace FlowRunFinderV2.UI.Model;

public sealed record ConnectionSelectionResult(bool CreateNew, ConnectionProfile? Connection)
{
    public static ConnectionSelectionResult New { get; } = new(true, null);

    public static ConnectionSelectionResult Open(ConnectionProfile connection)
    {
        return new ConnectionSelectionResult(false, connection);
    }
}
