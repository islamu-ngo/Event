using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Group;
using Explore.Application.Features.Groups.Handlers.Commands;
using Explore.Application.Features.Groups.Handlers.Queries;
using Explore.Application.Features.Groups.Requests.Commands;
using Explore.Application.Features.Groups.Requests.Queries;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static Event.Application.UnitTests.Profiles.OrganizationMapperTests;

namespace Event.Application.UnitTests.Profiles;

public sealed class OrganizationMappingHandlerTests
{
    [Test]
    public async Task GroupQueries_KeepPageOrderSnapshotMissingResultsAndRoleEnrichment()
    {
        var first = CreateGroup();
        var second = CreateGroup();
        second.Id = Stamp;
        var store = new GroupStore([second, first]);
        var members = new GroupMemberStore([new GroupMember
        {
            Id = ActorId, GroupTenant = first.TenantParticipations.Single(), UserId = ActorId, User = null!,
            RoleId = (int)RoleEnum.GroupAdmin, Role = null!, Tenant = null!, TenantId = TenantId
        }]);
        var list = new GetGroupListRequestHandler(store, null!, NullLogger<GetGroupListRequestHandler>.Instance);
        var mine = new GetMyGroupsRequestHandler(store, members, null!, NullLogger<GetMyGroupsRequestHandler>.Instance);
        var detail = new GetGroupDetailsRequestHandler(store, null!, NullLogger<GetGroupDetailsRequestHandler>.Instance, new InlineCache());
        var page = await list.Handle(new GetGroupListRequest { PageNumber = 1, PageSize = 10 }, default);
        await Assert.That(page.Items.Select(item => item.Id).SequenceEqual(new[] { Stamp, Id })).IsTrue();
        await Assert.That(page.TotalCount).IsEqualTo(2);
        await Assert.That(page.PageNumber).IsEqualTo(1);
        await Assert.That(page.PageSize).IsEqualTo(10);
        var own = await mine.Handle(new GetMyGroupsRequest { UserId = ActorId.ToString() }, default);
        await Assert.That(own.Items.Single().CurrentUserRoleId).IsEqualTo((int)RoleEnum.GroupAdmin);
        await Assert.That(await detail.Handle(new GetGroupDetailsRequest { Id = TenantId }, default)).IsNull();
        await Assert.That((await detail.Handle(new GetGroupDetailsRequest { Id = Id }, default)).ActorDisplayName).IsEqualTo("Public actor");
        await Assert.That((await mine.Handle(new GetMyGroupsRequest { UserId = "invalid" }, default)).Items).IsEmpty();
        store.Items.Clear();
        first.FullName = "Changed after publication";
        first.Actor!.Pii.DisplayName = "Changed actor";
        await Assert.That(page.Items[1].FullName).IsEqualTo("Community group");
        await Assert.That(own.Items[0].FullName).IsEqualTo("Community group");
    }

    [Test]
    public async Task GroupCreation_UsesOnlyBusinessInputAndHandlerOwnedParticipation()
    {
        var groups = new GroupStore([]);
        var participations = new GroupTenantStore();
        var members = new GroupMemberStore([]);
        var actors = new ActorStore();
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var handler = new CreateGroupCommandHandler(groups, participations, null!, members, actors, null!,
            new CacheInvalidator(), new TenantContext(TenantId), new InlineCache(), metrics);
        var result = await handler.Handle(new CreateGroupCommand
        {
            CreatorUserId = ActorId,
            GroupDto = new CreateGroupDto { FullName = "New group", Description = "New description" }
        }, default);
        await Assert.That(result.IsSuccess).IsTrue();
        var group = groups.Items.Single();
        await Assert.That(group.FullName).IsEqualTo("New group");
        await Assert.That(group.Description).IsEqualTo("New description");
        await Assert.That(group.IsDeleted).IsFalse();
        await Assert.That(group.CreatedBy).IsNull();
        await Assert.That(group.ConcurrencyStamp).IsEqualTo(Guid.Empty);
        await Assert.That(group.TenantParticipations).IsEmpty();
        var participation = participations.Items.Single();
        await Assert.That(participation.TenantId).IsEqualTo(TenantId);
        await Assert.That(participation.GroupId).IsEqualTo(Id);
        await Assert.That(participation.ApprovalStatusId).IsEqualTo((int)ApprovalStatusEnum.Pending);
        await Assert.That(participation.IsOrganizerEligible).IsFalse();
        await Assert.That(participation.ProfilePictureId).IsNull();
        await Assert.That(members.Items.Single().UserId).IsEqualTo(ActorId);
        await Assert.That(members.Items.Single().TenantId).IsEqualTo(TenantId);
        await Assert.That(members.Items.Single().RoleId).IsEqualTo((int)RoleEnum.GroupAdmin);
        await Assert.That(actors.Items.Single().DisplayName).IsEqualTo("New group");
        await Assert.That(actors.Items.Single().GroupId).IsEqualTo(Id);
    }

    internal abstract class Store<T, TKey> : IGenericRepository<T, TKey> where T : class
    {
        public virtual Task<T?> GetById(TKey id) => throw new NotSupportedException();
        public virtual Task<IReadOnlyList<T>> GetAll() => throw new NotSupportedException();
        public Task<(IReadOnlyList<T> Items, int TotalCount)> GetAllPaged(int pageNumber, int pageSize) => throw new NotSupportedException();
        public Task<bool> Exists(TKey id) => throw new NotSupportedException();
        public virtual Task<T> Create(T entity) => throw new NotSupportedException();
        public Task Update(T entity) => throw new NotSupportedException();
        public Task Delete(T entity) => throw new NotSupportedException();
    }

