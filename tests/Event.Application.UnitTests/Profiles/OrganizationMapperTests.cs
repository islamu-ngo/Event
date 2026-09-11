using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Group;
using Explore.Application.Mappings;
using Explore.Domain;
using Explore.Domain.ValueObjects;

namespace Event.Application.UnitTests.Profiles;

public sealed class OrganizationMapperTests
{
    internal static readonly Guid Id = Guid.Parse("01900000-0000-7000-8000-000000000001");
    internal static readonly Guid ActorId = Guid.Parse("01900000-0000-7000-8000-000000000002");
    internal static readonly Guid TenantId = Guid.Parse("01900000-0000-7000-8000-000000000003");
    internal static readonly Guid Stamp = Guid.Parse("01900000-0000-7000-8000-000000000004");
    internal static readonly DateTime CreatedAt = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task GroupDetail_PreservesDisclosureWithoutTraversingTenantCycles()
    {
        var group = CreateGroup();
        var dto = OrganizationMapper.ToGroupDetail(group);
        await Assert.That(dto.Id).IsEqualTo(Id);
        await Assert.That(dto.ConcurrencyStamp).IsEqualTo(Stamp);
        await Assert.That(dto.FullName).IsEqualTo("Community group");
        await Assert.That(dto.Description).IsEqualTo("Public description");
        await Assert.That(dto.ActorId).IsEqualTo(ActorId);
        await Assert.That(dto.ActorDisplayName).IsEqualTo("Public actor");
        await Assert.That(dto.ActorHandle).IsEqualTo("first.example.test");
        await Assert.That(dto.ActorBackgroundColor).IsEqualTo("blue");
        await Assert.That(dto.ActorBackgroundEffect).IsEqualTo("glow");
        await Assert.That(dto.ActorBannerColor).IsEqualTo("green");
        await Assert.That(dto.ActorProfilePictureUri).IsEqualTo("https://images.example.test/public.png");
        await Assert.That(dto.TenantId).IsEqualTo(Guid.Empty);
        await Assert.That(dto.ApprovalStatusId).IsEqualTo(0);
        await Assert.That(dto.TenantFullName).IsNull();
        await Assert.That(dto.ApprovalStatusFullName).IsNull();
        await Assert.That(dto.ApprovalStatusMasterCode).IsNull();
        await Assert.That(dto.ActorProfilePictureId).IsNull();
        await Assert.That(dto.ActorBannerPictureId).IsNull();
        await Assert.That(dto.ActorBannerPictureUri).IsNull();
        await Assert.That(dto.ActorBackgroundImageId).IsNull();
        await Assert.That(dto.ActorBackgroundImageUri).IsNull();
        await AssertFields(dto, "id", "concurrencyStamp", "fullName", "description", "approvalStatusId", "approvalStatusFullName", "approvalStatusMasterCode", "tenantId", "tenantFullName", "actorId", "actorDisplayName", "actorHandle", "actorProfilePictureId", "actorProfilePictureUri", "actorBackgroundColor", "actorBackgroundEffect", "actorBannerColor", "actorBannerPictureId", "actorBannerPictureUri", "actorBackgroundImageId", "actorBackgroundImageUri");
    }

