using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Caching;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Models;
using Explore.Application.Services;
using Explore.Application.Services.Registration;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Services.Moderation;
using Explore.Infrastructure.Services.Registration;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Explore.Tests.Shared.Telemetry;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class AdmissionCompositeDispatchTests
{
    [Test]
    [Arguments("credential", null)]
    [Arguments("credential", "null")]
    [Arguments("credential", "{")]
    [Arguments("credential", "{\"Unexpected\":true}")]
    [Arguments("recovery-request", null)]
    [Arguments("recovery-request", "null")]
    [Arguments("recovery-request", "{")]
    [Arguments("recovery-request", "{\"Unexpected\":true}")]
    [Arguments("recovery-delivery", null)]
    [Arguments("recovery-delivery", "null")]
    [Arguments("recovery-delivery", "{")]
    [Arguments("recovery-delivery", "{\"Unexpected\":true}")]
    public async Task PersistedMalformedAdmissionPointerFailsBeforeDelivery(string route, string? payload)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new ExploreDbContext(options);
        Guid tenantId = Guid.CreateVersion7();
        context.TenantContext = new AdmissionTenantContext(tenantId);
        await context.Database.EnsureCreatedAsync();
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = nameof(AdmissionTicket),
            AggregateId = Guid.CreateVersion7(),
            EventType = route switch
            {
                "credential" => AdmissionDeliveryEvents.CredentialDeliveryRequested,
                "recovery-request" => AdmissionRecoveryDeliveryEvents.RecoveryRequestProcessingRequested,
                _ => AdmissionRecoveryDeliveryEvents.RecoveryDeliveryRequested
            },
            Payload = payload,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            MaxRetries = 10
        };
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        OutboxMessage persisted = await context.OutboxMessages.SingleAsync();
        await Assert.That(persisted.Payload).IsEqualTo(payload);
        var unitOfWork = new EfCoreUnitOfWork(context);
        var recoveryService = new AdmissionRecoveryService(
            new AdmissionRecoveryRepository(context),
            new AdmissionRecoveryIdentityResolver(context),
            Substitute.For<IAdmissionRecoveryCapabilityService>(),
            unitOfWork,
            TimeProvider.System,
            Substitute.For<IAdmissionRecoveryDeliveryStager>(),
            Substitute.For<IAdmissionRecoveryAuditService>(),
            Substitute.For<IAdmissionRecoveryRateLimiter>(),
            Substitute.For<IAdmissionRecoveryRequestStager>(),
            NullLogger<AdmissionRecoveryService>.Instance);
        CompositeOutboxMessageDispatcher composite = CreateComposite(
            new AdmissionCredentialDeliveryOutboxHandler(
                context, Substitute.For<IAdmissionDeliveryEnvelopeProtector>(),
                Substitute.For<IAdmissionCredentialDirectDeliveryChannel>(), TimeProvider.System),
            new AdmissionRecoveryRequestOutboxHandler(
                context, Substitute.For<IAdmissionRecoveryRequestEnvelopeProtector>(),
                recoveryService, unitOfWork, TimeProvider.System),
            new AdmissionRecoveryDeliveryOutboxHandler(
                context, Substitute.For<IAdmissionRecoveryDeliveryEnvelopeProtector>(),
                Substitute.For<IAdmissionRecoveryDirectDeliveryChannel>(), TimeProvider.System));

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => composite.DispatchAsync(persisted));

        string classification = route switch
        {
            "credential" => "Admission delivery pointer is malformed.",
            "recovery-request" => "Recovery request pointer is malformed.",
            _ => "Recovery delivery pointer is malformed."
        };
        await Assert.That(failure.Message).IsEqualTo(classification);
        await Assert.That(failure.InnerException is JsonException).IsTrue();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        context.ChangeTracker.Clear();
        OutboxMessage unchanged = await context.OutboxMessages.SingleAsync();
        await Assert.That(unchanged.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(unchanged.RetryCount).IsEqualTo(0);
        await Assert.That(unchanged.NextRetryAt).IsNull();
        await Assert.That(unchanged.LastError).IsNull();
        await Assert.That(await context.AdmissionRecoveryRequestIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.AdmissionRecoveryDeliveryIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.AdmissionDeliveryIntents.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task ProductionAdmissionDispatcherRoutesThroughCompositeAndRetiresOnlyAfterAcknowledgement()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new ExploreDbContext(options);
        Guid tenantId = Guid.CreateVersion7();
        context.TenantContext = new AdmissionTenantContext(tenantId);
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        DateTime now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        string bearer = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        const string recipient = "attendee@example.test";
        var envelopeProtector = new AdmissionDeliveryEnvelopeProtector(new EphemeralDataProtectionProvider());
        AdmissionProtectedDeliveryMaterial protectedMaterial = envelopeProtector.Protect(
            new AdmissionCredentialDeliveryEnvelope(recipient, bearer));
        AdmissionTicket ticket = await SeedOwningOrderAsync(context, tenantId, now);
        var intent = new AdmissionDeliveryIntent(
            Guid.CreateVersion7(), tenantId, Guid.CreateVersion7(), ticket.RegistrationTicketAssignmentId, ticket.Id,
            protectedMaterial.Ciphertext, protectedMaterial.ProtectionVersion, now);
        context.AdmissionDeliveryIntents.Add(intent);
        await context.SaveChangesAsync();
        EmailMessage? observedMessage = null;
        var email = Substitute.For<IEmailService>();
        email.SendAsync(Arg.Do<EmailMessage>(message => observedMessage = message), Arg.Any<CancellationToken>())
            .Returns(EmailResult.Ok("accepted"));
        var channel = new AdmissionEmailCredentialDeliveryChannel(email);

        CompositeOutboxMessageDispatcher composite = CreateComposite(
            new AdmissionCredentialDeliveryOutboxHandler(context, envelopeProtector, channel, new FixedTimeProvider(now)));
        var dispatcher = new AdmissionDeliveryIntentDispatcher(
            context, composite, NullLogger<AdmissionDeliveryIntentDispatcher>.Instance);
        var unsafeMessage = new OutboxMessage
        {
            Id = intent.Id,
            AggregateType = nameof(AdmissionTicket),
            AggregateId = intent.AdmissionTicketId,
            EventType = AdmissionDeliveryEvents.CredentialDeliveryRequested,
            Payload = JsonSerializer.Serialize(new
            {
                TenantId = tenantId,
                AdmissionTicketId = intent.AdmissionTicketId,
                DeliveryIntentId = intent.Id,
                PlaintextCredential = "must-not-cross-outbox-boundary"
            })
        };

        await Assert.That(async () => await composite.DispatchAsync(unsafeMessage))
            .Throws<InvalidOperationException>();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        AdmissionDeliveryDispatchResult cancelledResult = await dispatcher.DispatchAsync(
            new AdmissionDeliveryDispatchRequest(intent.Id), cancelled.Token);
        AdmissionDeliveryDispatchResult malformedResult = await dispatcher.DispatchAsync(
            new AdmissionDeliveryDispatchRequest(Guid.Empty), CancellationToken.None);
        AdmissionDeliveryDispatchResult routed = await dispatcher.DispatchAsync(
            new AdmissionDeliveryDispatchRequest(intent.Id), CancellationToken.None);
        context.ChangeTracker.Clear();
        AdmissionDeliveryIntent pending = await context.AdmissionDeliveryIntents.SingleAsync();

        await Assert.That(cancelledResult).IsEqualTo(new AdmissionDeliveryDispatchResult(
            AdmissionDeliveryOutcome.RecoverablePending, AdmissionDeliveryFailure.Cancelled));
        await Assert.That(malformedResult).IsEqualTo(new AdmissionDeliveryDispatchResult(
            AdmissionDeliveryOutcome.Unrecoverable, AdmissionDeliveryFailure.InvalidIntent));
        await Assert.That(routed).IsEqualTo(new AdmissionDeliveryDispatchResult(AdmissionDeliveryOutcome.Delivered));
        await Assert.That(observedMessage).IsNotNull();
        await Assert.That(observedMessage!.To).IsEqualTo(recipient);
        await Assert.That(observedMessage.PlainTextBody).Contains(bearer);
        await Assert.That(observedMessage.CustomHeaders["X-Admission-Delivery-Idempotency-Key"])
            .IsEqualTo(intent.Id.ToString("N"));
        await Assert.That(pending.RoutedAt).IsEqualTo(now);
        await Assert.That(pending.HandoffCompletedAt).IsEqualTo(now);
        await Assert.That(pending.HandoffReceiptId).IsEqualTo($"smtp:{intent.Id:N}");
        await Assert.That(pending.ProtectedCredential).IsEmpty();
    }

    [Test]
    public async Task FailedAdmissionDispatch_EmitsZeroSensitivePersistenceEvidence()
    {
        string sentinel = Guid.CreateVersion7().ToString("N");
        string parameterValue = $"SQL-PARAMETER-{sentinel}";
        string connectionSecret = $"CONNECTION-SECRET-{sentinel}";
        string pii = $"private-attendee-{sentinel}@example.test";
        string tenantPayload = $"TENANT-PAYLOAD-{sentinel}";
        string providerBody = $"PROVIDER-BODY-{sentinel}";
        string providerFault =
            $"parameter={parameterValue}; connection=Host=db.internal;Password={connectionSecret}; " +
            $"pii={pii}; tenant_payload={tenantPayload}; provider_body={providerBody}";

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ExploreDbContext>()
            .UseSqlite(connection)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new ExploreDbContext(options);
        Guid tenantId = Guid.CreateVersion7();
        context.TenantContext = new AdmissionTenantContext(tenantId);
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
        DateTime now = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
        var protectedMaterial = new AdmissionDeliveryEnvelopeProtector(
                new EphemeralDataProtectionProvider())
            .Protect(new AdmissionCredentialDeliveryEnvelope(pii, "protected-bearer"));
        var intent = new AdmissionDeliveryIntent(
            Guid.CreateVersion7(),
            tenantId,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            protectedMaterial.Ciphertext,
            protectedMaterial.ProtectionVersion,
            now);
        context.AdmissionDeliveryIntents.Add(intent);
        await context.SaveChangesAsync();

        var downstream = Substitute.For<IOutboxMessageDispatcher>();
        downstream.DispatchAsync(Arg.Any<OutboxMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException(providerFault)));
        var logger = new CaptureLogger<AdmissionDeliveryIntentDispatcher>();
        var dispatcher = new AdmissionDeliveryIntentDispatcher(context, downstream, logger);

        AdmissionDeliveryDispatchResult result = await dispatcher.DispatchAsync(
            new AdmissionDeliveryDispatchRequest(intent.Id),
            CancellationToken.None);

        await Assert.That(result).IsEqualTo(new AdmissionDeliveryDispatchResult(
            AdmissionDeliveryOutcome.RecoverablePending,
            AdmissionDeliveryFailure.RouteUnavailable));
        string observable = string.Join('|', logger.Entries.Select(entry => entry.Observable));
        foreach (string forbidden in
                 new[] { parameterValue, connectionSecret, pii, tenantPayload, providerBody })
        {
            await Assert.That(observable).DoesNotContain(forbidden);
        }
    }

    private static async Task<AdmissionTicket> SeedOwningOrderAsync(ExploreDbContext context, Guid tenantId, DateTime now)
    {
        Guid eventId = Guid.CreateVersion7();
        var catalog = EventTicketCatalogVersion.Create(tenantId, eventId, "EUR", 1);
        var type = EventTicketType.Create(Guid.CreateVersion7(), tenantId, catalog.Id, "Admission", "EUR",
            TicketPricingModeEnum.Free, null, null, null, ParticipantDataCollectionModeEnum.None,
            null, null, null, false, false, null, null, null, null);
        catalog.AddTicketType(type, null);
        catalog.AddEntitlement(type, TicketTypeEntitlement.CreateForEvent(type.Id, tenantId, eventId, 1));
        catalog.Publish();
        var order = RegistrationOrder.Create(tenantId, eventId, Guid.CreateVersion7(), null,
            BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), (int)ParticipationHandlingModeEnum.PlatformManaged,
                (int)AdvanceRegistrationObligationEnum.Required, (int)IdentityAccessModeEnum.AccountRequired, null),
            null, null, "EUR", now, null);
        var line = RegistrationOrderLine.Create(catalog, type, order.Id, 1, null, null);
        var participant = RegistrationParticipant.Create(tenantId, order.Id, null, ParticipantTypeEnum.Adult, null);
        var assignment = RegistrationTicketAssignment.CreateAssigned(Guid.CreateVersion7(), line.Id, 1, participant, now);
        order.AddLine(line);
        order.AddParticipant(participant);
        order.AddAssignment(line, assignment, participant);
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("EUR", 0, 0, 0, 0));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, now);
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, now);
        order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, now);
        var ticket = AdmissionTicket.Issue(order, line, assignment, participant, catalog, type,
            Guid.CreateVersion7(), "COMPOSITE", Guid.CreateVersion7(), 1, 1,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), now);
        context.AddRange(catalog, order, ticket);
        await context.SaveChangesAsync();
        return ticket;
    }

    private static CompositeOutboxMessageDispatcher CreateComposite(
        IAdmissionCredentialDeliveryOutboxHandler admissionHandler,
        IAdmissionRecoveryRequestOutboxHandler? recoveryRequestHandler = null,
        IAdmissionRecoveryDeliveryOutboxHandler? recoveryDeliveryHandler = null)
    {
        HybridCache cache = new MinimalHybridCache();
        var correctionPlanner = Substitute.For<IAtprotoLocationPrivacyCorrectionPlanner>();
        correctionPlanner.PlanLocationPrivacyCorrectionAsync(
                Arg.Any<AtprotoLocationPrivacyCorrectionInput>(), Arg.Any<CancellationToken>())
            .Returns(AtprotoPublicationPlanningResult.Skipped("correction_already_planned"));
        var refundRepository = Substitute.For<IRefundAttemptRepository>();
        var campaignRepository = Substitute.For<IRefundCampaignRepository>();
        var refundCreator = Substitute.For<IRefundCreator>();
        return new CompositeOutboxMessageDispatcher(
            CreateNotificationHandoff(),
            Substitute.For<IEventPublishedNotificationFanoutService>(),
            Substitute.For<IEventModerationNotificationFanoutService>(),
            Substitute.For<IReportProviderSyncDispatcher>(),
            new EventDiscoveryIdentityCorrectionDispatcher(
                Substitute.For<IEventDiscoveryIdentityCorrectionNotificationService>(),
                Substitute.For<ITenantContextAccessor>()),
            new LocationPrivacyCorrectionDispatcher(cache, correctionPlanner, EventLocationPrivacyMetricsFactory.Create()),
            new PrivacyErasureCacheInvalidationDispatcher(cache),
            admissionHandler,
            recoveryRequestHandler ?? Substitute.For<IAdmissionRecoveryRequestOutboxHandler>(),
            recoveryDeliveryHandler ?? Substitute.For<IAdmissionRecoveryDeliveryOutboxHandler>(),
            Substitute.For<IOutboxRepository>(),
            campaignRepository,
            new RefundCampaignProcessor(
                campaignRepository, refundRepository,
                Substitute.For<IRegistrationMaterialChangeChoiceRepository>(),
                Substitute.For<IRegistrationPaymentAttemptRepository>(), TimeProvider.System),
            new RefundDispatchService(refundRepository, refundCreator, TimeProvider.System),
            new RefundReconciliationService(
                refundRepository, refundCreator, Substitute.For<IRefundRetriever>(), TimeProvider.System),
            new RegistrationPaymentCancellationService(
                Substitute.For<IRegistrationPaymentAttemptRepository>(), refundRepository, campaignRepository,
                Substitute.For<IPaymentCancellationProvider>(), TimeProvider.System),
            Substitute.For<IAdmissionRevocationService>(),
            Substitute.For<IAdmissionRefundRevocationService>(),
            Substitute.For<IAdmissionEventCancellationService>(),
            CreateMetrics(),
            TimeProvider.System,
            Substitute.For<ICommandHandler<ProcessManagedTenantProvisioningOperationCommand, bool>>(),
            Substitute.For<ICommandHandler<ReconcileManagedTenantProvisioningDeadLetterCommand, bool>>(),
            NullLogger<CompositeOutboxMessageDispatcher>.Instance,
            Substitute.For<IConfigurationManifestEffectDispatcher>(),
            Substitute.For<IConfigurationImportEffectDelivery>());
    }

    private static NotificationFanoutOccurrenceHandoffService CreateNotificationHandoff()
    {
        Type type = typeof(NotificationFanoutOccurrenceHandoffService);
        var constructor = type.GetConstructors().OrderByDescending(candidate => candidate.GetParameters().Length).First();
        object?[] arguments = constructor.GetParameters()
            .Select(parameter => Substitute.For([parameter.ParameterType], []))
            .ToArray();
        return (NotificationFanoutOccurrenceHandoffService)constructor.Invoke(arguments);
    }

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new BusinessMetrics(meterFactory);
    }

    private sealed record AdmissionTenantContext(Guid TenantId) : ITenantContext;

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<CapturedLog> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            Entries.Add(new CapturedLog(
                formatter(state, exception),
                properties.ToDictionary(property => property.Key, property => property.Value),
                exception));
        }
    }

    private sealed record CapturedLog(
        string Message,
        IReadOnlyDictionary<string, object?> State,
        Exception? Exception)
    {
        public string Observable => string.Join('|',
            Message,
            string.Join('|', State.Keys),
            string.Join('|', State.Values.Select(value => value?.ToString() ?? string.Empty)),
            Exception?.ToString() ?? string.Empty);
    }

    private sealed class MinimalHybridCache : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
            factory(state, cancellationToken);
        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
