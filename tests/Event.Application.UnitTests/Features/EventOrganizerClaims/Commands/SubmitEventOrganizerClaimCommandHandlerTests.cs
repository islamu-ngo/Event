using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventOrganizerClaim;
using Explore.Application.Features.EventOrganizerClaims.Handlers.Commands;
using Explore.Application.Features.EventOrganizerClaims.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Features.EventOrganizerClaims.Commands;

[Category("EventActorEligibility")]
public sealed class SubmitEventOrganizerClaimCommandHandlerTests
{
    [Test]
    public async Task Handle_UserActorWithoutActiveTenantUser_IsDenied()
    {
        var userId = Guid.CreateVersion7();
        var actor = ActorForUser(userId);

        var result = await SubmitAsync(actor, userId, activeTenantUser: false);

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Handle_SuspendedGlobalActorWithActiveTenantUser_IsDenied()
    {
        var userId = Guid.CreateVersion7();
        var actor = ActorForUser(userId);
        actor.IsSuspended = true;

        var result = await SubmitAsync(actor, userId, activeTenantUser: true);

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    [Arguments(ApprovalStatusEnum.Pending, true, false, false)]
    [Arguments(ApprovalStatusEnum.Approved, false, false, false)]
    [Arguments(ApprovalStatusEnum.Approved, true, true, false)]
    [Arguments(ApprovalStatusEnum.Approved, true, false, true)]
    public async Task Handle_OrganizationActorWithoutEligibleParticipation_IsDenied(
        ApprovalStatusEnum approvalStatus,
        bool organizerEligible,
        bool suspended,
        bool deleted)
    {
        var organizationId = Guid.CreateVersion7();
        var actor = ActorForOrganization(organizationId);
        var participation = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            Tenant = null!,
            OrganizationId = organizationId,
            Organization = null!,
            ApprovalStatusId = (int)approvalStatus,
            ApprovalStatus = null!,
            IsOrganizerEligible = organizerEligible,
            IsSuspended = suspended,
            IsDeleted = deleted
        };

        var result = await SubmitAsync(
            actor,
            Guid.CreateVersion7(),
            organizationParticipation: participation,
            organizationPermission: true);

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Handle_GroupActorWithApprovedEligibleParticipationAndPermission_IsAllowed()
    {
        var groupId = Guid.CreateVersion7();
        var actor = ActorForGroup(groupId);
        var participation = new GroupTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            Tenant = null!,
            GroupId = groupId,
            Group = null!,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!,
            IsOrganizerEligible = true
        };

        var result = await SubmitAsync(
            actor,
            Guid.CreateVersion7(),
            groupParticipation: participation,
            groupPermission: true);

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Handle_UserActorWithActiveTenantUser_IsAllowed()
    {
        var userId = Guid.CreateVersion7();

        var result = await SubmitAsync(ActorForUser(userId), userId, activeTenantUser: true);

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    public async Task Handle_OrganizationActorWithApprovedEligibleParticipationAndPermission_IsAllowed()
    {
        var organizationId = Guid.CreateVersion7();
        var participation = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            Tenant = null!,
            OrganizationId = organizationId,
            Organization = null!,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!,
            IsVisible = false,
            IsOrganizerEligible = true
        };

        var result = await SubmitAsync(
            ActorForOrganization(organizationId),
            Guid.CreateVersion7(),
            organizationParticipation: participation,
            organizationPermission: true);

        await Assert.That(result.IsSuccess).IsTrue();
    }

    [Test]
    [Arguments(ApprovalStatusEnum.Pending, true, false, false)]
    [Arguments(ApprovalStatusEnum.Approved, false, false, false)]
    [Arguments(ApprovalStatusEnum.Approved, true, true, false)]
    [Arguments(ApprovalStatusEnum.Approved, true, false, true)]
    public async Task Handle_GroupActorWithoutEligibleParticipation_IsDenied(
        ApprovalStatusEnum approvalStatus,
        bool organizerEligible,
        bool suspended,
        bool deleted)
    {
        var groupId = Guid.CreateVersion7();
        var participation = new GroupTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            Tenant = null!,
            GroupId = groupId,
            Group = null!,
            ApprovalStatusId = (int)approvalStatus,
            ApprovalStatus = null!,
            IsOrganizerEligible = organizerEligible,
            IsSuspended = suspended,
            IsDeleted = deleted
        };

        var result = await SubmitAsync(
            ActorForGroup(groupId),
            Guid.CreateVersion7(),
            groupParticipation: participation,
            groupPermission: true);

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Handle_ObservedExternalActor_IsDenied()
    {
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.ExternalUnclassified,
            ActorType = null!,
            ExternalActorSubjectId = Guid.CreateVersion7(),
            Pii = new ActorPii { DisplayName = "Observed external claimant" }
        };

        var result = await SubmitAsync(actor, Guid.CreateVersion7());

        await Assert.That(result.IsSuccess).IsFalse();
    }

    private static async Task<BaseCommandResponse<Guid>> SubmitAsync(
        Actor actor,
        Guid currentUserId,
        bool activeTenantUser = false,
        OrganizationTenant? organizationParticipation = null,
        GroupTenant? groupParticipation = null,
        bool organizationPermission = false,
        bool groupPermission = false)
    {
        var tenantId = organizationParticipation?.TenantId
            ?? groupParticipation?.TenantId
            ?? Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var eventRepository = Substitute.For<IEventRepository>();
        eventRepository.GetAuthorizationTargetByIdAsync(eventId, Arg.Any<CancellationToken>()).Returns(new Explore.Domain.Event
        {
            Id = eventId,
            TenantId = tenantId,
            Title = "Community event",
            Actor = null!,
            Tenant = null!,
            EventStatus = null!,
            EventFormat = null!,
            VisibilityType = null!
        });
        var claimRepository = Substitute.For<IEventOrganizerClaimRepository>();
        claimRepository.GetByEventAndClaimantAsync(
                eventId,
                actor.Id,
                false,
                Arg.Any<CancellationToken>())
            .Returns(EventOrganizerClaim.CreatePending(
                tenantId,
                eventId,
                actor.Id,
                "DOMAIN_EMAIL",
                "existing-evidence",
                DateTime.UtcNow));
        var actorRepository = Substitute.For<IActorRepository>();
        actorRepository.GetActorWithDetails(actor.Id, Arg.Any<CancellationToken>()).Returns(actor);
        var tenantUserRepository = Substitute.For<ITenantUserRepository>();
        if (actor.UserId is { } actorUserId)
        {
            tenantUserRepository.IsActiveTenantUserAsync(tenantId, actorUserId, Arg.Any<CancellationToken>())
                .Returns(activeTenantUser);
        }
        var organizationTenantRepository = Substitute.For<IOrganizationTenantRepository>();
        if (actor.OrganizationId is { } organizationId)
        {
            organizationTenantRepository.GetByOrganizationAndTenant(
                    organizationId,
                    tenantId,
                    Arg.Any<CancellationToken>())
                .Returns(organizationParticipation);
        }
        var groupTenantRepository = Substitute.For<IGroupTenantRepository>();
        if (actor.GroupId is { } groupId)
        {
            groupTenantRepository.GetByGroupAndTenant(groupId, tenantId, Arg.Any<CancellationToken>())
                .Returns(groupParticipation);
        }
        var organizationMemberRepository = Substitute.For<IOrganizationMemberRepository>();
        organizationMemberRepository.HasPermissionInOrganization(
                Arg.Any<Guid>(),
                currentUserId,
                Arg.Any<string>())
            .Returns(organizationPermission);
        var groupMemberRepository = Substitute.For<IGroupMemberRepository>();
        groupMemberRepository.HasPermissionInGroup(
                Arg.Any<Guid>(),
                currentUserId,
                Arg.Any<string>())
            .Returns(groupPermission);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.ExecuteSerializableAsync(
                Arg.Any<Func<CancellationToken, Task<BaseCommandResponse<Guid>>>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<CancellationToken, Task<BaseCommandResponse<Guid>>>>()(CancellationToken.None));
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        var currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(currentUserId);
        var handler = new SubmitEventOrganizerClaimCommandHandler(
            eventRepository,
            claimRepository,
            actorRepository,
            tenantUserRepository,
            organizationTenantRepository,
            groupTenantRepository,
            organizationMemberRepository,
            groupMemberRepository,
            unitOfWork,
            tenantContext,
            currentUser);

        return await handler.Handle(new SubmitEventOrganizerClaimCommand
        {
            EventId = eventId,
            Claim = new SubmitEventOrganizerClaimDto
            {
                ClaimantActorId = actor.Id,
                EvidenceType = "DOMAIN_EMAIL",
                EvidenceReference = "evidence-reference"
            }
        }, CancellationToken.None);
    }

    private static Actor ActorForUser(Guid userId) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        ActorTypeId = (int)ActorTypeEnum.User,
        ActorType = null!,
        Pii = new ActorPii { DisplayName = "User claimant" }
    };

    private static Actor ActorForOrganization(Guid organizationId) => new()
    {
        Id = Guid.CreateVersion7(),
        OrganizationId = organizationId,
        ActorTypeId = (int)ActorTypeEnum.Organization,
        ActorType = null!,
        Pii = new ActorPii { DisplayName = "Organization claimant" }
    };

    private static Actor ActorForGroup(Guid groupId) => new()
    {
        Id = Guid.CreateVersion7(),
        GroupId = groupId,
        ActorTypeId = (int)ActorTypeEnum.Group,
        ActorType = null!,
        Pii = new ActorPii { DisplayName = "Group claimant" }
    };
}
