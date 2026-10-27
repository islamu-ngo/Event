namespace Explore.Infrastructure.Tests.Infrastructure.ConfigurationManifest;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Managed;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Infrastructure;
using Explore.Infrastructure.ConfigurationManifest;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

public sealed class ConfigurationManifestStartupCompositionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DeferredStartup_RetainsOriginalRunnerAndRuntimeDeliveryWithoutClaimingPendingEffects(bool previouslyImmediate)
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder().Build();
        services.AddSingleton(configuration);
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(Substitute.For<IHostApplicationLifetime>());
        services.AddSingleton<IGlobalExploreDbContextFactory>(new GlobalExploreDbContextFactory(
            new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Global,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = Path.Combine(Path.GetTempPath(), $"manifest-startup-{Guid.CreateVersion7():N}.db")
            }));
        services.AddSingleton(new ExploreDbContext(
            new DbContextOptionsBuilder<ExploreDbContext>().Options));
        services.AddSingleton(
            Substitute.For<IDbContextFactory<ExploreDbContext>>());
        var outbox = new PendingEffects();
        var operations = Substitute.For<IConfigurationManifestOperationRepository>();
        var settings = Substitute.For<IHierarchicalSettingsResolver>();
        var observation = new EffectObservation();
        services.AddSingleton<IConfigurationManifestEffectOutboxRepository>(outbox);
        services.AddSingleton(operations);
        services.AddSingleton(settings);
        services.AddSingleton<IHierarchicalSettingsReader>(settings);
        services.AddSingleton(Substitute.For<ITypedSettingsDocumentResolver>());
        services.AddSingleton(Substitute.For<IConfigurationImportEffectOutboxRepository>());
        services.AddSingleton(Substitute.For<IConfigurationImportOperationRepository>());
        services.AddSingleton(Substitute.For<ITenantRepository>());
        services.AddSingleton<INotificationHandler<SettingChangedNotification>>(observation);
        services.AddConfigurationManifestPersistence();
        if (previouslyImmediate)
            services.AddScoped<IConfigurationManifestEffectDeliveryStrategy, ConfigurationManifestEffectDelivery>();
        services.AddConfigurationManifestStartup(
            configuration,
            ConfigurationManifestEffectDeliveryMode.DeferredToRuntime);
        services.AddConfigurationManifestApplication();

        await using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                // Resolve the actual target services below. This bounded fixture does not
                // claim to validate the unrelated full API payment/import authorization graph.
                ValidateScopes = true
            });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        var runner = scope.ServiceProvider
            .GetRequiredService<Explore.Application.Features.ConfigurationManifest.Application.IConfigurationManifestStartupRunner>();
        var applier = scope.ServiceProvider
            .GetRequiredService<IConfigurationManifestApplier>();
        var effects = scope.ServiceProvider
            .GetRequiredService<IConfigurationManifestEffectDeliveryStrategy>();

        await Assert.That(runner.GetType().Assembly)
            .IsSameReferenceAs(typeof(ConfigurationManifestPersistenceServicesRegistration).Assembly);
        await Assert.That(runner.GetType().IsNotPublic).IsTrue();
        await Assert.That(runner.GetType().IsSealed).IsTrue();
        var bootstrapOwner = scope.ServiceProvider.GetRequiredService<IConfigurationManifestBootstrapOwnerFactory>();
        await Assert.That(bootstrapOwner.GetType().IsNotPublic).IsTrue();
        await Assert.That(bootstrapOwner.GetType().IsSealed).IsTrue();
        await runner.RunAsync(CancellationToken.None);
        await Assert.That(applier).IsTypeOf<ConfigurationManifestApplier>();
        await Assert.That(effects)
            .IsTypeOf<DeferredConfigurationManifestEffectDelivery>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IConfigurationManifestEffectDispatcher>();
        await Assert.That(dispatcher).IsTypeOf<ConfigurationManifestEffectDispatcher>();
        await Assert.That(scope.ServiceProvider
                .GetRequiredService<IConfigurationImportEffectDelivery>())
            .IsTypeOf<ConfigurationImportEffectDelivery>();
        await Assert.That(services.Single(service => service.ServiceType == typeof(IHumanConfigurationImportAdmission))
            .ImplementationType).IsEqualTo(typeof(HumanConfigurationImportAdmission));
        await Assert.That(scope.ServiceProvider
                .GetService<IConfigurationDirectTransferChunkStore>())
            .IsNull();

        DateTime occurred = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        var operation = ConfigurationManifestOperation.Create(ConfigurationManifestAuditMode.Bootstrap,
            "configuration.islamu.org/v1alpha2", "ConfigurationManifest", "deferred-composition",
            new string('a', 64), ConfigurationManifestOperationStatus.Applied,
            0, 0, 0, 0, null, null, occurred, occurred,
            instanceSectionDigest: new string('b', 64), bootstrapGeneration: 1,
            instanceChangedSettingKeyNames: ["branding.display_name"]);
        Guid messageId = Guid.CreateVersion7();
        operation.BindEffectOutbox(messageId);
        await outbox.Create(ConfigurationManifestEffectOutbox.Create(operation, []));
        await effects.DrainPendingAsync(CancellationToken.None);
        await effects.DeliverAsync(messageId, CancellationToken.None);
        var pending = outbox.Message!;
        await Assert.That(pending.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(pending.NextRetryAt).IsNull();
        await Assert.That(observation.Notifications).IsEmpty();
        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatcher.DispatchAsync(pending, CancellationToken.None));

        // A semantic repository image, not a native execution claim: the native worker tests
        // separately prove actual admission and settlement on its original Runtime owner.
        pending.Status = OutboxMessageStatus.Processing;
        pending.NextRetryAt = occurred.AddMinutes(5);
        await dispatcher.DispatchAsync(pending, CancellationToken.None);
        await Assert.That(observation.Notifications.Select(notification => notification.Key))
            .IsEquivalentTo(["branding.display_name"]);
        await Assert.That(observation.Notifications.Single().ChangedAt).IsEqualTo(operation.StartedAt);
    }

    [Test]
    public async Task ProductionInfrastructure_SelectsDeferredBootstrapAndRetainsHumanImportAdmission()
    {
        var services = new ServiceCollection();
        services.AddConfigurationManifestApplication();
        services.ConfigureInfrastructureServices(new ConfigurationBuilder().Build());
        services.AddConfigurationManifestApplication();
        await Assert.That(services.Single(service => service.ServiceType == typeof(IConfigurationManifestEffectDeliveryStrategy))
            .ImplementationType).IsEqualTo(typeof(DeferredConfigurationManifestEffectDelivery));
        await Assert.That(services.Single(service => service.ServiceType == typeof(IConfigurationManifestEffectDispatcher))
            .ImplementationType).IsEqualTo(typeof(ConfigurationManifestEffectDispatcher));
        await Assert.That(services.Single(service => service.ServiceType == typeof(IHumanConfigurationImportAdmission))
            .ImplementationType).IsEqualTo(typeof(HumanConfigurationImportAdmission));
    }

    private sealed class EffectObservation : INotificationHandler<SettingChangedNotification>
    {
        internal List<SettingChangedNotification> Notifications { get; } = [];
        public Task HandleAsync(SettingChangedNotification notification, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Notifications.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class PendingEffects : IConfigurationManifestEffectOutboxRepository
    {
        internal OutboxMessage? Message { get; private set; }
        public Task<OutboxMessage> Create(OutboxMessage message)
        {
            if (Message is not null) throw new InvalidOperationException("An effect is already retained.");
            Message = message;
            return Task.FromResult(message);
        }
        public Task<OutboxMessage?> GetByIdAsync(Guid messageId, CancellationToken cancellationToken) =>
            Task.FromResult(Message?.Id == messageId ? Message : null);
        public Task<IReadOnlyList<OutboxMessage>> GetPendingManifestEffectsAsync(int batchSize, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Bootstrap cannot enumerate the runtime worker's pending effects.");
        public Task<DateTime?> TryClaimForProcessing(Guid id, DateTime claimedAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Bootstrap cannot claim the runtime worker's effect.");
        public Task<bool> MarkAsCompleted(Guid id, DateTime processingLeaseExpiresAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Bootstrap cannot settle the runtime worker's effect.");
        public Task<OutboxFailureTransition> MarkAsFailed(Guid id, DateTime processingLeaseExpiresAt, string error,
            bool isRetryable, int retryDelaySeconds, DateTime failedAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Bootstrap cannot fail the runtime worker's effect.");
    }
}