    [Test]
    public async Task GroupList_PreservesPublicScalarsAndLeavesMembershipAuthorityToHandler()
    {
        var group = CreateGroup();
        var dto = OrganizationMapper.ToGroupListItem(group);
        await Assert.That(dto.Id).IsEqualTo(Id);
        await Assert.That(dto.ConcurrencyStamp).IsEqualTo(Stamp);
        await Assert.That(dto.FullName).IsEqualTo("Community group");
        await Assert.That(dto.Description).IsEqualTo("Public description");
        await Assert.That(dto.CreatedAt).IsEqualTo(CreatedAt);
        await Assert.That(dto.TenantId).IsEqualTo(Guid.Empty);
        await Assert.That(dto.ApprovalStatusId).IsEqualTo(0);
        await Assert.That(dto.ApprovalStatusFullName).IsNull();
        await Assert.That(dto.CurrentUserRoleId).IsNull();
        await Assert.That(dto.ActorProfilePictureUri).IsEqualTo("https://images.example.test/public.png");
        await Assert.That(dto.ActorBackgroundColor).IsEqualTo("blue");
        await Assert.That(dto.ActorBackgroundEffect).IsEqualTo("glow");
        await Assert.That(dto.ActorBannerColor).IsEqualTo("green");
        await Assert.That(dto.ActorProfilePictureId).IsNull();
        await Assert.That(dto.ActorBannerPictureId).IsNull();
        await Assert.That(dto.ActorBannerPictureUri).IsNull();
        await Assert.That(dto.ActorBackgroundImageId).IsNull();
        await Assert.That(dto.ActorBackgroundImageUri).IsNull();
        await AssertFields(dto, "id", "concurrencyStamp", "tenantId", "fullName", "description", "approvalStatusId", "approvalStatusFullName", "createdAt", "currentUserRoleId", "actorProfilePictureId", "actorProfilePictureUri", "actorBackgroundColor", "actorBackgroundEffect", "actorBannerColor", "actorBannerPictureId", "actorBannerPictureUri", "actorBackgroundImageId", "actorBackgroundImageUri");
    }

    [Test]
    [Arguments("actor")]
    [Arguments("pii")]
    [Arguments("identities")]
    [Arguments("first-handle")]
    public async Task GroupDetail_PreservesAbsentOptionalProfileValues(string missing)
    {
        var source = CreateGroup();
        if (missing == "actor") source.Actor = null;
        if (missing == "pii") source.Actor!.Pii = null!;
        if (missing == "identities") source.Actor!.AtprotoIdentities.Clear();
        if (missing == "first-handle") source.Actor!.AtprotoIdentities.First().Handle = null;
        var dto = OrganizationMapper.ToGroupDetail(source);
        await Assert.That(dto.FullName).IsEqualTo("Community group");
        if (missing is "actor" or "pii")
        {
            await Assert.That(dto.ActorDisplayName).IsNull();
            await Assert.That(dto.ActorProfilePictureUri).IsNull();
        }
        if (missing != "pii") await Assert.That(dto.ActorHandle).IsNull();
    }

    internal static Group CreateGroup()
    {
        var group = new Group
        {
            Id = Id, FullName = "Community group", Description = "Public description", ConcurrencyStamp = Stamp,
            CreatedAt = CreatedAt, CreatedBy = TenantId, UpdatedBy = TenantId, IsDeleted = true, DeletedBy = TenantId,
            Actor = CreateActor()
        };
        group.Actor.Group = group;
        group.TenantParticipations.Add(new GroupTenant
        {
            TenantId = TenantId, Tenant = null!, GroupId = Id, Group = group, ApprovalStatusId = 7, ApprovalStatus = null!,
            DisplayNameOverride = "Private tenant display", ProfilePictureId = Stamp
        });
        return group;
    }

    internal static Actor CreateActor()
    {
        var actor = new Actor
        {
            Id = ActorId, ActorType = null!, Pii = new ActorPii { DisplayName = "Public actor", ProfilePictureUri = "https://images.example.test/public.png" },
            BackgroundColor = "blue", BackgroundEffect = "glow", BannerColor = "green", CreatedBy = TenantId,
            Description = "Do not disclose actor description", ModerationReasonCode = "private reason"
        };
        actor.Pii.Actor = actor;
        actor.AtprotoIdentities =
        [
            new AtprotoIdentity(AtprotoDid.Parse("did:plc:first")) { Actor = actor, Handle = "first.example.test" },
            new AtprotoIdentity(AtprotoDid.Parse("did:plc:second")) { Actor = actor, Handle = "second.example.test" }
        ];
        return actor;
    }

