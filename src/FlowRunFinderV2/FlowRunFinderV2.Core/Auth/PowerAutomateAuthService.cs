using Microsoft.Identity.Client;

namespace FlowRunFinderV2.Core.Auth;

public sealed class PowerAutomateAuthService
{
    private static readonly string[] Scopes = { "https://service.flow.microsoft.com/user_impersonation" };

    private readonly IPublicClientApplication _app;

    public PowerAutomateAuthService(TokenCacheOptions tokenCacheOptions)
        : this(tokenCacheOptions, AuthenticationClientIds.PowerAutomate)
    {
    }

    public PowerAutomateAuthService(TokenCacheOptions tokenCacheOptions, string? clientId)
        : this(
            (tokenCacheOptions ?? throw new ArgumentNullException(nameof(tokenCacheOptions))).PowerAutomateTokenCachePath,
            clientId)
    {
    }

    public PowerAutomateAuthService(string? tokenCachePath = null)
        : this(tokenCachePath, AuthenticationClientIds.PowerAutomate)
    {
    }

    public PowerAutomateAuthService(string? tokenCachePath, string? clientId)
    {
        _app = PublicClientApplicationBuilder
            .Create(NormalizeClientId(clientId, AuthenticationClientIds.PowerAutomate))
            .WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs)
            .WithDefaultRedirectUri()
            .Build();

        if (string.IsNullOrWhiteSpace(tokenCachePath))
        {
            TokenCacheProvider.Register(
                _app.UserTokenCache,
                TokenCacheOptions.DefaultPowerAutomateCacheFileName);
        }
        else
        {
            TokenCacheProvider.RegisterPath(_app.UserTokenCache, tokenCachePath!);
        }
    }

    private static string NormalizeClientId(string? clientId, string defaultClientId)
    {
        return string.IsNullOrWhiteSpace(clientId)
            ? defaultClientId
            : clientId.Trim();
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
