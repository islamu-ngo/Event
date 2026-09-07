using System.IO.Compression;
using Explore.API.BackgroundServices;
using Explore.API.Configuration;
using Explore.API.Extensions;
using Explore.API.HealthChecks;
using Explore.API.Mcp;
using Explore.API.OpenApi;
using Explore.API.Services;
using Explore.API.Services.Calendar;
using Explore.API.Services.OpenGraph;
using Explore.Application;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Events.OpenGraph;
using Explore.Application.Features.OrganizerPaymentConnections;
using Explore.Application.Services.Webhooks;
using Explore.Application.Telemetry;
using Explore.Infrastructure;
using Explore.Infrastructure.Geocoding;
using Explore.Infrastructure.HealthChecks;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.NotificationFanout;
using Explore.Infrastructure.Webhooks;
using Explore.Persistence;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Extensions;
using Explore.ServiceDefaults.Configuration;
using Explore.ServiceDefaults.HealthChecks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IO;
using ModelContextProtocol.Server;
using OpenFeature;
using OpenFeature.Hosting.Providers.Memory;
using Serilog;

namespace Explore.API.Hosting;

public sealed record ApiHostCompositionState(
    bool IsOpenApiGeneration,
    bool UseQuartzScheduler,
    bool HttpsRedirectionEnabled,
    bool OwnsDevelopmentMigrations = true);

