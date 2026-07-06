using Microsoft.Identity.Client;

namespace FlowRunFinderV2.Core.Auth;

public sealed class DataverseAuthService
{
    private readonly IPublicClientApplication _app;
    private readonly AuthenticationFlow _authenticationFlow;

    public DataverseAuthService(TokenCacheOptions tokenCacheOptions)
        : this(tokenCacheOptions, AuthenticationClientIds.PowerAutomate, AuthenticationFlow.InteractiveBrowser)
    {
    }

    public DataverseAuthService(
        TokenCacheOptions tokenCacheOptions,
        string? clientId,
        AuthenticationFlow authenticationFlow = AuthenticationFlow.InteractiveBrowser)
        : this(
            authenticationFlow == AuthenticationFlow.DeviceCode
                ? (tokenCacheOptions ?? throw new ArgumentNullException(nameof(tokenCacheOptions))).DataverseTokenCachePath
                : (tokenCacheOptions ?? throw new ArgumentNullException(nameof(tokenCacheOptions))).DataverseInteractiveBrowserTokenCachePath,
            clientId,
            authenticationFlow)
    {
    }

    public DataverseAuthService(string? tokenCachePath = null)
        : this(tokenCachePath, AuthenticationClientIds.PowerAutomate, AuthenticationFlow.InteractiveBrowser)
    {
    }

    public DataverseAuthService(
        string? tokenCachePath,
        string? clientId,
        AuthenticationFlow authenticationFlow = AuthenticationFlow.InteractiveBrowser)
    {
        ValidateAuthenticationFlow(authenticationFlow);
        _authenticationFlow = authenticationFlow;
        _app = PublicClientApplicationBuilder
            .Create(NormalizeClientId(clientId, AuthenticationClientIds.PowerAutomate))
            .WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs)
            .WithDefaultRedirectUri()
            .Build();

        if (string.IsNullOrWhiteSpace(tokenCachePath))
        {
            if (authenticationFlow == AuthenticationFlow.InteractiveBrowser)
            {
                TokenCacheProvider.RegisterEncryptedPath(
                    _app.UserTokenCache,
                    TokenCacheProvider.GetDefaultCachePath(
                        Path.Combine(
                            "auth",
                            "interactivebrowser",
                            "dataverse",
                            TokenCacheOptions.EncryptedCacheFileName)));
            }
            else
            {
                TokenCacheProvider.RegisterPath(
                    _app.UserTokenCache,
                    TokenCacheProvider.GetDefaultCachePath(
                        Path.Combine(
                            "auth",
                            "devicecode",
                            TokenCacheOptions.DefaultDataverseCacheFileName)));
            }
        }
        else
        {
            RegisterTokenCache(_app.UserTokenCache, tokenCachePath!, authenticationFlow);
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
            switch (_authenticationFlow)
            {
                case AuthenticationFlow.InteractiveBrowser:
                    return await AcquireWithInteractiveBrowserAsync(scopes, cancellationToken)
                        .ConfigureAwait(false);
                case AuthenticationFlow.DeviceCode:
                    return await AcquireWithDeviceCodeAsync(scopes, showPrompt, cancellationToken).ConfigureAwait(false);
                default:
                    throw new InvalidOperationException($"Unsupported authentication flow: {_authenticationFlow}.");
            }
        }
    }

    private async Task<AuthResult> AcquireWithInteractiveBrowserAsync(
        string[] scopes,
        CancellationToken cancellationToken)
    {
        var interactive = await _app.AcquireTokenInteractive(scopes)
            .WithUseEmbeddedWebView(false)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AuthResult(interactive.AccessToken, interactive.ExpiresOn);
    }

    private async Task<AuthResult> AcquireWithDeviceCodeAsync(
        string[] scopes,
        Action<DeviceCodePrompt> showPrompt,
        CancellationToken cancellationToken)
    {
        var deviceCode = await _app.AcquireTokenWithDeviceCode(scopes, prompt =>
            {
                showPrompt(new DeviceCodePrompt(prompt.VerificationUrl, prompt.UserCode, prompt.Message));
                return Task.CompletedTask;
            })
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AuthResult(deviceCode.AccessToken, deviceCode.ExpiresOn);
    }

    private static void RegisterTokenCache(
        ITokenCache tokenCache,
        string tokenCachePath,
        AuthenticationFlow authenticationFlow)
    {
        if (authenticationFlow == AuthenticationFlow.InteractiveBrowser)
        {
            TokenCacheProvider.RegisterEncryptedPath(tokenCache, tokenCachePath);
            return;
        }

        TokenCacheProvider.RegisterPath(tokenCache, tokenCachePath);
    }

    private static void ValidateAuthenticationFlow(AuthenticationFlow authenticationFlow)
    {
        if (authenticationFlow != AuthenticationFlow.InteractiveBrowser &&
            authenticationFlow != AuthenticationFlow.DeviceCode)
        {
            throw new ArgumentOutOfRangeException(nameof(authenticationFlow), authenticationFlow, "Unsupported authentication flow.");
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
