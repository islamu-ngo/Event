using System.Security.Cryptography;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class TicketPurchaseGovernancePersistenceTests(
    PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime UtcNow =
        new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task LiteralEffectiveCeilingFourAllowsExactlyOneConcurrentWinner()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid accountId = Guid.CreateVersion7();
        TicketPurchasePolicyVersion policy = CreatePolicy(tenantId, eventId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await AddPolicyAsync(policy, timeout.Token);

        TicketPurchaseReservationResult[] results = await Task.WhenAll(
            ReserveAsync(
                policy,
                Request(
                    tenantId,
                    eventId,
                    Guid.CreateVersion7(),
                    accountId,
                    purchaserActorId: null,
                    quantity: 3,
                    operationSeed: "race-a"),
                timeout.Token),
            ReserveAsync(
                policy,
                Request(
                    tenantId,
                    eventId,
                    Guid.CreateVersion7(),
                    accountId,
                    purchaserActorId: Guid.CreateVersion7(),
                    quantity: 3,
                    operationSeed: "race-b"),
                timeout.Token));

        await Assert.That(policy.EffectiveCeiling).IsEqualTo(4);
        await Assert.That(results.Count(result =>
            result.Disposition == TicketPurchaseReservationDisposition.Reserved))
            .IsEqualTo(1);
        await Assert.That(results.Count(result =>
            result.Disposition == TicketPurchaseReservationDisposition.CeilingExceeded))
            .IsEqualTo(1);
        await Assert.That(results.Where(result =>
                result.Disposition == TicketPurchaseReservationDisposition.Reserved)
            .Sum(result => result.ConsumedQuantity))
            .IsEqualTo(3);
    }

    [Test]
    public async Task ContextSwitchCannotBypassAccountAuthorityAndUnrelatedMemberIsIndependent()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid firstAccountId = Guid.CreateVersion7();
        Guid secondAccountId = Guid.CreateVersion7();
        Guid groupActorId = Guid.CreateVersion7();
        TicketPurchasePolicyVersion policy = CreatePolicy(tenantId, eventId);
        await AddPolicyAsync(policy, CancellationToken.None);

        TicketPurchaseReservationResult personal = await ReserveAsync(
            policy,
            Request(
                tenantId,
                eventId,
                Guid.CreateVersion7(),
                firstAccountId,
                purchaserActorId: null,
                quantity: 4,
                operationSeed: "personal"),
            CancellationToken.None);
        TicketPurchaseReservationResult sameAccountAsGroup = await ReserveAsync(
            policy,
            Request(
                tenantId,
                eventId,
                Guid.CreateVersion7(),
                firstAccountId,
                groupActorId,
                quantity: 1,
                operationSeed: "same-account-group"),
            CancellationToken.None);
        TicketPurchaseReservationResult unrelatedMember = await ReserveAsync(
            policy,
            Request(
                tenantId,
                eventId,
                Guid.CreateVersion7(),
                secondAccountId,
                groupActorId,
                quantity: 4,
                operationSeed: "other-member"),
            CancellationToken.None);

        await Assert.That(personal.Disposition)
            .IsEqualTo(TicketPurchaseReservationDisposition.Reserved);
        await Assert.That(sameAccountAsGroup.Disposition)
            .IsEqualTo(TicketPurchaseReservationDisposition.CeilingExceeded);
        await Assert.That(unrelatedMember.Disposition)
            .IsEqualTo(TicketPurchaseReservationDisposition.Reserved);
    }

    [Test]
    public async Task NameOnlyUsesPerOrderAuthorityInsteadOfClaimingCrossOrderIdentity()
    {
        await fixture.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        TicketPurchasePolicyVersion policy = CreatePolicy(tenantId, eventId);
        await AddPolicyAsync(policy, CancellationToken.None);
        Guid firstOrderId = Guid.CreateVersion7();
        Guid secondOrderId = Guid.CreateVersion7();

        TicketPurchaseReservationResult first = await ReserveAsync(
            policy,
            NameOnlyRequest(
                tenantId,
                eventId,
                firstOrderId,
                4,
                "name-only-a"),
            CancellationToken.None);
        TicketPurchaseReservationResult second = await ReserveAsync(
            policy,
            NameOnlyRequest(
                tenantId,
                eventId,
                secondOrderId,
                4,
                "name-only-b"),
            CancellationToken.None);
        TicketPurchaseReservationResult sameOrderOverflow = await ReserveAsync(
            policy,
            NameOnlyRequest(
                tenantId,
                eventId,
                firstOrderId,
                1,
                "name-only-c"),
            CancellationToken.None);

        await Assert.That(
                TicketPurchaseAuthorityDimension.NameOnly(firstOrderId)
                    .SupportsHardCrossOrderCeiling)
            .IsFalse();
        await Assert.That(first.Disposition)
            .IsEqualTo(TicketPurchaseReservationDisposition.Reserved);
        await Assert.That(second.Disposition)
            .IsEqualTo(TicketPurchaseReservationDisposition.Reserved);
        await Assert.That(sameOrderOverflow.Disposition)
            .IsEqualTo(TicketPurchaseReservationDisposition.CeilingExceeded);
    }

    [Test]
    public async Task DurableReplayConflictsOnFingerprintAndRemainsTenantQualified()
    {
        await fixture.ResetAsync();
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid accountId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        TicketPurchasePolicyVersion firstPolicy =
            CreatePolicy(firstTenantId, eventId);
        TicketPurchasePolicyVersion secondPolicy =
            CreatePolicy(secondTenantId, eventId);
        await AddPolicyAsync(firstPolicy, CancellationToken.None);
        await AddPolicyAsync(secondPolicy, CancellationToken.None);
        TicketPurchaseReservationRequest original = Request(
            firstTenantId,
            eventId,
            orderId,
            accountId,
            purchaserActorId: null,
            quantity: 2,
            operationSeed: "durable");

        TicketPurchaseReservationResult first = await ReserveAsync(
            firstPolicy,
            original,
            CancellationToken.None);
        TicketPurchaseReservationResult replay = await ReserveAsync(
            firstPolicy,
            original,
            CancellationToken.None);
        TicketPurchaseReservationResult conflict = await ReserveAsync(
            firstPolicy,
            original with
            {
                Operation = Operation("durable", "different-fingerprint"),
            },
            CancellationToken.None);
        TicketPurchaseReservationResult otherTenant = await ReserveAsync(
            secondPolicy,
            original with
            {
                TenantId = secondTenantId,
                OrderId = Guid.CreateVersion7(),
            },
            CancellationToken.None);

        await Assert.That(first.Disposition)
            .IsEqualTo(TicketPurchaseReservationDisposition.Reserved);
        await Assert.That(replay.Disposition)
            .IsEqualTo(TicketPurchaseReservationDisposition.Replay);
        await Assert.That(replay.ReservationId)
            .IsEqualTo(first.ReservationId);
        await Assert.That(replay.ConsumedQuantity)
            .IsEqualTo(first.ConsumedQuantity);
        await Assert.That(conflict.Disposition)
            .IsEqualTo(TicketPurchaseReservationDisposition.OperationConflict);
        await Assert.That(otherTenant.Disposition)
            .IsEqualTo(TicketPurchaseReservationDisposition.Reserved);
    }

    private async Task AddPolicyAsync(
        TicketPurchasePolicyVersion policy,
        CancellationToken cancellationToken)
    {
        await using ExploreDbContext context =
            fixture.CreateTenantFilteredDbContext(
                new TestTenantContext(policy.TenantId));
        var repository = new TicketPurchaseGovernanceRepository(context);
        await repository.AddPolicyVersionAsync(policy, cancellationToken);
    }

    private async Task<TicketPurchaseReservationResult> ReserveAsync(
        TicketPurchasePolicyVersion policy,
        TicketPurchaseReservationRequest request,
        CancellationToken cancellationToken)
    {
        await using ExploreDbContext context =
            fixture.CreateTenantFilteredDbContext(
                new TestTenantContext(request.TenantId));
        var repository = new TicketPurchaseGovernanceRepository(context);
        return await repository.ReserveAsync(
            policy,
            request,
            cancellationToken);
    }

    private static TicketPurchasePolicyVersion CreatePolicy(
        Guid tenantId,
        Guid eventId) => TicketPurchasePolicyVersion.Create(
        tenantId,
        eventId,
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        instanceCeiling: 5,
        tenantCeiling: 4,
        eventCeiling: 6,
        UtcNow);

    private static TicketPurchaseReservationRequest Request(
        Guid tenantId,
        Guid eventId,
        Guid orderId,
        Guid accountId,
        Guid? purchaserActorId,
        int quantity,
        string operationSeed) => new(
        tenantId,
        eventId,
        orderId,
        quantity,
        TicketPurchaseAuthorityDimension.Authenticated(
            accountId,
            purchaserActorId),
        Operation(operationSeed, operationSeed));

    private static TicketPurchaseReservationRequest NameOnlyRequest(
        Guid tenantId,
        Guid eventId,
        Guid orderId,
        int quantity,
        string operationSeed) => new(
        tenantId,
        eventId,
        orderId,
        quantity,
        TicketPurchaseAuthorityDimension.NameOnly(orderId),
        Operation(operationSeed, operationSeed));

    private static TicketPurchaseOperationIdentity Operation(
        string keySeed,
        string fingerprintSeed) =>
        TicketPurchaseOperationIdentity.Create(
            Hash(keySeed),
            Hash(fingerprintSeed));

    private static string Hash(string value) => Convert.ToBase64String(
        SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class TestTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
    }
}
