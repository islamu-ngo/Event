using System.Net;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class TenantDiscoveryCreationNativeTests
{
    [Test]
    [Arguments(DeploymentMode.SingleTenant)]
    [Arguments(DeploymentMode.MultiTenant)]
    public async Task RootTenantCreationRetainsItsInstanceOwnerWithoutSelectingATenant(DeploymentMode mode)
    {
        await database.ResetAsync();
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(
            OpenMigrator, database, deploymentMode: mode);
        _ = await CreateAndVerifyAsync(authentication, Request(assignAdministrator: true));
    }

    [Test]
    public async Task RootTenantCreationStillRequiresAuthentication()
    {
        await database.ResetAsync();
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(
            OpenMigrator, database, deploymentMode: DeploymentMode.MultiTenant);
        var before = await ReadTenantsAsync();
        using var response = await authentication.CreateTenantViaApiAsync(Request(), Token, authenticate: false);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(await ReadTenantsAsync()).IsEquivalentTo(before);
    }

    [Test]
    public async Task RootTenantCreationDoesNotExemptCollectionReadsOrTenantNavigation()
    {
        await database.ResetAsync();
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(
            OpenMigrator, database, deploymentMode: DeploymentMode.MultiTenant);
        var before = await ReadTenantsAsync();
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Get, "/api/Tenant"),
            (HttpMethod.Post, "/api/Tenant/navigation"),
            (HttpMethod.Get, "/api/Event")
        })
        {
            using var response = await authentication.ProbeTenantCreationRoutingAsync(method, path, Token);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        await Assert.That(await ReadTenantsAsync()).IsEquivalentTo(before);
    }

    [Test]
    public async Task RootTenantCreationRejectsAnAuthenticatedUserWithoutPlatformAuthority()
    {
        await database.ResetAsync();
        await using var authentication = await AuthenticatedSettingsFixture.CreateForTenantCreationApiAsync(
            OpenMigrator, database, deploymentMode: DeploymentMode.MultiTenant);
        await using (var setup = OpenMigrator())
            await setup.PlatformUserRoles.Where(row => row.UserId == authentication.ActorId).ExecuteDeleteAsync(Token);
        var before = await ReadTenantsAsync();
        using var response = await authentication.CreateTenantViaApiAsync(Request(), Token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(await ReadTenantsAsync()).IsEquivalentTo(before);
    }
}
