using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Users.Handlers.Queries;
using Explore.Application.Features.Users.Requests.Queries;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.Users.Queries;

public sealed class GetAdminAuthorityRequestHandlerTests
{
    [Test]
    public async Task Handle_ProjectsEveryPersistedAdministrativeScope()
    {
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        var groupId = Guid.CreateVersion7();
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(true);
        adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([tenantId]);
        adminContext.GetAdminOrganizationIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([organizationId]);
        adminContext.GetAdminGroupIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([groupId]);
        var handler = new GetAdminAuthorityRequestHandler(adminContext);

        var result = await handler.Handle(
            new GetAdminAuthorityRequest { UserId = userId },
            CancellationToken.None);

        await Assert.That(result.IsInstanceAdmin).IsTrue();
        await Assert.That(result.AdminTenantIds).IsEquivalentTo([tenantId]);
        await Assert.That(result.AdminOrganizationIds).IsEquivalentTo([organizationId]);
        await Assert.That(result.AdminGroupIds).IsEquivalentTo([groupId]);
        await Assert.That(result.HasAnyAuthority).IsTrue();
    }

    [Test]
    public async Task Handle_GroupOnlyAuthority_IsAdministrativeAuthority()
    {
        var userId = Guid.CreateVersion7();
        var groupId = Guid.CreateVersion7();
        var adminContext = Substitute.For<IAdminContext>();
        adminContext.IsInstanceAdminAsync(userId, Arg.Any<CancellationToken>()).Returns(false);
        adminContext.GetAdminTenantIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([]);
        adminContext.GetAdminOrganizationIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([]);
        adminContext.GetAdminGroupIdsAsync(userId, Arg.Any<CancellationToken>()).Returns([groupId]);
        var handler = new GetAdminAuthorityRequestHandler(adminContext);

        var result = await handler.Handle(
            new GetAdminAuthorityRequest { UserId = userId },
            CancellationToken.None);

        await Assert.That(result.HasAnyAuthority).IsTrue();
        await Assert.That(result.AdminGroupIds).IsEquivalentTo([groupId]);
    }
}
