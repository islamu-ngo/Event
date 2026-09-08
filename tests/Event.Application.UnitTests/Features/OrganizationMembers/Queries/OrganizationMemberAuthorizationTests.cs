using Explore.Application.Authorization;
using Explore.Application.DTOs.OrganizationMember;
using Explore.Application.Features.OrganizationMembers.Requests.Commands;
using Explore.Application.Features.OrganizationMembers.Requests.Queries;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Features.OrganizationMembers.Queries;

public sealed class OrganizationMemberAuthorizationTests
{
    [Test]
    public async Task QueryRequests_RequireOrganizationMemberViewAuthorization()
    {
        var listAttribute = typeof(GetOrganizationMembersRequest)
            .GetCustomAttributes(typeof(AuthorizeResourceAttribute), inherit: true)
            .Cast<AuthorizeResourceAttribute>()
            .SingleOrDefault();
        var detailAttribute = typeof(GetOrganizationMemberDetailsRequest)
            .GetCustomAttributes(typeof(AuthorizeResourceAttribute), inherit: true)
            .Cast<AuthorizeResourceAttribute>()
            .SingleOrDefault();

        await Assert.That(listAttribute).IsNotNull();
        await Assert.That(listAttribute!.Resource).IsEqualTo(ResourceKinds.OrganizationMember);
        await Assert.That(listAttribute.Action).IsEqualTo(AuthorizationActions.OrganizationMembers.View);
        await Assert.That(detailAttribute).IsNotNull();
        await Assert.That(detailAttribute!.Resource).IsEqualTo(ResourceKinds.OrganizationMember);
        await Assert.That(detailAttribute.Action).IsEqualTo(AuthorizationActions.OrganizationMembers.View);
        await Assert.That(typeof(ISecureRequest).IsAssignableFrom(typeof(GetOrganizationMembersRequest))).IsTrue();
        await Assert.That(typeof(ISecureRequest).IsAssignableFrom(typeof(GetOrganizationMemberDetailsRequest))).IsTrue();
    }

    [Test]
    public async Task QueryRequests_CarryTenantAndOrganizationAuthorizationContext()
    {
        var tenantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var listRequest = (ISecureRequest)new GetOrganizationMembersRequest
        {
            TenantId = tenantId,
            OrganizationId = organizationId
        };
        var detailRequest = (ISecureRequest)new GetOrganizationMemberDetailsRequest
        {
            Id = memberId,
            TenantId = tenantId
        };

        await Assert.That(listRequest.ResourceId).IsEqualTo(organizationId.ToString("D"));
        await Assert.That(listRequest.AuthorizationFacts)
            .IsEqualTo(new OrganizationMemberAuthorizationFacts(tenantId, organizationId, null, null));
        await Assert.That(detailRequest.ResourceId).IsEqualTo(memberId.ToString("D"));
        await Assert.That(detailRequest.AuthorizationFacts).IsTypeOf<OrganizationMemberAuthorizationFacts>();
    }

    [Test]
    public async Task OrganizationMemberDescriptor_CarriesTenantOrganizationAndUserContext()
    {
        var dto = new OrganizationMemberDto
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            UserId = Guid.NewGuid()
        };

        var facts = ResourceDescriptors.OrganizationMember.GetFacts(dto);
        var scope = ResourceDescriptors.OrganizationMember.GetScope(dto);

        await Assert.That(facts)
            .IsEqualTo(new OrganizationMemberAuthorizationFacts(dto.TenantId, dto.OrganizationId, dto.Id, dto.UserId));
        await Assert.That(scope.TenantId).IsEqualTo(dto.TenantId.ToString());
        await Assert.That(scope.OrganizationId).IsEqualTo(dto.OrganizationId.ToString());
    }

    [Test]
    public async Task AddOrganizationMemberCommand_CarriesTenantAndOrganizationAuthorizationContext()
    {
        var tenantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var command = (ISecureRequest)new AddOrganizationMemberCommand
        {
            TenantId = tenantId,
            RequesterUserId = Guid.NewGuid().ToString("D"),
            AddOrganizationMemberDto = new AddOrganizationMemberDto
            {
                OrganizationId = organizationId,
                Email = "member@example.test",
                Role = RoleEnum.OrgMember
            }
        };

        await Assert.That(command.ResourceId).IsEqualTo(organizationId.ToString("D"));
        await Assert.That(command.AuthorizationFacts)
            .IsEqualTo(new OrganizationMemberAuthorizationFacts(tenantId, organizationId, null, null));
    }
}
