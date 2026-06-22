using System.Text.Json;

namespace FlowRunFinderV2.Core.Configuration;

public sealed class AppDataStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public string AppDataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FlowRunFinderV2");

    public string ConnectionsFolder => Path.Combine(AppDataFolder, "connections");
    public string LogsFolder => Path.Combine(AppDataFolder, "logs");

    public AppDataStore()
    {
        Directory.CreateDirectory(AppDataFolder);
        Directory.CreateDirectory(ConnectionsFolder);
        Directory.CreateDirectory(LogsFolder);
    }

    public async Task<IReadOnlyList<ConnectionProfile>> LoadConnectionsAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(ConnectionsFolder);
        var connections = new List<ConnectionProfile>();

        foreach (var folder in Directory.EnumerateDirectories(ConnectionsFolder))
        {
            var metadataPath = Path.Combine(folder, "connection.json");
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            await using var stream = File.OpenRead(metadataPath);
            var connection = await JsonSerializer.DeserializeAsync<ConnectionProfile>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
            if (connection is not null)
            {
                connections.Add(connection);
            }
        }

        return connections
            .OrderBy(connection => connection.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ConnectionProfile> CreateConnectionAsync(
        string name,
        Uri environmentUrl,
        CancellationToken cancellationToken = default)
    {
        var connection = new ConnectionProfile
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            EnvironmentUrl = environmentUrl.GetLeftPart(UriPartial.Authority),
            CreatedOnUtc = DateTimeOffset.UtcNow
        };

        await SaveConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    public async Task SaveConnectionAsync(ConnectionProfile connection, CancellationToken cancellationToken = default)
    {
        var folder = GetConnectionFolder(connection.Id);
        Directory.CreateDirectory(folder);
        await using var stream = File.Create(Path.Combine(folder, "connection.json"));
        await JsonSerializer.SerializeAsync(stream, connection, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public string GetConnectionFolder(Guid connectionId)
    {
        return Path.Combine(ConnectionsFolder, connectionId.ToString("D"));
    }

    public string GetDataverseTokenCachePath(Guid connectionId)
    {
        return Path.Combine(GetConnectionFolder(connectionId), "dataverse_msal_cache.bin3");
    }

    public string GetPowerAutomateTokenCachePath(Guid connectionId)
    {
        return Path.Combine(GetConnectionFolder(connectionId), "power_automate_msal_cache.bin3");
    }
}

public sealed class AppSettings
{
    public int DefaultRunCount { get; set; } = 10;
    public LogVerbosity LogVerbosity { get; set; } = LogVerbosity.Info;
    public Dictionary<string, List<string>> SelectedTriggerColumnsByFlowId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum LogVerbosity
{
    Off = 0,
    Error = 1,
    Info = 2,
    Debug = 3,
    Trace = 4
}

public sealed class ConnectionProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string EnvironmentUrl { get; set; } = string.Empty;
    public DateTimeOffset CreatedOnUtc { get; set; }

    public override string ToString()
    {
        return $"{Name} ({EnvironmentUrl})";
    }
}
