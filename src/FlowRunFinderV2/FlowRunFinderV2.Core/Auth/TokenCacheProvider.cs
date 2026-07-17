using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace FlowRunFinderV2.Core.Auth;

internal static class TokenCacheProvider
{
    private static readonly object CacheLock = new object();
    private static readonly List<MsalCacheHelper> EncryptedCacheHelpers = new List<MsalCacheHelper>();

    public static void Register(ITokenCache tokenCache, string fileName = "msal_cache.bin3")
    {
        var cachePath = GetDefaultCachePath(fileName);
        RegisterPath(tokenCache, cachePath);
    }

    public static string GetDefaultCachePath(string fileName)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowRunFinderV2");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, fileName);
    }

    public static void RegisterPath(ITokenCache tokenCache, string cachePath)
    {
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(cacheDirectory))
        {
            Directory.CreateDirectory(cacheDirectory);
        }

        tokenCache.SetBeforeAccess(args =>
        {
            lock (CacheLock)
            {
                if (File.Exists(cachePath))
                {
                    args.TokenCache.DeserializeMsalV3(File.ReadAllBytes(cachePath));
                }
            }
        });

        tokenCache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged)
            {
                return;
            }

            lock (CacheLock)
            {
                File.WriteAllBytes(cachePath, args.TokenCache.SerializeMsalV3());
            }
        });
    }

    public static void RegisterEncryptedPath(ITokenCache tokenCache, string cachePath)
    {
        var cacheDirectory = Path.GetDirectoryName(cachePath);
        var cacheFileName = Path.GetFileName(cachePath);
        if (string.IsNullOrWhiteSpace(cacheDirectory) || string.IsNullOrWhiteSpace(cacheFileName))
        {
            throw new ArgumentException("Token cache path must include a directory and file name.", nameof(cachePath));
        }

        Directory.CreateDirectory(cacheDirectory);
        var storageProperties = new StorageCreationPropertiesBuilder(cacheFileName, cacheDirectory)
            .Build();
        var cacheHelper = MsalCacheHelper.CreateAsync(storageProperties)
            .GetAwaiter()
            .GetResult();

        cacheHelper.RegisterCache(tokenCache);
        lock (CacheLock)
        {
            EncryptedCacheHelpers.Add(cacheHelper);
        }
    }
}
