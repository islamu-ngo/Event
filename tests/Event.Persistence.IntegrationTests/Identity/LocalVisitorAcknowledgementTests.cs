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
public sealed class LocalVisitorAcknowledgementTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task PendingNativeActivationCannotAdvanceCommittedPrimaryAdmission(IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var prepared = await PrepareAdmissionAsync(fixture, topology);
        ReceiptImage? original = null;
        NativeImage? native = null;
        await WithStoreAsync(fixture, topology, prepared.Receipt.TenantId, async (store, primary, identity) =>
        {
            original = await ReadReceiptAsync(primary, prepared.Receipt.Id, fixture.CancellationToken);
            native = await ReadNativeAsync(identity, prepared.Receipt, fixture.CancellationToken);
            await RequireDeniedAsync(() => store.AcknowledgeVisitorActivationAsync(
                prepared.Receipt.Id, fixture.CancellationToken));
            await Assert.That(await ReadReceiptAsync(primary, prepared.Receipt.Id, fixture.CancellationToken))
                .IsEqualTo(original);
            await Assert.That(await ReadNativeAsync(identity, prepared.Receipt, fixture.CancellationToken))
                .IsEqualTo(native);
        });
        await WithStoreAsync(fixture, topology, prepared.Receipt.TenantId, async (_, primary, identity) =>
        {
            await Assert.That(await ReadReceiptAsync(primary, prepared.Receipt.Id, fixture.CancellationToken))
                .IsEqualTo(original);
            await Assert.That(await ReadNativeAsync(identity, prepared.Receipt, fixture.CancellationToken))
                .IsEqualTo(native);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    [Arguments(IdentityDatabaseTopology.External, true)]
    public async Task ExactNativeActivationAcknowledgesOnceAndReplaysAfterExpiryAndReadyPublication(
        IdentityDatabaseTopology topology, bool publishLockout)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var prepared = await PrepareAdmissionAsync(fixture, topology);
        NativeImage? ready = null;
        await WithStoreAsync(fixture, topology, prepared.Receipt.TenantId, async (store, _, identity) =>
        {
            var pending = await ReadNativeAsync(identity, prepared.Receipt, fixture.CancellationToken);
            await Assert.That(await store.ActivateVisitorAsync(prepared.Receipt.Id, prepared.OperationStamp,
                prepared.CredentialGeneration, fixture.CancellationToken)).IsEqualTo(LocalCredentialActivationOutcome.Activated);
            if (publishLockout)
            {
                fixture.Clock.Advance(TimeSpan.FromSeconds(1));
                var eligibility = await identity.Set<LocalIdentityEligibility>().SingleAsync(
                    row => row.LocalSubjectId == prepared.Receipt.LocalSubjectId, fixture.CancellationToken);
                // A lockout is authentication state, not a reversal of durable native activation.
                eligibility.SetLockout(eligibility.Generation, eligibility.CurrentOperationId,
                    fixture.Clock.GetUtcNow().UtcDateTime.AddHours(1), fixture.Clock.GetUtcNow().UtcDateTime);
                await identity.SaveChangesAsync(fixture.CancellationToken);
                identity.Entry(eligibility).State = EntityState.Detached;
            }
            ready = await ReadNativeAsync(identity, prepared.Receipt, fixture.CancellationToken);
            await Assert.That(ready.Hash == pending.Hash).IsTrue();
            await Assert.That(ready.SecurityStamp == pending.SecurityStamp).IsTrue();
            await Assert.That(ready.UserStamp == pending.UserStamp).IsTrue();
            await Assert.That(ready.EmailConfirmed).IsEqualTo(pending.EmailConfirmed);
            await Assert.That(ready.OperationStamp).IsEqualTo(pending.OperationStamp);
            await Assert.That(ready.Generation).IsEqualTo(prepared.CredentialGeneration + (publishLockout ? 2 : 1));
        });
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        ReceiptImage? acknowledged = null;
        await WithStoreAsync(fixture, topology, prepared.Receipt.TenantId, async (store, primary, identity) =>
        {
            var result = await store.AcknowledgeVisitorActivationAsync(prepared.Receipt.Id, fixture.CancellationToken)
                ?? throw new InvalidOperationException("Acknowledgement is absent.");
            await Assert.That(result.Stage).IsEqualTo(VisitorEnrollmentStage.Active);
            await Assert.That(result.Generation).IsEqualTo(prepared.Receipt.Generation + 1);
            await Assert.That(result.UpdatedAt).IsEqualTo(fixture.Clock.GetUtcNow().UtcDateTime);
            await Assert.That(result.TenantUserId).IsEqualTo(prepared.Receipt.TenantUserId);
            await Assert.That(result.CredentialGeneration).IsEqualTo(prepared.CredentialGeneration);
            await Assert.That(result.CredentialOperationId).IsEqualTo(prepared.Receipt.CredentialOperationId);
            acknowledged = await ReadReceiptAsync(primary, result.Id, fixture.CancellationToken);
            await Assert.That(await ReadNativeAsync(identity, result, fixture.CancellationToken)).IsEqualTo(ready);
            await Assert.That(primary.ChangeTracker.HasChanges()).IsFalse();
        });
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await WithStoreAsync(fixture, topology, prepared.Receipt.TenantId, async (store, primary, identity) =>
        {
            var replay = await store.AcknowledgeVisitorActivationAsync(prepared.Receipt.Id, fixture.CancellationToken)
                ?? throw new InvalidOperationException("Acknowledgement replay is absent.");
            await Assert.That(Image(replay)).IsEqualTo(acknowledged);
            await Assert.That(await ReadReceiptAsync(primary, replay.Id, fixture.CancellationToken)).IsEqualTo(acknowledged);
            await Assert.That(await ReadNativeAsync(identity, replay, fixture.CancellationToken)).IsEqualTo(ready);
            await Assert.That(primary.ChangeTracker.HasChanges()).IsFalse();
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ForeignTenantAndMissingReceiptReturnNoAcknowledgement(IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var prepared = await PrepareAdmissionAsync(fixture, topology);
        var foreign = await PrepareAdmissionAsync(fixture, topology);
        await WithStoreAsync(fixture, topology, foreign.Receipt.TenantId, async (store, _, _) =>
        {
            await Assert.That(await store.AcknowledgeVisitorActivationAsync(prepared.Receipt.Id,
                fixture.CancellationToken)).IsNull();
            await Assert.That(await store.AcknowledgeVisitorActivationAsync(Guid.CreateVersion7(),
                fixture.CancellationToken)).IsNull();
        });
        await WithStoreAsync(fixture, topology, prepared.Receipt.TenantId, async (_, primary, _) =>
        {
            await Assert.That(await ReadReceiptAsync(primary, prepared.Receipt.Id, fixture.CancellationToken))
                .IsEqualTo(Image(prepared.Receipt));
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    [Arguments(IdentityDatabaseTopology.External, true)]
    public async Task ChangedCurrentOperationCannotBeAdoptedEvenOnActiveReplay(
        IdentityDatabaseTopology topology, bool acknowledgeFirst)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var prepared = await PrepareAdmissionAsync(fixture, topology);
        await WithStoreAsync(fixture, topology, prepared.Receipt.TenantId, async (store, primary, identity) =>
        {
            await Assert.That(await store.ActivateVisitorAsync(prepared.Receipt.Id, prepared.OperationStamp,
                prepared.CredentialGeneration, fixture.CancellationToken)).IsEqualTo(LocalCredentialActivationOutcome.Activated);
            if (acknowledgeFirst)
                await store.AcknowledgeVisitorActivationAsync(prepared.Receipt.Id, fixture.CancellationToken);
            var original = await ReadReceiptAsync(primary, prepared.Receipt.Id, fixture.CancellationToken);
            var eligibility = await identity.Set<LocalIdentityEligibility>().SingleAsync(
                row => row.LocalSubjectId == prepared.Receipt.LocalSubjectId, fixture.CancellationToken);
            eligibility.Reset(eligibility.Generation, eligibility.CurrentOperationId, fixture.Receipt.OperationId,
                fixture.Clock.GetUtcNow().UtcDateTime);
            await identity.SaveChangesAsync(fixture.CancellationToken);
            identity.Entry(eligibility).State = EntityState.Detached;
            var changed = await ReadNativeAsync(identity, prepared.Receipt, fixture.CancellationToken);
            await RequireDeniedAsync(() => store.AcknowledgeVisitorActivationAsync(
                prepared.Receipt.Id, fixture.CancellationToken));
            await Assert.That(await ReadReceiptAsync(primary, prepared.Receipt.Id, fixture.CancellationToken))
                .IsEqualTo(original);
            await Assert.That(await ReadNativeAsync(identity, prepared.Receipt, fixture.CancellationToken))
                .IsEqualTo(changed);
        });
    }

    private static async Task RequireDeniedAsync(Func<Task<VisitorEnrollmentOperation?>> action)
    {
        bool denied = false;
        try { await action(); }
        catch (UnauthorizedAccessException) { denied = true; }
        await Assert.That(denied).IsTrue();
    }

    private static async Task<Prepared> PrepareAdmissionAsync(Fixture fixture, IdentityDatabaseTopology topology)
    {
        Guid tenant = Guid.CreateVersion7();
        await using (var scope = fixture.Provider.CreateAsyncScope())
        await using (var seed = new ExploreDbContext(
            scope.ServiceProvider.GetRequiredService<DbContextOptions<ExploreDbContext>>())
        {
            TenantContext = new SelectedTenant(tenant)
        })
        {
            seed.BindTenantLease();
            seed.Tenants.Add(new Tenant
            {
                Id = tenant, FullName = "Visitor acknowledgement tenant", Slug = $"visitor-{tenant:N}",
                TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!,
                CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            await seed.SaveChangesAsync(fixture.CancellationToken);
        }
        Prepared? result = null;
        await WithStoreAsync(fixture, topology, tenant, async (store, _, identity) =>
        {
            // These callbacks isolate native persistence evidence. They do not prove
            // current-configuration Application policy authorization.
            var reservation = await store.ReserveVisitorAsync(Guid.CreateVersion7(),
                Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
                _ => Task.CompletedTask, fixture.CancellationToken);
            var native = await store.PrepareVisitorAsync(reservation.Id, $"visitor-{Guid.CreateVersion7():N}",
                $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}", null, fixture.CancellationToken)
                ?? throw new InvalidOperationException("Native preparation is absent.");
            var prepared = await store.RecordVisitorPreparedAsync(reservation.Id, fixture.CancellationToken)
                ?? throw new InvalidOperationException("Prepared primary receipt is absent.");
            var admission = await store.AdmitVisitorAsync(reservation.Id, _ => Task.CompletedTask,
                fixture.CancellationToken) ?? throw new InvalidOperationException("Primary admission is absent.");
            await Assert.That(admission.Stage).IsEqualTo(VisitorEnrollmentStage.PrimaryAdmitted);
            var eligibility = await identity.Set<LocalIdentityEligibility>().AsNoTracking().SingleAsync(
                row => row.LocalSubjectId == reservation.LocalSubjectId, fixture.CancellationToken);
            await Assert.That(prepared.CredentialGeneration).IsEqualTo(eligibility.Generation);
            result = new(admission, native.OperationConcurrencyStamp, eligibility.Generation);
        });
        return result ?? throw new InvalidOperationException("Selected admission did not complete.");
    }

    private static async Task WithStoreAsync(Fixture fixture, IdentityDatabaseTopology topology, Guid tenant,
        Func<LocalIdentityCredentialStateStore, ExploreDbContext, DbContext, Task> action)
    {
        await using var provider = await LocalIdentityExecutionScopeFactoryTests.ComposeAsync(
            fixture, topology, tenantContext: new SelectedTenant(tenant));
        await using var policy = fixture.Provider.CreateAsyncScope();
        var installed = policy.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        await using var request = provider.CreateAsyncScope();
        await using var selected = await request.ServiceProvider.GetRequiredService<ILocalIdentityExecutionScopeFactory>()
            .CreateAsync(fixture.CancellationToken);
        var lease = selected.Services.GetRequiredService<LocalIdentityExecutionLease>();
        var primary = lease.Application ?? throw new InvalidOperationException("Selected primary owner is absent.");
        var identity = lease.Identity ?? throw new InvalidOperationException("Selected native owner is absent.");
        var manager = selected.Services.GetRequiredService<UserManager<LocalIdentityUser>>();
        manager.PasswordHasher = installed.PasswordHasher;
        manager.PasswordValidators.Clear();
        foreach (var validator in installed.PasswordValidators) manager.PasswordValidators.Add(validator);
        manager.UserValidators.Clear();
        foreach (var validator in installed.UserValidators) manager.UserValidators.Add(validator);
        var store = new LocalIdentityCredentialStateStore(identity, primary, manager, fixture.Clock,
            NativeIdentityRecognitionBoundary.For(primary, fixture.Clock), lease);
        await action(store, primary, identity);
    }

    private static async Task<ReceiptImage> ReadReceiptAsync(ExploreDbContext primary, Guid id, CancellationToken token) =>
        Image(await primary.Set<VisitorEnrollmentOperation>().AsNoTracking().SingleAsync(row => row.Id == id, token));

    private static ReceiptImage Image(VisitorEnrollmentOperation row) => new(row.Id, row.TenantId,
        row.LocalSubjectId, row.PersonalActorId, row.ExternalLoginId, row.CredentialOperationId,
        row.CredentialGeneration, row.TenantUserId, row.IntentDigest, row.Stage, row.Generation,
        row.CreatedAt, row.ExpiresAt, row.UpdatedAt);

    private static async Task<NativeImage> ReadNativeAsync(
        DbContext identity, VisitorEnrollmentOperation receipt, CancellationToken token)
    {
        var user = await identity.Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == receipt.LocalSubjectId, token);
        var operation = await identity.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .SingleAsync(row => row.Id == receipt.CredentialOperationId, token);
        var eligibility = await identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(row => row.LocalSubjectId == receipt.LocalSubjectId, token);
        var metadata = await identity.Set<IdentityUserToken<Guid>>().AsNoTracking().SingleAsync(
            row => row.UserId == receipt.LocalSubjectId
                && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                && row.Name == LocalCredentialStateMetadata.TokenName, token);
        return new(user.PasswordHash, user.SecurityStamp, user.ConcurrencyStamp, user.EmailConfirmed,
            user.LockoutEnd, operation.ConcurrencyStamp, operation.Stage, operation.UpdatedAt,
            eligibility.CurrentOperationId, eligibility.Generation, eligibility.State, eligibility.UpdatedAt,
            eligibility.LockoutUntilUtc, metadata.Value);
    }

    private sealed record Prepared(VisitorEnrollmentOperation Receipt, Guid OperationStamp, long CredentialGeneration);
    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
    private sealed record ReceiptImage(Guid Id, Guid Tenant, Guid Subject, Guid Actor, Guid Login, Guid Operation,
        long? CredentialGeneration, Guid? Member, string Digest, VisitorEnrollmentStage Stage, long Generation,
        DateTime CreatedAt, DateTime ExpiresAt, DateTime? UpdatedAt);
    private sealed record NativeImage(string? Hash, string? SecurityStamp, string? UserStamp, bool EmailConfirmed,
        DateTimeOffset? LockoutEnd, Guid OperationStamp, LocalCredentialOperationStage Stage, DateTime? OperationTime,
        Guid CurrentOperation, long Generation, LocalIdentityEligibilityState State, DateTime? EligibilityTime,
        DateTime? LockoutUntil, string? Metadata);
}