    [Test]
    public async Task GroupMembers_PreserveBoundedDisclosureOrderAndOptionalRelations()
    {
        var member = new GroupMember
        {
            Id = Id, GroupTenantId = Stamp, GroupTenant = CreateGroup().TenantParticipations.Single(),
            UserId = ActorId, User = CreateUser(), RoleId = 7,
            Role = new Role { Id = 7, MasterCode = "PRIVATE_ROLE_CODE", FullName = "Moderator" },
            GroupPositionId = 4, GroupPosition = new GroupPosition { Id = 4, FullName = "Coordinator", MasterCode = "PRIVATE_POSITION_CODE" },
            TenantId = TenantId, Tenant = null!, CreatedBy = TenantId, IsDeleted = true
        };
        var store = new OrganizationMappingHandlerTests.GroupMemberStore([member]);
        var detail = new Explore.Application.Features.GroupMembers.Handlers.Queries.GetGroupMemberDetailsRequestHandler(store);
        var list = new Explore.Application.Features.GroupMembers.Handlers.Queries.GetGroupMembersRequestHandler(store);
        var dto = (await detail.Handle(new Explore.Application.Features.GroupMembers.Requests.Queries.GetGroupMemberDetailsRequest { Id = Id }, default))!;
        await Assert.That(dto.Id).IsEqualTo(Id);
        // The old profile did not infer GroupId across GroupTenant. Do not invent an authority change here.
        await Assert.That(dto.GroupId).IsEqualTo(Guid.Empty);
        await Assert.That(dto.GroupFullName).IsEqualTo("Community group");
        await Assert.That(dto.UserId).IsEqualTo(ActorId);
        await Assert.That(dto.UserEmail).IsEqualTo("member@example.test");
        await Assert.That(dto.UserFullName).IsEqualTo("Member Name");
        await Assert.That(dto.RoleId).IsEqualTo(7);
        await Assert.That(dto.RoleName).IsEqualTo("Moderator");
        await Assert.That(dto.GroupPositionId).IsEqualTo(4);
        await Assert.That(dto.GroupPositionFullName).IsEqualTo("Coordinator");
        await AssertFields(dto, "id", "groupId", "groupFullName", "userId", "userEmail", "userFullName", "roleId", "roleName", "groupPositionId", "groupPositionFullName");
        var items = await list.Handle(new Explore.Application.Features.GroupMembers.Requests.Queries.GetGroupMembersRequest { GroupId = Id }, default);
        await Assert.That(items.Single()).IsEqualTo(dto);
        await Assert.That(await detail.Handle(new Explore.Application.Features.GroupMembers.Requests.Queries.GetGroupMemberDetailsRequest { Id = TenantId }, default)).IsNull();
        store.Items.Clear();
        member.User.Pii = null!;
        member.GroupTenant = null!;
        member.Role = null!;
        member.GroupPosition = null;
        var erased = OrganizationMapper.ToGroupMember(member);
        await Assert.That(erased.GroupFullName).IsNull();
        await Assert.That(erased.UserEmail).IsNull();
        await Assert.That(erased.UserFullName).IsNull();
        await Assert.That(erased.RoleName).IsNull();
        await Assert.That(erased.GroupPositionFullName).IsNull();
        await Assert.That(items.Single().UserFullName).IsEqualTo("Member Name");
        member.User = null!;
        await Assert.That(OrganizationMapper.ToGroupMember(member).UserFullName).IsNull();
    }

    internal static User CreateUser() => new()
    {
        Id = ActorId, Pii = new UserPii { Email = "member@example.test", FirstName = "Member", LastName = "Name" },
        LastActiveTenantId = TenantId, CreatedBy = TenantId
    };

    internal static async Task AssertFields<T>(T dto, params string[] fields)
    {
        var json = JsonSerializer.SerializeToNode(dto, JsonOptions)!.AsObject();
        await Assert.That(json.Select(property => property.Key).Order().SequenceEqual(fields.Order())).IsTrue();
    }
}
