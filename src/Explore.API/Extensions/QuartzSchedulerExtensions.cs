// ABOUTME: Registers Quartz.NET as the API host scheduler, persistence, and operator status surface.
// ABOUTME: Confines every Quartz dependency to the API layer so Application contracts stay scheduler-neutral.

using System.Globalization;
using Explore.API.Configuration;
using Explore.API.Scheduling;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Scheduling;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Quartz;
using Quartz.AspNetCore;
using Explore.Application.Configuration;
using Explore.Infrastructure;
using Explore.Infrastructure.Webhooks;
using Explore.Application.Features.OrganizerPaymentConnections;
using Explore.Application.Services.Webhooks;

namespace Explore.API.Extensions;

public static class QuartzSchedulerExtensions
{
    /// <summary>
    /// Registers the scheduler. Unlike the EF Core-backed scheduler it replaces, the ADO job store works on
    /// every supported primary database provider, so standalone SQLite deployments get durable scheduling too.
    /// </summary>
    public static IServiceCollection AddApiQuartzScheduler(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        bool enabled,
        bool useQuartzEmailDispatch)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var settings = configuration.GetSection(QuartzSchedulerSettings.SectionName).Get<QuartzSchedulerSettings>()
            ?? new QuartzSchedulerSettings();

        services.AddOptions<QuartzSchedulerSettings>()
            .Bind(configuration.GetSection(QuartzSchedulerSettings.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<QuartzSchedulerSettings>, QuartzSchedulerSettingsValidator>();

        // The administration policy is registered unconditionally so the operator surface can always explain why
        // it is unavailable. Without it a host with scheduling disabled would fail dependency resolution instead.
        services.TryAddSingleton<ISchedulerAdminPolicy, SchedulerAdminPolicy>();

        // Registered unconditionally alongside the policy: a refused action on a scheduler-less host is still an
        // attempted privileged operation and must leave a record.
        services.TryAddSingleton<ISchedulerAdminAuditSink, LoggingSchedulerAdminAuditSink>();

        if (!enabled || environment.IsEnvironment("Testing"))
        {
            services.TryAddSingleton<ISchedulerOperations, UnavailableSchedulerOperations>();
            return services;
        }

        var runtimeDatabase = PrimaryDatabaseConfiguration.BindRuntime(configuration);
        var connectionString = PrimaryDatabaseConfiguration.BuildConnectionString(runtimeDatabase).ConnectionString;

        if (settings.Enabled)
        {
            services.RemoveAll<IScheduledDeadlineDispatcher>();
            services.AddScoped<IScheduledDeadlineDispatcher, QuartzScheduledDeadlineDispatcher>();
        }
        services.AddSingleton<QuartzSchemaInitializer>();
        if (settings.Enabled)
        {
            services.TryAddSingleton<ISchedulerOperations, QuartzSchedulerOperations>();
        }
        else
        {
            services.TryAddSingleton<ISchedulerOperations, UnavailableSchedulerOperations>();
        }

        var desiredRecurringJobs = new HashSet<JobKey>();
        services.AddQuartz(quartz =>
        {
            quartz.SchedulerName = settings.SchedulerName;
            quartz.SchedulerId = settings.InstanceId;
            quartz.UseDefaultThreadPool(Math.Max(1, settings.MaxConcurrency));
            quartz.InterruptJobsOnShutdownWithWait = true;

            if (settings.UsePersistentStore)
            {
                ConfigurePersistentStore(quartz, settings, runtimeDatabase.Provider, connectionString);
            }
            else
            {
                quartz.UseInMemoryStore();
            }

            if (settings.Enabled)
            {
                RegisterRecurringJobs(quartz, configuration, useQuartzEmailDispatch, desiredRecurringJobs);
                RegisterMaintenanceSweeps(quartz, configuration, desiredRecurringJobs);
                RegisterOnDemandJobs(quartz);
            }

            // One listener observes every job, including jobs added later that forget to report themselves.
            // It is resolved from the container so it can reach BusinessMetrics; its own faults are
            // contained inside the listener, because an unhandled listener exception can disrupt the
            // scheduling cycle for every job in the process.
            quartz.AddJobListener<SchedulerTelemetryJobListener>();
        });

        // WaitForJobsToComplete keeps a mid-flight dispatch batch from being torn off during container shutdown.
        if (settings.Enabled)
        {
            services.AddQuartzServer(quartzServer =>
            {
                quartzServer.WaitForJobsToComplete = true;
                quartzServer.AwaitApplicationStarted = true;
            });
        }

        services.AddSingleton(new QuartzRecurringJobManifest(
            QuartzSchedulerKeys.OwnedRecurringJobs,
            desiredRecurringJobs));
        if (settings.Enabled)
        {
            services.AddHostedService<QuartzOwnedRecurringJobReconciler>();
        }

        return services;
    }

    /// <summary>
    /// Maps the read-only scheduler status surface behind the instance-admin policy. It is intentionally not
    /// part of the public API contract, so it stays out of OpenAPI generation and HAL link policies.
    /// </summary>
    public static WebApplication UseApiQuartzScheduler(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var settings = app.Configuration.GetSection(QuartzSchedulerSettings.SectionName).Get<QuartzSchedulerSettings>()
            ?? new QuartzSchedulerSettings();

        if (!settings.StatusEndpointEnabled)
        {
            return app;
        }

        var statusPath = string.IsNullOrWhiteSpace(settings.StatusEndpointPath)
            ? QuartzSchedulerSettings.DefaultStatusEndpointPath
            : settings.StatusEndpointPath;
        var statusPolicy = string.IsNullOrWhiteSpace(settings.StatusEndpointAuthorizationPolicy)
            ? QuartzSchedulerSettings.InstanceAdminPolicyName
            : settings.StatusEndpointAuthorizationPolicy;

        // Authorization runs as explicit middleware rather than endpoint metadata so an unauthenticated caller
        // is challenged before any scheduler state is read, matching the guardrail the previous scheduler had.
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(statusPath, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var authorizationService = context.RequestServices.GetRequiredService<IAuthorizationService>();
            var authorizationResult = await authorizationService.AuthorizeAsync(
                context.User,
                resource: null,
                statusPolicy);

            if (authorizationResult.Succeeded)
            {
                await next();
                return;
            }

            if (context.User.Identity?.IsAuthenticated == true)
            {
                await context.ForbidAsync();
                return;
            }

            await context.ChallengeAsync();
        });

        return app;
    }

