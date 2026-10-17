using System.Data.Common;
using Cerbos.Sdk.Builder;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Handlers.Commands;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    public async Task NativeEventDeletionPreservesMissingAndForeignNotFoundResponses()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var handler = ActivatorUtilities.CreateInstance<DeleteEventCommandHandler>(scope.ServiceProvider);
        foreach (Guid id in new[] { Guid.CreateVersion7(), data.ForeignId })
        {
            var result = await handler.ExecuteAsync(EventDeleteInput(id), default);
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.NotFound);
            await Assert.That(result.Id).IsEqualTo(id);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EventDeletionDeniesNonownerEvenWithInstanceAdministration(bool administrator)
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OutsiderId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        if (administrator)
            await GrantTemplateAdministrator(db, data.OutsiderId);

        var result = await ActivatorUtilities.CreateInstance<DeleteEventCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(EventDeleteInput(data.PublicId) with { UserId = data.OwnerId.ToString("D") }, default);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(DeleteEventFailureCodes.AuthorityDenied);
        await Assert.That(await db.Events.AnyAsync(row => row.Id == data.PublicId)).IsTrue();
    }

    [Test]
    public async Task EventDeletionCascadesCurrentSessionsAndUsesTheCanonicalActor()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid first = await PrepareDefinitionParent(db, data.PublicId, 2);
        Guid second = await PrepareDefinitionParent(db, data.PublicId, 2);
        db.ChangeTracker.Clear();

        var result = await ActivatorUtilities.CreateInstance<DeleteEventCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(EventDeleteInput(data.PublicId), default);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(await db.Events.AnyAsync(row => row.Id == data.PublicId)).IsFalse();
        await Assert.That(await db.EventSessions.AnyAsync(row => row.Id == first || row.Id == second)).IsFalse();
        var deleted = await db.Events.IgnoreQueryFilters().AsNoTracking().SingleAsync(row => row.Id == data.PublicId);
        await Assert.That(deleted.DeletedBy).IsEqualTo(data.OwnerId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PaidEvidenceBlocksEventDeletionIncludingAfterAnEarlierEmptyRead(bool atBoundary)
    {
        var boundary = new BeforeAuthorityTransaction();
        await using var factory = new NativeEventSeriesFactory { DatabaseInterceptor = boundary };
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid tenantId = db.TenantContext!.TenantId;
        await Assert.That(await scope.ServiceProvider.GetRequiredService<IRegistrationInventoryRepository>()
            .HasPaidEvidenceAsync(data.PublicId, tenantId, default)).IsFalse();
        if (atBoundary)
        {
            boundary.Arm(db, async token =>
            {
                await using var evidence = await scope.ServiceProvider
                    .GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContextAsync(token);
                evidence.TenantContext = new AuthorityTenant(tenantId);
                await SeedEventDeletionPayment(evidence, data.PublicId, data.OwnerId, token);
            });
        }
        else
            await SeedEventDeletionPayment(db, data.PublicId, data.OwnerId, default);
        db.ChangeTracker.Clear();

        var result = await ActivatorUtilities.CreateInstance<DeleteEventCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(EventDeleteInput(data.PublicId), default);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(DeleteEventFailureCodes.PaidEvidenceConflict);
        await Assert.That(await db.Events.AnyAsync(row => row.Id == data.PublicId)).IsTrue();
        if (atBoundary)
            await Assert.That(boundary.Applied).IsTrue();
    }

    [Test]
    public async Task EventDeletionNativeAuthorityRejectsRevokedTenantMembership()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IEventRepository>();
        var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await Assert.That(await unit.ExecuteSerializableAsync(token => repository.GetAuthorizedForDeleteAsync(
            db.TenantContext!.TenantId, data.OwnerId, data.PublicId, token))).IsNotNull();
        await db.TenantUsers.Where(row => row.UserId == data.OwnerId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));

        var result = await ActivatorUtilities.CreateInstance<DeleteEventCommandHandler>(scope.ServiceProvider)
            .ExecuteAsync(EventDeleteInput(data.PublicId), default);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(DeleteEventFailureCodes.AuthorityDenied);
        await Assert.That(await db.Events.AnyAsync(row => row.Id == data.PublicId)).IsTrue();
    }

    [Test]
    public async Task EventDeletionRollsBackRootAndCascadeWhenTheSharedCommitIsCancelled()
    {
        var boundary = new EventDeletionCommitBoundary();
        await using var factory = new NativeEventSeriesFactory { DatabaseInterceptor = boundary };
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await PrepareDefinitionParent(db, data.PublicId, 2);
        await PrepareDefinitionParent(db, data.PublicId, 2);
        db.ChangeTracker.Clear();
        boundary.Arm(db, data.PublicId);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ActivatorUtilities.CreateInstance<DeleteEventCommandHandler>(scope.ServiceProvider)
                .ExecuteAsync(EventDeleteInput(data.PublicId), default));

        await Assert.That(boundary.DeletedRootObserved).IsTrue();
        await Assert.That(boundary.DeletedSessionsObserved).IsEqualTo(2);
        await Assert.That(await db.Events.AnyAsync(row => row.Id == data.PublicId)).IsTrue();
        await Assert.That(await db.EventSessions.CountAsync(row => row.EventId == data.PublicId)).IsEqualTo(2);
    }

    [Test]
    public async Task EventDeletionSelectedCerbosOutageDoesNotFallBackToLocalOwnership()
    {
        var transport = new UnavailableInstanceKeyPolicyTransport();
        var client = CerbosClientBuilder.ForTarget("http://event-delete-policy.test").WithPlaintext()
            .WithGrpcChannelOptions(new GrpcChannelOptions { HttpHandler = transport, DisposeHttpClient = true }).Build();
        await using var factory = new NativeEventSeriesFactory { InstanceCerbosClient = client };
        factory.AdditionalConfiguration["Authorization:Provider"] = "cerbos";
        var data = await Seed(factory);
        using var scope = Scope(factory, data.OwnerId);

        await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(() =>
            ActivatorUtilities.CreateInstance<DeleteEventCommandHandler>(scope.ServiceProvider)
                .ExecuteAsync(EventDeleteInput(data.PublicId), default));

        await Assert.That(transport.Requests).IsGreaterThan(0);
        await Assert.That(await scope.ServiceProvider.GetRequiredService<ExploreDbContext>()
            .Events.AnyAsync(row => row.Id == data.PublicId)).IsTrue();
    }

    private static DeleteEventCommand EventDeleteInput(Guid eventId) => new()
    {
        Id = eventId,
        UserId = Guid.CreateVersion7().ToString("D")
    };

    private static async Task SeedEventDeletionPayment(
        ExploreDbContext db, Guid eventId, Guid userId, CancellationToken token)
    {
        Guid tenantId = db.TenantContext!.TenantId;
        Guid actorId = await db.Events.Where(row => row.Id == eventId).Select(row => row.ActorId).SingleAsync(token);
        var catalog = EventTicketCatalogVersion.Create(tenantId, eventId, "USD", 1);
        db.EventTicketCatalogVersions.Add(catalog);
        await db.SaveChangesAsync(token);
        DateTime now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var order = RegistrationOrder.Create(Guid.CreateVersion7(), tenantId, eventId, userId, null,
            BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), (int)ParticipationHandlingModeEnum.PlatformManaged,
                (int)AdvanceRegistrationObligationEnum.Required, (int)IdentityAccessModeEnum.AccountRequired, GuestRecoveryPolicyEnum.NoRecovery),
            null, null, "USD", now, now.AddHours(1));
        var recipient = OrganizerPaymentRecipientSnapshot.Create(tenantId, actorId, Guid.CreateVersion7(),
            "stripe", "native-test", $"acct_{Guid.CreateVersion7():N}", "US", "USD", Guid.CreateVersion7(), null, now);
        var payment = PaymentAttempt.Create(Guid.CreateVersion7(), tenantId, order.Id, recipient,
            "OrganizerDirect", "native-test", "native-test", Money.Create(100, "USD"), Money.Create(0, "USD"),
            Money.Create(0, "USD"), Guid.CreateVersion7().ToString("N"), now, now.AddHours(1));
        payment.MarkSucceeded($"payment_{Guid.CreateVersion7():N}", now.AddSeconds(1), null);
        db.RegistrationOrders.Add(order);
        db.PaymentAttempts.Add(payment);
        await db.SaveChangesAsync(token);
    }

    private sealed class EventDeletionCommitBoundary : DbTransactionInterceptor
    {
        private ExploreDbContext? _target;
        private Guid _eventId;
        public bool DeletedRootObserved { get; private set; }
        public int DeletedSessionsObserved { get; private set; }
        public void Arm(ExploreDbContext context, Guid eventId) { _target = context; _eventId = eventId; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(eventData.Context, _target))
                return ValueTask.FromResult(result);
            DeletedRootObserved = _target!.ChangeTracker.Entries<Explore.Domain.Event>()
                .Any(entry => entry.Entity.Id == _eventId && entry.Entity.IsDeleted);
            DeletedSessionsObserved = _target.ChangeTracker.Entries<EventSession>()
                .Count(entry => entry.Entity.EventId == _eventId && entry.Entity.IsDeleted);
            throw new OperationCanceledException("Abort the shared deletion commit.");
        }
    }
}
