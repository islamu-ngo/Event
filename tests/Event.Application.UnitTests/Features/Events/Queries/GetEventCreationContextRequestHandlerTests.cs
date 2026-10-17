using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Application.Features.Events.Handlers.Queries;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Events.Queries;

public class GetEventCreationContextRequestHandlerTests
{
    private readonly IUserContext _userContext;
    private readonly ITenantContext _tenantContext;
    private readonly IHierarchicalSettingsReader _settings;
    private readonly IHumanEventCreationPublisherReader _publishers;
    private readonly GetEventCreationContextRequestHandler _handler;

    public GetEventCreationContextRequestHandlerTests()
    {
        _userContext = Substitute.For<IUserContext>();
        _tenantContext = Substitute.For<ITenantContext>();
        _settings = Substitute.For<IHierarchicalSettingsReader>();
        _publishers = Substitute.For<IHumanEventCreationPublisherReader>();

        _handler = new GetEventCreationContextRequestHandler(
            _userContext,
            _tenantContext,
            _settings,
            _publishers);
    }

    [Test]
    public async Task Handle_WhenPersonalPublishingAllowed_ReturnsPersonalDefault()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _userContext.GetRequiredUserId().Returns(userId);
        _tenantContext.TenantId.Returns(tenantId);
        SetPolicy(tenantId, personal: true, organization: false, group: false, approval: true);

        // Act
        var result = await _handler.QueryAsync(new GetEventCreationContextRequest(), CancellationToken.None);

