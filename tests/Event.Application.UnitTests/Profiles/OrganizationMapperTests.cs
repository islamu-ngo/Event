using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Group;
using Explore.Application.DTOs.Organization;
using Explore.Application.DTOs.OrganizationMember;
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
    [Arguments(typeof(OrganizationDto))]
    [Arguments(typeof(OrganizationListDto))]
    public async Task ContactLabels_DeclareTheirExistingNullableOutputContract(Type contract)
    {
        string[] labels =
        [
            nameof(OrganizationDto.FullName), nameof(OrganizationDto.Email),
            nameof(OrganizationDto.Country), nameof(OrganizationDto.City),
            nameof(OrganizationDto.Postcode), nameof(OrganizationDto.Address)
        ];
        var nullability = new NullabilityInfoContext();
        foreach (var label in labels)
        {
            await Assert.That(nullability.Create(contract.GetProperty(label)!).ReadState)
                .IsEqualTo(NullabilityState.Nullable);
        }
    }

    [Test]
    [Arguments(typeof(OrganizationInvitationDto), nameof(OrganizationInvitationDto.OrganizationName))]
    [Arguments(typeof(OrganizationInvitationDto), nameof(OrganizationInvitationDto.Email))]
    [Arguments(typeof(OrganizationListDto), nameof(OrganizationListDto.ApprovalStatusFullName))]
    [Arguments(typeof(OrganizationListDto), nameof(OrganizationListDto.StatusTypeFullName))]
    [Arguments(typeof(GroupListDto), nameof(GroupListDto.ApprovalStatusFullName))]
    public async Task OptionalLabels_DeclareTheirExistingNullableOutputContract(Type contract, string label)
    {
        await Assert.That(new NullabilityInfoContext().Create(contract.GetProperty(label)!).ReadState)
            .IsEqualTo(NullabilityState.Nullable);
    }

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
        var dto = (await detail.QueryAsync(new Explore.Application.Features.GroupMembers.Requests.Queries.GetGroupMemberDetailsRequest { Id = Id }, default))!;
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
        var items = await list.QueryAsync(new Explore.Application.Features.GroupMembers.Requests.Queries.GetGroupMembersRequest { GroupId = Id }, default);
        await Assert.That(items.Single()).IsEqualTo(dto);
        await Assert.That(await detail.QueryAsync(new Explore.Application.Features.GroupMembers.Requests.Queries.GetGroupMemberDetailsRequest { Id = TenantId }, default)).IsNull();
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

    [Test]
    public async Task OrganizationProjections_PreserveContactAppearanceAndUnresolvedTenantMetadata()
    {
        var source = CreateOrganization();
        var detail = OrganizationMapper.ToOrganizationDetail(source);
        var list = OrganizationMapper.ToOrganizationListItem(source);
        foreach (var dto in new JsonObject[]
        {
            JsonSerializer.SerializeToNode(detail, JsonOptions)!.AsObject(),
            JsonSerializer.SerializeToNode(list, JsonOptions)!.AsObject()
        })
        {
            await Assert.That(dto["id"]!.GetValue<Guid>()).IsEqualTo(Id);
            await Assert.That(dto["concurrencyStamp"]!.GetValue<Guid>()).IsEqualTo(Stamp);
            await Assert.That(dto["fullName"]!.GetValue<string>()).IsEqualTo("Community organization");
            await Assert.That(dto["email"]!.GetValue<string>()).IsEqualTo("office@example.test");
            await Assert.That(dto["websiteUrl"]!.GetValue<string>()).IsEqualTo("https://community.example.test");
            await Assert.That(dto["country"]!.GetValue<string>()).IsEqualTo("BE");
            await Assert.That(dto["city"]!.GetValue<string>()).IsEqualTo("Brussels");
            await Assert.That(dto["postcode"]!.GetValue<string>()).IsEqualTo("1000");
            await Assert.That(dto["address"]!.GetValue<string>()).IsEqualTo("Square 1");
            await Assert.That(dto["tenantId"]!.GetValue<Guid>()).IsEqualTo(Guid.Empty);
            await Assert.That(dto["approvalStatusId"]!.GetValue<int>()).IsEqualTo(0);
            await Assert.That(dto["approvalStatusFullName"]).IsNull();
            await Assert.That(dto["actorProfilePictureUri"]!.GetValue<string>()).IsEqualTo("https://images.example.test/public.png");
            await Assert.That(dto["actorBackgroundColor"]!.GetValue<string>()).IsEqualTo("blue");
            await Assert.That(dto["actorBackgroundEffect"]!.GetValue<string>()).IsEqualTo("glow");
            await Assert.That(dto["actorBannerColor"]!.GetValue<string>()).IsEqualTo("green");
            foreach (var field in new[] { "actorProfilePictureId", "actorBannerPictureId", "actorBannerPictureUri", "actorBackgroundImageId", "actorBackgroundImageUri" })
                await Assert.That(dto[field]).IsNull();
        }
        await Assert.That(detail.ActorId).IsEqualTo(ActorId);
        await Assert.That(detail.ActorDisplayName).IsEqualTo("Public actor");
        await Assert.That(detail.ActorHandle).IsEqualTo("first.example.test");
        await Assert.That(detail.TenantFullName).IsNull();
        await Assert.That(detail.ApprovalStatusMasterCode).IsNull();
        await Assert.That(list.CreatedAt).IsEqualTo(CreatedAt);
        await Assert.That(list.CurrentUserRoleId).IsNull();
        await Assert.That(list.StatusTypeFullName).IsNull();
        await AssertFields(detail, "id", "concurrencyStamp", "fullName", "websiteUrl", "email", "country", "city", "postcode", "address", "approvalStatusId", "approvalStatusFullName", "approvalStatusMasterCode", "tenantId", "tenantFullName", "actorId", "actorDisplayName", "actorHandle", "actorProfilePictureId", "actorProfilePictureUri", "actorBackgroundColor", "actorBackgroundEffect", "actorBannerColor", "actorBannerPictureId", "actorBannerPictureUri", "actorBackgroundImageId", "actorBackgroundImageUri");
        await AssertFields(list, "id", "concurrencyStamp", "tenantId", "fullName", "websiteUrl", "email", "country", "city", "postcode", "address", "approvalStatusId", "approvalStatusFullName", "statusTypeFullName", "createdAt", "currentUserRoleId", "actorProfilePictureId", "actorProfilePictureUri", "actorBackgroundColor", "actorBackgroundEffect", "actorBannerColor", "actorBannerPictureId", "actorBannerPictureUri", "actorBackgroundImageId", "actorBackgroundImageUri");
        source.Pii = null!;
        source.Actor!.Pii = null!;
        source.Actor.AtprotoIdentities.First().Handle = null;
        var erased = OrganizationMapper.ToOrganizationDetail(source);
        await Assert.That(erased.FullName).IsNull();
        await Assert.That(erased.Email).IsNull();
        await Assert.That(erased.Country).IsNull();
        await Assert.That(erased.City).IsNull();
        await Assert.That(erased.Postcode).IsNull();
        await Assert.That(erased.Address).IsNull();
        await Assert.That(erased.ActorDisplayName).IsNull();
        await Assert.That(erased.ActorProfilePictureUri).IsNull();
        await Assert.That(erased.ActorHandle).IsNull();
        source.Actor = null;
        await Assert.That(OrganizationMapper.ToOrganizationDetail(source).ActorId).IsNull();
        await Assert.That(OrganizationMapper.ToOrganizationListItem(source).ActorBackgroundColor).IsNull();
    }

    internal static Organization CreateOrganization()
    {
        var organization = new Organization
        {
            Id = Id, ConcurrencyStamp = Stamp, CreatedAt = CreatedAt, CreatedBy = TenantId, IsDeleted = true,
            WebsiteUrl = "https://community.example.test", Actor = CreateActor(),
            Pii = new OrganizationPii { FullName = "Community organization", Email = "office@example.test", Country = "BE", City = "Brussels", Postcode = "1000", Address = "Square 1" }
        };
        organization.Actor.Organization = organization;
        organization.TenantParticipations.Add(new OrganizationTenant
        {
            Id = Stamp, OrganizationId = Id, Organization = organization, TenantId = TenantId, Tenant = null!, ApprovalStatusId = 7, ApprovalStatus = null!,
            ContactEmailOverride = "private-tenant@example.test", DisplayNameOverride = "Private tenant display", ProfilePictureId = Stamp
        });
        return organization;
    }

    [Test]
    public async Task OrganizationMembersAndInvitations_KeepDistinctDisclosureAndParticipationAuthority()
    {
        var member = new OrganizationMember
        {
            Id = ActorId, OrganizationTenantId = Stamp, OrganizationTenant = CreateOrganization().TenantParticipations.Single(),
            UserId = ActorId, User = CreateUser(), RoleId = 7, Role = new Role { FullName = "Administrator", MasterCode = "PRIVATE_ROLE_CODE" },
            OrganizationPositionId = 4, OrganizationPosition = new OrganizationPosition { FullName = "Coordinator", MasterCode = "PRIVATE_POSITION_CODE" },
            TenantId = TenantId, Tenant = null!, CreatedBy = Stamp, IsDeleted = true
        };
        var store = new OrganizationMappingHandlerTests.OrganizationMemberStore([member]);
        var detail = new Explore.Application.Features.OrganizationMembers.Handlers.Queries.GetOrganizationMemberDetailsRequestHandler(store);
        var list = new Explore.Application.Features.OrganizationMembers.Handlers.Queries.GetOrganizationMembersRequestHandler(store);
        var invites = new Explore.Application.Features.OrganizationMembers.Handlers.Queries.GetMyInvitationsRequestHandler(store);
        var dto = (await detail.Handle(new Explore.Application.Features.OrganizationMembers.Requests.Queries.GetOrganizationMemberDetailsRequest { Id = ActorId }, default))!;
        await Assert.That(dto.Id).IsEqualTo(ActorId);
        await Assert.That(dto.TenantId).IsEqualTo(TenantId);
        await Assert.That(dto.OrganizationId).IsEqualTo(Guid.Empty);
        await Assert.That(dto.OrganizationFullName).IsEqualTo("Community organization");
        await Assert.That(dto.UserId).IsEqualTo(ActorId);
        await Assert.That(dto.UserEmail).IsEqualTo("member@example.test");
        await Assert.That(dto.UserFullName).IsEqualTo("Member Name");
        await Assert.That(dto.RoleId).IsEqualTo(7);
        await Assert.That(dto.RoleName).IsEqualTo("Administrator");
        await Assert.That(dto.OrganizationPositionId).IsEqualTo(4);
        await Assert.That(dto.OrganizationPositionFullName).IsEqualTo("Coordinator");
        await AssertFields(dto, "id", "tenantId", "organizationId", "organizationFullName", "userId", "userEmail", "userFullName", "roleId", "roleName", "organizationPositionId", "organizationPositionFullName");
        var items = await list.Handle(new Explore.Application.Features.OrganizationMembers.Requests.Queries.GetOrganizationMembersRequest { OrganizationId = Id }, default);
        await Assert.That(items.Single()).IsEqualTo(dto);
        await Assert.That(await detail.Handle(new Explore.Application.Features.OrganizationMembers.Requests.Queries.GetOrganizationMemberDetailsRequest { Id = TenantId }, default)).IsNull();
        var invitations = await invites.Handle(new Explore.Application.Features.OrganizationMembers.Requests.Queries.GetMyInvitationsRequest { Email = "member@example.test" }, default);
        var invitation = invitations.Single();
        await Assert.That(invitation.Id).IsEqualTo(ActorId);
        await Assert.That(invitation.OrganizationId).IsEqualTo(Id);
        await Assert.That(invitation.OrganizationName).IsEqualTo("Community organization");
        await Assert.That((int)invitation.Role).IsEqualTo(7);
        await Assert.That(invitation.Email).IsEqualTo("member@example.test");
        await AssertFields(invitation, "id", "organizationId", "organizationName", "role", "email");
        await Assert.That(await invites.Handle(new Explore.Application.Features.OrganizationMembers.Requests.Queries.GetMyInvitationsRequest { Email = "other@example.test" }, default)).IsEmpty();
        store.Items.Clear();
        member.OrganizationTenant.Organization.Pii = null!;
        member.User.Pii = null!;
        member.Role = null!;
        member.OrganizationPosition = null;
        member.RoleId = 999;
        var erased = OrganizationMapper.ToOrganizationMember(member);
        await Assert.That(erased.OrganizationFullName).IsNull();
        await Assert.That(erased.UserEmail).IsNull();
        await Assert.That(erased.UserFullName).IsNull();
        await Assert.That(erased.RoleName).IsNull();
        await Assert.That(erased.OrganizationPositionFullName).IsNull();
        await Assert.That(OrganizationMapper.ToOrganizationInvitation(member).Email).IsNull();
        await Assert.That(OrganizationMapper.ToOrganizationInvitation(member).OrganizationName).IsNull();
        await Assert.That((int)OrganizationMapper.ToOrganizationInvitation(member).Role).IsEqualTo(999);
        member.OrganizationTenant = null!;
        member.User = null!;
        await Assert.That(OrganizationMapper.ToOrganizationInvitation(member).OrganizationId).IsEqualTo(Guid.Empty);
        await Assert.That(OrganizationMapper.ToOrganizationMember(member).UserFullName).IsNull();
        await Assert.That(items.Single().UserFullName).IsEqualTo("Member Name");
        await Assert.That(invitations.Single().OrganizationName).IsEqualTo("Community organization");
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
