using Microsoft.Identity.Client;

namespace FlowRunFinderV2.Core.Auth;

public sealed class DataverseAuthService
{
    // Public client id commonly used by Dataverse tooling. Replace with your own app registration if blocked.
    private const string DefaultClientId = "51f81489-12ee-4a9e-aaae-a2591f45987d";

    private readonly IPublicClientApplication _app;

    public DataverseAuthService(string? tokenCachePath = null)
    {
        _app = PublicClientApplicationBuilder
            .Create(DefaultClientId)
            .WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs)
            .WithDefaultRedirectUri()
            .Build();

        if (string.IsNullOrWhiteSpace(tokenCachePath))
        {
            TokenCacheProvider.Register(_app.UserTokenCache);
        }
        else
        {
            TokenCacheProvider.RegisterPath(_app.UserTokenCache, tokenCachePath);
        }
    }

    public async Task<AuthResult> GetTokenAsync(
        Uri environmentUrl,
        Action<DeviceCodePrompt> showPrompt,
        CancellationToken cancellationToken)
    {
        var scopes = new[] { $"{environmentUrl.GetLeftPart(UriPartial.Authority)}/user_impersonation" };
        var accounts = await _app.GetAccountsAsync().ConfigureAwait(false);

        try
        {
            var silent = await _app.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return new AuthResult(silent.AccessToken, silent.ExpiresOn);
        }
        catch (MsalUiRequiredException)
        {
            var interactive = await _app.AcquireTokenWithDeviceCode(scopes, prompt =>
                {
                    showPrompt(new DeviceCodePrompt(prompt.VerificationUrl, prompt.UserCode, prompt.Message));
                    return Task.CompletedTask;
                })
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            return new AuthResult(interactive.AccessToken, interactive.ExpiresOn);
        }
    }
}

public sealed record AuthResult(string AccessToken, DateTimeOffset ExpiresOn);

public sealed record DeviceCodePrompt(string VerificationUrl, string UserCode, string Message);
