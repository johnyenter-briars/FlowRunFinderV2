using Microsoft.Identity.Client;

namespace FlowRunFinderV2.Core.Auth;

public sealed class PowerAutomateAuthService
{
    private static readonly string[] Scopes = { "https://service.flow.microsoft.com/user_impersonation" };

    private readonly IPublicClientApplication _app;
    private readonly AuthenticationFlow _authenticationFlow;

    public PowerAutomateAuthService(TokenCacheOptions tokenCacheOptions)
        : this(tokenCacheOptions, AuthenticationClientIds.PowerAutomate, AuthenticationFlow.InteractiveBrowser)
    {
    }

    public PowerAutomateAuthService(
        TokenCacheOptions tokenCacheOptions,
        string? clientId,
        AuthenticationFlow authenticationFlow = AuthenticationFlow.InteractiveBrowser)
        : this(
            authenticationFlow == AuthenticationFlow.DeviceCode
                ? (tokenCacheOptions ?? throw new ArgumentNullException(nameof(tokenCacheOptions))).PowerAutomateTokenCachePath
                : (tokenCacheOptions ?? throw new ArgumentNullException(nameof(tokenCacheOptions))).PowerAutomateInteractiveBrowserTokenCachePath,
            clientId,
            authenticationFlow)
    {
    }

    public PowerAutomateAuthService(string? tokenCachePath = null)
        : this(tokenCachePath, AuthenticationClientIds.PowerAutomate, AuthenticationFlow.InteractiveBrowser)
    {
    }

    public PowerAutomateAuthService(
        string? tokenCachePath,
        string? clientId,
        AuthenticationFlow authenticationFlow = AuthenticationFlow.InteractiveBrowser)
    {
        ValidateAuthenticationFlow(authenticationFlow);
        _authenticationFlow = authenticationFlow;
        _app = PublicClientApplicationBuilder
            .Create(NormalizeClientId(clientId, AuthenticationClientIds.PowerAutomate))
            .WithAuthority(AadAuthorityAudience.AzureAdMultipleOrgs)
            .WithRedirectUri("http://localhost")
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
                            "powerautomate",
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
                            TokenCacheOptions.DefaultPowerAutomateCacheFileName)));
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
            switch (_authenticationFlow)
            {
                case AuthenticationFlow.InteractiveBrowser:
                    return await AcquireWithInteractiveBrowserAsync(cancellationToken)
                        .ConfigureAwait(false);
                case AuthenticationFlow.DeviceCode:
                    return await AcquireWithDeviceCodeAsync(showPrompt, cancellationToken).ConfigureAwait(false);
                default:
                    throw new InvalidOperationException($"Unsupported authentication flow: {_authenticationFlow}.");
            }
        }
    }

    private async Task<AuthResult> AcquireWithInteractiveBrowserAsync(CancellationToken cancellationToken)
    {
        var interactive = await _app.AcquireTokenInteractive(Scopes)
            .WithUseEmbeddedWebView(false)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AuthResult(interactive.AccessToken, interactive.ExpiresOn);
    }

    private async Task<AuthResult> AcquireWithDeviceCodeAsync(
        Action<DeviceCodePrompt> showPrompt,
        CancellationToken cancellationToken)
    {
        var deviceCode = await _app.AcquireTokenWithDeviceCode(Scopes, prompt =>
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
