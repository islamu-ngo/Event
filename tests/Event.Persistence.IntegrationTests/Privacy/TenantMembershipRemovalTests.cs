using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.TenantUsers.Handlers.Commands;
using Explore.Application.Features.TenantUsers.Requests.Commands;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Privacy;

[Category("EventLocationPrivacy")]
[ClassDataSource<TenantMembershipRemovalPostgreSqlFixture>(Shared = SharedType.PerClass)]
[NotInParallel("TenantMembershipRemovalDb")]
public sealed class TenantMembershipRemovalTests(TenantMembershipRemovalPostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset RemovedAt = new(2026, 7, 19, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task SelfRemoval_ChangesOnlyTargetTenantMembershipProfileAndGrants()
    {
        var scenario = await SeedTwoTenantScenarioAsync("self");
        await using var context = fixture.CreateTenantContext(scenario.TenantAId, scenario.UserId);
        var handler = CreateHandler(context, scenario.TenantAId, scenario.UserId);

        var first = await handler.ExecuteAsync(
            new RemoveTenantMembershipCommand(scenario.TenantAId, scenario.UserId),
            CancellationToken.None);
        var replay = await handler.ExecuteAsync(
            new RemoveTenantMembershipCommand(scenario.TenantAId, scenario.UserId),
            CancellationToken.None);

        await Assert.That(first).IsTrue();
        await Assert.That(replay).IsFalse();
        await AssertScenarioAsync(scenario, tenantARemoved: true);
    }

    [Test]
    public async Task ConcurrentSelfRemoval_HasExactlyOneWinner()
    {
        var scenario = await SeedTwoTenantScenarioAsync("concurrent");
        await using var contextA = fixture.CreateTenantContext(scenario.TenantAId, scenario.UserId);
        await using var contextB = fixture.CreateTenantContext(scenario.TenantAId, scenario.UserId);
        var handlerA = CreateHandler(contextA, scenario.TenantAId, scenario.UserId);
        var handlerB = CreateHandler(contextB, scenario.TenantAId, scenario.UserId);
        var command = new RemoveTenantMembershipCommand(scenario.TenantAId, scenario.UserId);

        var results = await Task.WhenAll(
            handlerA.ExecuteAsync(command, CancellationToken.None),
            handlerB.ExecuteAsync(command, CancellationToken.None));

        await Assert.That(results.Count(result => result)).IsEqualTo(1);
        await Assert.That(results.Count(result => !result)).IsEqualTo(1);
        await AssertScenarioAsync(scenario, tenantARemoved: true);
    }

    [Test]
    public async Task CancellationAfterMutation_RollsBackEveryTenantLocalChange()
    {
        var scenario = await SeedTwoTenantScenarioAsync("cancel");
        await using var context = fixture.CreateTenantContext(scenario.TenantAId, scenario.UserId);
        var repository = new TenantUserRepository(context, fixture.NativeQueueEnrollment);
        var unitOfWork = new EfCoreUnitOfWork(context);
        using var cancellation = new CancellationTokenSource();
        await using var humanGate = new HumanSecuritySourceGate(context);
        await using var heldHuman = await humanGate.AcquireAsync(cancellation.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var claimed = await repository.TryRemoveMembershipAsync(
                scenario.TenantAId,
                scenario.UserId,
                scenario.UserId,
                RemovedAt.UtcDateTime,
                ct);
            if (!claimed)
            {
                throw new InvalidOperationException("The rollback scenario requires an active membership.");
            }

            cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
        }, cancellation.Token));

        await AssertScenarioAsync(scenario, tenantARemoved: false);
    }

    [Test]
    public async Task RemovalWithAnotherActiveTenantFilter_FailsClosedWithoutChanges()
    {
        var scenario = await SeedTwoTenantScenarioAsync("wrong-filter");
        await using var context = fixture.CreateTenantContext(scenario.TenantBId, scenario.UserId);
        var repository = new TenantUserRepository(context, fixture.NativeQueueEnrollment);

        var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.TryRemoveMembershipAsync(
            scenario.TenantAId, scenario.UserId, scenario.UserId, RemovedAt.UtcDateTime));
        await Assert.That(rejected!.Message).IsEqualTo("Tenant membership removal requires the matching active tenant filter.");

        await AssertScenarioAsync(scenario, tenantARemoved: false);
    }

    [Test]
    public async Task RemovalWithTenantFilterAlreadyBypassed_FailsClosedWithoutChanges()
    {
        var scenario = await SeedTwoTenantScenarioAsync("bypassed-filter");
        await using var context = fixture.CreateTenantContext(scenario.TenantAId, scenario.UserId);
        context.EnableTenantFilterBypass("Tenant membership removal fail-closed integration scenario.");
        var repository = new TenantUserRepository(context, fixture.NativeQueueEnrollment);

        var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.TryRemoveMembershipAsync(
            scenario.TenantAId, scenario.UserId, scenario.UserId, RemovedAt.UtcDateTime));
        await Assert.That(rejected!.Message).IsEqualTo("Tenant membership removal requires the matching active tenant filter.");

        await AssertScenarioAsync(scenario, tenantARemoved: false);
    }

    private RemoveTenantMembershipCommandHandler CreateHandler(
        ExploreDbContext context,
        Guid tenantId,
        Guid currentUserId)
    {
        return new RemoveTenantMembershipCommandHandler(
            new TenantUserRepository(context, fixture.NativeQueueEnrollment),
            new TenantUserRoleGrantRepository(context),
            new EfCoreUnitOfWork(context),
            new TestTenantContext(tenantId),
            new TestCurrentUserService(currentUserId),
            new FixedTimeProvider(RemovedAt),
            () => new HumanSecuritySourceGate(context));
    }

    private async Task<MembershipScenario> SeedTwoTenantScenarioAsync(string discriminator)
    {
        await using var context = fixture.CreateSeedContext();
        await using var humanGate = new HumanSecuritySourceGate(context);
        await using var humanLease = await humanGate.AcquireAsync();
        await using var discoveryGate = new EventDiscoveryEnrollmentGate(context);
        await using var discoveryLease = await discoveryGate.AcquireAsync();
        var tenantA = CreateTenant($"membership-{discriminator}-a");
        var tenantB = CreateTenant($"membership-{discriminator}-b");
        var user = CreateUser($"membership-{discriminator}");
        context.AddRange(tenantA, tenantB, user);
        await context.SaveChangesAsync();

        var tenantAUser = CreateTenantUser(tenantA.Id, user.Id);
        var tenantBUser = CreateTenantUser(tenantB.Id, user.Id);
        context.TenantUsers.AddRange(tenantAUser, tenantBUser);
        await context.SaveChangesAsync();

        var tenantAProfile = CreateProfile(tenantA.Id, tenantAUser.Id, "Tenant A");
        var tenantBProfile = CreateProfile(tenantB.Id, tenantBUser.Id, "Tenant B");
        var tenantAGrant = CreateGrant(tenantA.Id, tenantAUser.Id);
        var tenantBGrant = CreateGrant(tenantB.Id, tenantBUser.Id);
        var tenantAHome = CreatePrivateHome(tenantA.Id, user.Id, "Tenant A Home");
        var tenantBHome = CreatePrivateHome(tenantB.Id, user.Id, "Tenant B Home");
        context.AddRange(
            tenantAProfile,
            tenantBProfile,
            tenantAGrant,
            tenantBGrant,
            tenantAHome,
            tenantBHome);
        await context.SaveChangesAsync();
        var tenantAHomeHash = await ReadHomeStateHashAsync(context, tenantAHome.Id);
        var tenantBHomeHash = await ReadHomeStateHashAsync(context, tenantBHome.Id);

        return new MembershipScenario(
            tenantA.Id,
            tenantB.Id,
            user.Id,
            tenantAUser.Id,
            tenantBUser.Id,
            tenantAProfile.Id,
            tenantBProfile.Id,
            tenantAGrant.Id,
            tenantBGrant.Id,
            tenantAHome.Id,
            tenantBHome.Id,
            tenantAHomeHash,
            tenantBHomeHash);
    }

    private async Task AssertScenarioAsync(MembershipScenario scenario, bool tenantARemoved)
    {
        await using var context = fixture.CreateSeedContext();
        var tenantAUser = await context.TenantUsers
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .SingleAsync(entity => entity.Id == scenario.TenantAUserId);
        var tenantBUser = await context.TenantUsers.SingleAsync(entity => entity.Id == scenario.TenantBUserId);
        var tenantAProfileCount = await context.TenantUserProfiles.CountAsync(entity => entity.Id == scenario.TenantAProfileId);
        var tenantBProfileCount = await context.TenantUserProfiles.CountAsync(entity => entity.Id == scenario.TenantBProfileId);
        var tenantAGrant = await context.TenantUserRoleGrants.SingleAsync(entity => entity.Id == scenario.TenantAGrantId);
        var tenantBGrant = await context.TenantUserRoleGrants.SingleAsync(entity => entity.Id == scenario.TenantBGrantId);
        var user = await context.Users.Include(entity => entity.Pii).SingleAsync(entity => entity.Id == scenario.UserId);
        var homes = await context.Locations
            .Include(entity => entity.Pii)
            .Where(entity => entity.Id == scenario.TenantAHomeId || entity.Id == scenario.TenantBHomeId)
            .ToListAsync();
        var tenantAHomeHash = await ReadHomeStateHashAsync(context, scenario.TenantAHomeId);
        var tenantBHomeHash = await ReadHomeStateHashAsync(context, scenario.TenantBHomeId);
        var globalErasureOutboxCount = await context.OutboxMessages.CountAsync(message =>
            message.EventType == LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType
            || message.EventType == LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType);

        await Assert.That(tenantAUser.IsDeleted).IsEqualTo(tenantARemoved);
        await Assert.That(tenantAUser.StatusId).IsEqualTo(tenantARemoved
            ? (int)TenantUserStatusEnum.Removed
            : (int)TenantUserStatusEnum.Active);
        await Assert.That(tenantAUser.RemovedBy).IsEqualTo(tenantARemoved ? scenario.UserId : null);
        await Assert.That(tenantAProfileCount).IsEqualTo(tenantARemoved ? 0 : 1);
        await Assert.That(tenantAGrant.RevokedAt.HasValue).IsEqualTo(tenantARemoved);
        await Assert.That(tenantAGrant.RevokedBy).IsEqualTo(tenantARemoved ? scenario.UserId : null);

        await Assert.That(tenantBUser.IsDeleted).IsFalse();
        await Assert.That(tenantBUser.StatusId).IsEqualTo((int)TenantUserStatusEnum.Active);
        await Assert.That(tenantBProfileCount).IsEqualTo(1);
        await Assert.That(tenantBGrant.RevokedAt).IsNull();
        await Assert.That(user.IsDeleted).IsFalse();
        await Assert.That(user.Pii).IsNotNull();
        await Assert.That(homes.Count).IsEqualTo(2);
        await Assert.That(homes.All(home => home.OwnerUserId == scenario.UserId)).IsTrue();
        await Assert.That(homes.All(home => home.Pii is not null)).IsTrue();
        await Assert.That(tenantAHomeHash).IsEqualTo(scenario.TenantAHomeHash);
        await Assert.That(tenantBHomeHash).IsEqualTo(scenario.TenantBHomeHash);
        await Assert.That(globalErasureOutboxCount).IsEqualTo(0);
    }

    private static Task<string> ReadHomeStateHashAsync(ExploreDbContext context, Guid homeId) =>
        context.Database.SqlQuery<string>($"""
                SELECT encode(
                    sha256(convert_to(to_jsonb(location_row)::text || '|' || to_jsonb(pii_row)::text, 'UTF8')),
                    'hex') AS "Value"
                FROM locations AS location_row
                INNER JOIN location_pii AS pii_row ON pii_row.location_id = location_row.id
                WHERE location_row.id = {homeId}
                """)
            .SingleAsync();

    private static Tenant CreateTenant(string slugPrefix) => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = slugPrefix,
        Slug = $"{slugPrefix}-{Guid.NewGuid():N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!,
    };

    private static User CreateUser(string emailPrefix) => new()
    {
        Id = Guid.CreateVersion7(),
        Pii = new UserPii
        {
            Email = $"{emailPrefix}-{Guid.NewGuid():N}@example.com",
            FirstName = "Membership",
            LastName = "User",
        },
        EmailVerified = true,
        CreatedAt = DateTime.UtcNow,
    };

    private static TenantUser CreateTenantUser(Guid tenantId, Guid userId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        UserId = userId,
        User = null!,
        StatusId = (int)TenantUserStatusEnum.Active,
        JoinedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
    };

    private static TenantUserProfile CreateProfile(Guid tenantId, Guid tenantUserId, string displayName) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        TenantUserId = tenantUserId,
        TenantUser = null!,
        DisplayNameOverride = displayName,
        CreatedAt = DateTime.UtcNow,
    };

    private static TenantUserRoleGrant CreateGrant(Guid tenantId, Guid tenantUserId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        TenantUserId = tenantUserId,
        TenantUser = null!,
        RoleId = (int)RoleEnum.TenantMember,
        Role = null!,
        RoleScopeId = (int)RoleScopeEnum.Tenant,
        GrantedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow,
    };

    private static Location CreatePrivateHome(Guid tenantId, Guid ownerUserId, string name)
    {
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            FullName = name,
            Country = "BE",
            City = "Brussels",
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        location.ClassifyAsPrivateHome(ownerUserId);
        location.SetManualAddress($"{name} address", "1000");
        return location;
    }

    private sealed record MembershipScenario(
        Guid TenantAId,
        Guid TenantBId,
        Guid UserId,
        Guid TenantAUserId,
        Guid TenantBUserId,
        Guid TenantAProfileId,
        Guid TenantBProfileId,
        Guid TenantAGrantId,
        Guid TenantBGrantId,
        Guid TenantAHomeId,
        Guid TenantBHomeId,
        string TenantAHomeHash,
        string TenantBHomeHash);

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed record TestCurrentUserService(Guid Id) : ICurrentUserService
    {
        public Guid? UserId => Id;
        public bool IsAuthenticated => true;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}

public sealed class TenantMembershipRemovalPostgreSqlFixture : PostgreSqlContainerFixture
{
    public ExploreDbContext CreateSeedContext()
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(builder.Options);
        context.EnableTenantFilterBypass("Tenant membership removal integration-test seed and verification context.");
        context.BindTenantLease();
        return context;
    }

    public ExploreDbContext CreateTenantContext(Guid tenantId, Guid currentUserId)
    {
        var context = CreateEnrolledTenantDbContext(new TestTenantContext(tenantId));
        context.CurrentUserService = new TestCurrentUserService(currentUserId);
        context.BindTenantLease();
        return context;
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed record TestCurrentUserService(Guid Id) : ICurrentUserService
    {
        public Guid? UserId => Id;
        public bool IsAuthenticated => true;
    }
}
