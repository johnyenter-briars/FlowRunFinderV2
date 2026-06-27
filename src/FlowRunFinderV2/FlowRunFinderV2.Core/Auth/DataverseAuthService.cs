using Microsoft.Identity.Client;

namespace FlowRunFinderV2.Core.Auth;

public sealed class DataverseAuthService
{
    private readonly IPublicClientApplication _app;

    public DataverseAuthService(TokenCacheOptions tokenCacheOptions)
        : this(tokenCacheOptions, AuthenticationClientIds.Dataverse)
    {
    }

    public DataverseAuthService(TokenCacheOptions tokenCacheOptions, string? clientId)
        : this(
            (tokenCacheOptions ?? throw new ArgumentNullException(nameof(tokenCacheOptions))).DataverseTokenCachePath,
            clientId)
    {
    }

    public DataverseAuthService(string? tokenCachePath = null)
        : this(tokenCachePath, AuthenticationClientIds.Dataverse)
    {
    }

    public DataverseAuthService(string? tokenCachePath, string? clientId)
    {
        _app = PublicClientApplicationBuilder
            .Create(NormalizeClientId(clientId, AuthenticationClientIds.Dataverse))
            .WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs)
            .WithDefaultRedirectUri()
            .Build();

        if (string.IsNullOrWhiteSpace(tokenCachePath))
        {
            TokenCacheProvider.Register(
                _app.UserTokenCache,
                TokenCacheOptions.DefaultDataverseCacheFileName);
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

public sealed class AuthResult
{
    public AuthResult(string accessToken, DateTimeOffset expiresOn)
    {
        AccessToken = accessToken;
        ExpiresOn = expiresOn;
    }

    public string AccessToken { get; }
    public DateTimeOffset ExpiresOn { get; }
}

public sealed class DeviceCodePrompt
{
    public DeviceCodePrompt(string verificationUrl, string userCode, string message)
    {
        VerificationUrl = verificationUrl;
        UserCode = userCode;
        Message = message;
    }

    public string VerificationUrl { get; }
    public string UserCode { get; }
    public string Message { get; }
}
