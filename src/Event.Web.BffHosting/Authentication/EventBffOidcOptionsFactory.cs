using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Event.Web.BffHosting.Authentication;

public interface IEventBffOidcOptionsFactory
{
    OpenIdConnectOptions CreateKeycloakOptions(EventBffOidcProviderOptions provider);

    OpenIdConnectOptions CreateGoogleOptions(EventBffOidcProviderOptions provider);
}

public sealed record EventBffOidcProviderOptions(
    string Authority,
    string ClientId,
    string? ClientSecret,
    string? MetadataAddress = null,
    bool? RequireHttpsMetadata = null);

public sealed class EventBffOidcOptionsFactory(
    IWebHostEnvironment environment,
    ISafeAuthDiagnosticsPolicy safeDiagnosticsPolicy)
    : IEventBffOidcOptionsFactory
{
    public OpenIdConnectOptions CreateKeycloakOptions(EventBffOidcProviderOptions provider)
    {
        var options = CreateCommonOptions(
            provider,
            callbackPath: "/signin-oidc",
            signedOutCallbackPath: "/signout-callback-oidc",
            nameClaimType: "preferred_username",
            requireHttpsMetadata: provider.RequireHttpsMetadata ?? !environment.IsDevelopment(),
            forceIpv4Backchannel: true);

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        return options;
    }

    public OpenIdConnectOptions CreateGoogleOptions(EventBffOidcProviderOptions provider)
    {
        var options = CreateCommonOptions(
            provider,
            callbackPath: "/signin-google",
            signedOutCallbackPath: null,
            nameClaimType: "name",
            requireHttpsMetadata: true,
            forceIpv4Backchannel: false);

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");

        return options;
    }

    private OpenIdConnectOptions CreateCommonOptions(
        EventBffOidcProviderOptions provider,
        string callbackPath,
        string? signedOutCallbackPath,
        string nameClaimType,
        bool requireHttpsMetadata,
        bool forceIpv4Backchannel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.Authority);
        ArgumentException.ThrowIfNullOrWhiteSpace(provider.ClientId);

        var cookieSecure = environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;

        var options = new OpenIdConnectOptions
        {
            Authority = provider.Authority,
            ClientId = provider.ClientId,
            ClientSecret = provider.ClientSecret?.Trim() ?? string.Empty,
            UsePkce = true,
            SaveTokens = true,
            GetClaimsFromUserInfoEndpoint = true,
            RequireHttpsMetadata = requireHttpsMetadata,
            CallbackPath = callbackPath,
            SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme,
            ResponseType = OpenIdConnectResponseType.Code,
            ResponseMode = OpenIdConnectResponseMode.Query,
            PushedAuthorizationBehavior = PushedAuthorizationBehavior.Disable,
            CorrelationCookie =
            {
                SameSite = SameSiteMode.Lax,
                SecurePolicy = cookieSecure
            },
            NonceCookie =
            {
                SameSite = SameSiteMode.Lax,
                SecurePolicy = cookieSecure
            },
            TokenValidationParameters = new TokenValidationParameters
            {
                NameClaimType = nameClaimType
            },
            Events = CreateEvents()
        };

        if (!string.IsNullOrWhiteSpace(provider.MetadataAddress))
        {
            options.MetadataAddress = provider.MetadataAddress;
        }

        if (!string.IsNullOrWhiteSpace(signedOutCallbackPath))
        {
            options.SignedOutCallbackPath = signedOutCallbackPath;
        }

        if (forceIpv4Backchannel)
        {
            options.BackchannelHttpHandler = CreateIpv4BackchannelHandler();
        }

        return options;
    }

    private OpenIdConnectEvents CreateEvents()
    {
        return new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AuthEndpoints");
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "[OIDC-DIAG] Redirecting to IDP: {AuthorizationEndpoint}, clientId={ClientId}, redirectUri={RedirectUri}, responseType={ResponseType}, scope={Scope}",
                        context.ProtocolMessage.AuthorizationEndpoint,
                        context.ProtocolMessage.ClientId,
                        context.ProtocolMessage.RedirectUri,
                        context.ProtocolMessage.ResponseType,
                        context.ProtocolMessage.Scope);
                }

                return Task.CompletedTask;
            },
            OnAuthorizationCodeReceived = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AuthEndpoints");
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "[OIDC-DIAG] Authorization code received. tokenEndpoint={TokenEndpoint}, hasClientId={HasClientId}, hasClientSecret={HasClientSecret}",
                        context.TokenEndpointRequest?.TokenEndpoint,
                        !string.IsNullOrWhiteSpace(context.TokenEndpointRequest?.ClientId),
                        !string.IsNullOrWhiteSpace(context.TokenEndpointRequest?.ClientSecret));
                }

                return Task.CompletedTask;
            },
            OnTokenResponseReceived = context =>
            {
                context.Properties?.Items[EventBffAuthenticationConstants.OidcSchemePropertyKey] = context.Scheme.Name;

                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AuthEndpoints");
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "[OIDC-DIAG] Token response received (idToken={HasIdToken}, accessToken={HasAccessToken}, refreshToken={HasRefreshToken}, hasError={HasError})",
                        !string.IsNullOrEmpty(context.TokenEndpointResponse?.IdToken),
                        !string.IsNullOrEmpty(context.TokenEndpointResponse?.AccessToken),
                        !string.IsNullOrEmpty(context.TokenEndpointResponse?.RefreshToken),
                        !string.IsNullOrWhiteSpace(context.TokenEndpointResponse?.Error));
                }

                return Task.CompletedTask;
            },
            OnRemoteFailure = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("AuthEndpoints");

                var provider = context.Scheme.Name.ToLowerInvariant();
                var returnUrl = context.Properties?.RedirectUri ?? "/";
                var diagnostic = safeDiagnosticsPolicy.CreateDiagnostic(
                    "oidc_remote_failure",
                    context.Failure);

                logger.LogError(
                    "[OIDC-DIAG] Remote authentication failure for {Provider} (errorCode={ErrorCode}, correlationId={CorrelationId}, failureCategory={FailureCategory})",
                    provider,
                    diagnostic.ErrorCode,
                    diagnostic.CorrelationId,
                    diagnostic.FailureCategory);

                var redirectUrl = safeDiagnosticsPolicy.BuildLoginRedirectUrl(
                    returnUrl,
                    provider,
                    diagnostic);
                context.Response.Redirect(redirectUrl);
                context.HandleResponse();
                return Task.CompletedTask;
            }
        };
    }

    public static SocketsHttpHandler CreateIpv4BackchannelHandler()
    {
        return new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(5),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
    }
}
