using System.Data;
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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Fixture = Event.Persistence.IntegrationTests.Identity.LocalCredentialFirstUseTests.Fixture;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
public sealed class LocalVisitorPrimaryAdmissionTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task FinalApplicationDenialLeavesPreparedCredentialWithoutPrimaryProjection(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var reservation = await ReserveAsync(fixture);
        var prepared = await PrepareAsync(fixture, topology, reservation);
        var denial = new InvalidOperationException("Test-owned Application admission denial.");
        bool observed = false;
        await WithStoreAsync(fixture, topology, reservation.TenantId, async (store, primary, identity) =>
        {
            try
            {
                await store.AdmitVisitorAsync(reservation.Id, token =>
                {
                    token.ThrowIfCancellationRequested();
                    return Task.FromException(denial);
                }, fixture.CancellationToken);
            }
            catch (InvalidOperationException failure) when (ReferenceEquals(failure, denial))
            {
                observed = true;
            }
            await AssertNoProjectionAsync(primary, reservation, fixture.CancellationToken);
            await AssertNativeUnchangedAsync(identity, reservation, prepared, fixture.CancellationToken);
        });
        await Assert.That(observed).IsTrue();
        // Reopen the selected owner to distinguish durable rollback from tracker state.
        await WithStoreAsync(fixture, topology, reservation.TenantId, async (_, primary, identity) =>
        {
            await AssertNoProjectionAsync(primary, reservation, fixture.CancellationToken);
            await AssertNativeUnchangedAsync(identity, reservation, prepared, fixture.CancellationToken);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task AdmissionProjectsOrdinaryAccountAndMembershipWithTheExactPrimaryReceipt(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var reservation = await ReserveAsync(fixture);
        var prepared = await PrepareAsync(fixture, topology, reservation);
        await WithStoreAsync(fixture, topology, reservation.TenantId, async (store, primary, identity) =>
        {
            bool authorized = false;
            var result = await store.AdmitVisitorAsync(reservation.Id, async token =>
            {
                await Assert.That(primary.Database.CurrentTransaction).IsNotNull();
                await Assert.That(primary.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel)
                    .IsEqualTo(IsolationLevel.Serializable);
                // The callback must run before any account, login or membership projection.
                await AssertNoProjectionAsync(primary, reservation, token);
                authorized = true;
            }, fixture.CancellationToken) ?? throw new InvalidOperationException("Admission receipt is absent.");
            await Assert.That(authorized).IsTrue();
            await Assert.That(result.Stage).IsEqualTo(VisitorEnrollmentStage.PrimaryAdmitted);
            await Assert.That(result.CredentialGeneration).IsEqualTo(prepared.Generation);
            await Assert.That(result.TenantUserId).IsNotNull();
            await AssertNativeUnchangedAsync(identity, reservation, prepared, fixture.CancellationToken);
        });
        await WithStoreAsync(fixture, topology, reservation.TenantId, async (_, primary, identity) =>
        {
            var receipt = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.Id, fixture.CancellationToken);
            var user = await primary.Users.AsNoTracking().SingleAsync(
                row => row.Id == reservation.LocalSubjectId, fixture.CancellationToken);
            await Assert.That(user.Pii.Email).IsEqualTo(string.Empty);
            await Assert.That(user.EmailVerified).IsFalse();
            await Assert.That(user.CreatedBy).IsNull();
            var actor = await primary.Actors.AsNoTracking().SingleAsync(
                row => row.Id == reservation.PersonalActorId, fixture.CancellationToken);
            await Assert.That(actor.UserId).IsEqualTo(user.Id);
            await Assert.That(actor.ActorTypeId).IsEqualTo((int)ActorTypeEnum.User);
            await Assert.That(actor.IsSuspended).IsFalse();
            var login = await primary.UserExternalLogins.AsNoTracking().SingleAsync(
                row => row.Id == reservation.ExternalLoginId, fixture.CancellationToken);
            await Assert.That(login.UserId).IsEqualTo(user.Id);
            await Assert.That(login.AuthenticationProviderId).IsEqualTo((int)AuthenticationProviderKind.Local);
            await Assert.That(login.ProviderKey).IsEqualTo(user.Id.ToString("D"));
            var member = await primary.Set<TenantUser>().AsNoTracking().Include(row => row.RoleGrants)
                .SingleAsync(row => row.Id == receipt.TenantUserId, fixture.CancellationToken);
            await Assert.That(member.TenantId).IsEqualTo(reservation.TenantId);
            await Assert.That(member.UserId).IsEqualTo(user.Id);
            await Assert.That(member.ActorId).IsEqualTo(actor.Id);
            await Assert.That(member.StatusId).IsEqualTo((int)TenantUserStatusEnum.Active);
            await Assert.That(member.IsDeleted).IsFalse();
            await Assert.That(member.RoleGrants.Count).IsEqualTo(0);
            await Assert.That(receipt.Stage).IsEqualTo(VisitorEnrollmentStage.PrimaryAdmitted);
            await AssertNativeUnchangedAsync(identity, reservation, prepared, fixture.CancellationToken);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    [Arguments(IdentityDatabaseTopology.External, true)]
    public async Task ExactCommittedMembershipReplaysAfterClosureAndExpiryWithoutNewAuthorization(
        IdentityDatabaseTopology topology, bool activate)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var reservation = await ReserveAsync(fixture);
        var prepared = await PrepareAsync(fixture, topology, reservation);
        Guid? membership = null;
        long generation = 0;
        await WithStoreAsync(fixture, topology, reservation.TenantId, async (store, _, _) =>
        {
            var admission = await store.AdmitVisitorAsync(reservation.Id, _ => Task.CompletedTask,
                fixture.CancellationToken) ?? throw new InvalidOperationException("Admission is absent.");
            membership = admission.TenantUserId;
            generation = admission.Generation;
            if (activate)
                await Assert.That(await store.ActivateVisitorAsync(reservation.Id, prepared.OperationStamp,
                    prepared.Generation, fixture.CancellationToken)).IsEqualTo(LocalCredentialActivationOutcome.Activated);
        });
        if (activate)
        {
            await using var scope = fixture.Provider.CreateAsyncScope();
            await using var seed = SeedContext(scope.ServiceProvider, reservation.TenantId);
            var receipt = await seed.Set<VisitorEnrollmentOperation>()
                .SingleAsync(row => row.Id == reservation.Id, fixture.CancellationToken);
            receipt.Activate(receipt.Generation, receipt.CredentialOperationId, fixture.Clock.GetUtcNow().UtcDateTime);
            generation = receipt.Generation;
            await seed.SaveChangesAsync(fixture.CancellationToken);
        }
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        await WithStoreAsync(fixture, topology, reservation.TenantId, async (store, primary, _) =>
        {
            var replay = await store.AdmitVisitorAsync(reservation.Id,
                _ => throw new InvalidOperationException("Later closure must not authorize an existing member again."),
                fixture.CancellationToken) ?? throw new InvalidOperationException("Replay receipt is absent.");
            await Assert.That(replay.TenantUserId).IsEqualTo(membership);
            await Assert.That(replay.Generation).IsEqualTo(generation);
            await Assert.That(replay.Stage).IsEqualTo(
                activate ? VisitorEnrollmentStage.Active : VisitorEnrollmentStage.PrimaryAdmitted);
            await Assert.That(await primary.Set<TenantUser>().AsNoTracking().CountAsync(
                row => row.UserId == reservation.LocalSubjectId, fixture.CancellationToken)).IsEqualTo(1);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    [Arguments(IdentityDatabaseTopology.External, true)]
    public async Task ChangedPendingGenerationOrBindingCannotProjectOrRewriteTheOriginalCredential(
        IdentityDatabaseTopology topology, bool changeBinding)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var reservation = await ReserveAsync(fixture);
        var prepared = await PrepareAsync(fixture, topology, reservation);
        await WithStoreAsync(fixture, topology, reservation.TenantId, async (store, primary, identity) =>
        {
            if (changeBinding)
                await identity.Set<LocalIdentityCredentialOperation>()
                    .Where(row => row.Id == reservation.CredentialOperationId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.PersonalActorId,
                        Guid.CreateVersion7()), fixture.CancellationToken);
            else
            {
                var changed = await identity.Set<LocalIdentityEligibility>().SingleAsync(
                    row => row.LocalSubjectId == reservation.LocalSubjectId, fixture.CancellationToken);
                changed.SetLockout(changed.Generation, changed.CurrentOperationId, null,
                    fixture.Clock.GetUtcNow().UtcDateTime);
                await identity.SaveChangesAsync(fixture.CancellationToken);
                identity.Entry(changed).State = EntityState.Detached;
            }
            bool denied = false;
            try
            {
                await store.AdmitVisitorAsync(reservation.Id,
                    _ => throw new InvalidOperationException("Changed evidence must fail before policy."),
                    fixture.CancellationToken);
            }
            catch (UnauthorizedAccessException) { denied = true; }
            await Assert.That(denied).IsTrue();
            await AssertNoProjectionAsync(primary, reservation, fixture.CancellationToken);
            var user = await identity.Set<LocalIdentityUser>().AsNoTracking().SingleAsync(
                row => row.Id == reservation.LocalSubjectId, fixture.CancellationToken);
            await Assert.That(user.PasswordHash == prepared.Hash).IsTrue();
            await Assert.That(user.SecurityStamp == prepared.SecurityStamp).IsTrue();
            await Assert.That(user.ConcurrencyStamp == prepared.UserStamp).IsTrue();
            var operation = await identity.Set<LocalIdentityCredentialOperation>().AsNoTracking().SingleAsync(
                row => row.Id == reservation.CredentialOperationId, fixture.CancellationToken);
            await Assert.That(operation.Stage).IsEqualTo(LocalCredentialOperationStage.ProvisioningPending);
            await Assert.That(operation.PersonalActorId == reservation.PersonalActorId).IsEqualTo(!changeBinding);
            var eligibility = await identity.Set<LocalIdentityEligibility>().AsNoTracking().SingleAsync(
                row => row.LocalSubjectId == reservation.LocalSubjectId, fixture.CancellationToken);
            await Assert.That(eligibility.Generation).IsEqualTo(prepared.Generation + (changeBinding ? 0 : 1));
        });
    }

    private static async Task<Prepared> PrepareAsync(
        Fixture fixture, IdentityDatabaseTopology topology, VisitorEnrollmentOperation reservation)
    {
        Prepared? prepared = null;
        await WithStoreAsync(fixture, topology, reservation.TenantId, async (store, primary, identity) =>
        {
            string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
            var native = await store.PrepareVisitorAsync(reservation.Id, $"visitor-{Guid.CreateVersion7():N}",
                password, null, fixture.CancellationToken)
                ?? throw new InvalidOperationException("Native preparation is absent.");
            var user = await identity.Set<LocalIdentityUser>().AsNoTracking().SingleAsync(
                row => row.Id == reservation.LocalSubjectId, fixture.CancellationToken);
            var eligibility = await identity.Set<LocalIdentityEligibility>().AsNoTracking().SingleAsync(
                row => row.LocalSubjectId == user.Id, fixture.CancellationToken);
            prepared = new(native.OperationConcurrencyStamp, eligibility.Generation,
                user.PasswordHash, user.SecurityStamp, user.ConcurrencyStamp);
            var receipt = await store.RecordVisitorPreparedAsync(reservation.Id, fixture.CancellationToken)
                ?? throw new InvalidOperationException("Primary preparation receipt is absent.");
            await Assert.That(receipt.Stage).IsEqualTo(VisitorEnrollmentStage.IdentityPrepared);
            await Assert.That(receipt.CredentialGeneration).IsEqualTo(eligibility.Generation);
            var replay = await store.RecordVisitorPreparedAsync(reservation.Id, fixture.CancellationToken)
                ?? throw new InvalidOperationException("Preparation replay is absent.");
            await Assert.That(replay.Generation).IsEqualTo(receipt.Generation);
            await AssertNoProjectionAsync(primary, reservation, fixture.CancellationToken);
        });
        return prepared ?? throw new InvalidOperationException("Selected native preparation did not complete.");
    }

    private static async Task AssertNoProjectionAsync(
        ExploreDbContext primary, VisitorEnrollmentOperation reservation, CancellationToken token)
    {
        var receipt = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
            .SingleAsync(row => row.Id == reservation.Id, token);
        await Assert.That(receipt.Stage).IsEqualTo(VisitorEnrollmentStage.IdentityPrepared);
        await Assert.That(receipt.TenantUserId).IsNull();
        await Assert.That(await primary.Users.AsNoTracking().AnyAsync(
            row => row.Id == reservation.LocalSubjectId, token)).IsFalse();
        await Assert.That(await primary.Actors.AsNoTracking().AnyAsync(
            row => row.Id == reservation.PersonalActorId || row.UserId == reservation.LocalSubjectId, token)).IsFalse();
        await Assert.That(await primary.UserExternalLogins.AsNoTracking().AnyAsync(
            row => row.Id == reservation.ExternalLoginId || row.UserId == reservation.LocalSubjectId, token)).IsFalse();
        await Assert.That(await primary.Set<TenantUser>().AsNoTracking().AnyAsync(
            row => row.UserId == reservation.LocalSubjectId, token)).IsFalse();
    }

    private static async Task AssertNativeUnchangedAsync(
        DbContext identity, VisitorEnrollmentOperation reservation, Prepared prepared, CancellationToken token)
    {
        var user = await identity.Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == reservation.LocalSubjectId, token);
        await Assert.That(user.PasswordHash == prepared.Hash).IsTrue();
        await Assert.That(user.SecurityStamp == prepared.SecurityStamp).IsTrue();
        await Assert.That(user.ConcurrencyStamp == prepared.UserStamp).IsTrue();
        await Assert.That(user.EmailConfirmed).IsFalse();
        var operation = await identity.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .SingleAsync(row => row.Id == reservation.CredentialOperationId, token);
        await Assert.That(operation.ConcurrencyStamp).IsEqualTo(prepared.OperationStamp);
        await Assert.That(operation.Stage).IsEqualTo(LocalCredentialOperationStage.ProvisioningPending);
        var eligibility = await identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(row => row.LocalSubjectId == reservation.LocalSubjectId, token);
        await Assert.That(eligibility.Generation).IsEqualTo(prepared.Generation);
        await Assert.That(eligibility.State).IsEqualTo(LocalIdentityEligibilityState.ProvisioningPending);
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
        manager.PasswordValidators.Clear();
        foreach (var validator in installed.PasswordValidators) manager.PasswordValidators.Add(validator);
        manager.UserValidators.Clear();
        foreach (var validator in installed.UserValidators) manager.UserValidators.Add(validator);
        var store = new LocalIdentityCredentialStateStore(identity, primary, manager, fixture.Clock,
            NativeIdentityRecognitionBoundary.For(primary, fixture.Clock), lease);
        await action(store, primary, identity);
    }

    private static ExploreDbContext SeedContext(IServiceProvider services, Guid tenant)
    {
        var context = new ExploreDbContext(services.GetRequiredService<DbContextOptions<ExploreDbContext>>())
        {
            TenantContext = new SelectedTenant(tenant)
        };
        context.BindTenantLease();
        return context;
    }

    private static async Task<VisitorEnrollmentOperation> ReserveAsync(Fixture fixture)
    {
        await using var scope = fixture.Provider.CreateAsyncScope();
        Guid tenant = Guid.CreateVersion7();
        await using var seed = SeedContext(scope.ServiceProvider, tenant);
        DateTime now = fixture.Clock.GetUtcNow().UtcDateTime;
        seed.Tenants.Add(new Tenant
        {
            Id = tenant,
            FullName = "Visitor primary admission tenant",
            Slug = $"visitor-{tenant:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = now
        });
        var reservation = VisitorEnrollmentOperation.Reserve(Guid.CreateVersion7(), tenant, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(), now);
        seed.Set<VisitorEnrollmentOperation>().Add(reservation);
        await seed.SaveChangesAsync(fixture.CancellationToken);
        return reservation;
    }

    private sealed record Prepared(Guid OperationStamp, long Generation, string? Hash, string? SecurityStamp, string? UserStamp);
    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
