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
public sealed class LocalVisitorReservationTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ApplicationDenialLeavesNoDurableReservationOrAccount(IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        Guid tenant = await SeedTenantAsync(fixture);
        Guid operation = Guid.CreateVersion7();
        var denial = new InvalidOperationException("Test-owned final Application policy denial.");
        Counts? before = null;
        await WithStoreAsync(fixture, topology, tenant, async (store, primary, identity) =>
        {
            before = await ReadCountsAsync(primary, identity, fixture.CancellationToken);
            bool denied = false;
            try
            {
                await store.ReserveVisitorAsync(operation, NewDigest(), _ => Task.FromException(denial),
                    fixture.CancellationToken);
            }
            catch (InvalidOperationException failure) when (ReferenceEquals(failure, denial)) { denied = true; }
            await Assert.That(denied).IsTrue();
        });
        await WithStoreAsync(fixture, topology, tenant, async (_, primary, identity) =>
        {
            await Assert.That(await ReadCountsAsync(primary, identity, fixture.CancellationToken)).IsEqualTo(before);
            await Assert.That(await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .AnyAsync(row => row.Id == operation, fixture.CancellationToken)).IsFalse();
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ReservationPersistsExactServerIdentifiersDigestAndFifteenMinuteDeadline(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        Guid tenant = await SeedTenantAsync(fixture);
        Guid operation = Guid.CreateVersion7();
        string digest = NewDigest();
        DateTime authorizedAt = fixture.Clock.GetUtcNow().UtcDateTime;
        VisitorEnrollmentOperation? reserved = null;
        await WithStoreAsync(fixture, topology, tenant, async (store, primary, identity) =>
        {
            Counts before = await ReadCountsAsync(primary, identity, fixture.CancellationToken);
            int authorizations = 0;
            reserved = await store.ReserveVisitorAsync(operation, digest, async token =>
            {
                await Assert.That(primary.Database.CurrentTransaction).IsNotNull();
                await Assert.That(primary.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel)
                    .IsEqualTo(IsolationLevel.Serializable);
                await Assert.That(await ReadCountsAsync(primary, identity, token)).IsEqualTo(before);
                authorizations++;
            }, fixture.CancellationToken);
            await Assert.That(authorizations).IsEqualTo(1);
            await Assert.That(primary.Entry(reserved).State).IsEqualTo(EntityState.Detached);
            await Assert.That(reserved.Id).IsEqualTo(operation);
            await Assert.That(reserved.TenantId).IsEqualTo(tenant);
            await Assert.That(reserved.IntentDigest).IsEqualTo(digest);
            await Assert.That(reserved.Stage).IsEqualTo(VisitorEnrollmentStage.Reserved);
            await Assert.That(reserved.Generation).IsEqualTo(1L);
            await Assert.That(reserved.CredentialGeneration).IsNull();
            await Assert.That(reserved.TenantUserId).IsNull();
            await Assert.That(reserved.UpdatedAt).IsNull();
            await Assert.That(reserved.CreatedAt).IsEqualTo(authorizedAt);
            await Assert.That(reserved.ExpiresAt).IsEqualTo(authorizedAt.AddMinutes(15));
            Guid[] ids = [reserved.Id, reserved.LocalSubjectId, reserved.PersonalActorId,
                reserved.ExternalLoginId, reserved.CredentialOperationId];
            await Assert.That(ids.All(id => id.Version == 7)).IsTrue();
            await Assert.That(ids.Distinct().Count()).IsEqualTo(5);
            Counts after = await ReadCountsAsync(primary, identity, fixture.CancellationToken);
            await Assert.That(after).IsEqualTo(before with { Reservations = before.Reservations + 1 });
            await AssertNoAccountAsync(primary, identity, reserved, fixture.CancellationToken);
        });
        await WithStoreAsync(fixture, topology, tenant, async (_, primary, identity) =>
        {
            var persisted = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == operation, fixture.CancellationToken);
            await AssertSameReceiptAsync(reserved!, persisted);
            await AssertNoAccountAsync(primary, identity, persisted, fixture.CancellationToken);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, VisitorEnrollmentStage.Reserved)]
    [Arguments(IdentityDatabaseTopology.External, VisitorEnrollmentStage.Reserved)]
    [Arguments(IdentityDatabaseTopology.Colocated, VisitorEnrollmentStage.IdentityPrepared)]
    [Arguments(IdentityDatabaseTopology.External, VisitorEnrollmentStage.IdentityPrepared)]
    [Arguments(IdentityDatabaseTopology.Colocated, VisitorEnrollmentStage.PrimaryAdmitted)]
    [Arguments(IdentityDatabaseTopology.External, VisitorEnrollmentStage.PrimaryAdmitted)]
    [Arguments(IdentityDatabaseTopology.Colocated, VisitorEnrollmentStage.Active)]
    [Arguments(IdentityDatabaseTopology.External, VisitorEnrollmentStage.Active)]
    public async Task ExactUnexpiredReplayPreservesEveryFieldDespiteLaterApplicationDenial(
        IdentityDatabaseTopology topology, VisitorEnrollmentStage stage)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        Guid tenant = await SeedTenantAsync(fixture);
        var reserved = await ReserveAsync(fixture, topology, tenant);
        // Only receipt replay is under test; native preparation and admission have their own witnesses.
        await using (var scope = fixture.Provider.CreateAsyncScope())
        await using (var seed = SeedContext(scope.ServiceProvider, tenant))
        {
            var receipt = await seed.Set<VisitorEnrollmentOperation>()
                .SingleAsync(row => row.Id == reserved.Id, fixture.CancellationToken);
            DateTime now = fixture.Clock.GetUtcNow().UtcDateTime;
            if (stage != VisitorEnrollmentStage.Reserved)
                receipt.RecordIdentityPrepared(receipt.Generation, receipt.CredentialOperationId, 3, now);
            if (stage is VisitorEnrollmentStage.PrimaryAdmitted or VisitorEnrollmentStage.Active)
                receipt.RecordPrimaryAdmission(receipt.Generation, receipt.CredentialOperationId,
                    Guid.CreateVersion7(), now);
            if (stage == VisitorEnrollmentStage.Active)
                receipt.Activate(receipt.Generation, receipt.CredentialOperationId, now);
            await seed.SaveChangesAsync(fixture.CancellationToken);
        }
        fixture.Clock.Advance(TimeSpan.FromMinutes(14));
        await WithStoreAsync(fixture, topology, tenant, async (store, primary, identity) =>
        {
            var original = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reserved.Id, fixture.CancellationToken);
            Counts before = await ReadCountsAsync(primary, identity, fixture.CancellationToken);
            var replay = await store.ReserveVisitorAsync(reserved.Id, reserved.IntentDigest,
                _ => throw new InvalidOperationException("Existing receipts must bypass new allocation policy."),
                fixture.CancellationToken);
            await AssertSameReceiptAsync(original, replay);
            await Assert.That(await ReadCountsAsync(primary, identity, fixture.CancellationToken)).IsEqualTo(before);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ChangedProofAndForeignTargetCannotAdoptOrOverwriteOriginalReceipt(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        Guid tenant = await SeedTenantAsync(fixture);
        Guid foreignTenant = await SeedTenantAsync(fixture);
        var reserved = await ReserveAsync(fixture, topology, tenant);
        await WithStoreAsync(fixture, topology, tenant, async (store, _, _) =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ReserveVisitorAsync(
                reserved.Id, reserved.IntentDigest == new string('a', 64) ? new string('b', 64) : new string('a', 64),
                _ => throw new InvalidOperationException("Changed proof must fail before policy."),
                fixture.CancellationToken));
        });
        await WithStoreAsync(fixture, topology, foreignTenant, async (store, primary, identity) =>
        {
            Counts before = await ReadCountsAsync(primary, identity, fixture.CancellationToken);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ReserveVisitorAsync(
                reserved.Id, reserved.IntentDigest, _ => Task.CompletedTask, fixture.CancellationToken));
            await Assert.That(await ReadCountsAsync(primary, identity, fixture.CancellationToken)).IsEqualTo(before);
        });
        await WithStoreAsync(fixture, topology, tenant, async (_, primary, identity) =>
        {
            var original = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reserved.Id, fixture.CancellationToken);
            await AssertSameReceiptAsync(reserved, original);
            await AssertNoAccountAsync(primary, identity, original, fixture.CancellationToken);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    [Arguments(IdentityDatabaseTopology.External, true)]
    public async Task ExpiredOrCanceledReceiptCannotBeRenewedOrReallocated(
        IdentityDatabaseTopology topology, bool cancel)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        Guid tenant = await SeedTenantAsync(fixture);
        var reserved = await ReserveAsync(fixture, topology, tenant);
        if (cancel)
        {
            await using var scope = fixture.Provider.CreateAsyncScope();
            await using var seed = SeedContext(scope.ServiceProvider, tenant);
            var receipt = await seed.Set<VisitorEnrollmentOperation>()
                .SingleAsync(row => row.Id == reserved.Id, fixture.CancellationToken);
            receipt.Cancel(receipt.Generation, receipt.CredentialOperationId, fixture.Clock.GetUtcNow().UtcDateTime);
            await seed.SaveChangesAsync(fixture.CancellationToken);
        }
        else fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        await WithStoreAsync(fixture, topology, tenant, async (store, primary, identity) =>
        {
            var original = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reserved.Id, fixture.CancellationToken);
            Counts before = await ReadCountsAsync(primary, identity, fixture.CancellationToken);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.ReserveVisitorAsync(
                reserved.Id, reserved.IntentDigest,
                _ => throw new InvalidOperationException("Terminal receipts must fail before policy."),
                fixture.CancellationToken));
            var after = await primary.Set<VisitorEnrollmentOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == reserved.Id, fixture.CancellationToken);
            await AssertSameReceiptAsync(original, after);
            await Assert.That(await ReadCountsAsync(primary, identity, fixture.CancellationToken)).IsEqualTo(before);
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task NonCanonicalIntentInputsNeverReachApplicationAuthorization(IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        Guid tenant = await SeedTenantAsync(fixture);
        await WithStoreAsync(fixture, topology, tenant, async (store, primary, identity) =>
        {
            Counts before = await ReadCountsAsync(primary, identity, fixture.CancellationToken);
            Task Authorize(CancellationToken _) => throw new InvalidOperationException("Invalid input reached policy.");
            byte[] invalidVariant = Guid.CreateVersion7().ToByteArray();
            invalidVariant[8] &= 0x3f;
            foreach (Guid operation in new[] { Guid.Empty, Guid.NewGuid(), new Guid(invalidVariant) })
                await Assert.ThrowsAsync<ArgumentException>(() => store.ReserveVisitorAsync(
                    operation, NewDigest(), Authorize, fixture.CancellationToken));
            foreach (string digest in new[] { "", new string('a', 63), new string('a', 65),
                         new string('A', 64), new string('g', 64), new string(' ', 64) })
                await Assert.ThrowsAsync<ArgumentException>(() => store.ReserveVisitorAsync(
                    Guid.CreateVersion7(), digest, Authorize, fixture.CancellationToken));
            await Assert.That(await ReadCountsAsync(primary, identity, fixture.CancellationToken)).IsEqualTo(before);
        });
    }

    private static async Task<VisitorEnrollmentOperation> ReserveAsync(
        Fixture fixture, IdentityDatabaseTopology topology, Guid tenant)
    {
        VisitorEnrollmentOperation? receipt = null;
        await WithStoreAsync(fixture, topology, tenant, async (store, _, _) =>
            receipt = await store.ReserveVisitorAsync(Guid.CreateVersion7(), NewDigest(),
                _ => Task.CompletedTask, fixture.CancellationToken));
        return receipt ?? throw new InvalidOperationException("Reservation did not complete.");
    }

    private static async Task AssertSameReceiptAsync(
        VisitorEnrollmentOperation expected, VisitorEnrollmentOperation actual)
    {
        await Assert.That(actual.Id).IsEqualTo(expected.Id);
        await Assert.That(actual.TenantId).IsEqualTo(expected.TenantId);
        await Assert.That(actual.LocalSubjectId).IsEqualTo(expected.LocalSubjectId);
        await Assert.That(actual.PersonalActorId).IsEqualTo(expected.PersonalActorId);
        await Assert.That(actual.ExternalLoginId).IsEqualTo(expected.ExternalLoginId);
        await Assert.That(actual.CredentialOperationId).IsEqualTo(expected.CredentialOperationId);
        await Assert.That(actual.CredentialGeneration).IsEqualTo(expected.CredentialGeneration);
        await Assert.That(actual.TenantUserId).IsEqualTo(expected.TenantUserId);
        await Assert.That(actual.IntentDigest).IsEqualTo(expected.IntentDigest);
        await Assert.That(actual.Stage).IsEqualTo(expected.Stage);
        await Assert.That(actual.Generation).IsEqualTo(expected.Generation);
        await Assert.That(actual.CreatedAt).IsEqualTo(expected.CreatedAt);
        await Assert.That(actual.ExpiresAt).IsEqualTo(expected.ExpiresAt);
        await Assert.That(actual.UpdatedAt).IsEqualTo(expected.UpdatedAt);
    }

    private static async Task AssertNoAccountAsync(
        ExploreDbContext primary, DbContext identity, VisitorEnrollmentOperation receipt, CancellationToken token)
    {
        await Assert.That(await primary.Users.AsNoTracking().AnyAsync(row => row.Id == receipt.LocalSubjectId, token))
            .IsFalse();
        await Assert.That(await primary.Actors.AsNoTracking().AnyAsync(row => row.Id == receipt.PersonalActorId, token))
            .IsFalse();
        await Assert.That(await primary.UserExternalLogins.AsNoTracking()
            .AnyAsync(row => row.Id == receipt.ExternalLoginId, token)).IsFalse();
        await Assert.That(await primary.Set<TenantUser>().AsNoTracking()
            .AnyAsync(row => row.UserId == receipt.LocalSubjectId, token)).IsFalse();
        await Assert.That(await identity.Set<LocalIdentityUser>().AsNoTracking()
            .AnyAsync(row => row.Id == receipt.LocalSubjectId, token)).IsFalse();
        await Assert.That(await identity.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .AnyAsync(row => row.Id == receipt.CredentialOperationId, token)).IsFalse();
    }

    private static async Task<Counts> ReadCountsAsync(ExploreDbContext primary, DbContext identity, CancellationToken token) =>
        new(await primary.Set<VisitorEnrollmentOperation>().AsNoTracking().CountAsync(token),
            await primary.Users.AsNoTracking().CountAsync(token),
            await primary.Actors.AsNoTracking().CountAsync(token),
            await primary.UserExternalLogins.AsNoTracking().CountAsync(token),
            await primary.Set<TenantUser>().AsNoTracking().CountAsync(token),
            await identity.Set<LocalIdentityUser>().AsNoTracking().CountAsync(token),
            await identity.Set<LocalIdentityCredentialOperation>().AsNoTracking().CountAsync(token));

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
        var store = new LocalIdentityCredentialStateStore(identity, primary,
            selected.Services.GetRequiredService<UserManager<LocalIdentityUser>>(), fixture.Clock,
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

    private static async Task<Guid> SeedTenantAsync(Fixture fixture)
    {
        await using var scope = fixture.Provider.CreateAsyncScope();
        Guid tenant = Guid.CreateVersion7();
        await using var seed = SeedContext(scope.ServiceProvider, tenant);
        seed.Tenants.Add(new Tenant
        {
            Id = tenant, FullName = "Visitor reservation tenant", Slug = $"visitor-{tenant:N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!,
            CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        await seed.SaveChangesAsync(fixture.CancellationToken);
        return tenant;
    }

    private static string NewDigest() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
    private sealed record Counts(int Reservations, int Users, int Actors, int Logins, int Members,
        int NativeUsers, int NativeOperations);
}
