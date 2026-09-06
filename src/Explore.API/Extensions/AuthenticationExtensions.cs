// ABOUTME: Registers bounded multi-scheme authentication and authorization for the API.
// ABOUTME: Dispatches exact setup, API-key, ATProto, privacy-receipt, and Keycloak bearer credentials.

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using Explore.API.Authentication;
using Explore.API.Configuration;
using Explore.API.ExceptionHandling;
using Explore.API.Mcp;
using Explore.Application.Authorization;
using Explore.Application.Configuration;
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Management;
using Explore.Domain.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Explore.API.Extensions;

public static class AuthenticationExtensions
{
    // Security: audiences validated in both 'aud' and 'azp' claims (Keycloak BFF pattern)
    private static readonly string[] DefaultValidAudiences = ["islamu-event-api", "islamu-event-blazor"];

    public static IServiceCollection AddApiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        bool skipAuthorityWarmup = false)
    {
        // Dynamic JWT authority: Authority / MetadataAddress / ValidIssuer are applied
        // by DynamicJwtBearerPostConfigureOptions from env + DB (IAuthProviderConfigurationService).
        // Handlers call IJwtAuthorityRefreshNotifier.ReloadAsync() after onboarding/save-config
        // to hot-swap Keycloak metadata without restarting the API.
        var validAudiences = configuration.GetSection("Keycloak:ValidAudiences").Get<string[]>()
            ?? configuration.GetSection("Authentication:ValidAudiences").Get<string[]>()
            ?? DefaultValidAudiences;
        var validAudienceSet = new HashSet<string>(validAudiences.Where(audience => !string.IsNullOrWhiteSpace(audience)), StringComparer.Ordinal);

        services.AddSingleton<DynamicJwtConfigurationService>();
        services.AddSingleton<IJwtAuthorityRefreshNotifier>(sp =>
            sp.GetRequiredService<DynamicJwtConfigurationService>());
        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, DynamicJwtBearerPostConfigureOptions>();
        services.AddOptions<AtprotoJwtOptions>()
            .Bind(configuration.GetSection(AtprotoJwtOptions.SectionName))
            .Validate(
                options => options.SessionLifetime >= TimeSpan.FromMinutes(1)
                           && options.SessionLifetime <= TimeSpan.FromHours(1),
                "ATProto session lifetime must be between one minute and one hour.")
            .ValidateOnStart();
        services.AddOptions<AuthenticationProviderDeploymentOptions>()
            .Bind(configuration.GetSection(AuthenticationProviderDeploymentOptions.SectionName))
            .Validate(
                AuthenticationProviderDeploymentOptions.IsValid,
                "Authentication:Provider must be blank, 'local', 'keycloak', or 'atproto'.")
            .ValidateOnStart();
        services.AddOptions<LocalIdentityOptions>()
            .Bind(configuration.GetSection(LocalIdentityOptions.SectionName))
            .Validate(
                LocalIdentityOptions.IsValid,
                "Local Identity lockout and token lifetime settings are outside their supported ranges.")
            .ValidateOnStart();
        services.AddScoped<AtprotoTransientAssertionValidator>();
        services.AddScoped<AtprotoJwtService>();
        services.AddScoped<IAtprotoSessionTokenIssuer>(provider => provider.GetRequiredService<AtprotoJwtService>());
        if (!skipAuthorityWarmup)
        {
            services.AddHostedService<JwtAuthorityWarmupHostedService>();
        }

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = ApiAuthenticationSchemeNames.MultiAuth;
                options.DefaultAuthenticateScheme = ApiAuthenticationSchemeNames.MultiAuth;
                options.DefaultChallengeScheme = ApiAuthenticationSchemeNames.MultiAuth;
            })
            .AddPolicyScheme(ApiAuthenticationSchemeNames.MultiAuth, ApiAuthenticationSchemeNames.MultiAuth, options =>
            {
                options.ForwardDefaultSelector = SelectDefaultAuthenticationScheme;
            })
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = string.Equals(
                    configuration["Keycloak:RequireHttpsMetadata"],
                    "true",
                    StringComparison.OrdinalIgnoreCase
                );

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    // Security: custom audience check covers both 'aud' and 'azp' claims
                    // because Keycloak puts the client ID in 'azp' rather than 'aud'
                    ValidateAudience = true,
                    AudienceValidator = (audiences, securityToken, _) =>
                    {
                        var audienceList = audiences?.ToList() ?? new List<string>();

                        if (audienceList.Exists(validAudienceSet.Contains))
                        {
                            return true;
                        }

                        if (securityToken is System.IdentityModel.Tokens.Jwt.JwtSecurityToken jwtToken)
                        {
                            var azp = jwtToken.Claims.FirstOrDefault(c => c.Type == "azp")?.Value;
                            if (!string.IsNullOrEmpty(azp) && validAudienceSet.Contains(azp))
                            {
                                return true;
                            }
                        }

                        return false;
                    },

                    ValidateIssuer = true,

                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5),

                    ValidateIssuerSigningKey = true,

                    NameClaimType = "preferred_username"
                };

                options.BackchannelHttpHandler = new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                    PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
                    ConnectTimeout = TimeSpan.FromSeconds(10),
                    KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                    KeepAlivePingTimeout = TimeSpan.FromSeconds(5),
                    KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
                    SslOptions = environment.IsDevelopment()
                        ? new SslClientAuthenticationOptions
                        {
                            RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                                IsAllowedDevelopmentCertificate(sender, errors, environment)
                        }
                        : default,
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

                // Security: PII-safe logging — only exception messages, never token claims or raw values
                options.Events = new JwtBearerEvents
                {
                    // Performance: skip JWT token validation entirely for onboarding paths.
                    // When Keycloak is unreachable, OIDC metadata discovery times out (3+ minutes),
                    // blocking [AllowAnonymous] endpoints that don't need authentication at all.
                    // Only POST /complete requires JWT auth (has [Authorize]) and is excluded.
                    OnMessageReceived = context =>
                    {
                        var path = context.HttpContext.Request.Path;
                        if (path.StartsWithSegments("/api/instanceonboarding", StringComparison.OrdinalIgnoreCase)
                            && path.Value?.EndsWith("/complete", StringComparison.OrdinalIgnoreCase) != true)
                        {
                            context.Token = null;
                        }

                        return Task.CompletedTask;
                    },

                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogWarning(
                            "[JWT] Authentication failed for {Method} {Path}: {Error}",
                            context.Request.Method,
                            context.Request.Path,
                            context.Exception?.Message);
                        return Task.CompletedTask;
                    },

                    OnChallenge = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                        logger.LogDebug(
                            "[JWT] Challenge issued for {Path}. Error: {Error}, Description: {Desc}",
                            context.Request.Path,
                            context.Error,
                            context.ErrorDescription);
                        return Task.CompletedTask;
                    }
                };
            })
            .AddJwtBearer(ApiAuthenticationSchemeNames.LocalIdentity, options =>
            {
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        ResolveLocalIdentityValidationKey(configuration)),
                    ValidateIssuer = true,
                    ValidIssuer = LocalIdentityOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = LocalIdentityOptions.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = JwtRegisteredClaimNames.Email,
                    RoleClaimType = "roles"
                };
            })
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiAuthenticationSchemeNames.ApiKey,
                options =>
                {
                    configuration.GetSection(ApiKeyAuthenticationOptions.SectionName).Bind(options);

                    if (string.IsNullOrWhiteSpace(options.HeaderName))
                    {
                        options.HeaderName = ApiAuthenticationHeaderNames.ApiKey;
                    }
                })
            .AddScheme<AuthenticationSchemeOptions, ManagedControlPlaneAuthenticationHandler>(
                ApiAuthenticationSchemeNames.ManagedControlPlane,
                _ => { })
            .AddScheme<AuthenticationSchemeOptions, SetupSecretAuthenticationHandler>(
                ApiAuthenticationSchemeNames.SetupSecret,
                _ => { })
            .AddScheme<AuthenticationSchemeOptions, AtprotoTransientAuthenticationHandler>(
                AtprotoTransientAuthenticationDefaults.Scheme,
                _ => { })
            .AddScheme<AuthenticationSchemeOptions, AtprotoBootstrapAuthenticationHandler>(
                ApiAuthenticationSchemeNames.AtprotoBootstrap,
                _ => { })
            .AddScheme<AuthenticationSchemeOptions, AtprotoSessionAuthenticationHandler>(
                ApiAuthenticationSchemeNames.AtprotoSession,
                _ => { })
            .AddScheme<AuthenticationSchemeOptions, PrivacyErasureReceiptAuthenticationHandler>(
                ApiAuthenticationSchemeNames.PrivacyErasureReceipt,
                _ => { })
            .AddScheme<AuthenticationSchemeOptions, AdmissionScannerAuthenticationHandler>(
                ApiAuthenticationSchemeNames.AdmissionScanner,
                _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy(ApiAuthorizationPolicies.Admin, policy => policy
                .RequireRole("Admin"))
            .AddPolicy(McpAuthorizationPolicies.Read, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => ApiKeyCallerHasAnyScopeOrIsUser(
                    context.User,
                    ExternalApiKeyScopes.McpRead,
                    ExternalApiKeyScopes.McpPropose)))
            .AddPolicy(McpAuthorizationPolicies.EventManagementRead, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => ApiKeyCallerHasMcpReadAndEventReadAuthorityOrIsUser(context.User)))
            .AddPolicy(McpAuthorizationPolicies.Propose, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => ApiKeyCallerHasAnyScopeOrIsUser(
                    context.User,
                    ExternalApiKeyScopes.McpPropose)))
            .AddPolicy(ModerationIntegrationAuthorizationPolicies.OspreyCallback, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => ApiKeyCallerPassesScopeGate(
                    context.User,
                    scopes => MachineScopeMapping.ScopesPermit(
                        scopes,
                        ResourceKinds.Event,
                        AuthorizationActions.Events.ModerateLight))))
            .AddPolicy(ModerationIntegrationAuthorizationPolicies.CoopCallback, policy => policy
                .RequireAuthenticatedUser()
                .RequireAssertion(context => ApiKeyCallerPassesScopeGate(
                    context.User,
                    scopes => MachineScopeMapping.ScopesPermit(
                                  scopes,
                                  ResourceKinds.Event,
                                  AuthorizationActions.Events.ModerateLight)
                              || MachineScopeMapping.ScopesPermit(
                                  scopes,
                                  ResourceKinds.Event,
                                  AuthorizationActions.Events.ModerateHeavy))))
            .AddPolicy(QuartzSchedulerSettings.InstanceAdminPolicyName, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(AdminClaimTypes.InstanceAdmin, "true"))
            .AddPolicy(ManagedControlPlaneAuthorizationPolicies.Read, policy => policy
                .AddAuthenticationSchemes(ApiAuthenticationSchemeNames.ManagedControlPlane)
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User
                    .FindAll(ManagedControlPlaneAuthenticationDefaults.ScopeClaim)
                    .Any(claim => claim.Value is ManagedControlPlaneContract.ControlPlaneReadScope
                        or ManagedControlPlaneContract.ControlPlaneWriteScope)))
            .AddPolicy(ManagedControlPlaneAuthorizationPolicies.Write, policy => policy
                .AddAuthenticationSchemes(ApiAuthenticationSchemeNames.ManagedControlPlane)
                .RequireAuthenticatedUser()
                .RequireClaim(
                    ManagedControlPlaneAuthenticationDefaults.ScopeClaim,
                    ManagedControlPlaneContract.ControlPlaneWriteScope));

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemDetailsAuthorizationMiddlewareResultHandler>();

        return services;
    }

    internal static string SelectDefaultAuthenticationScheme(HttpContext context)
    {
        if (AtprotoTransientAuthenticationDefaults.IsPrivatePath(context.Request.Path))
        {
            return AtprotoTransientAuthenticationDefaults.Scheme;
        }

        if (SetupSecretAuthenticationHandler.SupportsRequest(context.Request)
            && context.Request.Headers.ContainsKey(SetupSecretAuthenticationHandler.HeaderName))
        {
            return ApiAuthenticationSchemeNames.SetupSecret;
        }

        if (HttpMethods.IsPost(context.Request.Method)
            && string.Equals(context.Request.Path.Value, AtprotoJwtOptions.BridgePath, StringComparison.Ordinal)
            && context.Request.Headers.ContainsKey(AtprotoJwtOptions.BootstrapHeaderName))
        {
            return ApiAuthenticationSchemeNames.AtprotoBootstrap;
        }

        bool requiresManagedCredential = context.GetEndpoint()?.Metadata
            .GetOrderedMetadata<IAuthorizeData>()
            .Any(authorize => authorize.Policy is ManagedControlPlaneAuthorizationPolicies.Read
                or ManagedControlPlaneAuthorizationPolicies.Write) == true;
        if (requiresManagedCredential && ApiKeyHeaderReader.HasNonEmptyApiKey(
                context.Request,
                ManagedControlPlaneAuthenticationDefaults.HeaderName))
        {
            return ApiAuthenticationSchemeNames.ManagedControlPlane;
        }

        if (context.Request.Path.StartsWithSegments(
                "/api/admission/scanner/check-ins",
                StringComparison.OrdinalIgnoreCase))
        {
            return ApiAuthenticationSchemeNames.AdmissionScanner;
        }

        if (ApiKeyHeaderReader.HasNonEmptyApiKey(context.Request))
        {
            return ApiAuthenticationSchemeNames.ApiKey;
        }

        if (IsAtprotoSessionBearer(context.Request))
        {
            return ApiAuthenticationSchemeNames.AtprotoSession;
        }

        return IsLocalIdentityBearer(context.Request)
            ? ApiAuthenticationSchemeNames.LocalIdentity
            : JwtBearerDefaults.AuthenticationScheme;
    }

    internal static bool IsAtprotoSessionBearer(HttpRequest request)
        => IsBearerFromIssuer(request, AtprotoJwtOptions.SessionIssuer);

    internal static bool IsLocalIdentityBearer(HttpRequest request)
        => IsBearerFromIssuer(request, LocalIdentityOptions.Issuer);

    private static bool IsBearerFromIssuer(HttpRequest request, string issuer)
    {
        var authorization = request.Headers.Authorization;
        if (authorization.Count != 1
            || authorization[0] is not { } value
            || !value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = value["Bearer ".Length..].Trim();
        if (!AtprotoJwtService.IsBoundedCompactJwt(token, AtprotoJwtOptions.MaximumSessionTokenBytes))
        {
            return false;
        }

        try
        {
            return string.Equals(
                new JwtSecurityTokenHandler().ReadJwtToken(token).Issuer,
                issuer,
                StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static byte[] ResolveLocalIdentityValidationKey(
        IConfiguration configuration)
    {
        string? configured = configuration[
            $"{LocalIdentityOptions.SectionName}:JwtKey"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return RandomNumberGenerator.GetBytes(32);
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configured);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "Local Identity JWT validation authority is invalid.",
                exception);
        }

        return key.Length >= 32
            ? key
            : throw new InvalidOperationException(
                "Local Identity JWT validation authority does not meet the 256-bit minimum.");
    }

    private static bool ApiKeyCallerHasMcpReadAndEventReadAuthorityOrIsUser(ClaimsPrincipal principal)
        => ApiKeyCallerHasAnyScopeOrIsUser(principal, ExternalApiKeyScopes.McpRead)
           && ApiKeyCallerPassesScopeGateOrIsUser(principal, scopes => MachineScopeMapping.ScopesPermit(
               scopes,
               ResourceKinds.Event,
               AuthorizationActions.View));

    private static bool ApiKeyCallerHasAnyScopeOrIsUser(ClaimsPrincipal principal, params string[] scopes)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var apiKeyId = principal.FindFirst(ApiAuthenticationClaimTypes.ApiKeyId)?.Value;
        if (string.IsNullOrWhiteSpace(apiKeyId))
        {
            return true;
        }

        var scopeSet = new HashSet<string>(scopes, StringComparer.OrdinalIgnoreCase);
        return principal
            .FindAll(ApiAuthenticationClaimTypes.Scope)
            .Any(claim => scopeSet.Contains(claim.Value));
    }

    private static bool ApiKeyCallerPassesScopeGateOrIsUser(
        ClaimsPrincipal principal,
        Func<IReadOnlyList<string>, bool> scopeGate)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var apiKeyId = principal.FindFirst(ApiAuthenticationClaimTypes.ApiKeyId)?.Value;
        if (string.IsNullOrWhiteSpace(apiKeyId))
        {
            return true;
        }

        var apiKeyScopes = principal
            .FindAll(ApiAuthenticationClaimTypes.Scope)
            .Select(claim => claim.Value)
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .ToArray();

        return scopeGate(apiKeyScopes);
    }

    private static bool ApiKeyCallerPassesScopeGate(
        ClaimsPrincipal principal,
        Func<IReadOnlyList<string>, bool> scopeGate)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var apiKeyId = principal.FindFirst(ApiAuthenticationClaimTypes.ApiKeyId)?.Value;
        if (string.IsNullOrWhiteSpace(apiKeyId))
        {
            return false;
        }

        var apiKeyScopes = principal
            .FindAll(ApiAuthenticationClaimTypes.Scope)
            .Select(claim => claim.Value)
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .ToArray();

        return scopeGate(apiKeyScopes);
    }

    private static bool IsAllowedDevelopmentCertificate(object? sender, SslPolicyErrors errors, IWebHostEnvironment environment)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        if (!environment.IsDevelopment())
        {
            return false;
        }

        return sender is SslStream { TargetHostName: { } host }
               && IsDevelopmentTrustedHost(host);
    }

    private static bool IsDevelopmentTrustedHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::1", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("100.64.0.2", StringComparison.OrdinalIgnoreCase)
            || IsTailscaleAddress(host))
        {
            return true;
        }

        var additionalHosts = Environment.GetEnvironmentVariable("BFF_DEV_TRUSTED_HOSTS");
        if (string.IsNullOrWhiteSpace(additionalHosts))
        {
            return false;
        }

        return additionalHosts
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTailscaleAddress(string host)
    {
        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        // Tailscale/CGNAT range: 100.64.0.0/10
        return bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127;
    }
}
