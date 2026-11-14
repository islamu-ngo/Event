using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Fixture = Event.Persistence.IntegrationTests.Identity.LocalCredentialFirstUseTests.Fixture;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
public sealed partial class LocalVisitorCancellationTests
{
    public enum ChangedEvidence
    {
        NativeActor, Generation, CurrentOtherOperation, Metadata, MissingMetadata, Verified, PrimaryUser, Privileged
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    [Arguments(IdentityDatabaseTopology.External, true)]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    public async Task ExpiryOrExplicitDenialCancelsBeforeSeparateExactCleanup(
        IdentityDatabaseTopology topology, bool expired)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var unrelated = await fixture.ReadAsync();
        Guid tenant = await SeedTenantAsync(fixture);
        var reservation = await ReserveAsync(fixture, topology, tenant);
        var prepared = await PrepareAsync(fixture, topology, reservation);
        await WithStoreAsync(fixture, topology, tenant, async (store, _, identity) =>
        {
            await Assert.That(await store.DeleteCanceledVisitorAsync(reservation.Id, fixture.CancellationToken)).IsFalse();
            await Assert.That(await store.CancelVisitorAsync(reservation.Id, true, fixture.CancellationToken)).IsNull();
            await AssertOriginalUserAsync(identity, reservation, prepared, fixture.CancellationToken);
        });
        if (expired) fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        VisitorEnrollmentOperation? canceled = null;
        await WithStoreAsync(fixture, topology, tenant, async (store, _, identity) =>
        {
            canceled = await store.CancelVisitorAsync(reservation.Id, expired, fixture.CancellationToken);
            await Assert.That(canceled).IsNotNull();
            await Assert.That(canceled!.Stage).IsEqualTo(VisitorEnrollmentStage.Canceled);
            await Assert.That(canceled.Generation).IsEqualTo(3L);
            await Assert.That(canceled.CredentialGeneration).IsEqualTo(prepared.Generation);
            await AssertOriginalUserAsync(identity, reservation, prepared, fixture.CancellationToken);
        });
        // Reopen the owner: cleanup must consume a durable primary cancellation, not a tracker transition.
        await WithStoreAsync(fixture, topology, tenant, async (store, primary, identity) =>
        {
            var replay = await store.CancelVisitorAsync(reservation.Id, expired, fixture.CancellationToken);
            await Assert.That(replay!.Generation).IsEqualTo(canceled!.Generation);
            await Assert.That(replay.UpdatedAt).IsEqualTo(canceled.UpdatedAt);
            await Assert.That(await store.DeleteCanceledVisitorAsync(reservation.Id, fixture.CancellationToken)).IsTrue();
            await AssertPurgedAsync(primary, identity, reservation, canceled, fixture.CancellationToken);
        });
        await WithStoreAsync(fixture, topology, tenant, async (store, primary, identity) =>
        {
            await Assert.That(await store.DeleteCanceledVisitorAsync(reservation.Id, fixture.CancellationToken)).IsTrue();
            var replay = await store.CancelVisitorAsync(reservation.Id, expired, fixture.CancellationToken);
            await Assert.That(replay!.Generation).IsEqualTo(canceled!.Generation);
            await AssertPurgedAsync(primary, identity, reservation, canceled, fixture.CancellationToken);
            // The normalized username is released only by deleting the original pending account.
            var next = await store.ReserveVisitorAsync(Guid.CreateVersion7(), Digest(), _ => Task.CompletedTask,
                fixture.CancellationToken);
            await Assert.That(await store.PrepareVisitorAsync(next.Id, prepared.Username, Password(), null,
                fixture.CancellationToken)).IsNotNull();
            await Assert.That(next.LocalSubjectId == reservation.LocalSubjectId).IsFalse();
        });
        await Assert.That(await fixture.ReadAsync()).IsEqualTo(unrelated);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ReservationWithCoherentNativeAbsenceCanCancelAndAcknowledgeCleanup(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var unrelated = await fixture.ReadAsync();
        Guid tenant = await SeedTenantAsync(fixture);
        var reservation = await ReserveAsync(fixture, topology, tenant);
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        await WithStoreAsync(fixture, topology, tenant, async (store, _, _) =>
        {
            var canceled = await store.CancelVisitorAsync(reservation.Id, true, fixture.CancellationToken);
            await Assert.That(canceled!.Generation).IsEqualTo(2L);
            await Assert.That(canceled.CredentialGeneration).IsNull();
        });
        await WithStoreAsync(fixture, topology, tenant, async (store, primary, identity) =>
        {
            await Assert.That(await store.DeleteCanceledVisitorAsync(reservation.Id, fixture.CancellationToken)).IsTrue();
            await Assert.That(await store.DeleteCanceledVisitorAsync(reservation.Id, fixture.CancellationToken)).IsTrue();
            var canceled = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.Id, fixture.CancellationToken);
            await Assert.That(canceled.Generation).IsEqualTo(2L);
            await AssertPurgedAsync(primary, identity, reservation, canceled, fixture.CancellationToken);
        });
        await Assert.That(await fixture.ReadAsync()).IsEqualTo(unrelated);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    [Arguments(IdentityDatabaseTopology.External, true)]
    public async Task CommittedAdmissionAndActivationSurviveExpiryCancellationAndPurge(
        IdentityDatabaseTopology topology, bool activate)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        Guid tenant = await SeedTenantAsync(fixture);
        var reservation = await ReserveAsync(fixture, topology, tenant);
        var prepared = await PrepareAsync(fixture, topology, reservation);
        Guid? member = null;
        await WithStoreAsync(fixture, topology, tenant, async (store, _, _) =>
        {
            // This callback arranges persistence admission; it does not prove settings authorization.
            var admission = await store.AdmitVisitorAsync(reservation.Id, _ => Task.CompletedTask,
                fixture.CancellationToken);
            member = admission!.TenantUserId;
            if (activate)
            {
                await Assert.That(await store.ActivateVisitorAsync(reservation.Id, prepared.OperationStamp,
                    prepared.Generation, fixture.CancellationToken)).IsEqualTo(LocalCredentialActivationOutcome.Activated);
                await Assert.That((await store.AcknowledgeVisitorActivationAsync(reservation.Id,
                    fixture.CancellationToken))!.Stage).IsEqualTo(VisitorEnrollmentStage.Active);
            }
        });
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        await WithStoreAsync(fixture, topology, tenant, async (store, primary, identity) =>
        {
            await Assert.That(await store.CancelVisitorAsync(reservation.Id, true, fixture.CancellationToken)).IsNull();
            await Assert.That(await store.CancelVisitorAsync(reservation.Id, false, fixture.CancellationToken)).IsNull();
            await Assert.That(await store.DeleteCanceledVisitorAsync(reservation.Id, fixture.CancellationToken)).IsFalse();
            await AssertOriginalUserAsync(identity, reservation, prepared, fixture.CancellationToken);
            var receipt = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.Id, fixture.CancellationToken);
            await Assert.That(receipt.Stage).IsEqualTo(
                activate ? VisitorEnrollmentStage.Active : VisitorEnrollmentStage.PrimaryAdmitted);
            await Assert.That(receipt.TenantUserId).IsEqualTo(member);
            await Assert.That(await primary.Set<TenantUser>().AsNoTracking()
                .AnyAsync(row => row.Id == member, fixture.CancellationToken)).IsTrue();
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ForeignTenantAndMissingReceiptCannotCancelOrDeleteOriginalPendingIdentity(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        Guid tenant = await SeedTenantAsync(fixture);
        Guid foreign = await SeedTenantAsync(fixture);
        var reservation = await ReserveAsync(fixture, topology, tenant);
        var prepared = await PrepareAsync(fixture, topology, reservation);
        await WithStoreAsync(fixture, topology, foreign, async (store, _, identity) =>
        {
            await Assert.That(await store.CancelVisitorAsync(reservation.Id, false, fixture.CancellationToken)).IsNull();
            await Assert.That(await store.DeleteCanceledVisitorAsync(reservation.Id, fixture.CancellationToken)).IsFalse();
            await Assert.That(await store.DeleteCanceledVisitorAsync(Guid.CreateVersion7(), fixture.CancellationToken))
                .IsFalse();
            await AssertOriginalUserAsync(identity, reservation, prepared, fixture.CancellationToken);
        });
        await WithStoreAsync(fixture, topology, tenant, async (store, _, _) =>
        {
            await store.CancelVisitorAsync(reservation.Id, false, fixture.CancellationToken);
        });
        await WithStoreAsync(fixture, topology, foreign, async (store, _, identity) =>
        {
            await Assert.That(await store.DeleteCanceledVisitorAsync(reservation.Id, fixture.CancellationToken)).IsFalse();
            await AssertOriginalUserAsync(identity, reservation, prepared, fixture.CancellationToken);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, ChangedEvidence.NativeActor)]
    [Arguments(IdentityDatabaseTopology.External, ChangedEvidence.NativeActor)]
    [Arguments(IdentityDatabaseTopology.Colocated, ChangedEvidence.Generation)]
    [Arguments(IdentityDatabaseTopology.External, ChangedEvidence.Generation)]
    [Arguments(IdentityDatabaseTopology.Colocated, ChangedEvidence.CurrentOtherOperation)]
    [Arguments(IdentityDatabaseTopology.External, ChangedEvidence.CurrentOtherOperation)]
    [Arguments(IdentityDatabaseTopology.Colocated, ChangedEvidence.Metadata)]
    [Arguments(IdentityDatabaseTopology.External, ChangedEvidence.Metadata)]
    [Arguments(IdentityDatabaseTopology.Colocated, ChangedEvidence.MissingMetadata)]
    [Arguments(IdentityDatabaseTopology.External, ChangedEvidence.MissingMetadata)]
    [Arguments(IdentityDatabaseTopology.Colocated, ChangedEvidence.Verified)]
    [Arguments(IdentityDatabaseTopology.External, ChangedEvidence.Verified)]
    [Arguments(IdentityDatabaseTopology.Colocated, ChangedEvidence.PrimaryUser)]
    [Arguments(IdentityDatabaseTopology.External, ChangedEvidence.PrimaryUser)]
    [Arguments(IdentityDatabaseTopology.Colocated, ChangedEvidence.Privileged)]
    [Arguments(IdentityDatabaseTopology.External, ChangedEvidence.Privileged)]
    public async Task ChangedOrPartialAccountEvidenceVetoesCleanupWithoutReplacingOriginalHash(
        IdentityDatabaseTopology topology, ChangedEvidence change)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var unrelated = await fixture.ReadAsync();
        Guid tenant = await SeedTenantAsync(fixture);
        var reservation = await ReserveAsync(fixture, topology, tenant);
        var prepared = await PrepareAsync(fixture, topology, reservation);
        await WithStoreAsync(fixture, topology, tenant, async (store, _, _) =>
        {
            await store.CancelVisitorAsync(reservation.Id, false, fixture.CancellationToken);
        });
        await WithStoreAsync(fixture, topology, tenant, async (_, primary, identity) =>
        {
            switch (change)
            {
                case ChangedEvidence.NativeActor:
                    await identity.Set<LocalIdentityCredentialOperation>()
                        .Where(row => row.Id == reservation.CredentialOperationId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.PersonalActorId,
                            Guid.CreateVersion7()), fixture.CancellationToken);
                    break;
                case ChangedEvidence.Generation:
                    var eligibility = await identity.Set<LocalIdentityEligibility>().SingleAsync(
                        row => row.LocalSubjectId == reservation.LocalSubjectId, fixture.CancellationToken);
                    eligibility.SetLockout(eligibility.Generation, eligibility.CurrentOperationId, null,
                        fixture.Clock.GetUtcNow().UtcDateTime);
                    await identity.SaveChangesAsync(fixture.CancellationToken);
                    break;
                case ChangedEvidence.CurrentOtherOperation:
                    // A real persisted second operation satisfies the Restrict FK while breaking the original pointer.
                    await identity.Set<LocalIdentityEligibility>()
                        .Where(row => row.LocalSubjectId == reservation.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.CurrentOperationId,
                            fixture.Receipt.OperationId), fixture.CancellationToken);
                    break;
                case ChangedEvidence.Metadata:
                    await identity.Set<IdentityUserToken<Guid>>()
                        .Where(row => row.UserId == reservation.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "{}"),
                            fixture.CancellationToken);
                    break;
                case ChangedEvidence.MissingMetadata:
                    await identity.Set<IdentityUserToken<Guid>>()
                        .Where(row => row.UserId == reservation.LocalSubjectId)
                        .ExecuteDeleteAsync(fixture.CancellationToken);
                    break;
                case ChangedEvidence.Verified:
                    await identity.Set<LocalIdentityUser>().Where(row => row.Id == reservation.LocalSubjectId)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.EmailConfirmed, true),
                            fixture.CancellationToken);
                    break;
                case ChangedEvidence.PrimaryUser:
                    primary.Users.Add(new User
                    {
                        Id = reservation.LocalSubjectId, CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                        Pii = new UserPii { Email = string.Empty, FirstName = "Pending", LastName = string.Empty }
                    });
                    await primary.SaveChangesAsync(fixture.CancellationToken);
                    break;
                case ChangedEvidence.Privileged:
                    identity.Set<IdentityUserClaim<Guid>>().Add(new IdentityUserClaim<Guid>
                    {
                        UserId = reservation.LocalSubjectId, ClaimType = "role", ClaimValue = "administrator"
                    });
                    await identity.SaveChangesAsync(fixture.CancellationToken);
                    break;
            }
        });
        await WithStoreAsync(fixture, topology, tenant, async (store, primary, identity) =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                store.DeleteCanceledVisitorAsync(reservation.Id, fixture.CancellationToken));
            await AssertOriginalUserAsync(identity, reservation, prepared, fixture.CancellationToken);
            await Assert.That(await identity.Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .AnyAsync(row => row.Id == reservation.CredentialOperationId, fixture.CancellationToken)).IsTrue();
            await Assert.That(await identity.Set<LocalIdentityEligibility>().AsNoTracking()
                .AnyAsync(row => row.LocalSubjectId == reservation.LocalSubjectId, fixture.CancellationToken)).IsTrue();
            var receipt = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.Id, fixture.CancellationToken);
            await Assert.That(receipt.Stage).IsEqualTo(VisitorEnrollmentStage.Canceled);
            await Assert.That(receipt.Generation).IsEqualTo(3L);
        });
        await Assert.That(await fixture.ReadAsync()).IsEqualTo(unrelated);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ChangedNativeBindingCannotAdvanceThePrimaryCancellationReceipt(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        Guid tenant = await SeedTenantAsync(fixture);
        var reservation = await ReserveAsync(fixture, topology, tenant);
        var prepared = await PrepareAsync(fixture, topology, reservation);
        await WithStoreAsync(fixture, topology, tenant, async (_, _, identity) =>
        {
            await identity.Set<LocalIdentityCredentialOperation>()
                .Where(row => row.Id == reservation.CredentialOperationId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.PersonalActorId,
                    Guid.CreateVersion7()), fixture.CancellationToken);
        });
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        await WithStoreAsync(fixture, topology, tenant, async (store, primary, identity) =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                store.CancelVisitorAsync(reservation.Id, true, fixture.CancellationToken));
            await Assert.That(await store.DeleteCanceledVisitorAsync(reservation.Id, fixture.CancellationToken)).IsFalse();
            await AssertOriginalUserAsync(identity, reservation, prepared, fixture.CancellationToken);
            var receipt = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.Id, fixture.CancellationToken);
            await Assert.That(receipt.Stage).IsEqualTo(VisitorEnrollmentStage.IdentityPrepared);
            await Assert.That(receipt.Generation).IsEqualTo(2L);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ReservedReceiptWithNativePreparationCanPurgeWithoutFabricatingPreparedAcknowledgement(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        Guid tenant = await SeedTenantAsync(fixture);
        var reservation = await ReserveAsync(fixture, topology, tenant);
        await WithStoreAsync(fixture, topology, tenant, async (store, _, _) =>
        {
            await Assert.That(await store.PrepareVisitorAsync(reservation.Id, $"visitor-{Guid.CreateVersion7():N}",
                Password(), null, fixture.CancellationToken)).IsNotNull();
        });
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        await WithStoreAsync(fixture, topology, tenant, async (store, _, _) =>
        {
            var canceled = await store.CancelVisitorAsync(reservation.Id, true, fixture.CancellationToken);
            await Assert.That(canceled!.Generation).IsEqualTo(2L);
            await Assert.That(canceled.CredentialGeneration).IsNull();
        });
        await WithStoreAsync(fixture, topology, tenant, async (store, primary, identity) =>
        {
            await Assert.That(await store.DeleteCanceledVisitorAsync(reservation.Id, fixture.CancellationToken)).IsTrue();
            var canceled = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.Id, fixture.CancellationToken);
            await AssertPurgedAsync(primary, identity, reservation, canceled, fixture.CancellationToken);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task OrdinaryRuntimeOwnerCannotCancelOrPurgeVisitors(IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        await using var ordinary = fixture.Provider.CreateAsyncScope();
        var store = fixture.StateStore(ordinary);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            store.CancelVisitorAsync(Guid.CreateVersion7(), false, fixture.CancellationToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            store.DeleteCanceledVisitorAsync(Guid.CreateVersion7(), fixture.CancellationToken));
    }

    private static async Task AssertOriginalUserAsync(DbContext identity, VisitorEnrollmentOperation reservation,
        Prepared prepared, CancellationToken token)
    {
        var user = await identity.Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == reservation.LocalSubjectId, token);
        await Assert.That(user.PasswordHash == prepared.Hash).IsTrue();
        await Assert.That(user.SecurityStamp == prepared.SecurityStamp).IsTrue();
        await Assert.That(user.ConcurrencyStamp == prepared.UserStamp).IsTrue();
        await Assert.That(user.UserName).IsEqualTo(prepared.Username);
    }

    private static async Task AssertPurgedAsync(ExploreDbContext primary, DbContext identity,
        VisitorEnrollmentOperation original, VisitorEnrollmentOperation canceled, CancellationToken token)
    {
        await Assert.That(await identity.Set<LocalIdentityUser>().AsNoTracking()
            .AnyAsync(row => row.Id == original.LocalSubjectId, token)).IsFalse();
        await Assert.That(await identity.Set<IdentityUserToken<Guid>>().AsNoTracking()
            .AnyAsync(row => row.UserId == original.LocalSubjectId, token)).IsFalse();
        await Assert.That(await identity.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .AnyAsync(row => row.Id == original.CredentialOperationId || row.LocalSubjectId == original.LocalSubjectId,
                token)).IsFalse();
        await Assert.That(await identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .AnyAsync(row => row.LocalSubjectId == original.LocalSubjectId, token)).IsFalse();
        var receipt = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
            .SingleAsync(row => row.Id == original.Id, token);
        await Assert.That(receipt.Stage).IsEqualTo(VisitorEnrollmentStage.Canceled);
        await Assert.That(receipt.Generation).IsEqualTo(canceled.Generation);
        await Assert.That(receipt.UpdatedAt).IsEqualTo(canceled.UpdatedAt);
        await Assert.That(receipt.CredentialGeneration).IsEqualTo(canceled.CredentialGeneration);
        await Assert.That(receipt.LocalSubjectId).IsEqualTo(original.LocalSubjectId);
        await Assert.That(receipt.CredentialOperationId).IsEqualTo(original.CredentialOperationId);
        await Assert.That(receipt.PersonalActorId).IsEqualTo(original.PersonalActorId);
        await Assert.That(receipt.ExternalLoginId).IsEqualTo(original.ExternalLoginId);
        await Assert.That(receipt.IntentDigest).IsEqualTo(original.IntentDigest);
        await Assert.That(receipt.ExpiresAt).IsEqualTo(original.ExpiresAt);
        await Assert.That(receipt.TenantUserId).IsNull();
    }

    private static async Task<Prepared> PrepareAsync(
        Fixture fixture, IdentityDatabaseTopology topology, VisitorEnrollmentOperation reservation)
    {
        Prepared? result = null;
        await WithStoreAsync(fixture, topology, reservation.TenantId, async (store, _, identity) =>
        {
            string username = $"visitor-{Guid.CreateVersion7():N}";
            var native = await store.PrepareVisitorAsync(reservation.Id, username, Password(), null,
                fixture.CancellationToken) ?? throw new InvalidOperationException("Preparation is absent.");
            var receipt = await store.RecordVisitorPreparedAsync(reservation.Id, fixture.CancellationToken)
                ?? throw new InvalidOperationException("Prepared receipt is absent.");
            var user = await identity.Set<LocalIdentityUser>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.LocalSubjectId, fixture.CancellationToken);
            result = new(username, user.PasswordHash, user.SecurityStamp, user.ConcurrencyStamp,
                native.OperationConcurrencyStamp, receipt.CredentialGeneration!.Value);
        });
        return result ?? throw new InvalidOperationException("Native preparation did not run.");
    }

    private static async Task<VisitorEnrollmentOperation> ReserveAsync(
        Fixture fixture, IdentityDatabaseTopology topology, Guid tenant)
    {
        VisitorEnrollmentOperation? result = null;
        await WithStoreAsync(fixture, topology, tenant, async (store, _, _) =>
        {
            // Persistence tests supply the final policy seam, not settings authorization evidence.
            result = await store.ReserveVisitorAsync(Guid.CreateVersion7(), Digest(), _ => Task.CompletedTask,
                fixture.CancellationToken);
        });
        return result ?? throw new InvalidOperationException("Reservation is absent.");
    }

    private static async Task<Guid> SeedTenantAsync(Fixture fixture)
    {
        await using var scope = fixture.Provider.CreateAsyncScope();
        Guid tenant = Guid.CreateVersion7();
        await using var seed = new ExploreDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<ExploreDbContext>>())
        {
            TenantContext = new SelectedTenant(tenant)
        };
        seed.BindTenantLease();
        seed.Tenants.Add(new Tenant
        {
            Id = tenant, FullName = "Visitor cancellation tenant", Slug = $"visitor-{tenant:N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!,
            CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        await seed.SaveChangesAsync(fixture.CancellationToken);
        return tenant;
    }

    private static async Task WithStoreAsync(Fixture fixture, IdentityDatabaseTopology topology, Guid tenant,
        Func<LocalIdentityCredentialStateStore, ExploreDbContext, DbContext, Task> action)
    {
        await using var provider = await LocalIdentityExecutionScopeFactoryTests.ComposeAsync(
            fixture, topology, tenantContext: new SelectedTenant(tenant));
        await using var request = provider.CreateAsyncScope();
        await using var selected = await request.ServiceProvider.GetRequiredService<ILocalIdentityExecutionScopeFactory>()
            .CreateAsync(fixture.CancellationToken);
        var lease = selected.Services.GetRequiredService<LocalIdentityExecutionLease>();
        var primary = lease.Application ?? throw new InvalidOperationException("Selected primary owner is absent.");
        var identity = lease.Identity ?? throw new InvalidOperationException("Selected native owner is absent.");
        var manager = selected.Services.GetRequiredService<UserManager<LocalIdentityUser>>();
        await using var policy = fixture.Provider.CreateAsyncScope();
        var installed = policy.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        manager.PasswordHasher = installed.PasswordHasher;
        manager.KeyNormalizer = installed.KeyNormalizer;
        manager.PasswordValidators.Clear();
        foreach (var validator in installed.PasswordValidators) manager.PasswordValidators.Add(validator);
        manager.UserValidators.Clear();
        foreach (var validator in installed.UserValidators) manager.UserValidators.Add(validator);
        var store = new LocalIdentityCredentialStateStore(identity, primary, manager, fixture.Clock,
            NativeIdentityRecognitionBoundary.For(primary, fixture.Clock), lease);
        await action(store, primary, identity);
    }

    private static string Digest() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    private static string Password() => $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
    private sealed record Prepared(string Username, string? Hash, string? SecurityStamp, string? UserStamp,
        Guid OperationStamp, long Generation);
}
