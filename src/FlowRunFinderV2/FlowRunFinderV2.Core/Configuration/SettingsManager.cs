using System.Text.Json;

namespace FlowRunFinderV2.Core.Configuration;

public sealed class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private readonly string _settingsPath;
    private readonly string _settingsFolder;

    public SettingsManager(string appDataFolder)
    {
        _settingsFolder = appDataFolder;
        _settingsPath = Path.Combine(appDataFolder, "settings.json");
    }

    public AppSettings Current { get; private set; } = new();

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_settingsFolder);
            if (!File.Exists(_settingsPath))
            {
                Current = new AppSettings();
                return Current;
            }

            using (var stream = new FileStream(
                       _settingsPath,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read))
            {
                Current = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                              .ConfigureAwait(false)
                          ?? new AppSettings();
            }

            EnsureCaseInsensitiveColumnCache(Current);
            return Current;
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Current = settings;
            EnsureCaseInsensitiveColumnCache(Current);
            await SaveCurrentCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    public async Task UpdateAsync(Action<AppSettings> update, CancellationToken cancellationToken = default)
    {
        await _settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            update(Current);
            EnsureCaseInsensitiveColumnCache(Current);
            await SaveCurrentCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    private async Task SaveCurrentCoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_settingsFolder);

        var tempPath = Path.Combine(
            _settingsFolder,
            $"settings.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, Current, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (File.Exists(_settingsPath))
            {
                File.Delete(_settingsPath);
            }

            File.Move(tempPath, _settingsPath);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void EnsureCaseInsensitiveColumnCache(AppSettings settings)
    {
        if (settings.SelectedTriggerColumnsByFlowId is null)
        {
            settings.SelectedTriggerColumnsByFlowId = new Dictionary<string, List<string>>(
                StringComparer.OrdinalIgnoreCase);
            return;
        }

        if (settings.SelectedTriggerColumnsByFlowId.Comparer == StringComparer.OrdinalIgnoreCase)
        {
            return;
        }

        settings.SelectedTriggerColumnsByFlowId = new Dictionary<string, List<string>>(
            settings.SelectedTriggerColumnsByFlowId,
            StringComparer.OrdinalIgnoreCase);
    }
}