    /// <summary>Maps the scheduler status endpoint itself; separated so hosts control endpoint ordering.</summary>
    public static WebApplication MapApiQuartzSchedulerEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var settings = app.Configuration.GetSection(QuartzSchedulerSettings.SectionName).Get<QuartzSchedulerSettings>()
            ?? new QuartzSchedulerSettings();

        if (!settings.StatusEndpointEnabled)
        {
            return app;
        }

        var statusPath = string.IsNullOrWhiteSpace(settings.StatusEndpointPath)
            ? QuartzSchedulerSettings.DefaultStatusEndpointPath
            : settings.StatusEndpointPath;
        var statusPolicy = string.IsNullOrWhiteSpace(settings.StatusEndpointAuthorizationPolicy)
            ? QuartzSchedulerSettings.InstanceAdminPolicyName
            : settings.StatusEndpointAuthorizationPolicy;

        app.MapGet(statusPath, QuartzSchedulerStatusEndpoint.HandleAsync)
            .RequireAuthorization(statusPolicy)
            .ExcludeFromDescription();

        return app;
    }

    public static async Task ApplyQuartzSchedulerSchemaAsync(this WebApplication app, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);

        var initializer = app.Services.GetService<QuartzSchemaInitializer>();
        if (initializer is null)
        {
            return;
        }

        var runtimeDatabase = PrimaryDatabaseConfiguration.BindRuntime(app.Configuration);
        await initializer.ApplyAsync(runtimeDatabase.Provider, cancellationToken);
    }

    public static bool IsQuartzSchedulerEnabled(IConfiguration configuration, IWebHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        if (environment.IsEnvironment("Testing"))
        {
            return false;
        }

        var settings = configuration.GetSection(QuartzSchedulerSettings.SectionName).Get<QuartzSchedulerSettings>()
            ?? new QuartzSchedulerSettings();
        return settings.Enabled;
    }

    private static void ConfigurePersistentStore(
        IServiceCollectionQuartzConfigurator quartz,
        QuartzSchedulerSettings settings,
        PrimaryDatabaseProvider provider,
        string connectionString)
    {
        quartz.UsePersistentStore(store =>
        {
            // Pointer-only payloads are plain strings, so property storage avoids any binary serialization
            // of application types into scheduler rows.
            store.UseProperties = true;
            store.UseSystemTextJsonSerializer();

            // Stated explicitly rather than left to the provider default: Quartz degrades gracefully when an
            // optional column is missing — it logs a warning and silently drops the behaviour that column
            // supports. Startup validation converts that into a loud failure naming the table.
            store.PerformSchemaValidation = settings.ValidateSchemaOnStartup;

            void ConfigureAdo(SchedulerBuilder.AdoProviderOptions ado)
            {
                ado.ConnectionString = connectionString;
                ado.TablePrefix = settings.TablePrefix;
            }

            switch (provider)
            {
                case PrimaryDatabaseProvider.PostgreSql:
                    store.UsePostgres(ConfigureAdo);
                    break;
                case PrimaryDatabaseProvider.Sqlite:
                    store.UseMicrosoftSQLite(ConfigureAdo);
                    break;
                case PrimaryDatabaseProvider.SqlServer:
                    store.UseSqlServer(ConfigureAdo);
                    break;
                case PrimaryDatabaseProvider.MariaDb:
                case PrimaryDatabaseProvider.MySql:
                    store.UseMySqlConnector(ConfigureAdo);
                    break;
                default:
                    throw new NotSupportedException(
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"Quartz persistent store is not configured for database provider '{provider}'."));
            }

            if (settings.ClusteringEnabled)
            {
                store.UseClustering(clustering => clustering.CheckinInterval =
                    TimeSpan.FromSeconds(Math.Max(1, settings.ClusterCheckinIntervalSeconds)));
            }
        });
    }

    private static void RegisterRecurringJobs(
        IServiceCollectionQuartzConfigurator quartz,
        IConfiguration configuration,
        bool useQuartzEmailDispatch,
        ISet<JobKey> desiredRecurringJobs)
    {
        if (useQuartzEmailDispatch)
        {
            AddCronJob<EmailDispatchDrainJob>(
                quartz,
                QuartzSchedulerKeys.EmailDispatchDrain,
                QuartzSchedulerKeys.EmailDispatchDrainCron,
                "Claims due EmailDispatchOutbox rows and executes approved dispatch transports.",
                desiredRecurringJobs);

            AddCronJob<EmailDispatchRecoveryScanJob>(
                quartz,
                QuartzSchedulerKeys.EmailDispatchRecoveryScan,
                QuartzSchedulerKeys.EmailDispatchRecoveryScanCron,
                "Marks stale EmailDispatchOutbox processing leases as Unknown for operator review.",
                desiredRecurringJobs);
        }

        // The safety net behind the per-order hold-expiry deadline. It is registered unconditionally because
        // correctness rests on it, not on the deadline trigger: pre-existing holds, lost triggers, and
        // interrupted recoveries are only ever cleaned up here.
        AddCronJob<InventoryHoldExpiryReconciliationJob>(
            quartz,
            QuartzSchedulerKeys.InventoryHoldExpiryReconciliation,
            QuartzSchedulerKeys.InventoryHoldExpiryReconciliationCron,
            "Releases expired registration capacity holds and recovers orders no hold deadline covered.",
            desiredRecurringJobs);

        AddCronJob<RegistrationFinalizationDrainJob>(
            quartz,
            QuartzSchedulerKeys.RegistrationFinalizationDrain,
            QuartzSchedulerKeys.RegistrationFinalizationDrainCron,
            "Drains durable registration-finalization effects under the shared fenced claim.",
            desiredRecurringJobs);

        AddCronJob<PaymentReconciliationDrainJob>(
            quartz,
            QuartzSchedulerKeys.PaymentReconciliationDrain,
            QuartzSchedulerKeys.PaymentReconciliationDrainCron,
            "Drains durable Checkout dispatch and retrieves authoritative provider payment state.",
            desiredRecurringJobs);

        AddCronJob<FairReturnOrchestrationJob>(
            quartz,
            QuartzSchedulerKeys.FairReturnOrchestration,
            QuartzSchedulerKeys.FairReturnOrchestrationCron,
            "Wakes the bounded durable fair-return " +
            "payment and refund orchestration drain.",
            desiredRecurringJobs);

        AddCronJob<RegistrationProviderSubmissionWriteDrainJob>(
            quartz,
            QuartzSchedulerKeys.RegistrationProviderSubmissionWriteDrain,
            QuartzSchedulerKeys.RegistrationProviderSubmissionWriteDrainCron,
            "Drains fenced outbound registration-provider submission effects.",
            desiredRecurringJobs);

        AddCronJob<RegistrationProviderSubscriptionLifecycleDrainJob>(
            quartz,
            QuartzSchedulerKeys.RegistrationProviderSubscriptionLifecycleDrain,
            QuartzSchedulerKeys.RegistrationProviderSubscriptionLifecycleDrainCron,
            "Renews provider subscriptions and reconciles response checkpoints.",
            desiredRecurringJobs);

        var integrationSync = Bind<IntegrationSyncProcessorSettings>(
            configuration,
            IntegrationSyncProcessorSettings.SectionName);
        AddSweepJob<IntegrationSyncDrainJob>(
            quartz,
            QuartzSchedulerKeys.IntegrationSyncDrain,
            "Drains tenant-bound integration sync rows with fenced provider handoff settlement.",
            integrationSync.Enabled,
            initialDelaySeconds: 5,
            TimeSpan.FromSeconds(integrationSync.PollingIntervalSeconds),
            desiredRecurringJobs);

        var pdsSync = Bind<PdsSyncSettings>(configuration, PdsSyncSettings.SectionName);
        AddSweepJob<PdsSyncDrainJob>(
            quartz,
            QuartzSchedulerKeys.PdsSyncDrain,
            "Drains durable AT Protocol PDS work with fenced leases and bounded concurrency.",
            pdsSync.Enabled,
            initialDelaySeconds: 0,
            TimeSpan.FromSeconds(pdsSync.PollingIntervalSeconds),
            desiredRecurringJobs);

        var localWebhook = Bind<WebhookDeliveryProcessorSettings>(
            configuration,
            WebhookDeliveryProcessorSettings.SectionName);
        AddSweepJob<LocalWebhookDeliveryDrainJob>(
            quartz,
            QuartzSchedulerKeys.LocalWebhookDeliveryDrain,
            "Recovers stale Local-provider leases and drains durable HTTP delivery attempts.",
            localWebhook.Enabled,
            localWebhook.InitialDelaySeconds,
            TimeSpan.FromSeconds(localWebhook.PollingIntervalSeconds),
            desiredRecurringJobs);

        var incomingWebhook = Bind<IncomingWebhookProcessingSettings>(
            configuration,
            IncomingWebhookProcessingSettings.SectionName);
        AddSweepJob<IncomingWebhookIntakeDrainJob>(
            quartz,
            QuartzSchedulerKeys.IncomingWebhookIntakeDrain,
            "Claims and processes durable incoming webhook messages in their tenant context.",
            incomingWebhook.Enabled,
            initialDelaySeconds: 0,
            TimeSpan.FromSeconds(incomingWebhook.PollIntervalSeconds),
            desiredRecurringJobs);
        AddSweepJob<IncomingWebhookEffectDrainJob>(
            quartz,
            QuartzSchedulerKeys.IncomingWebhookEffectDrain,
            "Executes durable incoming-webhook effect pointers with fenced settlement.",
            incomingWebhook.Enabled,
            initialDelaySeconds: 0,
            TimeSpan.FromSeconds(incomingWebhook.PollIntervalSeconds),
            desiredRecurringJobs);

        var bulkReplay = Bind<WebhookBulkReplaySettings>(
            configuration,
            WebhookBulkReplaySettings.SectionName);
        AddSweepJob<WebhookBulkReplayDrainJob>(
            quartz,
            QuartzSchedulerKeys.WebhookBulkReplayDrain,
            "Processes bounded queued bulk-replay operations.",
            bulkReplay.Enabled,
            bulkReplay.InitialDelaySeconds,
            TimeSpan.FromSeconds(bulkReplay.PollingIntervalSeconds),
            desiredRecurringJobs);

        var providerPublication = Bind<WebhookProviderPublicationProcessorSettings>(
            configuration,
            WebhookProviderPublicationProcessorSettings.SectionName);
        AddSweepJob<WebhookProviderPublicationDrainJob>(
            quartz,
            QuartzSchedulerKeys.WebhookProviderPublicationDrain,
            "Dispatches and reconciles durable provider-publication work.",
            providerPublication.Enabled,
            initialDelaySeconds: 0,
            TimeSpan.FromSeconds(providerPublication.PollingIntervalSeconds),
            desiredRecurringJobs);

    }

    /// <summary>
    /// Deadline-driven jobs are stored durably with no trigger of their own; runtime code attaches one-off
    /// triggers to them through <see cref="QuartzScheduledDeadlineDispatcher"/>.
    /// </summary>
    private static void RegisterOnDemandJobs(IServiceCollectionQuartzConfigurator quartz)
    {
        quartz.AddJob<EventReminderDispatchJob>(job => job
            .WithIdentity(QuartzSchedulerKeys.EventReminderDispatch)
            .WithDescription("Wakes a pre-persisted event reminder EmailDispatchOutbox row at its scheduled time.")
            .StoreDurably());

        quartz.AddJob<InventoryHoldExpiryJob>(job => job
            .WithIdentity(QuartzSchedulerKeys.InventoryHoldExpiry)
            .WithDescription("Releases one registration order's due capacity holds at its earliest hold expiry.")
            .StoreDurably());
    }

    /// <summary>
    /// Registers the periodic maintenance sweeps that previously each ran as a hand-rolled
    /// <c>BackgroundService</c> timer loop. Enablement and cadence stay in each feature's own settings section
    /// so operator-facing configuration keys are unchanged; only the mechanism moved.
    /// </summary>
    private static void RegisterMaintenanceSweeps(
        IServiceCollectionQuartzConfigurator quartz,
        IConfiguration configuration,
        ISet<JobKey> desiredRecurringJobs)
    {
        // Retained login rows must expire even after the optional ATProto provider is disabled.
        AddSweepJob<AtprotoTransientCleanupJob>(
            quartz,
            QuartzSchedulerKeys.AtprotoTransientCleanup,
            "Removes expired protected ATProto login records and assertion replay claims.",
            enabled: true,
            initialDelaySeconds: 60,
            TimeSpan.FromMinutes(1),
            desiredRecurringJobs);

        var idempotency = Bind<IdempotencyCleanupSettings>(configuration, IdempotencyCleanupSettings.SectionName);
        AddSweepJob<IdempotencyCleanupJob>(
            quartz,
            QuartzSchedulerKeys.IdempotencyCleanup,
            "Removes expired idempotency replay-cache rows.",
            idempotency.Enabled,
            idempotency.InitialDelaySeconds,
            idempotency.PollingIntervalMinutes,
            desiredRecurringJobs);

        var aiRetention = Bind<AiRetentionCleanupSettings>(configuration, AiRetentionCleanupSettings.SectionName);
        AddSweepJob<AiRetentionCleanupJob>(
            quartz,
            QuartzSchedulerKeys.AiRetentionCleanup,
            "Applies AI conversation retention policy across tenants.",
            aiRetention.Enabled,
            aiRetention.InitialDelaySeconds,
            aiRetention.PollingIntervalMinutes,
            desiredRecurringJobs);

        var emailRetention = Bind<EmailDispatchRetentionSettings>(configuration, EmailDispatchRetentionSettings.SectionName);
        AddSweepJob<EmailDispatchRetentionCleanupJob>(
            quartz,
            QuartzSchedulerKeys.EmailDispatchRetentionCleanup,
            "Applies email dispatch outbox and receipt retention policy.",
            emailRetention.Enabled,
            emailRetention.InitialDelaySeconds,
            emailRetention.PollingIntervalMinutes,
            desiredRecurringJobs);

        var webhookRetention = Bind<WebhookRetentionSettings>(configuration, WebhookRetentionSettings.SectionName);
        AddSweepJob<WebhookRetentionCleanupJob>(
            quartz,
            QuartzSchedulerKeys.WebhookRetentionCleanup,
            "Applies webhook message and delivery-attempt retention policy across tenants.",
            webhookRetention.Enabled,
            webhookRetention.InitialDelaySeconds,
            webhookRetention.PollingIntervalMinutes,
            desiredRecurringJobs);

        var storage = Bind<StorageReconciliationSettings>(configuration, StorageReconciliationSettings.SectionName);
        AddSweepJob<StorageReconciliationJob>(
            quartz,
            QuartzSchedulerKeys.StorageReconciliation,
            "Reconciles storage object state against the configured provider.",
            storage.Enabled,
            storage.InitialDelaySeconds,
            storage.PollingIntervalMinutes,
            desiredRecurringJobs);

        // Registration retention has no settings section: its cadence is fixed by the immutable-deadline
        // policy rather than being operator-tunable, so the schedule is stated here rather than bound.
        AddSweepJob<RegistrationRetentionCleanupJob>(
            quartz,
            QuartzSchedulerKeys.RegistrationRetentionCleanup,
            "Applies registration answer and PII retention deadlines for every active tenant.",
            enabled: true,
            initialDelaySeconds: 300,
            TimeSpan.FromDays(1),
            desiredRecurringJobs);

        AddSweepJob<ConfigurationPortabilityRetentionCleanupJob>(
            quartz,
            QuartzSchedulerKeys.ConfigurationPortabilityRetentionCleanup,
            "Removes expired protected configuration import, snapshot, and transfer bytes.",
            enabled: true,
            initialDelaySeconds: 300,
            TimeSpan.FromHours(1),
            desiredRecurringJobs);

        var organizerPayment = Bind<OrganizerPaymentReadinessReconciliationOptions>(
            configuration,
            OrganizerPaymentReadinessReconciliationOptions.SectionName);
        AddSweepJob<OrganizerPaymentReadinessReconciliationJob>(
            quartz,
            QuartzSchedulerKeys.OrganizerPaymentReadinessReconciliation,
            "Refreshes stale organizer payment provider readiness state.",
            organizerPayment.Enabled,
            organizerPayment.InitialDelaySeconds,
            TimeSpan.FromSeconds(Math.Max(5, organizerPayment.PollingIntervalSeconds)),
            desiredRecurringJobs);

        // Privacy erasure credential cleanup keeps its own settings shape: its cadence is expressed as a
        // TimeSpan poll interval shared with the provider lease machinery rather than as whole minutes.
        var privacyErasure = Bind<PrivacyErasureOptions>(configuration, PrivacyErasureOptions.SectionName);
        AddSweepJob<PrivacyErasureCredentialCleanupJob>(
            quartz,
            QuartzSchedulerKeys.PrivacyErasureCredentialCleanup,
            "Expires privacy-erasure provider credentials and locators past their retention horizon.",
            privacyErasure.RetentionCleanupEnabled,
            initialDelaySeconds: 0,
            privacyErasure.ProviderPollingInterval,
            desiredRecurringJobs);
    }

    private static TSettings Bind<TSettings>(IConfiguration configuration, string sectionName)
        where TSettings : new()
        => configuration.GetSection(sectionName).Get<TSettings>() ?? new TSettings();

    private static void AddSweepJob<TJob>(
        IServiceCollectionQuartzConfigurator quartz,
        JobKey jobKey,
        string description,
        bool enabled,
        int initialDelaySeconds,
        int intervalMinutes,
        ISet<JobKey> desiredRecurringJobs)
        where TJob : IJob
        => AddSweepJob<TJob>(quartz, jobKey, description, enabled, initialDelaySeconds, TimeSpan.FromMinutes(Math.Max(1, intervalMinutes)), desiredRecurringJobs);

    /// <summary>
    /// A disabled sweep is simply not registered, which keeps a turned-off feature out of the persistent
    /// store entirely instead of leaving a dormant trigger for operators to puzzle over.
    /// </summary>
    private static void AddSweepJob<TJob>(
        IServiceCollectionQuartzConfigurator quartz,
        JobKey jobKey,
        string description,
        bool enabled,
        int initialDelaySeconds,
        TimeSpan interval,
        ISet<JobKey> desiredRecurringJobs)
        where TJob : IJob
    {
        if (!enabled)
        {
            return;
        }

        desiredRecurringJobs.Add(jobKey);

        quartz.AddJob<TJob>(job => job
            .WithIdentity(jobKey)
            .WithDescription(description)
            .StoreDurably());

        quartz.AddTrigger(trigger => trigger
            .WithIdentity(QuartzSchedulerKeys.RecurringTriggerFor(jobKey))
            .ForJob(jobKey)
            .StartAt(DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, initialDelaySeconds)))
            .WithSimpleSchedule(schedule => schedule
                .WithInterval(interval)
                .RepeatForever()
                // Maintenance sweeps are idempotent and horizon-based, so a backlog of missed runs collapses
                // into the next pass rather than firing once per skipped interval after downtime.
                .WithMisfireHandlingInstructionNextWithRemainingCount()));
    }

    private static void AddCronJob<TJob>(
        IServiceCollectionQuartzConfigurator quartz,
        JobKey jobKey,
        string cronExpression,
        string description,
        ISet<JobKey> desiredRecurringJobs)
        where TJob : IJob
    {
        desiredRecurringJobs.Add(jobKey);
        quartz.AddJob<TJob>(job => job
            .WithIdentity(jobKey)
            .WithDescription(description)
            .StoreDurably());

        quartz.AddTrigger(trigger => trigger
            .WithIdentity(QuartzSchedulerKeys.RecurringTriggerFor(jobKey))
            .ForJob(jobKey)
            .WithCronSchedule(cronExpression, schedule => schedule
                .InTimeZone(TimeZoneInfo.Utc)
                // A restart after downtime should resume the normal cadence, not replay every missed occurrence:
                // the durable outbox already holds the backlog and the next pass drains it.
                .WithMisfireHandlingInstructionDoNothing()));
    }
}