public static class ApiHostServiceCollectionExtensions
{
    public static ApiHostCompositionState AddApiHostServices(
        this WebApplicationBuilder builder,
        Func<bool> isShuttingDown,
        bool ownsDevelopmentMigrations = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(isShuttingDown);

        var isOpenApiGeneration = OpenApiGenerationMode.IsBuildTimeGeneration;

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.KeepAliveTimeout =
                TimeSpan.FromSeconds(ApiHostStartupExtensions.GracefulShutdownSeconds + 5);
        });
        builder.Host.ConfigureHostOptions(options =>
        {
            options.ShutdownTimeout =
                TimeSpan.FromSeconds(ApiHostStartupExtensions.GracefulShutdownSeconds + 5);
        });

        builder.AddServiceDefaults();
        if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("cache")))
        {
            builder.AddRedisDistributedCache(connectionName: "cache");
        }
        else
        {
            builder.Services.AddDistributedMemoryCache();
        }

        builder.AddDistributedCacheReadinessCheck();
        builder.AddOidcDiscoveryReadinessCheck();
        if ((isOpenApiGeneration || builder.Environment.IsEnvironment("Testing"))
            && builder.Configuration[$"{SecretProviderOptions.SectionName}:Provider"] is null
            && builder.Configuration["SECRET_PROVIDER"] is null)
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{SecretProviderOptions.SectionName}:Provider"] = nameof(SecretProviderType.Environment)
            });
        }
        builder.Configuration.AddSecretAuthorityConfiguration(builder.Environment.EnvironmentName);
        builder.Services.AddSecretManagement(
            builder.Configuration,
            enableAuditing: true,
            enableRefreshService: !isOpenApiGeneration);
        builder.Services.AddHttpContextAccessor();

        var forwardedHeadersTrust = builder.Configuration
            .GetSection(ForwardedHeadersTrustOptions.SectionName)
            .Get<ForwardedHeadersTrustOptions>() ?? new ForwardedHeadersTrustOptions();

        if ((builder.Environment.IsEnvironment("Testing") || builder.Environment.IsDevelopment()) &&
            builder.Configuration[$"{ForwardedHeadersTrustOptions.SectionName}:TrustLoopbackProxy"] is null &&
            !forwardedHeadersTrust.TrustLoopbackProxy &&
            forwardedHeadersTrust.KnownProxies.Count == 0 &&
            forwardedHeadersTrust.KnownNetworks.Count == 0)
        {
            forwardedHeadersTrust.TrustLoopbackProxy = true;
        }

        forwardedHeadersTrust.Validate();
        builder.Services.Configure<ForwardedHeadersOptions>(options => forwardedHeadersTrust.ApplyTo(
            options,
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost));
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                ["application/json", "application/hal+json"]);
        });
        builder.Services.Configure<BrotliCompressionProviderOptions>(
            options => options.Level = CompressionLevel.Fastest);
        builder.Services.Configure<GzipCompressionProviderOptions>(
            options => options.Level = CompressionLevel.Fastest);

        builder.Services.AddApiCaching();
        builder.Services.AddScoped<IAtprotoDiscoveryCacheInvalidator, AtprotoDiscoveryCacheInvalidator>();
        builder.Services.AddScoped<IEventReportingOutputCacheInvalidator, EventReportingOutputCacheInvalidator>();
        builder.Services.AddSingleton<IEventCalendarFileBuilder, IcalNetEventCalendarFileBuilder>();
        builder.Services.AddSingleton<IEventOpenGraphImageRenderer, SkiaEventOpenGraphImageRenderer>();
        builder.Services.AddScoped<ICoopWebhookSignatureValidator, CoopWebhookSignatureValidator>();
        builder.Services.AddScoped<IIncomingWebhookVerifier, CoopIncomingWebhookVerifier>();
        builder.Services.AddScoped<IIncomingWebhookVerifier, SvixIncomingWebhookVerifier>();
        builder.Services.AddScoped<IIncomingWebhookVerifier, RegistrationProviderIncomingWebhookVerifier>();
        builder.Services.AddScoped<IRegistrationProviderCallbackBindingResolver, RegistrationProviderCallbackBindingResolver>();
        builder.Services.AddScoped<IRegistrationProviderCallbackReceiptProtector, RegistrationProviderCallbackReceiptProtector>();
        builder.Services.AddScoped<IRegistrationProviderCallbackUriBuilder, RegistrationProviderCallbackUriBuilder>();
        builder.Services.AddScoped<IIncomingWebhookVerifierRegistry, IncomingWebhookVerifierRegistry>();
        builder.Services.AddScoped<IIncomingWebhookIntakeService, IncomingWebhookIntakeService>();
        builder.Services.AddScoped<IManagedEventHealthProbe, ManagedEventHealthProbe>();
        builder.Services.AddRouting(options => options.LowercaseUrls = true);

        builder.Services.ConfigureApplicationServices(
            builder.Configuration,
            validateInstanceOperatorIdentityOnStart: !isOpenApiGeneration);
        builder.Services.ConfigureInfrastructureServices(builder.Configuration, builder.Environment);
        builder.Services.Configure<CerbosPolicyBootSyncOptions>(
            builder.Configuration.GetSection(CerbosPolicyBootSyncOptions.SectionName));
        builder.Services.PostConfigure<McpAdapterSettings>(settings =>
        {
            if (string.IsNullOrWhiteSpace(settings.EndpointPath))
            {
                return;
            }

            var endpointPath = settings.EndpointPath.Trim();
            settings.EndpointPath = endpointPath.StartsWith('/') ? endpointPath : $"/{endpointPath}";
        });
        builder.Services.AddOptions<McpAdapterSettings>()
            .Bind(builder.Configuration.GetSection(McpAdapterSettings.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<McpAdapterSettings>, McpAdapterSettingsValidator>();
        builder.Services.AddScoped<IMcpRuntimeStateService, McpRuntimeStateService>();

        var mcpAdapterSettings = builder.Configuration
            .GetSection(McpAdapterSettings.SectionName)
            .Get<McpAdapterSettings>() ?? new McpAdapterSettings();
        var emailDispatchProcessorSettings = builder.Configuration
            .GetSection(EmailDispatchProcessorSettings.SectionName)
            .Get<EmailDispatchProcessorSettings>() ?? new EmailDispatchProcessorSettings();
        var emailDispatchRabbitMqSettings = builder.Configuration
            .GetSection(EmailDispatchRabbitMqSettings.SectionName)
            .Get<EmailDispatchRabbitMqSettings>() ?? new EmailDispatchRabbitMqSettings();
        var useQuartzEmailDispatch = emailDispatchProcessorSettings.Enabled &&
            emailDispatchProcessorSettings.Mode == EmailDispatchProcessorMode.Quartz;
        var useQuartzScheduler = !isOpenApiGeneration && !builder.Environment.IsEnvironment("Testing");

        builder.Services.AddSingleton<EmailDispatchHostedDrainRunner>();
        builder.Services.AddApiQuartzScheduler(
            builder.Configuration,
            builder.Environment,
            enabled: useQuartzScheduler,
            useQuartzEmailDispatch);

        var skipDbContext = builder.Environment.IsEnvironment("Testing") || isOpenApiGeneration;
        builder.Services.ConfigurePersistenceServices(
            builder.Configuration,
            skipDbContextRegistration: skipDbContext,
            skipLookupCacheInitializer: isOpenApiGeneration,
            environmentName: builder.Environment.EnvironmentName);
        var dataProtection = builder.Services.AddDataProtection()
            .SetApplicationName("islamu-event");
        if (!skipDbContext)
        {
            dataProtection.PersistKeysToDbContext<DataProtectionKeyContext>();
        }

        builder.Services.AddScoped<ITenantResolverService, Explore.Infrastructure.Services.TenantResolverService>();
        builder.Services.AddScoped<ITenantContext, Explore.Infrastructure.Services.TenantContext>();
        builder.Services.AddHateoas();
        builder.Services.AddHateoasAssemblers();
        builder.Services.AddApiMediaTypeVersioning();
        builder.Services.AddSingleton<BusinessMetrics>();

        // The scheduler's telemetry listener depends on the narrow recording port, not on the whole metrics
        // surface; the same singleton instance backs both so job metrics land on the platform meter.
        builder.Services.AddSingleton<ISchedulerJobTelemetry>(
            provider => provider.GetRequiredService<BusinessMetrics>());
        builder.Services.AddSingleton<TranslationMetrics>();
        builder.Services.AddSingleton<ProjectionMetrics>();
        builder.Services.AddSingleton<RecyclableMemoryStreamManager>();

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(ApiHostServiceCollectionExtensions).Assembly)
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.TypeInfoResolverChain.Add(
                    Explore.Application.Serialization.ExploreJsonContext.Default);
                options.JsonSerializerOptions.PropertyNamingPolicy =
                    System.Text.Json.JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.DefaultIgnoreCondition =
                    System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
                options.JsonSerializerOptions.UnmappedMemberHandling =
                    System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
                options.JsonSerializerOptions.Converters.Add(
                    new Explore.API.Serialization.OptionalUpdateJsonConverterFactory());
                options.JsonSerializerOptions.Converters.Add(
                    new System.Text.Json.Serialization.JsonStringEnumConverter());
            });
        builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory =
                Explore.API.ExceptionHandling.ApiValidationProblemDetailsFactory.CreateInvalidModelStateResponse;
            options.ClientErrorMapping[StatusCodes.Status415UnsupportedMediaType].Title =
                "Unsupported media type";
            options.ClientErrorMapping[StatusCodes.Status415UnsupportedMediaType].Link =
                "https://tools.ietf.org/html/rfc9110#section-15.5.16";
        });
        builder.Services.AddApiExceptionHandling();
        builder.Services.AddLocalization();
        builder.Services.Configure<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>(options =>
        {
            var cultures = Explore.Domain.Common.Localization.CultureRegistry.GetAll()
                .Select(entry => new System.Globalization.CultureInfo(entry.Code))
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

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGenWithAuth(builder.Configuration);
        builder.Services.AddOpenApi("islamu-event", options =>
        {
            options.ShouldInclude = description => true;
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Event API";
                document.Info.Version = "v0.1";
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer<KeycloakOpenApiSecurityTransformer>();
            options.AddDocumentTransformer<ManagedControlPlaneOpenApiSecurityTransformer>();
            options.AddDocumentTransformer<PrivacyErasureReceiptOpenApiSecurityTransformer>();
            options.AddDocumentTransformer<AdmissionScannerOpenApiSecurityTransformer>();
            options.AddDocumentTransformer<HalDtoSchemaTransformer>();
            options.AddDocumentTransformer<QuotaExceededDetailsOpenApiTransformer>();
            options.AddDocumentTransformer<OpenApiStringEnumDocumentTransformer>();
            options.AddDocumentTransformer<SetupLiveRequestBodyTransformer>();
            options.AddDocumentTransformer<OperationIdInvariantTransformer>();
            options.AddOperationTransformer<EndpointClassificationTransformer>();
            options.AddOperationTransformer<KeycloakOpenApiSecurityTransformer>();
            options.AddOperationTransformer<ManagedControlPlaneOpenApiSecurityTransformer>();
            options.AddOperationTransformer<PrivacyErasureReceiptOpenApiSecurityTransformer>();
            options.AddOperationTransformer<AdmissionScannerOpenApiSecurityTransformer>();
            options.AddOperationTransformer<StorageUploadRequestBodyTransformer>();
            options.AddOperationTransformer<SetupLiveRequestBodyTransformer>();
            options.AddOperationTransformer<ConfigurationImportRequestBodyTransformer>();
            options.AddOperationTransformer<EventOpenGraphImageResponseTransformer>();
            options.AddOperationTransformer<ConfigurationManifestExportResponseTransformer>();
        });

        builder.AddApiBackgroundProcessing(
            isOpenApiGeneration,
            emailDispatchProcessorSettings,
            emailDispatchRabbitMqSettings);

        builder.Services.AddApiCors(builder.Configuration);
        builder.Host.UseSerilog(
            (context, services, loggerConfiguration) => loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext(),
            writeToProviders: true);
        builder.Services.AddApiAuthentication(
            builder.Configuration,
            builder.Environment,
            skipAuthorityWarmup: isOpenApiGeneration ||
                builder.Configuration.GetValue<bool>("Testing:SkipJwtAuthorityWarmup"));

        builder.Services.AddHsts(options =>
        {
            options.Preload = true;
            options.IncludeSubDomains = true;
            options.MaxAge = TimeSpan.FromDays(365);
        });
        var httpsRedirectionEnabled = builder.Configuration.GetValue("HttpsRedirection:Enabled", true);
        builder.Services.AddHttpsRedirection(options =>
        {
            options.RedirectStatusCode = StatusCodes.Status308PermanentRedirect;
            var configuredHttpsPort = builder.Configuration.GetValue<int?>("HttpsRedirection:HttpsPort");
            if (configuredHttpsPort.HasValue)
            {
                options.HttpsPort = configuredHttpsPort.Value;
            }
            else if (builder.Environment.IsDevelopment())
            {
                options.HttpsPort = 7039;
            }
        });

        builder.Services.AddHealthChecks()
            .AddCheck(
                "shutdown",
                () => isShuttingDown()
                    ? HealthCheckResult.Unhealthy("Application is shutting down")
                    : HealthCheckResult.Healthy(),
                tags: ["live", "ready"])
            .AddDbContextCheck<ExploreDbContext>("database", tags: ["ready"])
            .AddCheck<SmtpHealthCheck>(
                "smtp",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "smtp", "infrastructure"],
                timeout: TimeSpan.FromSeconds(5))
            .AddCheck<EmailDispatchHealthCheck>(
                "email-dispatch",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "email", "dispatch", "infrastructure"])
            .AddCheck<EmailDispatchRetentionCleanupHealthCheck>(
                "email-dispatch-retention-cleanup",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "email", "retention", "cleanup", "infrastructure"])
            .AddCheck<EmailDispatchRabbitMqHealthCheck>(
                "email-dispatch-rabbitmq",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "email", "dispatch", "rabbitmq", "infrastructure"])
            .AddCheck<WebPushDispatchHealthCheck>(
                "web-push-dispatch",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "web-push", "dispatch", "infrastructure"])
            .AddCheck<NotificationFanoutHealthCheck>(
                "notification-fanout",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "notification", "fanout", "infrastructure"])
            .AddCheck<SchedulerHealthCheck>(
                "scheduler",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "scheduler", "background", "infrastructure"])
            .AddCheck<IdempotencyCleanupHealthCheck>(
                "idempotency-cleanup",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "idempotency", "cleanup", "infrastructure"])
            .AddCheck<AiRetentionCleanupHealthCheck>(
                "ai-retention-cleanup",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "ai", "retention", "cleanup", "infrastructure"])
            .AddCheck<McpAdapterHealthCheck>(
                "mcp-adapter",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "ai", "mcp", "infrastructure"])
            .AddCheck<StorageReadinessHealthCheck>(
                "storage",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "storage", "infrastructure"])
            .AddCheck<StorageReconciliationHealthCheck>(
                "storage-reconciliation",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "storage", "reconciliation", "infrastructure"])
            .AddCheck<LocalWebhookDeliveryHealthCheck>(
                "webhook-local-delivery",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "webhooks", "local", "infrastructure", "webhook-local-readiness"])
            .AddCheck<IncomingWebhookEffectHealthCheck>(
                "webhook-coop-effects",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "webhooks", "coop", "infrastructure", "webhook-coop-effect-readiness"])
            .AddCheck<QueueDrainReadinessHealthCheck>(
                "queue-drains",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "scheduler", "background", "infrastructure", "queue-drain-readiness"])
            .AddCheck<PaymentReconciliationHealthCheck>(
                "payment-reconciliation",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "payments", "reconciliation", "infrastructure"])
            .AddCheck<RefundReconciliationHealthCheck>(
                "refund-reconciliation",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "payments", "refunds", "reconciliation", "infrastructure"])
            .AddCheck<SvixWebhookProviderHealthCheck>(
                "webhook-svix-provider",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "webhooks", "svix", "infrastructure", "webhook-svix-readiness"])
            .AddCheck<ListmonkIntegrationHealthCheck>(
                "listmonk-integration",
                failureStatus: HealthStatus.Degraded,
                tags: ["ready", "integrations", "listmonk", "infrastructure"],
                timeout: TimeSpan.FromSeconds(5))
            .AddCheck<CerbosReadinessHealthCheck>(
                "cerbos",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "cerbos", "infrastructure"])
            .AddCheck<AtprotoJetstreamReadinessHealthCheck>(
                "atproto-jetstream",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "atproto", "federation", "infrastructure"])
            .AddCheck<AiProviderHealthCheck>(
                "ai-provider",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "ai", "provider", "infrastructure"])
            .AddCheck<GeocodingReadinessHealthCheck>(
                "geocoding",
                failureStatus: HealthStatus.Degraded,
                tags: ["ready", "geocoding", "provider", "infrastructure"])
            .AddCheck<PrivacyErasureReadinessHealthCheck>(
                "privacy-erasure",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "privacy", "erasure", "infrastructure"])
            .AddCheck<EventLocationReviewQueueHealthCheck>(
                "event-location-privacy-review",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready", "privacy", "location", "remediation", "infrastructure"]);

        builder.Services.AddApiRequestTimeouts(builder.Configuration);
        if (!isOpenApiGeneration)
        {
            builder.Services.AddMcpServer()
                .WithHttpTransport(options => options.Stateless = mcpAdapterSettings.Stateless)
                .AddAuthorizationFilters()
                .WithTools<AiToolRegistryMcpTools>()
                .WithTools<AiAssistantMcpTools>()
                .WithTools<EventManagementMcpTools>()
                .WithResources<AiAssistantMcpResources>()
                .WithResources<EventManagementMcpResources>()
                .WithPrompts<AiAssistantMcpPrompts>();
            // Scoped because the disclosure gateway it wraps resolves per-request tenant disclosure policy.
            builder.Services.AddScoped<Explore.API.Mcp.EventMcpLocationDisclosureGuard>();
            builder.Services.AddSingleton<IConfigureOptions<McpServerOptions>, AiMcpProjectedToolOptionsSetup>();
        }

        builder.Services.AddApiRateLimiting(builder.Configuration, builder.Environment);
        builder.Services.AddOpenFeature(featureBuilder =>
            featureBuilder.AddInMemoryProvider(flags => { }));

        return new ApiHostCompositionState(
            isOpenApiGeneration,
            useQuartzScheduler,
            httpsRedirectionEnabled,
            ownsDevelopmentMigrations);
    }


    /// <summary>
    /// Registers every long-running in-process worker the API host owns, in one place, with its enablement
    /// condition stated inline.
    /// <para>
    /// The topology is deliberately literal rather than data-driven: which workers run in which environment,
    /// and under which feature flag, is an operational fact self-hosters need to be able to read off the
    /// composition root. Periodic maintenance sweeps are absent here because they moved to the Quartz
    /// scheduler — see <c>QuartzSchedulerExtensions.RegisterMaintenanceSweeps</c>.
    /// </para>
    /// <para>
    /// OpenAPI generation runs the host to emit a document, so nothing that would start doing real work may
    /// be registered during it; the <c>Testing</c> environment is excluded for the same reason.
    /// </para>
    /// </summary>
    private static void AddApiBackgroundProcessing(
        this WebApplicationBuilder builder,
        bool isOpenApiGeneration,
        EmailDispatchProcessorSettings emailDispatchProcessorSettings,
        EmailDispatchRabbitMqSettings emailDispatchRabbitMqSettings)
    {
        var isTesting = builder.Environment.IsEnvironment("Testing");

        if (!isOpenApiGeneration && !isTesting)
        {
            builder.Services.AddHostedService<Explore.Infrastructure.Services.Federation.AtprotoJetstreamSubscriber>();
        }

        builder.Services.AddSingleton<IAiAssistantRunQueue, AiAssistantRunQueue>();
        if (isOpenApiGeneration)
        {
            return;
        }

        builder.Services.AddHostedService<AiAssistantRunWorker>();
        builder.Services.AddHostedService<OutboxProcessor>();

        if (!isTesting)
        {
            builder.Services.AddHostedService<NotificationFanoutProcessor>();
            builder.Services.AddHostedService<WebhookEventTypeCatalogSyncWorker>();
            builder.Services.AddHostedService<SvixWebhookEventTypeSyncWorker>();
            builder.Services.AddHostedService<ManagedControlPlaneRegistrationWorker>();
            builder.Services.AddHostedService<AiProviderSettingsBootstrapWorker>();
            builder.Services.AddSingleton<CerbosPolicyBootSyncRunner>();
            builder.Services.AddHostedService<CerbosPolicyBootSyncWorker>();
        }

        // The hosted-service dispatch loop is the scheduler-free fallback for the same drain service the
        // Quartz `email-dispatch-drain` job uses; exactly one of the two modes runs.
        if (emailDispatchProcessorSettings.Enabled &&
            emailDispatchProcessorSettings.Mode == EmailDispatchProcessorMode.HostedService)
        {
            builder.Services.AddHostedService<EmailDispatchProcessor>();
        }

        if (!emailDispatchRabbitMqSettings.Enabled)
        {
            return;
        }

        builder.Services.AddHostedService<EmailDispatchRabbitMqPointerPublisherService>();
        builder.Services.AddHostedService<EmailDispatchRabbitMqConsumerService>();
        if (emailDispatchRabbitMqSettings.DeadLetterReplayEnabled)
        {
            builder.Services.AddHostedService<EmailDispatchRabbitMqDeadLetterReplayService>();
        }
    }
}
