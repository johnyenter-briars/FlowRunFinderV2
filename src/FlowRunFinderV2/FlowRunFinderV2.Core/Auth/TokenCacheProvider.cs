using Microsoft.Identity.Client;

namespace FlowRunFinderV2.Core.Auth;

internal static class TokenCacheProvider
{
    private static readonly object CacheLock = new object();

    public static string CachePath => GetCachePath("msal_cache.bin3");

    public static string GetCachePath(string fileName)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowRunFinderV2");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, fileName);
    }

    public static void Register(ITokenCache tokenCache, string fileName = "msal_cache.bin3")
    {
        var cachePath = GetCachePath(fileName);
        RegisterPath(tokenCache, cachePath);
    }

    public static void RegisterPath(ITokenCache tokenCache, string cachePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
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
}
