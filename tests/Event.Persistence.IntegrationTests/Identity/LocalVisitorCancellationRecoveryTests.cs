using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Fixture = Event.Persistence.IntegrationTests.Identity.LocalCredentialFirstUseTests.Fixture;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed partial class LocalVisitorCancellationTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    [Arguments(IdentityDatabaseTopology.External, true)]
    public async Task RecoveryAcknowledgesOnlyConfirmedCleanupAndCannotRenewCompletedCancellation(
        IdentityDatabaseTopology topology, bool prepared)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var reservation = await ReserveAsync(fixture, topology, await SeedTenantAsync(fixture));
        if (prepared) await PrepareAsync(fixture, topology, reservation);
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        await WithStoreAsync(fixture, topology, reservation.TenantId, async (store, primary, identity) =>
        {
            var original = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.Id, fixture.CancellationToken);
            var candidate = Candidate(original);
            await Assert.That(await store.RecoverVisitorAsync(candidate, fixture.CancellationToken)).IsTrue();
            var canceled = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.Id, fixture.CancellationToken);
            await Assert.That(canceled.Stage).IsEqualTo(VisitorEnrollmentStage.Canceled);
            await Assert.That(canceled.Generation).IsEqualTo(original.Generation + 2);
            await Assert.That(canceled.CleanupCompletedAt).IsNotNull();
            await AssertPurgedAsync(primary, identity, original, canceled, fixture.CancellationToken);
            await Assert.That(await store.RecoverVisitorAsync(candidate, fixture.CancellationToken)).IsFalse();
            await Assert.That(await store.RecoverVisitorAsync(Candidate(canceled), fixture.CancellationToken)).IsFalse();
            var retained = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.Id, fixture.CancellationToken);
            await Assert.That(retained.Generation).IsEqualTo(canceled.Generation);
            await Assert.That(retained.CleanupCompletedAt).IsEqualTo(canceled.CleanupCompletedAt);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, "generation")]
    [Arguments(IdentityDatabaseTopology.External, "generation")]
    [Arguments(IdentityDatabaseTopology.Colocated, "stage")]
    [Arguments(IdentityDatabaseTopology.External, "stage")]
    [Arguments(IdentityDatabaseTopology.Colocated, "deadline")]
    [Arguments(IdentityDatabaseTopology.External, "deadline")]
    public async Task ChangedRecoveryVersionCannotCancelOrPurgeTheOriginalPendingCredential(
        IdentityDatabaseTopology topology, string changed)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var reservation = await ReserveAsync(fixture, topology, await SeedTenantAsync(fixture));
        var prepared = await PrepareAsync(fixture, topology, reservation);
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        await WithStoreAsync(fixture, topology, reservation.TenantId, async (store, primary, identity) =>
        {
            var original = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.Id, fixture.CancellationToken);
            var candidate = Candidate(original);
            candidate = changed switch
            {
                "generation" => candidate with { Generation = candidate.Generation + 1 },
                "stage" => candidate with { Stage = VisitorEnrollmentStage.Reserved },
                "deadline" => candidate with { ExpiresAt = candidate.ExpiresAt.AddSeconds(1) },
                _ => throw new ArgumentOutOfRangeException(nameof(changed))
            };
            await Assert.That(await store.RecoverVisitorAsync(candidate, fixture.CancellationToken)).IsFalse();
            await AssertOriginalUserAsync(identity, original, prepared, fixture.CancellationToken);
            var retained = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.Id, fixture.CancellationToken);
            await Assert.That(retained.Generation).IsEqualTo(original.Generation);
            await Assert.That(retained.Stage).IsEqualTo(VisitorEnrollmentStage.IdentityPrepared);
            await Assert.That(retained.CleanupCompletedAt).IsNull();
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    [Arguments(IdentityDatabaseTopology.External, true)]
    public async Task AdmittedRecoveryCompletesActivationAndAcknowledgementAfterExpiry(
        IdentityDatabaseTopology topology, bool activationAlreadyCommitted)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var reservation = await ReserveAsync(fixture, topology, await SeedTenantAsync(fixture));
        var prepared = await PrepareAsync(fixture, topology, reservation);
        await WithStoreAsync(fixture, topology, reservation.TenantId, async (store, primary, _) =>
        {
            var admitted = await store.AdmitVisitorAsync(reservation.Id, _ => Task.CompletedTask,
                fixture.CancellationToken) ?? throw new InvalidOperationException("Admission is absent.");
            if (activationAlreadyCommitted)
                await Assert.That(await store.ActivateVisitorAsync(reservation.Id, prepared.OperationStamp,
                    prepared.Generation, fixture.CancellationToken))
                    .IsEqualTo(Explore.Application.Contracts.Identity.LocalCredentialActivationOutcome.Activated);
            fixture.Clock.Advance(TimeSpan.FromHours(1));
            await Assert.That(await store.RecoverVisitorAsync(Candidate(admitted), fixture.CancellationToken)).IsTrue();
            var active = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.Id, fixture.CancellationToken);
            await Assert.That(active.Stage).IsEqualTo(VisitorEnrollmentStage.Active);
            await Assert.That(active.TenantUserId).IsEqualTo(admitted.TenantUserId);
            await Assert.That(active.CleanupCompletedAt).IsNull();
        });
    }

    private static VisitorEnrollmentRecoveryCandidate Candidate(VisitorEnrollmentOperation row) =>
        new(row.Id, row.TenantId, row.Stage, row.Generation, row.ExpiresAt);
}