        // Assert
        await Assert.That(result.CanCreate).IsTrue();
        await Assert.That(result.RequiresApproval).IsTrue();
        await Assert.That(result.DefaultPublisherMode).IsEqualTo("personal");
        await Assert.That(result.PublisherOptions.Count).IsEqualTo(1);
        await Assert.That(result.PublisherOptions[0].CanPublish).IsTrue();
        await Assert.That(result.PublisherOptions[0].DisplayName).IsEqualTo("Personal profile");
    }

    [Test]
    public async Task QueryAsync_WhenOrganizationAndGroupPublishingAllowed_ReturnsPermissionBackedOptions()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var allowedOrganizationId = Guid.NewGuid();
        var blockedOrganizationId = Guid.NewGuid();
        var allowedGroupId = Guid.NewGuid();
        var blockedGroupId = Guid.NewGuid();

        _userContext.GetRequiredUserId().Returns(userId);
        _tenantContext.TenantId.Returns(tenantId);
        SetPolicy(tenantId, personal: false, organization: true, group: true, approval: false);

        _publishers.ReadAsync(tenantId, userId, Arg.Any<CancellationToken>()).Returns((
            (IReadOnlyList<OrganizationMember>)new List<OrganizationMember>
        {
            CreateOrganizationMembership(blockedOrganizationId, "A blocked org", roleId: 24),
            CreateOrganizationMembership(allowedOrganizationId, "Z allowed org", roleId: 22)
        }, (IReadOnlyList<GroupMember>)new List<GroupMember>
        {
            CreateGroupMembership(blockedGroupId, "A blocked group", roleId: 34),
            CreateGroupMembership(allowedGroupId, "Allowed Group", roleId: 31)
        }, (IReadOnlyList<RolePermission>)new List<RolePermission>
        {
            CreatePermission(22, active: true),
            CreatePermission(31, active: true),
            CreatePermission(24, active: false)
        }));

        // Act
        var result = await _handler.QueryAsync(new GetEventCreationContextRequest(), CancellationToken.None);

        // Assert
        await Assert.That(result.CanCreate).IsTrue();
        await Assert.That(result.DefaultPublisherMode).IsEqualTo("organization");
        await Assert.That(result.PublisherOptions.Count).IsEqualTo(4);
        await Assert.That(result.PublisherOptions.Select(option => option.PublisherId).ToArray())
            .IsEquivalentTo(new Guid?[] { allowedOrganizationId, blockedOrganizationId, allowedGroupId, blockedGroupId });
        await Assert.That(result.PublisherOptions[0].PublisherId).IsEqualTo((Guid?)allowedOrganizationId);
        await Assert.That(result.PublisherOptions[1].PublisherId).IsEqualTo((Guid?)blockedOrganizationId);
        await Assert.That(result.PublisherOptions[2].PublisherId).IsEqualTo((Guid?)allowedGroupId);
        await Assert.That(result.PublisherOptions[3].PublisherId).IsEqualTo((Guid?)blockedGroupId);

        var allowedOrgOption = result.PublisherOptions.Single(option => option.PublisherId == allowedOrganizationId);
        await Assert.That(allowedOrgOption.PublisherMode).IsEqualTo("organization");
        await Assert.That(allowedOrgOption.CanPublish).IsTrue();

        var blockedOrgOption = result.PublisherOptions.Single(option => option.PublisherId == blockedOrganizationId);
        await Assert.That(blockedOrgOption.CanPublish).IsFalse();
        await Assert.That(blockedOrgOption.DisplayName).IsEqualTo("A blocked org");
        await Assert.That(blockedOrgOption.RoleId).IsEqualTo((int?)24);
        await Assert.That(blockedOrgOption.Reason).IsEqualTo("Your organization role cannot create events.");

        var groupOption = result.PublisherOptions.Single(option => option.PublisherId == allowedGroupId);
        await Assert.That(groupOption.PublisherMode).IsEqualTo("group");
        await Assert.That(groupOption.CanPublish).IsTrue();
        var blockedGroupOption = result.PublisherOptions.Single(option => option.PublisherId == blockedGroupId);
        await Assert.That(blockedGroupOption.CanPublish).IsFalse();
        await Assert.That(blockedGroupOption.RoleId).IsEqualTo((int?)34);
        await Assert.That(blockedGroupOption.Reason).IsEqualTo("Your group role cannot create events.");
    }

    [Test]
    public async Task QueryAsync_WhenAllMembershipsLackPermission_KeepsOptionsWithoutDefaultPublisher()
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        _userContext.GetRequiredUserId().Returns(user);
        _tenantContext.TenantId.Returns(tenant);
        SetPolicy(tenant, personal: false, organization: true, group: true, approval: true);
        _publishers.ReadAsync(tenant, user, Arg.Any<CancellationToken>()).Returns((
            (IReadOnlyList<OrganizationMember>)new[] { CreateOrganizationMembership(Guid.NewGuid(), "Organization", 22) },
            (IReadOnlyList<GroupMember>)new[] { CreateGroupMembership(Guid.NewGuid(), "Group", 31) },
            (IReadOnlyList<RolePermission>)Array.Empty<RolePermission>()));

        var result = await _handler.QueryAsync(new GetEventCreationContextRequest(), CancellationToken.None);

        await Assert.That(result.PublisherOptions.Count).IsEqualTo(2);
        await Assert.That(result.PublisherOptions.All(option => !option.CanPublish && option.Reason is not null)).IsTrue();
        await Assert.That(result.CanCreate).IsFalse();
        await Assert.That(result.DefaultPublisherMode).IsNull();
        await Assert.That(result.UnavailableReason).IsNotNull();
    }

    [Test]
    public async Task QueryAsync_OrdersEachPublisherKindByCapabilityThenCaseInsensitiveName()
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        _userContext.GetRequiredUserId().Returns(user);
        _tenantContext.TenantId.Returns(tenant);
        SetPolicy(tenant, personal: true, organization: true, group: true, approval: false);
        var organizations = new[]
        {
            CreateOrganizationMembership(Guid.NewGuid(), "charlie", 24),
            CreateOrganizationMembership(Guid.NewGuid(), "Zulu", 22),
            CreateOrganizationMembership(Guid.NewGuid(), "Bravo", 24),
            CreateOrganizationMembership(Guid.NewGuid(), "alpha", 22)
        };
        var groups = new[]
        {
            CreateGroupMembership(Guid.NewGuid(), "charlie", 34),
            CreateGroupMembership(Guid.NewGuid(), "Zulu", 31),
            CreateGroupMembership(Guid.NewGuid(), "Bravo", 34),
            CreateGroupMembership(Guid.NewGuid(), "alpha", 31)
        };
        _publishers.ReadAsync(tenant, user, Arg.Any<CancellationToken>()).Returns((
            (IReadOnlyList<OrganizationMember>)organizations, (IReadOnlyList<GroupMember>)groups,
            (IReadOnlyList<RolePermission>)new[] { CreatePermission(22, true), CreatePermission(31, true) }));

        var result = await _handler.QueryAsync(new GetEventCreationContextRequest(), CancellationToken.None);

        await Assert.That(result.DefaultPublisherMode).IsEqualTo("personal");
        await Assert.That(result.PublisherOptions.Select(option => option.DisplayName).SequenceEqual(
            new[] { "Personal profile", "alpha", "Zulu", "Bravo", "charlie", "alpha", "Zulu", "Bravo", "charlie" })).IsTrue();
        await Assert.That(result.PublisherOptions.Select(option => option.PublisherMode).SequenceEqual(
            new[] { "personal", "organization", "organization", "organization", "organization", "group", "group", "group", "group" })).IsTrue();
    }

    [Test]
    public async Task QueryAsync_WhenNoPublishersAllowed_ReturnsUnavailableReason()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _userContext.GetRequiredUserId().Returns(userId);
        _tenantContext.TenantId.Returns(tenantId);
        SetPolicy(tenantId, personal: false, organization: false, group: false, approval: false);

        // Act
        var result = await _handler.QueryAsync(new GetEventCreationContextRequest(), CancellationToken.None);

        // Assert
        await Assert.That(result.CanCreate).IsFalse();
        await Assert.That(result.DefaultPublisherMode).IsNull();
        await Assert.That(result.UnavailableReason).IsEqualTo("No available publisher can create events for the current user.");
        await Assert.That(result.PublisherOptions.Count).IsEqualTo(0);
    }

    private void SetPolicy(Guid tenantId, bool personal, bool organization, bool group, bool approval)
    {
        foreach (var (key, value) in new[]
        {
            (GovernanceSettingKeys.Events.UserSubmissionEnabled, personal),
            (GovernanceSettingKeys.Events.OrganizationSubmissionEnabled, organization),
            (GovernanceSettingKeys.Events.GroupSubmissionEnabled, group),
            (GovernanceSettingKeys.Events.RequireApproval, approval)
        })
            _settings.ResolveAsync<bool>(key, Arg.Is<SettingContext>(context =>
                context != null && context.TenantId == tenantId && context.OrganizationId == null && context.GroupId == null
                && context.UserId == null && context.RequireAuthoritativeReads), Arg.Any<CancellationToken>())
                .Returns(value);
    }

    private static RolePermission CreatePermission(int roleId, bool active) => new()
    {
        RoleId = roleId, Role = null!, PermissionId = 1,
        Permission = new Permission
        {
            Id = 1, MasterCode = PermissionCodes.EventCreate, IsActive = active,
            ResourceKind = "event", Action = "create", FullName = "Create event", GroupName = "Events"
        }
    };

    private static OrganizationMember CreateOrganizationMembership(Guid organizationId, string organizationName, int roleId)
    {
        Guid tenantId = Guid.NewGuid();
        return new OrganizationMember
        {
            Id = Guid.NewGuid(),
            OrganizationTenantId = Guid.NewGuid(),
            OrganizationTenant = new OrganizationTenant
            {
                TenantId = tenantId,
                Tenant = null!,
                ApprovalStatus = null!,
                OrganizationId = organizationId,
                Organization = new Organization
                {
                    Id = organizationId,
                    Pii = new OrganizationPii { FullName = organizationName }
                }
            },
            User = null!,
            RoleId = roleId,
            Role = null!,
            TenantId = tenantId,
            Tenant = null!
        };
    }

    private static GroupMember CreateGroupMembership(Guid groupId, string groupName, int roleId)
    {
        Guid tenantId = Guid.NewGuid();
        return new GroupMember
        {
            Id = Guid.NewGuid(),
            GroupTenantId = Guid.NewGuid(),
            GroupTenant = new GroupTenant
            {
                TenantId = tenantId,
                Tenant = null!,
                ApprovalStatus = null!,
                GroupId = groupId,
                Group = new Group
                {
                    Id = groupId,
                    FullName = groupName
                }
            },
            User = null!,
            RoleId = roleId,
            Role = null!,
            TenantId = tenantId,
            Tenant = null!
        };
    }
}
