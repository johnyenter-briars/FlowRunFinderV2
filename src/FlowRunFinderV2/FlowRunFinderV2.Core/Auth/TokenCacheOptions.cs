namespace FlowRunFinderV2.Core.Auth;

public sealed class TokenCacheOptions
{
    public const string DefaultDataverseCacheFileName = "dataverse_msal_cache.bin3";
    public const string DefaultPowerAutomateCacheFileName = "power_automate_msal_cache.bin3";
    public const string EncryptedCacheFileName = "msalcache.dat";

    public TokenCacheOptions(
        string cacheDirectory,
        string dataverseCacheFileName = DefaultDataverseCacheFileName,
        string powerAutomateCacheFileName = DefaultPowerAutomateCacheFileName)
    {
        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            throw new ArgumentException("Token cache directory cannot be empty.", nameof(cacheDirectory));
        }

        CacheDirectory = cacheDirectory;
        DataverseCacheFileName = dataverseCacheFileName;
        PowerAutomateCacheFileName = powerAutomateCacheFileName;
    }

    public string CacheDirectory { get; }
    public string DataverseCacheFileName { get; }
    public string PowerAutomateCacheFileName { get; }

    public string DeviceCodeCacheDirectory => Path.Combine(CacheDirectory, "auth", "devicecode");
    public string InteractiveBrowserCacheDirectory => Path.Combine(CacheDirectory, "auth", "interactivebrowser");

    public string DataverseTokenCachePath => Path.Combine(DeviceCodeCacheDirectory, DataverseCacheFileName);
    public string PowerAutomateTokenCachePath => Path.Combine(DeviceCodeCacheDirectory, PowerAutomateCacheFileName);
    public string DataverseInteractiveBrowserTokenCachePath => Path.Combine(InteractiveBrowserCacheDirectory, "dataverse", EncryptedCacheFileName);
    public string PowerAutomateInteractiveBrowserTokenCachePath => Path.Combine(InteractiveBrowserCacheDirectory, "powerautomate", EncryptedCacheFileName);
}
