using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.Events.Handlers.Queries;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.Events.Queries;

public class GetEventCreationContextRequestHandlerTests
{
    private readonly IUserContext _userContext;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantPolicySettingService _tenantPolicySettingService;
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly GetEventCreationContextRequestHandler _handler;

    public GetEventCreationContextRequestHandlerTests()
    {
        _userContext = Substitute.For<IUserContext>();
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantPolicySettingService = Substitute.For<ITenantPolicySettingService>();
        _organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();
        _groupMemberRepository = Substitute.For<IGroupMemberRepository>();

        _handler = new GetEventCreationContextRequestHandler(
            _userContext,
            _tenantContext,
            _tenantPolicySettingService,
            _organizationMemberRepository,
            _groupMemberRepository);
    }

    [Test]
    public async Task Handle_WhenPersonalPublishingAllowed_ReturnsPersonalDefault()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _userContext.GetRequiredUserId().Returns(userId);
        _tenantContext.TenantId.Returns(tenantId);
        _tenantPolicySettingService.ReadEffectiveTenantSettingsAsync(tenantId).Returns(new TenantPolicySettingsDto
        {
            AllowUserSubmittedEvents = true,
            AllowOrganizationSubmittedEvents = false,
            AllowGroupSubmittedEvents = false,
            RequireEventApproval = true
        });

        // Act
        var result = await _handler.Handle(new GetEventCreationContextRequest(), CancellationToken.None);

        // Assert
        await Assert.That(result.CanCreate).IsTrue();
        await Assert.That(result.RequiresApproval).IsTrue();
        await Assert.That(result.DefaultPublisherMode).IsEqualTo("personal");
        await Assert.That(result.PublisherOptions.Count).IsEqualTo(1);
        await Assert.That(result.PublisherOptions[0].CanPublish).IsTrue();
        await Assert.That(result.PublisherOptions[0].DisplayName).IsEqualTo("Personal profile");
    }

    [Test]
    public async Task Handle_WhenOrganizationAndGroupPublishingAllowed_ReturnsPermissionBackedOptions()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var allowedOrganizationId = Guid.NewGuid();
        var blockedOrganizationId = Guid.NewGuid();
        var allowedGroupId = Guid.NewGuid();

        _userContext.GetRequiredUserId().Returns(userId);
        _tenantContext.TenantId.Returns(tenantId);
        _tenantPolicySettingService.ReadEffectiveTenantSettingsAsync(tenantId).Returns(new TenantPolicySettingsDto
        {
            AllowUserSubmittedEvents = false,
            AllowOrganizationSubmittedEvents = true,
            AllowGroupSubmittedEvents = true
        });

        _organizationMemberRepository.GetMembershipsByUser(userId).Returns(new List<OrganizationMember>
        {
            CreateOrganizationMembership(allowedOrganizationId, "Allowed Org", roleId: 22),
            CreateOrganizationMembership(blockedOrganizationId, "Blocked Org", roleId: 24)
        });
        _organizationMemberRepository.GetOrganizationIdsWhereUserHasPermission(userId, PermissionCodes.EventCreate)
            .Returns([allowedOrganizationId]);

        _groupMemberRepository.GetMembershipsByUser(userId).Returns(new List<GroupMember>
        {
            CreateGroupMembership(allowedGroupId, "Allowed Group", roleId: 31)
        });
        _groupMemberRepository.GetGroupIdsWhereUserHasPermission(userId, PermissionCodes.EventCreate)
            .Returns([allowedGroupId]);

        // Act
        var result = await _handler.Handle(new GetEventCreationContextRequest(), CancellationToken.None);

        // Assert
        await Assert.That(result.CanCreate).IsTrue();
        await Assert.That(result.DefaultPublisherMode).IsEqualTo("organization");
        await Assert.That(result.PublisherOptions.Count).IsEqualTo(3);

        var allowedOrgOption = result.PublisherOptions.Single(option => option.PublisherId == allowedOrganizationId);
        await Assert.That(allowedOrgOption.PublisherMode).IsEqualTo("organization");
        await Assert.That(allowedOrgOption.CanPublish).IsTrue();

        var blockedOrgOption = result.PublisherOptions.Single(option => option.PublisherId == blockedOrganizationId);
        await Assert.That(blockedOrgOption.CanPublish).IsFalse();
        await Assert.That(blockedOrgOption.Reason).IsEqualTo("Your organization role cannot create events.");

        var groupOption = result.PublisherOptions.Single(option => option.PublisherId == allowedGroupId);
        await Assert.That(groupOption.PublisherMode).IsEqualTo("group");
        await Assert.That(groupOption.CanPublish).IsTrue();
    }

    [Test]
    public async Task Handle_WhenNoPublishersAllowed_ReturnsUnavailableReason()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        _userContext.GetRequiredUserId().Returns(userId);
        _tenantContext.TenantId.Returns(tenantId);
        _tenantPolicySettingService.ReadEffectiveTenantSettingsAsync(tenantId).Returns(new TenantPolicySettingsDto
        {
            AllowUserSubmittedEvents = false,
            AllowOrganizationSubmittedEvents = false,
            AllowGroupSubmittedEvents = false
        });

        // Act
        var result = await _handler.Handle(new GetEventCreationContextRequest(), CancellationToken.None);

        // Assert
        await Assert.That(result.CanCreate).IsFalse();
        await Assert.That(result.DefaultPublisherMode).IsNull();
        await Assert.That(result.UnavailableReason).IsEqualTo("No available publisher can create events for the current user.");
        await Assert.That(result.PublisherOptions.Count).IsEqualTo(0);
    }

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
