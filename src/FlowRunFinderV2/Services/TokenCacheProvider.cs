using Microsoft.Identity.Client;

namespace FlowRunFinderV2.Services;

internal static class TokenCacheProvider
{
    private static readonly object CacheLock = new();

    public static string CachePath
    {
        get
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FlowRunFinderV2");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "msal_cache.bin3");
        }
    }

    public static void Register(ITokenCache tokenCache)
    {
        tokenCache.SetBeforeAccess(args =>
        {
            lock (CacheLock)
            {
                if (File.Exists(CachePath))
                {
                    args.TokenCache.DeserializeMsalV3(File.ReadAllBytes(CachePath));
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
                File.WriteAllBytes(CachePath, args.TokenCache.SerializeMsalV3());
            }
        });
    }
}
