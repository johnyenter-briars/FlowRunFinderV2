using Microsoft.Identity.Client;

namespace FlowRunFinderV2.Core.Auth;

public sealed class PowerAutomateAuthService
{
    private const string ClientId = "1950a258-227b-4e31-a9cf-717495945fc2";
    private static readonly string[] Scopes = ["https://service.flow.microsoft.com/user_impersonation"];

    private readonly IPublicClientApplication _app;

    public PowerAutomateAuthService(string? tokenCachePath = null)
    {
        _app = PublicClientApplicationBuilder
            .Create(ClientId)
            .WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs)
            .WithDefaultRedirectUri()
            .Build();

        if (string.IsNullOrWhiteSpace(tokenCachePath))
        {
            TokenCacheProvider.Register(_app.UserTokenCache, "power_automate_msal_cache.bin3");
        }
        else
        {
            TokenCacheProvider.RegisterPath(_app.UserTokenCache, tokenCachePath);
        }
    }

    public async Task<AuthResult> GetTokenAsync(
        Action<DeviceCodePrompt> showPrompt,
        CancellationToken cancellationToken)
    {
        var accounts = await _app.GetAccountsAsync().ConfigureAwait(false);

        try
        {
            var silent = await _app.AcquireTokenSilent(Scopes, accounts.FirstOrDefault())
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return new AuthResult(silent.AccessToken, silent.ExpiresOn);
        }
        catch (MsalUiRequiredException)
        {
            var device = await _app.AcquireTokenWithDeviceCode(Scopes, prompt =>
                {
                    showPrompt(new DeviceCodePrompt(prompt.VerificationUrl, prompt.UserCode, prompt.Message));
                    return Task.CompletedTask;
                })
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return new AuthResult(device.AccessToken, device.ExpiresOn);
        }
    }
}
