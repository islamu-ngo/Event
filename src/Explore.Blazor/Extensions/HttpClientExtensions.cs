// ABOUTME: Centralizes all HttpClient registrations for the Blazor BFF server.
// ABOUTME: Eliminates repeated ConfigurePrimaryHttpMessageHandler blocks for dev cert bypass.

using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Http;
using Explore.Blazor.HealthChecks;
using Explore.Blazor.Hosting;
using Explore.Blazor.Services;
using Explore.Blazor.Services.Auth;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.Timeout;

namespace Explore.Blazor.Extensions;

public static class HttpClientExtensions
{
    private static readonly TimeSpan InteractiveTotalTimeout = TimeSpan.FromSeconds(330);
    private static readonly TimeSpan InteractiveAttemptTimeout = TimeSpan.FromSeconds(310);

    /// <summary>
    /// Registers all API-facing HttpClient instances used by the Blazor BFF server.
    /// Each client calls the Event API directly with access token forwarding.
    /// </summary>
    public static IServiceCollection AddApiHttpClients(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment) =>
        services.AddApiHttpClients(configuration, environment, BlazorHostProfile.Split);

    public static IServiceCollection AddApiHttpClients(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        BlazorHostProfile profile)
    {
        var apiBaseUrl = profile == BlazorHostProfile.Combined
            ? InProcessEventApiDispatcher.InternalBaseAddress.AbsoluteUri
            : ResolveApiBaseUrl(configuration);

        services.AddTransient<EventApiBehaviorMessageHandler>();
        services.AddTransient<AccessTokenForwardingHandler>();
        services.AddTransient<TenantHeaderForwardingHandler>();
        services.AddTransient<SetupSecretForwardingHandler>();
        services.AddTransient<SupportAccessForwardingHandler>();
        services.AddTransient<BffCookieForwardingHandler>();
        if (profile == BlazorHostProfile.Combined)
        {
            services.AddSingleton<InProcessEventApiDispatcher>();
            services.AddTransient<InProcessEventApiHttpMessageHandler>();
        }

        // Named "BffSelfClient" — used by InteractiveServer components calling BFF endpoints on this server.
        // No BaseAddress here; components set it from NavigationManager.BaseUri at runtime.
        services.AddHttpClient("BffSelfClient")
            .AddHttpMessageHandler<BffCookieForwardingHandler>()
            .ConfigureDevCertBypass(environment, allowAutoRedirect: false);


        foreach (var (interfaceType, implementationType) in GeneratedEventApiClients.ClientTypes)
        {
            services.AddTypedApiClient(interfaceType, implementationType, apiBaseUrl, environment, profile)
                .AddInteractiveResilience();
        }

        services.AddScoped<ILocalizationAdminService, LocalizationAdminService>();

        // Admin claims transformation client (shorter timeout, no token forwarding handler)
        services.AddHttpClient(BffAdminClaimsTransformation.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(5);
        }).ConfigureApiTransport(environment, profile)
          .AddAdminResilience();