    internal sealed class GroupStore(List<Group> items) : Store<Group, Guid>, IGroupRepository
    {
        public List<Group> Items { get; } = items;
        public override Task<Group> Create(Group entity) { entity.Id = Id; Items.Add(entity); return Task.FromResult(entity); }
        public Task<Group?> GetGroupWithDetails(Guid id) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));
        public Task<(List<Group> Items, int TotalCount)> GetGroupsWithDetailsPaged(int pageNumber, int pageSize) => Task.FromResult((Items.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), Items.Count));
        public Task<(List<Group> Items, int TotalCount)> GetMyGroupsPaged(Guid userId, int pageNumber, int pageSize) => Task.FromResult((userId == ActorId ? Items.Where(item => item.Id == Id).ToList() : [], userId == ActorId ? 1 : 0));
        public Task<T> ExecuteWithHierarchyMutationLock<T>(Guid tenantId, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) => operation(cancellationToken);
        public Task<List<Group>> GetGroupsWithDetails() => throw new NotSupportedException();
        public Task<List<Group>> GetMyGroups(Guid userId) => throw new NotSupportedException();
        public Task<bool> OrganizationExistsInTenant(Guid organizationId, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> GroupExistsInTenant(Guid groupId, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> WouldCreateHierarchyCycle(Guid groupId, Guid parentGroupId, Guid tenantId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> WouldExceedHierarchyDepth(Guid? parentGroupId, Guid tenantId, int maxDepth, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> WouldExceedHierarchyDepthForMove(Guid groupId, Guid? parentGroupId, Guid tenantId, int maxDepth, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    internal sealed class GroupTenantStore : Store<GroupTenant, Guid>, IGroupTenantRepository
    {
        public List<GroupTenant> Items { get; } = [];
        public override Task<GroupTenant> Create(GroupTenant entity) { entity.Id = Stamp; Items.Add(entity); return Task.FromResult(entity); }
        public Task<GroupTenant?> GetByGroupAndTenant(Guid groupId, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(item => item.GroupId == groupId && item.TenantId == tenantId));
    }

    internal sealed class GroupMemberStore(List<GroupMember> items) : Store<GroupMember, Guid>, IGroupMemberRepository
    {
        public List<GroupMember> Items { get; } = items;
        public override Task<GroupMember> Create(GroupMember entity) { Items.Add(entity); return Task.FromResult(entity); }
        public Task<GroupMember?> GetGroupMemberWithDetails(Guid id) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));
        public Task<List<GroupMember>> GetMembersByGroupId(Guid groupId) => Task.FromResult(Items.Where(item => item.GroupTenant.GroupId == groupId).ToList());
        public Task<List<GroupMember>> GetMembershipsByUser(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(Items.Where(item => item.UserId == userId).ToList());
        public Task<List<GroupMember>> GetGroupMembersWithDetails() => throw new NotSupportedException();
        public Task<GroupMember?> GetByGroupAndUser(Guid groupId, Guid userId) => throw new NotSupportedException();
        public Task<bool> Exists(Guid groupId, Guid userId) => throw new NotSupportedException();
        public Task<bool> HasPermissionInGroup(Guid groupId, Guid userId, string permissionMasterCode) => throw new NotSupportedException();
        public Task<List<Guid>> GetGroupIdsWhereUserHasPermission(Guid userId, string permissionMasterCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class ActorStore : Store<Actor, Guid>, IActorRepository
    {
        public List<Actor> Items { get; } = [];
        public override Task<Actor> Create(Actor entity) { Items.Add(entity); return Task.FromResult(entity); }
        public Task<Actor?> GetActorWithDetails(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Actor?> GetPublicActorProfileAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Actor?> GetPublicActorProfileByTenantAsync(Guid tenantId, Guid actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Actor?> GetLocallyDiscoverableSubscriptionTargetAsync(Guid tenantId, Guid actorId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Actor?> GetActorByDid(string did, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Actor?> GetActorByHandle(string handle, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Actor>> GetActorsByTenant(Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DidExists(string did) => throw new NotSupportedException();
        public Task<(List<Actor> Items, int TotalCount)> GetActorsWithDetailsPaged(int pageNumber, int pageSize, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Actor>> SearchAiReferenceActorsAsync(string searchTerm, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Actor?> GetActorByUserId(Guid userId) => throw new NotSupportedException();
        public Task<Actor?> GetTrackedActorByUserId(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Actor?> GetActorByUserIdAndTenantId(Guid userId, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Actor?> GetActorByOrganizationId(Guid organizationId) => throw new NotSupportedException();
        public Task<Actor?> GetActorByGroupId(Guid groupId) => throw new NotSupportedException();
        public Task<int> ForgetPiiAsync(Guid actorId) => throw new NotSupportedException();
    }

    internal sealed record TenantContext(Guid TenantId) : ITenantContext;
    internal sealed class CacheInvalidator : IAdminCacheInvalidator
    {
        public void InvalidateUser(Guid userId) { }
        public void InvalidateAll() => throw new NotSupportedException();
    }
    internal sealed class InlineCache : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) => factory(state, cancellationToken);
        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public override ValueTask SetAsync<T>(string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
