using Blazouter.Extensions;
using Event.Web.BffHosting.Authentication;
using Event.Web.BffHosting.Extensions;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Extensions;
using Explore.Blazor.HealthChecks;
using Explore.Blazor.Services;
using Explore.ServiceDefaults.Configuration;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MudBlazor.Services;
using Serilog;

namespace Explore.Blazor.Hosting;

public static class BlazorHostServiceCollectionExtensions
{
    public static WebApplicationBuilder AddBlazorHostServices(
        this WebApplicationBuilder builder,
        BlazorHostProfile profile,
        GracefulShutdownState shutdownState)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(shutdownState);

        var existingProfile = builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(BlazorHostProfileRegistration))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<BlazorHostProfileRegistration>()
            .SingleOrDefault();
        if (existingProfile is not null)
        {
            throw new InvalidOperationException(
                $"Blazor host services are already registered for profile '{existingProfile.Profile}'.");
        }

        builder.Services.AddSingleton(new BlazorHostProfileRegistration(profile));

        if (profile == BlazorHostProfile.Split)
        {
            builder.Services.AddScoped<IExploreApiReadinessProbe, ExploreApiReadinessProbe>();
            ForwardedHeadersTrustOptions forwardedHeadersTrust = builder.Configuration
                .GetSection(ForwardedHeadersTrustOptions.SectionName)
                .Get<ForwardedHeadersTrustOptions>() ?? new ForwardedHeadersTrustOptions
                {
                    TrustLoopbackProxy = true
                };
            forwardedHeadersTrust.Validate();
            builder.Services.Configure<ForwardedHeadersOptions>(options => forwardedHeadersTrust.ApplyTo(
                options,
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto));

            builder.Host.UseSerilog((context, services, loggerConfiguration) =>
                loggerConfiguration.ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext(),
                writeToProviders: true);

            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Limits.KeepAliveTimeout =
                    TimeSpan.FromSeconds(GracefulShutdownState.GracePeriodSeconds + 5);
            });

            builder.Host.ConfigureHostOptions(options =>
            {
                options.ShutdownTimeout =
                    TimeSpan.FromSeconds(GracefulShutdownState.GracePeriodSeconds + 5);
            });

            builder.AddServiceDefaults();
            builder.AddResilientDistributedCache(connectionName: "cache");
            builder.AddDistributedCacheReadinessCheck();
            builder.AddOidcDiscoveryReadinessCheck();
        }

        if (builder.Environment.IsEnvironment("Testing")
            && builder.Configuration["SecretProvider:Provider"] is null
            && builder.Configuration["SECRET_PROVIDER"] is null)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecretProvider:Provider"] = "Environment"
            });
        }
        builder.Configuration.AddSecretAuthorityConfiguration(builder.Environment.EnvironmentName);

        builder.Services.AddMudServices(config =>
        {
            config.PopoverOptions.Duration = TimeSpan.FromMilliseconds(300);
            config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomCenter;
            config.SnackbarConfiguration.PreventDuplicates = true;
            config.SnackbarConfiguration.NewestOnTop = true;
            config.SnackbarConfiguration.ShowCloseIcon = true;
            config.SnackbarConfiguration.VisibleStateDuration = 5000;
            config.SnackbarConfiguration.HideTransitionDuration = 200;
            config.SnackbarConfiguration.ShowTransitionDuration = 200;
        });
        builder.Services.AddApplicationServices();
        builder.Services.AddServerOnlyServices(builder.Configuration);
        builder.Services.AddEventControlPlaneClient();
        builder.Services.AddApiHttpClients(builder.Configuration, builder.Environment, profile);
        if (profile == BlazorHostProfile.Combined)
        {
            builder.Services.AddSingleton<DynamicAuthSchemeInitializer>();
        }

        var detailedErrors = builder.Configuration.GetValue<bool>("DetailedErrors");
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents(options => options.DetailedErrors = detailedErrors)
            .AddInteractiveWebAssemblyComponents()
            .AddAuthenticationStateSerialization(options =>
            {
                options.SerializationCallback = AuthStateSerializationPolicy.SerializeDisplaySafeClaimsAsync;
            });
        builder.Services.Configure<Microsoft.AspNetCore.SignalR.HubOptions>(options =>
        {
            options.MaximumReceiveMessageSize = 512 * 1024;
            options.EnableDetailedErrors = detailedErrors;
        });

        builder.Services.AddBlazouter();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddOptions();

        builder.Services.AddEventBffHosting(
            builder.Configuration,
            builder.Environment,
            EventBffHostProfile.PublicWeb);
        builder.Services.AddBffAuthentication(builder.Configuration, builder.Environment);
        builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
        builder.Services.AddBffRateLimiting(builder.Configuration, builder.Environment);
        builder.Services.AddBffTrustedRequestEnrichment();
        if (profile == BlazorHostProfile.Split)
        {
            builder.Services.AddBffReverseProxy(builder.Configuration, builder.Environment);
        }

        builder.Services.AddAuthorizationBuilder();
        builder.Services.AddCascadingAuthenticationState();

        builder.Services.AddLocalization();
        builder.Services.Configure<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>(options =>
        {
            var cultures = BffCultureRegistry.GetSupportedCultureCodes()
                .Select(code => new System.Globalization.CultureInfo(code))
                .ToArray();

            options.SupportedCultures = cultures;
            options.SupportedUICultures = cultures;
            options.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en");
            options.RequestCultureProviders.Clear();
            options.RequestCultureProviders.Insert(
                0,
                new Microsoft.AspNetCore.Localization.CookieRequestCultureProvider());
            options.RequestCultureProviders.Insert(
                1,
                new Microsoft.AspNetCore.Localization.AcceptLanguageHeaderRequestCultureProvider());
        });

        builder.Services.AddControllersWithViews()
            .AddApplicationPart(typeof(BlazorHostServiceCollectionExtensions).Assembly);

        var healthChecks = builder.Services.AddHealthChecks();
        if (profile == BlazorHostProfile.Split)
        {
            healthChecks.AddCheck("shutdown", () =>
            {
                if (shutdownState.IsShuttingDown)
                {
                    return HealthCheckResult.Unhealthy("Application is shutting down");
                }

                return HealthCheckResult.Healthy();
            }, tags: ["live", "ready"]);
        }

        healthChecks.AddCheck<DataProtectionKeyStoreHealthCheck>(
            "data-protection-keys",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready", "data-protection", "redis"]);

        if (profile == BlazorHostProfile.Split)
        {
            healthChecks.AddCheck<ApiReadinessHealthCheck>(
                "explore-api",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "api", "infrastructure"]);
        }

        healthChecks.AddCheck<AtprotoAuthenticationHealthCheck>(
            "atproto-authentication",
            failureStatus: HealthStatus.Unhealthy,
            tags: ["ready", "authentication"]);

        return builder;
    }
}
