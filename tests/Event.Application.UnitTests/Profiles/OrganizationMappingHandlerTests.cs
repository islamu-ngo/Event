using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Group;
using Explore.Application.DTOs.Organization;
using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.Features.OrganizationReviews.Commands.CreateOrganizationReview;
using Explore.Application.Features.OrganizationReviews.Queries.GetMyReviews;
using Explore.Application.Features.OrganizationReviews.Queries.GetOrganizationReviews;
using Explore.Application.Mappings;
using System.Text.Json;
using Explore.Application.Features.Organizations.Handlers.Commands;
using Explore.Application.Features.Organizations.Handlers.Queries;
using Explore.Application.Features.Organizations.Requests.Commands;
using Explore.Application.Features.Organizations.Requests.Queries;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Exceptions;
using NSubstitute;
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

    [Test]
    public async Task OrganizationQueries_KeepActorFallbackPageSnapshotsRolesAndSelfOnlyAuthority()
    {
        var first = CreateOrganization();
        var second = CreateOrganization();
        second.Id = Stamp;
        var store = new OrganizationStore([second, first]);
        var members = new OrganizationMemberStore([new OrganizationMember
        {
            Id = ActorId, OrganizationTenant = first.TenantParticipations.Single(), UserId = ActorId, User = CreateUser(),
            RoleId = (int)RoleEnum.OrgAdmin, Role = null!, Tenant = null!, TenantId = TenantId
        }]);
        var list = new GetOrganizationListRequestHandler(store, null!, NullLogger<GetOrganizationListRequestHandler>.Instance);
        var mine = new GetMyOrganizationsRequestHandler(store, members, null!, NullLogger<GetMyOrganizationsRequestHandler>.Instance);
        var detail = new GetOrganizationDetailsRequestHandler(store, null!, NullLogger<GetOrganizationDetailsRequestHandler>.Instance, new InlineCache());
        var page = await list.Handle(new GetOrganizationListRequest { PageNumber = 1, PageSize = 10 }, default);
        await Assert.That(page.Items.Select(item => item.Id).SequenceEqual(new[] { Stamp, Id })).IsTrue();
        await Assert.That(page.TotalCount).IsEqualTo(2);
        await Assert.That(page.PageSize).IsEqualTo(10);
        await Assert.That(page.PageNumber).IsEqualTo(1);
        await Assert.That(await detail.Handle(new GetOrganizationDetailsRequest(TenantId), default)).IsNull();
        await Assert.That((await detail.Handle(new GetOrganizationDetailsRequest(Id), default))!.ActorDisplayName).IsEqualTo("Public actor");
        await Assert.That((await detail.Handle(new GetOrganizationDetailsRequest(ActorId), default))!.FullName).IsEqualTo("Community organization");
        var own = await mine.Handle(new GetMyOrganizationsRequest { UserId = ActorId.ToString() }, default);
        await Assert.That(own.Items.Single().CurrentUserRoleId).IsEqualTo((int)RoleEnum.OrgAdmin);
        await Assert.That((await mine.Handle(new GetMyOrganizationsRequest { UserId = "invalid" }, default)).Items).IsEmpty();
        var userOrganizations = new GetUserOrganizationsRequestHandler(members, new CurrentUser(ActorId));
        await Assert.That((await userOrganizations.Handle(new GetUserOrganizationsRequest(ActorId), default)).Single().CurrentUserRoleId).IsEqualTo((int)RoleEnum.OrgAdmin);
        await Assert.That(async () => await userOrganizations.Handle(new GetUserOrganizationsRequest(TenantId), default)).Throws<AuthorizationException>();
        var anonymous = new GetUserOrganizationsRequestHandler(members, new CurrentUser(null));
        await Assert.That(async () => await anonymous.Handle(new GetUserOrganizationsRequest(ActorId), default)).Throws<AuthorizationException>();
        store.Items.Clear();
        first.Pii.FullName = "Changed after publication";
        await Assert.That(page.Items[1].FullName).IsEqualTo("Community organization");
        await Assert.That(own.Items.Single().FullName).IsEqualTo("Community organization");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrganizationCreation_KeepsInputAllowlistAndTenantApprovalAuthority(bool administrator)
    {
        var organizations = new OrganizationStore([]);
        var participations = new OrganizationTenantStore();
        var members = new OrganizationMemberStore([]);
        var actors = new ActorStore();
        var authority = Substitute.For<IAdminContext>();
        authority.IsTenantAdminAsync(TenantId, Arg.Any<CancellationToken>()).Returns(administrator);
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        using var metrics = new BusinessMetrics(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var handler = new CreateOrganizationCommandHandler(organizations, participations, members, actors, null!, authority,
            new CacheInvalidator(), new TenantContext(TenantId), new InlineCache(), metrics, new InlineUnitOfWork());
        var result = await handler.Handle(new CreateOrganizationCommand
        {
            CreatorUserId = ActorId,
            OrganizationDto = new CreateOrganizationDto
            {
                FullName = "New organization", WebsiteUrl = "https://new.example.test", Email = "office@example.test",
                Country = "BE", City = "Brussels", Postcode = 1000, Address = "Square 1"
            }
        }, default);
        await Assert.That(result.IsSuccess).IsTrue();
        var organization = organizations.Items.Single();
        await Assert.That(organization.FullName).IsEqualTo("New organization");
        await Assert.That(organization.WebsiteUrl).IsEqualTo("https://new.example.test");
        await Assert.That(organization.Email).IsEqualTo("office@example.test");
        await Assert.That(organization.Country).IsEqualTo("BE");
        await Assert.That(organization.City).IsEqualTo("Brussels");
        await Assert.That(organization.Postcode).IsEqualTo("1000");
        await Assert.That(organization.Address).IsEqualTo("Square 1");
        await Assert.That(organization.IsDeleted).IsFalse();
        await Assert.That(organization.ConcurrencyStamp).IsEqualTo(Guid.Empty);
        await Assert.That(organization.CreatedBy).IsNull();
        await Assert.That(organization.TenantParticipations).IsEmpty();
        var participation = participations.Items.Single();
        await Assert.That(participation.TenantId).IsEqualTo(TenantId);
        await Assert.That(participation.OrganizationId).IsEqualTo(Id);
        await Assert.That(participation.ApprovalStatusId).IsEqualTo(administrator ? (int)ApprovalStatusEnum.Approved : (int)ApprovalStatusEnum.Pending);
        await Assert.That(participation.IsVisible).IsEqualTo(administrator);
        await Assert.That(participation.IsOrganizerEligible).IsEqualTo(administrator);
        await Assert.That(participation.ApprovedBy).IsEqualTo(administrator ? ActorId : (Guid?)null);
        await Assert.That(participation.ProfilePictureId).IsNull();
        await Assert.That(members.Items.Single().UserId).IsEqualTo(ActorId);
        await Assert.That(members.Items.Single().TenantId).IsEqualTo(TenantId);
        await Assert.That(members.Items.Single().OrganizationTenantId).IsEqualTo(Stamp);
        await Assert.That(members.Items.Single().RoleId).IsEqualTo((int)RoleEnum.OrgAdmin);
        await Assert.That(actors.Items.Single().DisplayName).IsEqualTo("New organization");
        await Assert.That(actors.Items.Single().OrganizationId).IsEqualTo(Id);
    }

    [Test]
    public async Task ReviewCreation_KeepsProgramTranslationAndTrustedTenantUserAuditFields()
    {
        var store = new ReviewStore([]);
        var handler = new CreateOrganizationReviewCommandHandler(store, new TenantContext(TenantId));
        var input = JsonSerializer.Deserialize<CreateOrganizationReviewDto>("""
            {
              "organizationId":"01900000-0000-7000-8000-000000000001",
              "programId":"01900000-0000-7000-8000-000000000004",
              "reviewerName":"Submitted reviewer", "rating":4, "comment":"Useful event",
              "tenantId":"01900000-0000-7000-8000-000000000004",
              "userId":"01900000-0000-7000-8000-000000000004", "isDeleted":true
            }
            """, JsonOptions)!;
        var result = await handler.Handle(new CreateOrganizationReviewCommand { CreateOrganizationReviewDto = input, ReviewerUserId = ActorId }, default);
        await Assert.That(result.IsSuccess).IsTrue();
        var review = store.Items.Single();
        await Assert.That(review.OrganizationId).IsEqualTo(Id);
        await Assert.That(review.EventId).IsEqualTo(Stamp);
        await Assert.That(review.ReviewerName).IsEqualTo("Submitted reviewer");
        await Assert.That(review.Rating).IsEqualTo(4);
        await Assert.That(review.Comment).IsEqualTo("Useful event");
        await Assert.That(review.TenantId).IsEqualTo(TenantId);
        await Assert.That(review.UserId).IsEqualTo(ActorId);
        await Assert.That(review.CreatedBy).IsEqualTo(ActorId);
        await Assert.That(review.UpdatedBy).IsEqualTo(ActorId);
        await Assert.That(review.IsDeleted).IsFalse();
        await Assert.That(review.DeletedBy).IsNull();
        var mine = await new GetMyReviewsQueryHandler(store).Handle(new GetMyReviewsQuery(ActorId), default);
        await Assert.That(mine.Single().UserFullName).IsNull();
        await Assert.That(mine.Single().Rating).IsEqualTo(4);
        await Assert.That(await new GetMyReviewsQueryHandler(store).Handle(new GetMyReviewsQuery(TenantId), default)).IsEmpty();
        await AssertFields(mine.Single(), "id", "organizationId", "organizationFullName", "userId", "userFullName", "rating", "comment", "createdAt");
    }

    [Test]
    public async Task ReviewQueries_PreserveOrderedSnapshotsWithoutReviewerOrTenantDisclosure()
    {
        var first = new OrganizationReview
        {
            Id = Id, OrganizationId = Id, Organization = CreateOrganization(), EventId = Stamp, Event = null!,
            UserId = ActorId, User = CreateUser(), ReviewerName = "Private submitted reviewer", Rating = 5,
            Comment = "Public comment", CreatedAt = CreatedAt, TenantId = TenantId, Tenant = null!, CreatedBy = TenantId, IsDeleted = true
        };
        var second = new OrganizationReview
        {
            Id = Stamp, OrganizationId = Id, Organization = null!, Event = null!, UserId = ActorId,
            ReviewerName = "Do not use as fallback", Rating = 1, Tenant = null!, Comment = null
        };
        var store = new ReviewStore([second, first]);
        var items = await new GetOrganizationReviewsQueryHandler(store).Handle(new GetOrganizationReviewsQuery(Id), default);
        await Assert.That(items.Select(item => item.Id).SequenceEqual(new[] { Stamp, Id })).IsTrue();
        await Assert.That(items[0].Comment).IsNull();
        await Assert.That(items[0].UserFullName).IsNull();
        var dto = items[1];
        await Assert.That(dto.OrganizationId).IsEqualTo(Id);
        await Assert.That(dto.OrganizationFullName).IsEqualTo("Community organization");
        await Assert.That(dto.UserId).IsEqualTo(ActorId);
        await Assert.That(dto.UserFullName).IsEqualTo("Member Name");
        await Assert.That(dto.Rating).IsEqualTo(5);
        await Assert.That(dto.Comment).IsEqualTo("Public comment");
        await Assert.That(dto.CreatedAt).IsEqualTo(CreatedAt);
        await AssertFields(dto, "id", "organizationId", "organizationFullName", "userId", "userFullName", "rating", "comment", "createdAt");
        store.Items.Clear();
        first.User!.Pii = null!;
        first.Organization.Pii = null!;
        first.Comment = "Changed";
        var erased = OrganizationMapper.ToOrganizationReview(first);
        await Assert.That(erased.UserFullName).IsNull();
        await Assert.That(erased.OrganizationFullName).IsNull();
        await Assert.That(items[1].Comment).IsEqualTo("Public comment");
        await Assert.That(items[1].UserFullName).IsEqualTo("Member Name");
    }

    [Test]
    public async Task ApprovalStatuses_PreserveOrderedScalarSnapshotsAndNullableDescription()
    {
        var first = new ApprovalStatus { Id = 7, MasterCode = "APPROVED", FullName = "Approved", Description = "Public description" };
        var second = new ApprovalStatus { Id = 2, MasterCode = "PENDING", FullName = "Pending", Description = null };
        var store = new ApprovalStatusStore([first, second]);
        var handler = new Explore.Application.Features.StatusTypes.Handlers.Queries.GetStatusTypeListRequestHandler(store);
        var items = await handler.Handle(new Explore.Application.Features.StatusTypes.Requests.Queries.GetStatusTypeListRequest { FullName = "Not a filter", Id = 999 }, default);
        await Assert.That(items.Select(item => item.Id).SequenceEqual(new[] { 7, 2 })).IsTrue();
        await Assert.That(items[0].MasterCode).IsEqualTo("APPROVED");
        await Assert.That(items[0].FullName).IsEqualTo("Approved");
        await Assert.That(items[0].Description).IsEqualTo("Public description");
        await Assert.That(items[1].MasterCode).IsEqualTo("PENDING");
        await Assert.That(items[1].FullName).IsEqualTo("Pending");
        await Assert.That(items[1].Description).IsNull();
        await AssertFields(items[0], "id", "masterCode", "fullName", "description");
        store.Items.Clear();
        first.FullName = "Changed";
        await Assert.That(items[0].FullName).IsEqualTo("Approved");
        await Assert.That(await handler.Handle(new Explore.Application.Features.StatusTypes.Requests.Queries.GetStatusTypeListRequest { FullName = "" }, default)).IsEmpty();
    }

    internal sealed class ApprovalStatusStore(List<ApprovalStatus> items) : Store<ApprovalStatus, int>, IApprovalStatusRepository
    {
        public List<ApprovalStatus> Items { get; } = items;
        public override Task<IReadOnlyList<ApprovalStatus>> GetAll() => Task.FromResult<IReadOnlyList<ApprovalStatus>>(Items);
    }

    internal sealed class ReviewStore(List<OrganizationReview> items) : Store<OrganizationReview, Guid>, IOrganizationReviewRepository
    {
        public List<OrganizationReview> Items { get; } = items;
        public override Task<OrganizationReview> Create(OrganizationReview entity) { entity.Id = Id; Items.Add(entity); return Task.FromResult(entity); }
        public Task<List<OrganizationReview>> GetByOrganizationId(Guid organizationId) => Task.FromResult(Items.Where(item => item.OrganizationId == organizationId).ToList());
        public Task<List<OrganizationReview>> GetByUserId(Guid userId) => Task.FromResult(Items.Where(item => item.UserId == userId).ToList());
        public Task<bool> HasUserReviewedProgram(Guid userId, Guid programId) => throw new NotSupportedException();
    }

    internal sealed class OrganizationStore(List<Organization> items) : Store<Organization, Guid>, IOrganizationRepository
    {
        public List<Organization> Items { get; } = items;
        public override Task<Organization> Create(Organization entity) { entity.Id = Id; Items.Add(entity); return Task.FromResult(entity); }
        public Task<Organization?> GetOrganizationWithDetails(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));
        public Task<Organization?> GetOrganizationWithDetailsByActorId(Guid actorId, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(item => item.Actor?.Id == actorId));
        public Task<(List<Organization> Items, int TotalCount)> GetOrganizationsWithDetailsPaged(int pageNumber, int pageSize, CancellationToken cancellationToken = default) => Task.FromResult((Items.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), Items.Count));
        public Task<(List<Organization> Items, int TotalCount)> GetMyOrganizationsPaged(Guid userId, int pageNumber, int pageSize, CancellationToken cancellationToken = default) => Task.FromResult((userId == ActorId ? Items.Where(item => item.Id == Id).ToList() : [], userId == ActorId ? 1 : 0));
        public Task<List<Organization>> GetOrganizationsWithDetails(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<Organization>> GetMyOrganizations(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> ForgetPiiAsync(Guid organizationId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed class OrganizationTenantStore : Store<OrganizationTenant, Guid>, IOrganizationTenantRepository
    {
        public List<OrganizationTenant> Items { get; } = [];
        public override Task<OrganizationTenant> Create(OrganizationTenant entity) { entity.Id = Stamp; Items.Add(entity); return Task.FromResult(entity); }
        public Task<OrganizationTenant?> GetByOrganizationAndTenant(Guid organizationId, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(item => item.OrganizationId == organizationId && item.TenantId == tenantId));
    }

    internal sealed class OrganizationMemberStore(List<OrganizationMember> items) : Store<OrganizationMember, Guid>, IOrganizationMemberRepository
    {
        public List<OrganizationMember> Items { get; } = items;
        public override Task<OrganizationMember> Create(OrganizationMember entity) { Items.Add(entity); return Task.FromResult(entity); }
        public Task<OrganizationMember?> GetOrganizationMemberWithDetails(Guid id) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));
        public Task<List<OrganizationMember>> GetMembersByOrganizationId(Guid organizationId) => Task.FromResult(Items.Where(item => item.OrganizationTenant.OrganizationId == organizationId).ToList());
        public Task<List<OrganizationMember>> GetMembershipsByUser(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(Items.Where(item => item.UserId == userId).ToList());
        public Task<List<OrganizationMember>> GetInvitesByEmail(string email) => Task.FromResult(Items.Where(item => item.User?.Pii?.Email == email).ToList());
        public Task<List<User>> GetUsersByOrganization(Guid organizationId) => throw new NotSupportedException();
        public Task<List<Organization>> GetOrganizationsByUser(Guid userId) => throw new NotSupportedException();
        public Task<bool> Exists(Guid organizationId, Guid userId) => throw new NotSupportedException();
        public Task<List<OrganizationMember>> GetOrganizationMembersWithDetails() => throw new NotSupportedException();
        public Task<OrganizationMember?> GetByOrganizationAndUser(Guid organizationId, Guid userId) => throw new NotSupportedException();
        public Task<bool> HasPermissionInOrganization(Guid organizationId, Guid userId, string permissionMasterCode) => throw new NotSupportedException();
        public Task<List<Guid>> GetOrganizationIdsWhereUserHasPermission(Guid userId, string permissionMasterCode, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    internal sealed record CurrentUser(Guid? UserId) : ICurrentUserService
    {
        public bool IsAuthenticated => UserId.HasValue;
    }

    internal sealed class InlineUnitOfWork : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
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
