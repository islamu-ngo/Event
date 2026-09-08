using System.Security.Cryptography;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<PostgreSqlContainerFixture>(
    Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class TicketTransferConcurrencyTests(
    PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime UtcNow =
        new(
            2026,
            8,
            28,
            12,
            0,
            0,
            DateTimeKind.Utc);

    [Test]
    public async Task TransferRowsAreTenantFilteredAndOneOfferIsOpenPerTicket()
    {
        await using ExploreDbContext context =
            fixture.CreateDbContext();
        IEntityType? transfer = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(
                typeof(AdmissionTicketTransfer));

        await Assert.That(transfer).IsNotNull();
        await Assert.That(
                transfer!.FindDeclaredQueryFilter(
                    QueryFilterNames.Tenant))
            .IsNotNull();
        await Assert.That(
                transfer.GetIndexes().Any(index =>
                    index.IsUnique
                    && HasProperties(
                        index.Properties,
                        nameof(AdmissionTicketTransfer.TenantId),
                        nameof(AdmissionTicketTransfer.OpenAdmissionTicketId))))
            .IsTrue();
        await Assert.That(
                transfer.GetForeignKeys().Any(key =>
                    key.PrincipalEntityType.ClrType ==
                    typeof(AdmissionTicket)
                    && HasProperties(
                        key.Properties,
                        nameof(AdmissionTicketTransfer.TenantId),
                        nameof(AdmissionTicketTransfer.AdmissionTicketId))))
            .IsTrue();
    }

    [Test]
    public async Task TransferStateStoresReferencesWithoutPiiOrCommerceMutation()
    {
        await using ExploreDbContext context =
            fixture.CreateDbContext();
        IEntityType? transfer = context
            .GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(
                typeof(AdmissionTicketTransfer));

        await Assert.That(transfer).IsNotNull();
        string[] forbidden =
        [
            "email",
            "phone",
            "name",
            "address",
            "amount",
            "currency",
            "price",
            "payment",
            "refund",
            "merchant",
        ];
        string[] forbiddenProperties = transfer!
            .GetProperties()
            .Where(property =>
                property.Name != nameof(
                    AdmissionTicketTransfer.ConcurrencyStamp)
                &&
                forbidden.Any(fragment =>
                    property.Name.Contains(
                        fragment,
                        StringComparison.OrdinalIgnoreCase)))
            .Select(property => property.Name)
            .ToArray();
        await Assert.That(forbiddenProperties).IsEmpty();
        string[] requiredProperties =
        [
            nameof(AdmissionTicketTransfer.FromParticipantId),
            nameof(AdmissionTicketTransfer.ToParticipantId),
            nameof(AdmissionTicketTransfer.RecipientSubjectUserId),
            nameof(AdmissionTicketTransfer.CapabilityDigest),
        ];
        await Assert.That(requiredProperties.All(name =>
                transfer.FindProperty(name) is not null))
            .IsTrue();
    }

    [Test]
    public async Task TransferPreservesAppendOnlyCheckInAndCommerceLineage()
    {
        Type transfer = typeof(AdmissionTicketTransfer);
        string[] lineageProperties =
        [
            nameof(AdmissionTicketTransfer.RegistrationOrderId),
            nameof(AdmissionTicketTransfer.RegistrationOrderLineId),
            nameof(AdmissionTicketTransfer.RegistrationTicketAssignmentId),
        ];
        await Assert.That(lineageProperties.All(name =>
                transfer.GetProperty(name) is not null))
            .IsTrue();
        await Assert.That(
                transfer.GetProperties().Any(property =>
                    property.Name.Contains(
                        "CheckIn",
                        StringComparison.OrdinalIgnoreCase)
                    && property.SetMethod is not null))
            .IsFalse();
        await Assert.That(
                transfer.GetMethods()
                    .Any(method =>
                        method.Name.Contains(
                            "Payment",
                            StringComparison.OrdinalIgnoreCase)
                        || method.Name.Contains(
                            "Refund",
                            StringComparison.OrdinalIgnoreCase)))
            .IsFalse();
    }

    [Test]
    public async Task AcceptanceStagesPointerNotificationsInSameTransaction()
    {
        Type intent = typeof(AdmissionTransferDeliveryIntent);
        await Assert.That(
                intent.GetProperties().Any(property =>
                    property.Name.Contains(
                        "Email",
                        StringComparison.OrdinalIgnoreCase)
                    || property.Name.Contains(
                        "Token",
                        StringComparison.OrdinalIgnoreCase)))
            .IsFalse();
        string[] pointerProperties =
        [
            nameof(AdmissionTransferDeliveryIntent
                .AdmissionTicketTransferId),
            nameof(AdmissionTransferDeliveryIntent
                .OutboxMessageId),
        ];
        await Assert.That(pointerProperties.All(name =>
                intent.GetProperty(name) is not null))
            .IsTrue();
    }

    [Test]
    public async Task ConcurrentOffersCreateOneOpenTransferAndReplayOneWinner()
    {
        TransferSeed seed = await SeedTransferAsync(
            "offer-race");
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(30));
        var gate = new TransferRaceGate(2);

        async Task<AdmissionTicketTransferResult> OfferAsync(
            string discriminator)
        {
            await using ExploreDbContext context =
                TenantContext(seed.TenantId);
            await gate.ArriveAsync(timeout.Token);
            return await new EfCoreUnitOfWork(context)
                .ExecuteInTransactionAsync(
                    token =>
                        new AdmissionTicketTransferRepository(
                            context)
                            .OfferAsync(
                                OfferRequest(
                                    seed,
                                    Guid.CreateVersion7(),
                                    Digest(discriminator)),
                                token),
                    timeout.Token);
        }

        Task<AdmissionTicketTransferResult> first =
            OfferAsync("first");
        Task<AdmissionTicketTransferResult> second =
            OfferAsync("second");
        await gate.AllArrived.WaitAsync(timeout.Token);
        gate.Release();
        AdmissionTicketTransferResult[] results =
            await Task.WhenAll(first, second);

        await using ExploreDbContext verification =
            TenantContext(seed.TenantId);
        AdmissionTicketTransfer[] persisted =
            await verification.AdmissionTicketTransfers
                .AsNoTracking()
                .Where(value =>
                    value.TenantId == seed.TenantId
                    && value.AdmissionTicketId ==
                    seed.TicketId)
                .ToArrayAsync(timeout.Token);
        await Assert.That(results.Count(result =>
                result.Outcome ==
                AdmissionTicketTransferOutcome.Offered))
            .IsEqualTo(1);
        await Assert.That(results.Count(result =>
                result.Outcome ==
                AdmissionTicketTransferOutcome
                    .AlreadyOffered))
            .IsEqualTo(1);
        await Assert.That(persisted).HasSingleItem();
        await Assert.That(persisted.Single().IsOpen)
            .IsTrue();
    }

    [Test]
    public async Task AcceptanceVersusOldCredentialCheckInHasOneTerminalWinner()
    {
        TransferSeed seed = await SeedTransferAsync(
            "check-in-race");
        string capabilityDigest = Digest(
            "check-in-capability");
        AdmissionTicketTransfer transfer =
            await OfferSeedAsync(
                seed,
                capabilityDigest);
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(30));
        var gate = new TransferRaceGate(2);

        async Task<AdmissionTicketTransferResult> AcceptAsync()
        {
            await using ExploreDbContext context =
                TenantContext(seed.TenantId);
            await gate.ArriveAsync(timeout.Token);
            return await new EfCoreUnitOfWork(context)
                .ExecuteInTransactionAsync(
                    token =>
                        new AdmissionTicketTransferRepository(
                            context)
                            .ApplyAcceptanceAsync(
                                AcceptanceRequest(
                                    seed,
                                    transfer.Id,
                                    capabilityDigest),
                                token),
                    timeout.Token);
        }

        async Task<AdmissionCheckInDecision?> CheckInAsync()
        {
            await using ExploreDbContext context =
                TenantContext(seed.TenantId);
            await gate.ArriveAsync(timeout.Token);
            return await new EfCoreUnitOfWork(context)
                .ExecuteInTransactionAsync(
                    token =>
                        new AdmissionCheckInRepository(
                            context)
                            .ExecuteAsync(
                                new AdmissionCheckInTransactionRequest(
                                    seed.TenantId,
                                    seed.EventId,
                                    seed.AdmissionTargetId,
                                    [
                                        new AdmissionCheckInCredentialDigestCandidate(
                                            seed.OldCredentialDigest,
                                            7),
                                    ],
                                    AdmissionCheckInAction.CheckIn,
                                    ReasonCode: null,
                                    seed.ActorId,
                                    ScannerCapabilityId: null,
                                    new DateTimeOffset(
                                        UtcNow.AddMinutes(2)),
                                    Guid.CreateVersion7()),
                                token),
                    timeout.Token);
        }

        Task<AdmissionTicketTransferResult> acceptance =
            AcceptAsync();
        Task<AdmissionCheckInDecision?> checkIn =
            CheckInAsync();
        await gate.AllArrived.WaitAsync(timeout.Token);
        gate.Release();
        AdmissionTicketTransferResult accepted =
            await acceptance;
        AdmissionCheckInDecision? checkedIn =
            await checkIn;
        bool transferWon =
            accepted.Outcome ==
            AdmissionTicketTransferOutcome.Accepted;
        bool checkInWon = checkedIn?.Event is not null;

        await Assert.That(transferWon ^ checkInWon)
            .IsTrue();
        await using ExploreDbContext verification =
            TenantContext(seed.TenantId);
        AdmissionTicket ticket =
            await verification.AdmissionTickets
                .Include(value => value.Credentials)
                .SingleAsync(
                    value => value.Id == seed.TicketId,
                    timeout.Token);
        if (transferWon)
        {
            await Assert.That(ticket.ParticipantId)
                .IsEqualTo(seed.RecipientParticipantId);
            await Assert.That(ticket.ValidatesCredential(
                    1,
                    7,
                    seed.OldCredentialDigest))
                .IsFalse();
            await Assert.That(ticket.CredentialGeneration)
                .IsEqualTo(2);
        }
        else
        {
            await Assert.That(ticket.ParticipantId)
                .IsEqualTo(seed.SourceParticipantId);
            await Assert.That(
                    await verification
                        .AdmissionCheckInEvents
                        .CountAsync(
                            value =>
                                value.AdmissionTicketId ==
                                seed.TicketId,
                            timeout.Token))
                .IsEqualTo(1);
        }
    }

    [Test]
    public async Task AcceptanceVersusRevocationLeavesNoFutureActiveCredential()
    {
        TransferSeed seed = await SeedTransferAsync(
            "revocation-race");
        string capabilityDigest = Digest(
            "revocation-capability");
        AdmissionTicketTransfer transfer =
            await OfferSeedAsync(
                seed,
                capabilityDigest);
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(30));
        var gate = new TransferRaceGate(2);

        async Task<AdmissionTicketTransferOutcome?> AcceptAsync()
        {
            await using ExploreDbContext context =
                TenantContext(seed.TenantId);
            await gate.ArriveAsync(timeout.Token);
            try
            {
                AdmissionTicketTransferResult result =
                    await new EfCoreUnitOfWork(context)
                        .ExecuteInTransactionAsync(
                            token =>
                                new AdmissionTicketTransferRepository(
                                    context)
                                    .ApplyAcceptanceAsync(
                                        AcceptanceRequest(
                                            seed,
                                            transfer.Id,
                                            capabilityDigest),
                                        token),
                            timeout.Token);
                return result.Outcome;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        async Task RevokeAsync()
        {
            await using ExploreDbContext context =
                TenantContext(seed.TenantId);
            var repository =
                new ParticipantAdmissionEligibilityRepository(
                    context);
            await gate.ArriveAsync(timeout.Token);
            await new EfCoreUnitOfWork(context)
                .ExecuteInTransactionAsync(
                    async token =>
                    {
                        ParticipantAdmissionEligibility
                            eligibility =
                            await repository.LoadForUpdateAsync(
                                seed.TenantId,
                                seed.AssignmentId,
                                token)
                            ?? throw new InvalidOperationException(
                                "Eligibility disappeared.");
                        eligibility.Revoke(
                            seed.ActorId,
                            UtcNow.AddMinutes(3),
                            Guid.CreateVersion7());
                        AdmissionTicket ticket =
                            await repository
                                .GetIssuedTicketForUpdateAsync(
                                    seed.TenantId,
                                    seed.AssignmentId,
                                    token)
                            ?? throw new InvalidOperationException(
                                "Admission ticket disappeared.");
                        ticket.TransitionTo(
                            AdmissionTicketStatusEnum.Revoked,
                            UtcNow.AddMinutes(3));
                        await repository.ApplyDecisionAsync(
                            eligibility,
                            token);
                        return true;
                    },
                    timeout.Token);
        }

        Task<AdmissionTicketTransferOutcome?> acceptance =
            AcceptAsync();
        Task revocation = RevokeAsync();
        await gate.AllArrived.WaitAsync(timeout.Token);
        gate.Release();
        await Task.WhenAll(acceptance, revocation);

        await using ExploreDbContext verification =
            TenantContext(seed.TenantId);
        AdmissionTicket ticket =
            await verification.AdmissionTickets
                .Include(value => value.Credentials)
                .SingleAsync(
                    value => value.Id == seed.TicketId,
                    timeout.Token);
        ParticipantAdmissionEligibility eligibility =
            await verification
                .ParticipantAdmissionEligibilities
                .SingleAsync(
                    value =>
                        value.RegistrationTicketAssignmentId ==
                        seed.AssignmentId,
                    timeout.Token);
        await Assert.That(ticket.AdmissionTicketStatusId)
            .IsEqualTo(
                (int)AdmissionTicketStatusEnum.Revoked);
        await Assert.That(ticket.Credentials.Any(
                credential =>
                    credential
                        .AdmissionTicketCredentialStatusId ==
                    (int)AdmissionTicketCredentialStatusEnum
                        .Active))
            .IsFalse();
        await Assert.That(eligibility.RevokedAt)
            .IsNotNull();
    }

    [Test]
    public async Task AcceptanceVersusRecoveryReissueRotatesExactlyOneCredential()
    {
        TransferSeed seed = await SeedTransferAsync(
            "reissue-race");
        string capabilityDigest = Digest(
            "transfer-reissue-capability");
        AdmissionTicketTransfer transfer =
            await OfferSeedAsync(
                seed,
                capabilityDigest);
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(30));
        var gate = new TransferRaceGate(2);

        async Task<AdmissionTicketTransferOutcome?> AcceptAsync()
        {
            await using ExploreDbContext context =
                TenantContext(seed.TenantId);
            await gate.ArriveAsync(timeout.Token);
            try
            {
                AdmissionTicketTransferResult result =
                    await new EfCoreUnitOfWork(context)
                        .ExecuteInTransactionAsync(
                            token =>
                                new AdmissionTicketTransferRepository(
                                    context)
                                    .ApplyAcceptanceAsync(
                                        AcceptanceRequest(
                                            seed,
                                            transfer.Id,
                                            capabilityDigest),
                                        token),
                            timeout.Token);
                return result.Outcome;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        async Task<bool> ReissueAsync()
        {
            await using ExploreDbContext context =
                TenantContext(seed.TenantId);
            await gate.ArriveAsync(timeout.Token);
            return await new EfCoreUnitOfWork(context)
                .ExecuteInTransactionAsync(
                    async token =>
                    {
                        AdmissionRecoveryCapability capability =
                            await context
                                .AdmissionRecoveryCapabilities
                                .AsNoTracking()
                                .SingleAsync(
                                    value =>
                                        value.Id ==
                                        seed.RecoveryCapabilityId,
                                    token);
                        bool consumed =
                            await new AdmissionRecoveryRepository(
                                    context)
                                .TryConsumeAsync(
                                    seed.TenantId,
                                    capability.Id,
                                    capability.LookupKeyVersion,
                                    capability.LookupDigest,
                                    capability.ConcurrencyStamp,
                                    UtcNow.AddMinutes(2),
                                    token);
                        if (!consumed)
                        {
                            return false;
                        }

                        AdmissionTicket ticket =
                            await new AdmissionTicketRepository(
                                    context)
                                .GetByIdForUpdateAsync(
                                    seed.TenantId,
                                    seed.TicketId,
                                    token)
                            ?? throw new InvalidOperationException(
                                "Admission ticket disappeared.");
                        ticket.RotateCredential(
                            Guid.CreateVersion7(),
                            ticket.CredentialGeneration + 1,
                            7,
                            Digest("reissued"),
                            UtcNow.AddMinutes(2));
                        await context.SaveChangesAsync(token);
                        return true;
                    },
                    timeout.Token);
        }

        Task<AdmissionTicketTransferOutcome?> acceptance =
            AcceptAsync();
        Task<bool> reissue = ReissueAsync();
        await gate.AllArrived.WaitAsync(timeout.Token);
        gate.Release();
        AdmissionTicketTransferOutcome? acceptanceOutcome =
            await acceptance;
        bool reissueWon = await reissue;
        bool transferWon =
            acceptanceOutcome ==
            AdmissionTicketTransferOutcome.Accepted;

        await Assert.That(transferWon ^ reissueWon)
            .IsTrue();
        await using ExploreDbContext verification =
            TenantContext(seed.TenantId);
        AdmissionTicket ticket =
            await verification.AdmissionTickets
                .Include(value => value.Credentials)
                .SingleAsync(
                    value => value.Id == seed.TicketId,
                    timeout.Token);
        AdmissionRecoveryCapability recovery =
            await verification.AdmissionRecoveryCapabilities
                .AsNoTracking()
                .SingleAsync(
                    value =>
                        value.Id ==
                        seed.RecoveryCapabilityId,
                    timeout.Token);
        await Assert.That(ticket.CredentialGeneration)
            .IsEqualTo(2);
        await Assert.That(ticket.ValidatesCredential(
                1,
                7,
                seed.OldCredentialDigest))
            .IsFalse();
        await Assert.That(
                recovery.ConsumedAt.HasValue
                ^ recovery.RotatedAt.HasValue)
            .IsTrue();
        await Assert.That(ticket.ParticipantId)
            .IsEqualTo(
                transferWon
                    ? seed.RecipientParticipantId
                    : seed.SourceParticipantId);
    }

    [Test]
    public async Task ReplayStaleGenerationAndConsumedCapabilityFailClosed()
    {
        TransferSeed seed = await SeedTransferAsync(
            "replay");
        Guid operationKey = Guid.CreateVersion7();
        string capabilityDigest = Digest(
            "replay-capability");
        AdmissionTicketTransferOfferRequest offer =
            OfferRequest(
                seed,
                operationKey,
                capabilityDigest);
        await using ExploreDbContext firstContext =
            TenantContext(seed.TenantId);
        AdmissionTicketTransferResult first =
            await new EfCoreUnitOfWork(firstContext)
                .ExecuteInTransactionAsync(
                    token =>
                        new AdmissionTicketTransferRepository(
                            firstContext)
                            .OfferAsync(offer, token));
        await using ExploreDbContext replayContext =
            TenantContext(seed.TenantId);
        AdmissionTicketTransferResult replay =
            await new EfCoreUnitOfWork(replayContext)
                .ExecuteInTransactionAsync(
                    token =>
                        new AdmissionTicketTransferRepository(
                            replayContext)
                            .OfferAsync(
                                offer with
                                {
                                    CapabilityDigest =
                                        Digest("changed"),
                                },
                                token));

        Guid wrongTenantId = Guid.CreateVersion7();
        await using ExploreDbContext wrongTenantContext =
            TenantContext(wrongTenantId);
        AdmissionTicketTransferResult wrongTenant =
            await new EfCoreUnitOfWork(wrongTenantContext)
                .ExecuteInTransactionAsync(
                    token =>
                        new AdmissionTicketTransferRepository(
                            wrongTenantContext)
                            .ApplyAcceptanceAsync(
                                AcceptanceRequest(
                                    seed,
                                    first.Transfer!.Id,
                                    capabilityDigest)
                                with
                                {
                                    TenantId = wrongTenantId,
                                },
                                token));
        await using ExploreDbContext wrongResourceContext =
            TenantContext(seed.TenantId);
        AdmissionTicketTransferResult wrongResource =
            await new EfCoreUnitOfWork(
                    wrongResourceContext)
                .ExecuteInTransactionAsync(
                    token =>
                        new AdmissionTicketTransferRepository(
                            wrongResourceContext)
                            .ApplyAcceptanceAsync(
                                AcceptanceRequest(
                                    seed,
                                    first.Transfer!.Id,
                                    capabilityDigest)
                                with
                                {
                                    AdmissionTicketId =
                                        Guid.CreateVersion7(),
                                },
                                token));

        await using ExploreDbContext staleContext =
            TenantContext(seed.TenantId);
        AdmissionTicketTransferAcceptanceRequest valid =
            AcceptanceRequest(
                seed,
                first.Transfer!.Id,
                capabilityDigest);
        AdmissionTicketTransferResult stale =
            await new EfCoreUnitOfWork(staleContext)
                .ExecuteInTransactionAsync(
                    token =>
                        new AdmissionTicketTransferRepository(
                            staleContext)
                            .ApplyAcceptanceAsync(
                                valid with
                                {
                                    ExpectedCredentialGeneration =
                                        99,
                                },
                                token));
        await using ExploreDbContext acceptanceContext =
            TenantContext(seed.TenantId);
        AdmissionTicketTransferResult accepted =
            await new EfCoreUnitOfWork(acceptanceContext)
                .ExecuteInTransactionAsync(
                    token =>
                        new AdmissionTicketTransferRepository(
                            acceptanceContext)
                            .ApplyAcceptanceAsync(
                                valid,
                                token));
        await using ExploreDbContext consumedContext =
            TenantContext(seed.TenantId);
        AdmissionTicketTransferResult consumed =
            await new EfCoreUnitOfWork(consumedContext)
                .ExecuteInTransactionAsync(
                    token =>
                        new AdmissionTicketTransferRepository(
                            consumedContext)
                            .ApplyAcceptanceAsync(
                                valid,
                                token));

        await Assert.That(first.Outcome)
            .IsEqualTo(
                AdmissionTicketTransferOutcome.Offered);
        await Assert.That(replay.Outcome)
            .IsEqualTo(
                AdmissionTicketTransferOutcome
                    .AlreadyOffered);
        await Assert.That(replay.Transfer?.Id)
            .IsEqualTo(first.Transfer?.Id);
        await Assert.That(stale.Outcome)
            .IsEqualTo(
                AdmissionTicketTransferOutcome
                    .StaleGeneration);
        await Assert.That(wrongTenant.Outcome)
            .IsEqualTo(
                AdmissionTicketTransferOutcome.Unavailable);
        await Assert.That(wrongResource.Outcome)
            .IsEqualTo(
                AdmissionTicketTransferOutcome.Unavailable);
        await Assert.That(accepted.Outcome)
            .IsEqualTo(
                AdmissionTicketTransferOutcome.Accepted);
        await Assert.That(consumed.Outcome)
            .IsEqualTo(
                AdmissionTicketTransferOutcome.Unavailable);

        await using ExploreDbContext verification =
            TenantContext(seed.TenantId);
        AdmissionTicket persistedTicket =
            await verification.AdmissionTickets
                .AsNoTracking()
                .SingleAsync(value =>
                    value.Id == seed.TicketId);
        RegistrationOrder persistedOrder =
            await verification.RegistrationOrders
                .AsNoTracking()
                .SingleAsync(value =>
                    value.Id == seed.OrderId);
        OutboxMessage outbox =
            await verification.OutboxMessages
                .AsNoTracking()
                .SingleAsync(value =>
                    value.AggregateId ==
                    first.Transfer!.Id
                    && value.EventType ==
                    "AdmissionTicketTransferAccepted");
        AdmissionTransferDeliveryIntent intent =
            await verification
                .AdmissionTransferDeliveryIntents
                .AsNoTracking()
                .SingleAsync(value =>
                    value.AdmissionTicketTransferId ==
                    first.Transfer!.Id);
        await Assert.That(
                persistedTicket.RegistrationOrderId)
            .IsEqualTo(seed.OrderId);
        await Assert.That(
                persistedTicket.RegistrationOrderLineId)
            .IsEqualTo(seed.LineId);
        await Assert.That(
                persistedOrder.AccountUserId)
            .IsEqualTo(seed.SourceSubjectUserId);
        await Assert.That(
                persistedOrder.TotalDueMinorSnapshot)
            .IsEqualTo(0);
        await Assert.That(outbox.Payload).IsNull();
        await Assert.That(intent.OutboxMessageId)
            .IsEqualTo(outbox.Id);
    }

    [Test]
    public async Task CutoffExpiryAndHopLimitFailClosedWithoutMutatingCommerce()
    {
        TransferSeed expiredSeed =
            await SeedTransferAsync(
                "cutoff",
                maximumHops: 1);
        await using ExploreDbContext cutoffContext =
            TenantContext(expiredSeed.TenantId);
        AdmissionTicketTransferResult cutoff =
            await new EfCoreUnitOfWork(cutoffContext)
                .ExecuteInTransactionAsync(
                    token =>
                        new AdmissionTicketTransferRepository(
                            cutoffContext)
                            .OfferAsync(
                                OfferRequest(
                                    expiredSeed,
                                    Guid.CreateVersion7(),
                                    Digest("cutoff"))
                                with
                                {
                                    OfferedAtUtc =
                                        UtcNow.AddDays(2)
                                            .AddMinutes(-29),
                                },
                                token));
        await Assert.That(cutoff.Outcome)
            .IsEqualTo(
                AdmissionTicketTransferOutcome.Expired);

        TransferSeed hopSeed = await SeedTransferAsync(
            "hop-limit",
            maximumHops: 1);
        string capabilityDigest = Digest("hop-capability");
        AdmissionTicketTransfer transfer =
            await OfferSeedAsync(
                hopSeed,
                capabilityDigest);
        await using ExploreDbContext acceptanceContext =
            TenantContext(hopSeed.TenantId);
        AdmissionTicketTransferResult acceptance =
            await new EfCoreUnitOfWork(acceptanceContext)
                .ExecuteInTransactionAsync(
                    token =>
                        new AdmissionTicketTransferRepository(
                            acceptanceContext)
                            .ApplyAcceptanceAsync(
                                AcceptanceRequest(
                                    hopSeed,
                                    transfer.Id,
                                    capabilityDigest),
                                token));
        await using ExploreDbContext hopContext =
            TenantContext(hopSeed.TenantId);
        AdmissionTicketTransferResult hopLimit =
            await new EfCoreUnitOfWork(hopContext)
                .ExecuteInTransactionAsync(
                    token =>
                        new AdmissionTicketTransferRepository(
                            hopContext)
                            .OfferAsync(
                                OfferRequest(
                                    hopSeed,
                                    Guid.CreateVersion7(),
                                    Digest("next-hop")),
                                token));

        await Assert.That(acceptance.Outcome)
            .IsEqualTo(
                AdmissionTicketTransferOutcome.Accepted);
        await Assert.That(hopLimit.Outcome)
            .IsEqualTo(
                AdmissionTicketTransferOutcome
                    .HopLimitReached);
        await using ExploreDbContext verification =
            TenantContext(hopSeed.TenantId);
        RegistrationOrder order =
            await verification.RegistrationOrders
                .AsNoTracking()
                .SingleAsync(value =>
                    value.Id == hopSeed.OrderId);
        await Assert.That(order.AccountUserId)
            .IsEqualTo(hopSeed.SourceSubjectUserId);
        await Assert.That(order.TotalDueMinorSnapshot)
            .IsEqualTo(0);
    }

    private async Task<TransferSeed> SeedTransferAsync(
        string suffix,
        int maximumHops = 2)
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context =
            fixture.CreateDbContext();
        var tenant = new Tenant
        {
            FullName = $"Transfer {suffix}",
            Slug =
                $"transfer-{suffix}-{Guid.CreateVersion7():N}",
            TenantStatusId =
                (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
        var sourceUser = new User
        {
            Pii = new UserPii
            {
                Email =
                    $"source-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Source",
                LastName = "Holder",
            },
        };
        var recipientUser = new User
        {
            Pii = new UserPii
            {
                Email =
                    $"recipient-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Recipient",
                LastName = "Holder",
            },
        };
        context.AddRange(
            tenant,
            sourceUser,
            recipientUser);
        await context.SaveChangesAsync();
        var actor = new Actor
        {
            Pii = new ActorPii
            {
                DisplayName = "Transfer organizer",
            },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = sourceUser.Id,
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();
        var eventEntity =
            new DomainEvent(EventStatusEnum.Draft)
            {
                Id = Guid.CreateVersion7(),
                Title = $"Transfer {suffix}",
                Subtitle = string.Empty,
                Description = string.Empty,
                FirstSessionDate =
                    DateOnly.FromDateTime(
                        UtcNow.AddDays(2)),
                LastSessionDate =
                    DateOnly.FromDateTime(
                        UtcNow.AddDays(2)),
                EventTypeId = 1,
                AudienceGenderId = 1,
                AudienceAgeId = 1,
                ActorId = actor.Id,
                Actor = null!,
                OrganizerActorId = actor.Id,
                TenantId = tenant.Id,
                Tenant = tenant,
                VisibilityTypeId = 1,
                VisibilityType = null!,
                EventStatus = null!,
                EventFormatId = 1,
                EventFormat = null!,
                EventProvenanceTypeId =
                    (int)EventProvenanceTypeEnum
                        .OrganizerCreated,
            };
        EventTicketCatalogVersion catalog =
            EventTicketCatalogVersion.Create(
                tenant.Id,
                eventEntity.Id,
                "USD",
                1);
        EventTicketType ticketType =
            EventTicketType.Create(
                Guid.CreateVersion7(),
                tenant.Id,
                catalog.Id,
                "Transfer ticket",
                "USD",
                TicketPricingModeEnum.Free,
                fixedPrice: null,
                minimumPrice: null,
                suggestedPrice: null,
                ParticipantDataCollectionModeEnum.None,
                capacityPoolId: null,
                minimumAge: null,
                maximumAge: null,
                requiresGuardian: false,
                requiresApproval: false,
                perOrderLimit: null,
                perAccountLimit: null,
                perVerifiedContactLimit: null,
                perBookingPartyLimit: null);
        catalog.AddTicketType(ticketType, null);
        catalog.AddEntitlement(
            ticketType,
            TicketTypeEntitlement.CreateForEvent(
                ticketType.Id,
                tenant.Id,
                eventEntity.Id,
                includedQuantity: 1));
        catalog.Publish();
        RegistrationOrder order =
            RegistrationOrder.Create(
                tenant.Id,
                eventEntity.Id,
                sourceUser.Id,
                actor.Id,
                BookingPartyTypeEnum.Individual,
                catalog.Id,
                RegistrationParticipationSnapshot.Create(
                    Guid.CreateVersion7(),
                    1,
                    1,
                    1,
                    null),
                registrationWorkflowVersionId: null,
                guestAccessTokenHash: null,
                "USD",
                UtcNow,
                expiresAt: null);
        RegistrationOrderLine line =
            RegistrationOrderLine.Create(
                catalog,
                ticketType,
                order.Id,
                quantity: 1,
                chosenUnitPriceAmount: null,
                platformFeePolicy: null);
        order.AddLine(line);
        RegistrationParticipant source =
            RegistrationParticipant.Create(
                tenant.Id,
                order.Id,
                sourceUser.Id,
                ParticipantTypeEnum.Adult,
                guardian: null);
        RegistrationParticipant recipient =
            RegistrationParticipant.Create(
                tenant.Id,
                order.Id,
                recipientUser.Id,
                ParticipantTypeEnum.Adult,
                guardian: null);
        RegistrationTicketAssignment assignment =
            RegistrationTicketAssignment.CreateAssigned(
                Guid.CreateVersion7(),
                line.Id,
                1,
                source,
                UtcNow);
        assignment.ConcurrencyStamp =
            Guid.CreateVersion7();
        order.AddParticipant(source);
        order.AddParticipant(recipient);
        order.AddAssignment(line, assignment, source);
        order.ApplyTotals(
            RegistrationOrderTotalsSnapshot.Create(
                "USD",
                organizerDirectedTotalMinor: 0,
                platformFeeTotalMinor: 0,
                organizerEarningsTotalMinor: 0,
                platformContributionTotalMinor: 0));
        order.TransitionTo(
            RegistrationOrderStatusEnum.AwaitingRequirements,
            UtcNow);
        order.TransitionTo(
            RegistrationOrderStatusEnum.ReadyForCheckout,
            UtcNow);
        order.TransitionTo(
            RegistrationOrderStatusEnum.Confirmed,
            UtcNow);
        context.AddRange(eventEntity, catalog, order);
        await context.SaveChangesAsync();

        ParticipantAdmissionEligibility eligibility =
            ParticipantAdmissionEligibility.Create(
                tenant.Id,
                eventEntity.Id,
                assignment,
                source,
                consentRequired: false,
                approvalRequired: false,
                UtcNow);
        eligibility.RecordSubjectCompletion(
            source,
            sourceUser.Id,
            subjectConsentRecordId: null,
            UtcNow,
            Guid.CreateVersion7());
        string oldCredentialDigest = Digest(
            $"old-{suffix}");
        AdmissionTicket ticket = AdmissionTicket.Issue(
            order,
            line,
            assignment,
            source,
            catalog,
            ticketType,
            Guid.CreateVersion7(),
            $"T-{Guid.CreateVersion7():N}",
            Guid.CreateVersion7(),
            credentialVersion: 1,
            lookupKeyVersion: 7,
            oldCredentialDigest,
            UtcNow);
        ticket.ConcurrencyStamp = Guid.CreateVersion7();
        TicketTransferPolicy transferPolicy =
            TicketTransferPolicy.Create(
                Guid.CreateVersion7(),
                tenant.Id,
                ticketType,
                isEnabled: true,
                maximumHops,
                offerLifetimeMinutes: 60,
                cutoffMinutesBeforeEvent: 30,
                UtcNow);
        AdmissionTarget target = AdmissionTarget.Create(
            Guid.CreateVersion7(),
            tenant.Id,
            eventEntity.Id,
            AdmissionTargetTypeEnum.Event,
            eventDayId: null,
            eventSessionId: null);
        AdmissionCheckInPolicy checkInPolicy =
            AdmissionCheckInPolicy.Create(
                Guid.CreateVersion7(),
                target,
                UtcNow.AddHours(-1),
                UtcNow.AddHours(1),
                maximumEntries: 1);
        AdmissionRecoveryCapability recoveryCapability =
            AdmissionRecoveryCapability.Create(
                Guid.CreateVersion7(),
                tenant.Id,
                Guid.CreateVersion7(),
                ticket.Id,
                AdmissionRecoveryPurpose.TicketRecovery
                    .ToString(),
                1,
                7,
                Digest($"recovery-{suffix}"),
                UtcNow.AddDays(1),
                UtcNow,
                Digest($"locator-{suffix}"));
        context.AddRange(
            eligibility,
            ticket,
            transferPolicy,
            target,
            checkInPolicy);
        await context.SaveChangesAsync();
        context.AdmissionRecoveryCapabilities.Add(
            recoveryCapability);
        await context.SaveChangesAsync();
        return new TransferSeed(
            tenant.Id,
            eventEntity.Id,
            actor.Id,
            sourceUser.Id,
            order.Id,
            line.Id,
            assignment.Id,
            source.Id,
            recipient.Id,
            recipientUser.Id,
            ticket.Id,
            target.Id,
            recoveryCapability.Id,
            oldCredentialDigest);
    }

    private async Task<AdmissionTicketTransfer>
        OfferSeedAsync(
            TransferSeed seed,
            string capabilityDigest)
    {
        await using ExploreDbContext context =
            TenantContext(seed.TenantId);
        AdmissionTicketTransferResult result =
            await new EfCoreUnitOfWork(context)
                .ExecuteInTransactionAsync(
                    token =>
                        new AdmissionTicketTransferRepository(
                            context)
                            .OfferAsync(
                                OfferRequest(
                                    seed,
                                    Guid.CreateVersion7(),
                                    capabilityDigest),
                                token));
        return result.Transfer
            ?? throw new InvalidOperationException(
                "Transfer offer was not persisted.");
    }

    private static AdmissionTicketTransferOfferRequest
        OfferRequest(
            TransferSeed seed,
            Guid operationKey,
            string capabilityDigest) =>
        new(
            seed.TenantId,
            seed.EventId,
            seed.TicketId,
            operationKey,
            capabilityDigest,
            UtcNow.AddDays(2),
            UtcNow.AddMinutes(1));

    private static AdmissionTicketTransferAcceptanceRequest
        AcceptanceRequest(
            TransferSeed seed,
            Guid transferId,
            string capabilityDigest) =>
        new(
            seed.TenantId,
            seed.EventId,
            seed.TicketId,
            transferId,
            capabilityDigest,
            ExpectedCredentialGeneration: 1,
            seed.RecipientParticipantId,
            seed.RecipientSubjectUserId,
            RequirementsComplete: true,
            SubjectConsentRecordId: null,
            ApprovedByActorId: null,
            Guid.CreateVersion7(),
            LookupKeyVersion: 7,
            Digest($"new-{Guid.CreateVersion7():N}"),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            UtcNow.AddMinutes(2));

    private ExploreDbContext TenantContext(Guid tenantId) =>
        fixture.CreateTenantFilteredDbContext(
            new TransferTenantContext(tenantId));

    private static string Digest(string value) =>
        Convert.ToBase64String(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(value)));

    private static bool HasProperties(
        IReadOnlyList<IReadOnlyProperty> actual,
        params string[] expected) =>
        actual.Select(property => property.Name)
            .SequenceEqual(expected);

    private sealed record TransferSeed(
        Guid TenantId,
        Guid EventId,
        Guid ActorId,
        Guid SourceSubjectUserId,
        Guid OrderId,
        Guid LineId,
        Guid AssignmentId,
        Guid SourceParticipantId,
        Guid RecipientParticipantId,
        Guid RecipientSubjectUserId,
        Guid TicketId,
        Guid AdmissionTargetId,
        Guid RecoveryCapabilityId,
        string OldCredentialDigest);

    private sealed class TransferTenantContext(
        Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
    }

    private sealed class TransferRaceGate(int participants)
    {
        private readonly TaskCompletionSource allArrived =
            new(TaskCreationOptions
                .RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release =
            new(TaskCreationOptions
                .RunContinuationsAsynchronously);
        private int arrivals;

        public Task AllArrived => allArrived.Task;

        public async Task ArriveAsync(
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrivals) ==
                participants)
            {
                allArrived.TrySetResult();
            }
            await release.Task.WaitAsync(
                cancellationToken);
        }

        public void Release() => release.TrySetResult();
    }
}
