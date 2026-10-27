using Blazouter.Extensions;
using Event.Web.BffHosting.Authentication;
using Event.Web.BffHosting.Extensions;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Configuration;
using Explore.Blazor.Extensions;
using Explore.Blazor.HealthChecks;
using Explore.Blazor.Services;
using Explore.ServiceDefaults.Configuration;
using Explore.ServiceDefaults.DataProtection;
using Explore.ServiceDefaults.Secrets;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MudBlazor.Services;
using Serilog;

namespace Explore.Blazor.Hosting;

public static class BlazorHostServiceCollectionExtensions
{
    private sealed record BffSecretPreparation(SecretAuthorityAcquisition Owner);

    /// <summary>Prepares frontend credentials and the serving partition before host services can consume them.</summary>
    public static async ValueTask PrepareBlazorHostSecretsAsync(
        this WebApplicationBuilder builder,
        BlazorHostProfile profile,
        SecretAuthorityAcquisition? owner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        cancellationToken.ThrowIfCancellationRequested();
        if (owner is null)
        {
            if (builder.Environment.IsEnvironment("Testing"))
                return;
            throw new InvalidOperationException("bff_secret_preparation_required");
        }
        SecretConsumerRole expectedConsumer = profile switch
        {
            BlazorHostProfile.Split => SecretConsumerRole.Bff,
            BlazorHostProfile.Combined => SecretConsumerRole.Combined,
            _ => throw new InvalidOperationException("secret_consumer_scope_invalid")
        };
        if (owner.Selection.Consumer != expectedConsumer
            || !string.Equals(owner.Selection.EnvironmentName, builder.Environment.EnvironmentName,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("secret_consumer_scope_invalid");
        var receipts = new AuthenticationCredentialAcquisitionReceipts(
            owner.Selection.AuthenticationProfileRevision);
        await builder.Configuration.AddPreparedBffConfigurationAsync(owner, receipts, cancellationToken);
        if (profile == BlazorHostProfile.Split)
            await builder.Services.PrepareExternalDataProtectionAsync(owner, "bff", cancellationToken);
        else if (!builder.Services.Any(descriptor =>
            descriptor.ServiceType == typeof(SecretAuthorityAcquisition)
            && ReferenceEquals(descriptor.ImplementationInstance, owner))
            || !builder.Services.Any(descriptor =>
                descriptor.ImplementationInstance is ExternalDataProtectionPartition { Name: "combined" }))
            throw new InvalidOperationException("api_secret_preparation_required");
        cancellationToken.ThrowIfCancellationRequested();
        builder.Services.TryAddSingleton(owner);
        builder.Services.AddSingleton(receipts);
        builder.Services.AddSingleton(new BffSecretPreparation(owner));
    }

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
            builder.Services.AddOptions<ExploreApiReadinessOptions>()
                .Bind(builder.Configuration.GetSection(ExploreApiReadinessOptions.SectionName));
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
                    .Enrich.FromLogContext()
                    .WithBffDiagnosticConfidentiality(),
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
            builder.Logging.AddFilter((category, _) => !BffDiagnosticLoggingExtensions.IsFrameworkCategory(category));
            builder.Logging.AddOpenTelemetry(options => options.IncludeScopes = false);
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
        _ = SecretAuthoritySelection.Select(builder.Configuration, builder.Environment.EnvironmentName,
            profile == BlazorHostProfile.Split ? SecretConsumerRole.Bff : SecretConsumerRole.Combined);
        if (!builder.Environment.IsEnvironment("Testing"))
        {
            var preparation = builder.Services.Select(descriptor => descriptor.ImplementationInstance)
                .OfType<BffSecretPreparation>().SingleOrDefault();
            if (preparation is null
                || preparation.Owner.Selection.Consumer !=
                    (profile == BlazorHostProfile.Split ? SecretConsumerRole.Bff : SecretConsumerRole.Combined)
                || !string.Equals(preparation.Owner.Selection.EnvironmentName,
                    builder.Environment.EnvironmentName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("bff_secret_preparation_required");
        }
        builder.Services.TryAddSingleton<AuthenticationCredentialAcquisitionReceipts>();

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
        builder.Services.AddScoped(services => OnboardingRequestOriginResolver.Resolve(
            services.GetRequiredService<IHttpContextAccessor>().HttpContext));
        builder.Services.AddInstanceAdminClient();
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
            tags: ["ready", "data-protection", "external-authority"]);

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