        services.AddHttpClient(ApiBackedOAuthSessionStore.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(20);
        }).ConfigureApiTransport(environment, profile);

        var transient = services.AddHttpClient(ApiBackedAtprotoTransientStore.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(apiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        if (profile == BlazorHostProfile.Combined)
            transient.ConfigurePrimaryHttpMessageHandler<InProcessEventApiHttpMessageHandler>();
        else
            transient.ConfigureDevCertBypass(environment, allowAutoRedirect: false);
#pragma warning disable EXTEXP0001
        // A lost consume response is indeterminate: neither global retries nor hedging may resend it.
        transient.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        return services;
    }

    private static IHttpClientBuilder AddTypedApiClient<TInterface, TImplementation>(
        this IServiceCollection services,
        string baseUrl,
        IWebHostEnvironment environment,
        BlazorHostProfile profile)
        where TInterface : class
        where TImplementation : class, TInterface =>
        services.AddTypedApiClient(
            typeof(TInterface),
            typeof(TImplementation),
            baseUrl,
            environment,
            profile);

    private static IHttpClientBuilder AddTypedApiClient(
        this IServiceCollection services,
        Type interfaceType,
        Type implementationType,
        string baseUrl,
        IWebHostEnvironment environment,
        BlazorHostProfile profile)
    {
        return GeneratedEventApiClients.AddTypedApiClient(
            services,
            interfaceType,
            implementationType,
            client => client.BaseAddress = new Uri(baseUrl))
        .AddHttpMessageHandler<EventApiBehaviorMessageHandler>()
        .AddHttpMessageHandler<AccessTokenForwardingHandler>()
        .AddHttpMessageHandler<TenantHeaderForwardingHandler>()
        .AddHttpMessageHandler<SetupSecretForwardingHandler>()
        .AddHttpMessageHandler<SupportAccessForwardingHandler>()
        .ConfigureApiTransport(environment, profile);
    }

    private static IHttpClientBuilder ConfigureApiTransport(
        this IHttpClientBuilder builder,
        IWebHostEnvironment environment,
        BlazorHostProfile profile) =>
        profile == BlazorHostProfile.Combined
            ? builder.ConfigurePrimaryHttpMessageHandler<InProcessEventApiHttpMessageHandler>()
            : builder.ConfigureDevCertBypass(environment);

    // Interactive BFF->API calls use a lean custom pipeline: no circuit breaker (same-machine
    // traffic; a shared breaker trips unrelated UI requests after a single slow endpoint), one
    // retry on safe response status codes only, and an attempt timeout long enough for local AI
    // providers. RemoveAllResilienceHandlers() guards against accidental global
    // ConfigureHttpClientDefaults stacking (dotnet/extensions #5695).
    private static IHttpClientBuilder AddInteractiveResilience(this IHttpClientBuilder builder)
    {
#pragma warning disable EXTEXP0001
        builder.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        builder.AddResilienceHandler("bff-interactive", pipeline =>
        {
            pipeline.AddTimeout(InteractiveTotalTimeout);

            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.FromMilliseconds(200),
                BackoffType = DelayBackoffType.Constant,
                ShouldHandle = args =>
                {
                    if (args.Outcome.Result is { } response && IsUnsafeMethod(response.RequestMessage?.Method))
                    {
                        return ValueTask.FromResult(false);
                    }

                    if (args.Outcome.Exception is { } exception
                        && (exception is HttpRequestException
                            || exception.GetType().FullName == "Polly.Timeout.TimeoutRejectedException"))
                    {
                        // A transport timeout/exception may happen after an unsafe request reached
                        // the API. Retrying that POST can collide with the in-flight command and
                        // surface as "conversation not active" while the original run continues.
                        return ValueTask.FromResult(false);
                    }

                    if (args.Outcome.Result is { } result)
                    {
                        return ValueTask.FromResult(result.StatusCode
                            is System.Net.HttpStatusCode.RequestTimeout
                            or System.Net.HttpStatusCode.BadGateway
                            or System.Net.HttpStatusCode.ServiceUnavailable
                            or System.Net.HttpStatusCode.GatewayTimeout);
                    }

                    return ValueTask.FromResult(false);
                },
            });

            pipeline.AddTimeout(InteractiveAttemptTimeout);
        });

        return builder;
    }

    private static IHttpClientBuilder AddAdminResilience(this IHttpClientBuilder builder)
    {
#pragma warning disable EXTEXP0001
        builder.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        builder.AddStandardResilienceHandler(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            options.Retry.MaxRetryAttempts = 3;
            options.Retry.Delay = TimeSpan.FromMilliseconds(500);
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.DisableForUnsafeHttpMethods();
            options.CircuitBreaker.MinimumThroughput = 5;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
            options.CircuitBreaker.FailureRatio = 0.5;
        });

        return builder;
    }

    private static IHttpClientBuilder AddBackgroundResilience(this IHttpClientBuilder builder)
    {
#pragma warning disable EXTEXP0001
        builder.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        builder.AddStandardResilienceHandler(options =>
        {
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
            options.Retry.MaxRetryAttempts = 4;
            options.Retry.Delay = TimeSpan.FromSeconds(1);
            options.Retry.BackoffType = DelayBackoffType.Exponential;
            options.Retry.DisableForUnsafeHttpMethods();
            options.CircuitBreaker.MinimumThroughput = 5;
            options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            options.CircuitBreaker.FailureRatio = 0.5;
        });

        return builder;
    }

    private static bool IsUnsafeMethod(HttpMethod? method)
    {
        return method is not null
            && (method == HttpMethod.Post
                || method == HttpMethod.Put
                || method == HttpMethod.Patch
                || method == HttpMethod.Delete);
    }

    /// <summary>
    /// Configures the primary handler for every BFF → API HttpClient.
    /// <para>
    /// Uses <see cref="SocketsHttpHandler"/> with bounded connection pooling to prevent the
    /// classic HTTP/1.1 stale-connection anti-pattern where Kestrel silently closes idle
    /// sockets while the BFF pool still considers them alive. The scavenger's zero-byte
    /// read-ahead then hangs on the half-open socket until the Polly attempt timeout fires.
    /// </para>
    /// <para>
    /// Settings (per Microsoft's official HttpClient guidance):
    /// <list type="bullet">
    /// <item><c>PooledConnectionLifetime</c> = 2 min — forces periodic recycle so DNS and
    /// config changes propagate and connections can't grow permanently stale.</item>
    /// <item><c>PooledConnectionIdleTimeout</c> = 30 s — drops idle connections well before
    /// Kestrel's default 130 s keep-alive timeout, avoiding the race entirely.</item>
    /// <item><c>ConnectTimeout</c> = 10 s — caps the initial TCP+TLS handshake so a dead
    /// endpoint fails fast instead of consuming the Polly attempt budget.</item>
    /// <item>HTTP/2 keep-alive pings — detect broken connections proactively when the
    /// remote is using HTTP/2 (Kestrel auto-negotiates over TLS).</item>
    /// </list>
    /// </para>
    /// <para>
    /// In development, also short-circuits SSL cert validation for <c>localhost</c>.
    /// </para>
    /// </summary>
    private static IHttpClientBuilder ConfigureDevCertBypass(
        this IHttpClientBuilder builder,
        IWebHostEnvironment environment,
        bool allowAutoRedirect = true)
    {
        builder.ConfigurePrimaryHttpMessageHandler(() => CreatePooledHandler(environment, allowAutoRedirect));

        // Keep pooled handlers alive for the full PooledConnectionLifetime window so the
        // handler's internal pool actually gets to reuse connections. Otherwise the factory
        // would rotate the entire SocketsHttpHandler (and its pool) every 2 minutes by
        // default, defeating the point of tuning these values.
        builder.SetHandlerLifetime(TimeSpan.FromMinutes(5));

        return builder;
    }

    private static SocketsHttpHandler CreatePooledHandler(
        IWebHostEnvironment environment,
        bool allowAutoRedirect)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = allowAutoRedirect,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(5),
            KeepAlivePingPolicy = System.Net.Http.HttpKeepAlivePingPolicy.WithActiveRequests,
        };

        if (environment.IsDevelopment())
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
            {
                if (errors == System.Net.Security.SslPolicyErrors.None)
                {
                    return true;
                }

                // SocketsHttpHandler does not surface the HttpRequestMessage in the SSL
                // callback, but the sender is the SslStream whose TargetHost is the request
                // authority. In dev, trust only a small explicit allowlist.
                if (sender is System.Net.Security.SslStream { TargetHostName: { } host }
                    && IsDevelopmentTrustedHost(host))
                {
                    return true;
                }

                return false;
            };
        }

        return handler;
    }

    private static bool IsDevelopmentTrustedHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("::1", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("100.64.0.2", StringComparison.OrdinalIgnoreCase)
            || IsTailscaleAddress(host))
        {
            return true;
        }

        // Optional override: explicit, comma-separated hosts/IPs can be configured
        // for additional trusted dev targets (still environment- and dev-only).
        var additionalHosts = System.Environment.GetEnvironmentVariable("BFF_DEV_TRUSTED_HOSTS");
        if (!string.IsNullOrWhiteSpace(additionalHosts))
        {
            return additionalHosts
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(h => host.Equals(h, StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private static bool IsTailscaleAddress(string host)
    {
        if (!System.Net.IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        // Tailscale/CGNAT range: 100.64.0.0/10
        return bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127;
    }

    private static string ResolveApiBaseUrl(IConfiguration configuration)
    {
        // 1. Explicit configuration (overrides everything — for standalone dev, Docker, prod)
        var explicitUrl = configuration["ExploreApi:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(explicitUrl))
        {
            return NormalizeBaseUrl(explicitUrl);
        }

        // 2. Aspire service discovery env vars (injected by .WithReference in AppHost)
        // Prefer HTTP over HTTPS for local inter-service communication to avoid SSL proxy handshake delays
        var aspireHttp = GetAspireApiReference(configuration, "http");
        if (!string.IsNullOrWhiteSpace(aspireHttp))
        {
            return NormalizeBaseUrl(aspireHttp);
        }

        var aspireHttps = GetAspireApiReference(configuration, "https");
        if (!string.IsNullOrWhiteSpace(aspireHttps))
        {
            return NormalizeBaseUrl(aspireHttps);
        }

        // 3. Fallback for standalone development (matches API launchSettings default)
        return "https://localhost:7039/";
    }

    private static string NormalizeBaseUrl(string url)
    {
        return url.EndsWith('/') ? url : url + "/";
    }

    private static string? GetAspireApiReference(IConfiguration configuration, string scheme) =>
        configuration[$"services:explore-api:{scheme}:0"]
        ?? configuration[$"services__explore-api__{scheme}__0"];
}
