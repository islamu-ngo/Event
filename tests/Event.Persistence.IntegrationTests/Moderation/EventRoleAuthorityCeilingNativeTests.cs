using Explore.Application.Authorization;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class EventRoleAuthorityCeilingNativeTests
{
    [Test]
    public async Task Same_event_authority_delegates_subordinate_presets_but_not_owner_permissions()
    {
        await using var fixture = await CeilingFixture.CreateAsync();
        var presets = await fixture.Service.GetAssignableRolePresetsAsync(
            fixture.TenantId, fixture.EventId, fixture.UserId, CancellationToken.None);
        await Assert.That(presets.Select(row => row.RoleId)).Contains((int)RoleEnum.EventManager);
        await Assert.That(presets.Select(row => row.RoleId)).Contains((int)RoleEnum.RegistrationManager);
        await Assert.That(presets.Select(row => row.RoleId)).Contains((int)RoleEnum.CheckInStaff);
        await Assert.That(presets.Select(row => row.RoleId)).DoesNotContain((int)RoleEnum.EventOwner);
        var owner = await fixture.AssignAsync(RoleEnum.EventOwner);
        await Assert.That(owner.IsAllowed).IsFalse();
        await Assert.That(owner.FailureCode).IsEqualTo(EventRoleAuthorityFailureCodes.AuthorityCeilingExceeded);
        await Assert.That(owner.MissingPermissionCodes).Contains(PermissionCodes.EventManageOwner);
    }

    [Test]
    public async Task Publish_approval_remains_nondelegable_even_when_the_actor_holds_it()
    {
        await using var fixture = await CeilingFixture.CreateAsync();
        var permissions = (await fixture.Roles.GetPermissionsForRoleAsync((int)RoleEnum.EventManager))
            .Select(row => row.Id).Append(await fixture.PermissionIdAsync(PermissionCodes.EventApprovePublish));
        await fixture.Roles.ReplacePermissionsAsync((int)RoleEnum.EventManager, permissions);
        var result = await fixture.AssignAsync(RoleEnum.EventManager);
        await Assert.That(result.IsAllowed).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(EventRoleAuthorityFailureCodes.AuthorityCeilingExceeded);
        await Assert.That(result.MissingPermissionCodes).Contains(PermissionCodes.EventApprovePublish);
    }

    [Test]
    public async Task Missing_team_management_cannot_be_replaced_by_other_event_permissions()
    {
        await using var fixture = await CeilingFixture.CreateAsync();
        await fixture.Context.EventRoleAssignments.Where(row => row.EventId == fixture.EventId).ExecuteDeleteAsync();
        fixture.Context.EventRoleAssignments.Add(fixture.Assignment(RoleEnum.CheckInStaff));
        await fixture.Context.SaveChangesAsync();
        var result = await fixture.AssignAsync(RoleEnum.CheckInStaff);
        await Assert.That(result.IsAllowed).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(EventRoleAuthorityFailureCodes.AuthorityMissing);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Discovery_review_requires_the_complete_same_event_review_ceiling(bool hasReviewAuthority)
    {
        await using var fixture = await CeilingFixture.CreateAsync();
        if (hasReviewAuthority)
        {
            fixture.Context.EventRoleAssignments.Add(fixture.Assignment(RoleEnum.EventDiscoveryReviewer));
            await fixture.Context.SaveChangesAsync();
        }
        var presets = await fixture.Service.GetAssignableRolePresetsAsync(
            fixture.TenantId, fixture.EventId, fixture.UserId, CancellationToken.None);
        var result = await fixture.AssignAsync(RoleEnum.EventDiscoveryReviewer);
        await Assert.That(presets.Any(row => row.RoleId == (int)RoleEnum.EventDiscoveryReviewer)).IsEqualTo(hasReviewAuthority);
        await Assert.That(result.IsAllowed).IsEqualTo(hasReviewAuthority);
        if (!hasReviewAuthority)
        {
            await Assert.That(result.FailureCode).IsEqualTo(EventRoleAuthorityFailureCodes.AuthorityCeilingExceeded);
            await Assert.That(result.MissingPermissionCodes).Contains(PermissionCodes.EventReviewDiscoveryIdentity);
            await Assert.That(result.MissingPermissionCodes).Contains(PermissionCodes.EventReverseDiscoveryIdentity);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Current_filtering_not_a_warmed_display_cache_controls_delegation(bool filteredBefore)
    {
        await using var fixture = await CeilingFixture.CreateAsync();
        int id = await fixture.PermissionIdAsync(PermissionCodes.EventCheckInManage);
        await fixture.Context.Permissions.Where(row => row.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsFiltered, filteredBefore));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new PermissionRegistryService(new PermissionRepository(fixture.Context), cache);
        var display = await registry.GetAllPermissionsAsync();
        await fixture.Context.Permissions.Where(row => row.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsFiltered, !filteredBefore));
        var result = await fixture.AssignAsync(RoleEnum.CheckInStaff);
        await Assert.That(result.IsAllowed).IsEqualTo(filteredBefore);
        await Assert.That(display.Single(row => row.Id == id).IsFiltered).IsEqualTo(filteredBefore);
        if (!filteredBefore)
            await Assert.That(result.MissingPermissionCodes).Contains(PermissionCodes.EventCheckInManage);
    }

    [Test]
    public async Task Newly_active_native_permissions_are_not_withheld_by_an_old_display_cache()
    {
        await using var fixture = await CeilingFixture.CreateAsync();
        int id = await fixture.PermissionIdAsync(PermissionCodes.EventCheckInManage);
        await fixture.Context.Permissions.Where(row => row.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsActive, false));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var registry = new PermissionRegistryService(new PermissionRepository(fixture.Context), cache);
        await Assert.That((await registry.GetAllPermissionsAsync()).Any(row => row.Id == id)).IsFalse();
        await fixture.Context.Permissions.Where(row => row.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsActive, true));
        await Assert.That((await fixture.AssignAsync(RoleEnum.CheckInStaff)).IsAllowed).IsTrue();
    }

    internal sealed class CeilingFixture(SourceDatabase database, ExploreDbContext context, Guid eventId) : IAsyncDisposable
    {
        public SourceDatabase Database => database;
        public ExploreDbContext Context => context;
        public Guid TenantId => database.TenantId;
        public Guid UserId => database.AdministratorId;
        public Guid EventId => eventId;
        public RoleRepository Roles { get; } = new(context);
        public EventRoleAuthorityCeilingService Service { get; } = new(
            new EventAuthoritySnapshotService(context), new RoleRepository(context), new PermissionRepository(context));
        public Task<int> PermissionIdAsync(string code) => context.Permissions.Where(row => row.MasterCode == code).Select(row => row.Id).SingleAsync();
        public Task<EventRoleAssignmentAuthorityResult> AssignAsync(RoleEnum role) => Service.CanAssignRoleAsync(
            TenantId, EventId, UserId, (int)role, CancellationToken.None);
        public EventRoleAssignment Assignment(RoleEnum role) => EventRoleAssignment.Create(
            TenantId, EventId, UserId, (int)role, EventRoleAssignmentStatus.Active,
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), UserId);

        public static async Task<CeilingFixture> CreateAsync()
        {
            var database = await SourceDatabase.CreateAsync();
            var context = database.Context();
            Guid eventId = Guid.CreateVersion7();
            var fixture = new CeilingFixture(database, context, eventId);
            string[] manager = [PermissionCodes.EventManageTeam, PermissionCodes.EventUpdate,
                PermissionCodes.EventRegistrationView, PermissionCodes.EventRegistrationManage,
                PermissionCodes.EventCheckInView, PermissionCodes.EventCheckInManage];
            string[] owner = [PermissionCodes.EventManageTeam, PermissionCodes.EventManageOwner,
                PermissionCodes.EventTransferOwnership, PermissionCodes.EventDelete, PermissionCodes.EventManageFinance];
            string[] review = ["event:view", PermissionCodes.EventUpdate,
                PermissionCodes.EventReviewDiscoveryIdentity, PermissionCodes.EventReverseDiscoveryIdentity];
            var codes = manager.Concat(owner).Concat(review).Append(PermissionCodes.EventApprovePublish).Distinct().ToArray();
            int next = (await context.Permissions.MaxAsync(row => (int?)row.Id) ?? 0) + 1;
            foreach (string code in codes)
            {
                var permission = await context.Permissions.SingleOrDefaultAsync(row => row.MasterCode == code);
                if (permission is null)
                {
                    permission = new Permission
                    {
                        Id = next++, MasterCode = code, FullName = code, ResourceKind = "event",
                        Action = code.Split(':')[1], GroupName = "Events", Scope = RoleScopeEnum.Event, IsSystem = true
                    };
                    context.Permissions.Add(permission);
                }
                permission.IsActive = true;
                permission.IsFiltered = false;
            }
            var actor = new Actor
            {
                Id = Guid.CreateVersion7(), ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                UserId = fixture.UserId, Pii = new ActorPii { DisplayName = "Event delegation" }
            };
            context.Events.Add(new Explore.Domain.Event(EventStatusEnum.Published)
            {
                Id = eventId, TenantId = fixture.TenantId, Tenant = null!, ActorId = actor.Id, Actor = actor,
                OrganizerActorId = actor.Id, Title = "Native delegation", PublicCode = Guid.CreateVersion7().ToString("N")[..12],
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, EventStatus = null!, Timezone = "UTC"
            });
            context.EventRoleAssignments.AddRange(fixture.Assignment(RoleEnum.EventOwner), fixture.Assignment(RoleEnum.EventManager));
            await context.SaveChangesAsync();
            await ReplaceAsync(RoleEnum.EventOwner, owner);
            await ReplaceAsync(RoleEnum.EventManager, manager);
            await ReplaceAsync(RoleEnum.RegistrationManager, [PermissionCodes.EventRegistrationView, PermissionCodes.EventRegistrationManage]);
            await ReplaceAsync(RoleEnum.CheckInStaff, [PermissionCodes.EventRegistrationView, PermissionCodes.EventCheckInView, PermissionCodes.EventCheckInManage]);
            await ReplaceAsync(RoleEnum.EventDiscoveryReviewer, review);
            return fixture;

            async Task ReplaceAsync(RoleEnum role, string[] permissions) => await fixture.Roles.ReplacePermissionsAsync(
                (int)role, await context.Permissions.Where(row => permissions.Contains(row.MasterCode)).Select(row => row.Id).ToArrayAsync());
        }

        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync();
            await database.DisposeAsync();
        }
    }
}
