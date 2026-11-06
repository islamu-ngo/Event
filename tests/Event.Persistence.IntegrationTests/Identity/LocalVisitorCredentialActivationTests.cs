using System.Security.Cryptography;
using System.Text.Json;
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
public sealed class LocalVisitorCredentialActivationTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    [Arguments(IdentityDatabaseTopology.External, true)]
    public async Task OnlyDurableExactAdmissionCanReadyTheOriginalCredentialAndReplay(
        IdentityDatabaseTopology topology, bool recoverAfterReservationExpiry)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        Guid tenant = Guid.CreateVersion7();
        VisitorEnrollmentOperation reservation = await ReserveAsync(fixture, tenant);
        var prepared = await PrepareAsync(fixture, topology, reservation);
        await WithStoreAsync(fixture, topology, tenant, async (store, identity) =>
        {
            await Assert.That(await store.ActivateVisitorAsync(reservation.Id,
                prepared.Snapshot.OperationConcurrencyStamp, prepared.Generation, fixture.CancellationToken))
                .IsEqualTo(LocalCredentialActivationOutcome.Conflict);
            await AssertPendingAsync(identity, reservation, prepared, fixture.CancellationToken);
        });

        await AdmitAsync(fixture, reservation, prepared.Generation, exactBinding: true);
        if (recoverAfterReservationExpiry)
            fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        await WithStoreAsync(fixture, topology, tenant, async (store, identity) =>
        {
            await Assert.That(await store.ActivateVisitorAsync(reservation.Id, Guid.CreateVersion7(),
                prepared.Generation, fixture.CancellationToken)).IsEqualTo(LocalCredentialActivationOutcome.Conflict);
            await Assert.That(await store.ActivateVisitorAsync(reservation.Id,
                prepared.Snapshot.OperationConcurrencyStamp, prepared.Generation + 1, fixture.CancellationToken))
                .IsEqualTo(LocalCredentialActivationOutcome.Conflict);
            await AssertPendingAsync(identity, reservation, prepared, fixture.CancellationToken);
            await Assert.That(await store.ActivateVisitorAsync(reservation.Id,
                prepared.Snapshot.OperationConcurrencyStamp, prepared.Generation, fixture.CancellationToken))
                .IsEqualTo(LocalCredentialActivationOutcome.Activated);
            await AssertReadyAsync(identity, reservation, prepared, fixture.CancellationToken);
            await Assert.That(await store.ActivateVisitorAsync(reservation.Id,
                prepared.Snapshot.OperationConcurrencyStamp, prepared.Generation, fixture.CancellationToken))
                .IsEqualTo(LocalCredentialActivationOutcome.AlreadyActivated);
            await Assert.That(await store.ActivateVisitorAsync(reservation.Id, Guid.CreateVersion7(),
                prepared.Generation, fixture.CancellationToken)).IsEqualTo(LocalCredentialActivationOutcome.Conflict);
            await AssertReadyAsync(identity, reservation, prepared, fixture.CancellationToken);
        });

        // The primary workflow records its own acknowledgement after native activation.
        await using (var scope = fixture.Provider.CreateAsyncScope())
        await using (var seed = SeedContext(scope.ServiceProvider, tenant))
        {
            var admission = await seed.Set<VisitorEnrollmentOperation>().SingleAsync(
                row => row.Id == reservation.Id, fixture.CancellationToken);
            admission.Activate(admission.Generation, admission.CredentialOperationId, fixture.Clock.GetUtcNow().UtcDateTime);
            await seed.SaveChangesAsync(fixture.CancellationToken);
        }
        await WithStoreAsync(fixture, topology, tenant, async (store, identity) =>
        {
            await Assert.That(await store.ActivateVisitorAsync(reservation.Id,
                prepared.Snapshot.OperationConcurrencyStamp, prepared.Generation, fixture.CancellationToken))
                .IsEqualTo(LocalCredentialActivationOutcome.AlreadyActivated);
            await AssertReadyAsync(identity, reservation, prepared, fixture.CancellationToken);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task DurableReceiptWithoutItsExactActiveTenantMembershipCannotActivate(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        Guid tenant = Guid.CreateVersion7();
        var reservation = await ReserveAsync(fixture, tenant);
        var prepared = await PrepareAsync(fixture, topology, reservation);
        await AdmitAsync(fixture, reservation, prepared.Generation, exactBinding: false);
        await WithStoreAsync(fixture, topology, tenant, async (store, identity) =>
        {
            await Assert.That(await store.ActivateVisitorAsync(reservation.Id,
                prepared.Snapshot.OperationConcurrencyStamp, prepared.Generation, fixture.CancellationToken))
                .IsEqualTo(LocalCredentialActivationOutcome.BindingIncomplete);
            await AssertPendingAsync(identity, reservation, prepared, fixture.CancellationToken);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task AnotherSelectedTenantCannotConsumeThePrimaryAdmissionReceipt(IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var reservation = await ReserveAsync(fixture, Guid.CreateVersion7());
        var prepared = await PrepareAsync(fixture, topology, reservation);
        await AdmitAsync(fixture, reservation, prepared.Generation, exactBinding: true);
        var other = await ReserveAsync(fixture, Guid.CreateVersion7());
        await WithStoreAsync(fixture, topology, other.TenantId, async (store, identity) =>
        {
            await Assert.That(await store.ActivateVisitorAsync(reservation.Id,
                prepared.Snapshot.OperationConcurrencyStamp, prepared.Generation, fixture.CancellationToken))
                .IsEqualTo(LocalCredentialActivationOutcome.NotFound);
            await AssertPendingAsync(identity, reservation, prepared, fixture.CancellationToken);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ChangedNativePendingGenerationCannotBeAdoptedByTheOriginalAdmission(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var reservation = await ReserveAsync(fixture, Guid.CreateVersion7());
        var prepared = await PrepareAsync(fixture, topology, reservation);
        await AdmitAsync(fixture, reservation, prepared.Generation, exactBinding: true);
        await WithStoreAsync(fixture, topology, reservation.TenantId, async (store, identity) =>
        {
            var changed = await identity.Set<LocalIdentityEligibility>()
                .SingleAsync(row => row.LocalSubjectId == reservation.LocalSubjectId, fixture.CancellationToken);
            changed.SetLockout(changed.Generation, changed.CurrentOperationId, null,
                fixture.Clock.GetUtcNow().UtcDateTime);
            await identity.SaveChangesAsync(fixture.CancellationToken);
            identity.Entry(changed).State = EntityState.Detached;

            await Assert.That(await store.ActivateVisitorAsync(reservation.Id,
                prepared.Snapshot.OperationConcurrencyStamp, prepared.Generation, fixture.CancellationToken))
                .IsEqualTo(LocalCredentialActivationOutcome.Conflict);
            var after = await identity.Set<LocalIdentityEligibility>().AsNoTracking()
                .SingleAsync(row => row.LocalSubjectId == reservation.LocalSubjectId, fixture.CancellationToken);
            await Assert.That(after.State).IsEqualTo(LocalIdentityEligibilityState.ProvisioningPending);
            await Assert.That(after.Generation).IsEqualTo(prepared.Generation + 1);
            var operation = await identity.Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.CredentialOperationId, fixture.CancellationToken);
            await Assert.That(operation.Stage).IsEqualTo(LocalCredentialOperationStage.ProvisioningPending);
            await AssertCredentialAsync(identity, reservation, prepared,
                LocalCredentialState.RegistrationPending, fixture.CancellationToken);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ReadyLockoutPublicationCannotPreventPrimaryAcknowledgementRecovery(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        var reservation = await ReserveAsync(fixture, Guid.CreateVersion7());
        var prepared = await PrepareAsync(fixture, topology, reservation);
        await AdmitAsync(fixture, reservation, prepared.Generation, exactBinding: true);
        await WithStoreAsync(fixture, topology, reservation.TenantId, async (store, identity) =>
        {
            await Assert.That(await store.ActivateVisitorAsync(reservation.Id,
                prepared.Snapshot.OperationConcurrencyStamp, prepared.Generation, fixture.CancellationToken))
                .IsEqualTo(LocalCredentialActivationOutcome.Activated);
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            var eligibility = await identity.Set<LocalIdentityEligibility>()
                .SingleAsync(row => row.LocalSubjectId == reservation.LocalSubjectId, fixture.CancellationToken);
            eligibility.SetLockout(eligibility.Generation, eligibility.CurrentOperationId, null,
                fixture.Clock.GetUtcNow().UtcDateTime);
            await identity.SaveChangesAsync(fixture.CancellationToken);
            identity.Entry(eligibility).State = EntityState.Detached;

            await Assert.That(await store.ActivateVisitorAsync(reservation.Id,
                prepared.Snapshot.OperationConcurrencyStamp, prepared.Generation, fixture.CancellationToken))
                .IsEqualTo(LocalCredentialActivationOutcome.AlreadyActivated);
            var after = await identity.Set<LocalIdentityEligibility>().AsNoTracking()
                .SingleAsync(row => row.LocalSubjectId == reservation.LocalSubjectId, fixture.CancellationToken);
            await Assert.That(after.Generation).IsEqualTo(prepared.Generation + 2);
            await Assert.That(after.State).IsEqualTo(LocalIdentityEligibilityState.Ready);
            await AssertCredentialAsync(identity, reservation, prepared, LocalCredentialState.Ready, fixture.CancellationToken);
        });
    }

    private static async Task<Prepared> PrepareAsync(
        Fixture fixture, IdentityDatabaseTopology topology, VisitorEnrollmentOperation reservation)
    {
        Prepared? prepared = null;
        await WithStoreAsync(fixture, topology, reservation.TenantId, async (store, identity) =>
        {
            string username = $"visitor-{Guid.CreateVersion7():N}";
            string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
            var snapshot = await store.PrepareVisitorAsync(reservation.Id, username, password,
                $"visitor-{Guid.CreateVersion7():N}@example.test", fixture.CancellationToken)
                ?? throw new InvalidOperationException("Native visitor preparation was rejected.");
            var user = await identity.Set<LocalIdentityUser>().AsNoTracking()
                .SingleAsync(row => row.Id == reservation.LocalSubjectId, fixture.CancellationToken);
            var eligibility = await identity.Set<LocalIdentityEligibility>().AsNoTracking()
                .SingleAsync(row => row.LocalSubjectId == user.Id, fixture.CancellationToken);
            prepared = new(snapshot, eligibility.Generation, user.PasswordHash, user.SecurityStamp,
                user.ConcurrencyStamp, user.EmailConfirmed);
        });
        return prepared ?? throw new InvalidOperationException("The selected preparation did not complete.");
    }

    private static async Task WithStoreAsync(Fixture fixture, IdentityDatabaseTopology topology, Guid tenant,
        Func<LocalIdentityCredentialStateStore, DbContext, Task> action)
    {
        await using var provider = await LocalIdentityExecutionScopeFactoryTests.ComposeAsync(
            fixture, topology, tenantContext: new SelectedTenant(tenant));
        await using var request = provider.CreateAsyncScope();
        await using var selected = await request.ServiceProvider.GetRequiredService<ILocalIdentityExecutionScopeFactory>()
            .CreateAsync(fixture.CancellationToken);
        var lease = selected.Services.GetRequiredService<LocalIdentityExecutionLease>();
        var application = lease.Application ?? throw new InvalidOperationException("The selected application owner is absent.");
        var identity = lease.Identity ?? throw new InvalidOperationException("The selected identity owner is absent.");
        var manager = selected.Services.GetRequiredService<UserManager<LocalIdentityUser>>();
        await using var policy = fixture.Provider.CreateAsyncScope();
        var installed = policy.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        manager.PasswordHasher = installed.PasswordHasher;
        manager.PasswordValidators.Clear();
        foreach (var validator in installed.PasswordValidators) manager.PasswordValidators.Add(validator);
        manager.UserValidators.Clear();
        foreach (var validator in installed.UserValidators) manager.UserValidators.Add(validator);
        var store = new LocalIdentityCredentialStateStore(identity, application, manager, fixture.Clock,
            NativeIdentityRecognitionBoundary.For(application, fixture.Clock), lease);
        await action(store, identity);
    }

    private static ExploreDbContext SeedContext(IServiceProvider services, Guid tenant)
    {
        var options = services.GetRequiredService<DbContextOptions<ExploreDbContext>>();
        var context = new ExploreDbContext(options) { TenantContext = new SelectedTenant(tenant) };
        context.BindTenantLease();
        return context;
    }

    private static async Task<VisitorEnrollmentOperation> ReserveAsync(Fixture fixture, Guid tenant)
    {
        await using var scope = fixture.Provider.CreateAsyncScope();
        await using var seed = SeedContext(scope.ServiceProvider, tenant);
        DateTime at = fixture.Clock.GetUtcNow().UtcDateTime;
        seed.Tenants.Add(new Tenant
        {
            Id = tenant, FullName = "Visitor activation tenant", Slug = $"visitor-{tenant:N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!, CreatedAt = at
        });
        var row = VisitorEnrollmentOperation.Reserve(Guid.CreateVersion7(), tenant, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(), at);
        seed.Set<VisitorEnrollmentOperation>().Add(row);
        await seed.SaveChangesAsync(fixture.CancellationToken);
        return row;
    }

    private static async Task AdmitAsync(
        Fixture fixture, VisitorEnrollmentOperation reservation, long generation, bool exactBinding)
    {
        await using var scope = fixture.Provider.CreateAsyncScope();
        await using var seed = SeedContext(scope.ServiceProvider, reservation.TenantId);
        DateTime at = fixture.Clock.GetUtcNow().UtcDateTime;
        var row = await seed.Set<VisitorEnrollmentOperation>().SingleAsync(
            value => value.Id == reservation.Id, fixture.CancellationToken);
        row.RecordIdentityPrepared(row.Generation, row.CredentialOperationId, generation, at);
        var user = new User
        {
            Id = row.LocalSubjectId, EmailVerified = false, CreatedAt = at,
            Pii = new UserPii { Email = string.Empty, FirstName = "Visitor", LastName = "Owner" }
        };
        var actor = new Actor
        {
            Id = row.PersonalActorId, UserId = user.Id, User = user,
            ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!, CreatedAt = at,
            Pii = new ActorPii { DisplayName = "Visitor Owner" }
        };
        seed.Actors.Add(actor);
        seed.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = row.ExternalLoginId, UserId = user.Id, User = user,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Local, AuthenticationProvider = null!,
            ProviderKey = user.Id.ToString("D"), CreatedAt = at
        });
        Guid membership = Guid.CreateVersion7();
        seed.Set<TenantUser>().Add(new TenantUser
        {
            Id = membership, TenantId = row.TenantId, Tenant = null!, UserId = user.Id, User = user,
            ActorId = exactBinding ? actor.Id : null, StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = at, CreatedAt = at
        });
        row.RecordPrimaryAdmission(row.Generation, row.CredentialOperationId, membership, at);
        await seed.SaveChangesAsync(fixture.CancellationToken);
    }

    private static async Task AssertPendingAsync(DbContext identity, VisitorEnrollmentOperation reservation,
        Prepared prepared, CancellationToken token)
    {
        var operation = await identity.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .SingleAsync(row => row.Id == reservation.CredentialOperationId, token);
        await Assert.That(operation.Stage).IsEqualTo(LocalCredentialOperationStage.ProvisioningPending);
        await Assert.That(operation.ConcurrencyStamp).IsEqualTo(prepared.Snapshot.OperationConcurrencyStamp);
        var eligibility = await identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(row => row.LocalSubjectId == reservation.LocalSubjectId, token);
        await Assert.That(eligibility.State).IsEqualTo(LocalIdentityEligibilityState.ProvisioningPending);
        await Assert.That(eligibility.Generation).IsEqualTo(prepared.Generation);
        await AssertCredentialAsync(identity, reservation, prepared, LocalCredentialState.RegistrationPending, token);
    }

    private static async Task AssertReadyAsync(DbContext identity, VisitorEnrollmentOperation reservation,
        Prepared prepared, CancellationToken token)
    {
        var operation = await identity.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .SingleAsync(row => row.Id == reservation.CredentialOperationId, token);
        await Assert.That(operation.Stage).IsEqualTo(LocalCredentialOperationStage.Replaced);
        await Assert.That(operation.ConcurrencyStamp).IsEqualTo(prepared.Snapshot.OperationConcurrencyStamp);
        await Assert.That(operation.Kind).IsEqualTo(LocalCredentialOperationKind.VisitorCreate);
        await Assert.That(operation.VisitorEnrollmentOperationId).IsEqualTo(reservation.Id);
        await Assert.That(operation.InitiatingApplicationUserId).IsNull();
        await Assert.That(operation.VerifiedByApplicationUserId).IsNull();
        await Assert.That(operation.VerifiedAt).IsNull();
        var eligibility = await identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(row => row.LocalSubjectId == reservation.LocalSubjectId, token);
        await Assert.That(eligibility.State).IsEqualTo(LocalIdentityEligibilityState.Ready);
        await Assert.That(eligibility.CurrentOperationId).IsEqualTo(reservation.CredentialOperationId);
        await Assert.That(eligibility.Generation).IsEqualTo(prepared.Generation + 1);
        await Assert.That(eligibility.UpdatedAt).IsEqualTo(operation.UpdatedAt);
        await AssertCredentialAsync(identity, reservation, prepared, LocalCredentialState.Ready, token);
    }

    private static async Task AssertCredentialAsync(DbContext identity, VisitorEnrollmentOperation reservation,
        Prepared prepared, LocalCredentialState expectedState, CancellationToken token)
    {
        var user = await identity.Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == reservation.LocalSubjectId, token);
        await Assert.That(user.PasswordHash == prepared.Hash).IsTrue();
        await Assert.That(user.SecurityStamp == prepared.SecurityStamp).IsTrue();
        await Assert.That(user.ConcurrencyStamp == prepared.UserStamp).IsTrue();
        await Assert.That(user.EmailConfirmed).IsEqualTo(prepared.EmailConfirmed);
        var metadataToken = await identity.Set<IdentityUserToken<Guid>>().AsNoTracking()
            .SingleAsync(row => row.UserId == user.Id
                && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                && row.Name == LocalCredentialStateMetadata.TokenName, token);
        var metadata = JsonSerializer.Deserialize<LocalCredentialStateMetadata>(metadataToken.Value!)
            ?? throw new InvalidOperationException("Credential metadata is absent.");
        await Assert.That(metadata.State).IsEqualTo(expectedState);
        await Assert.That(metadata.OperationId).IsEqualTo(reservation.CredentialOperationId);
        await Assert.That(metadata.ApplicationUserId).IsEqualTo(reservation.LocalSubjectId);
    }

    private sealed record Prepared(LocalCredentialProvisioningSnapshot Snapshot, long Generation,
        string? Hash, string? SecurityStamp, string? UserStamp, bool EmailConfirmed);
    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
