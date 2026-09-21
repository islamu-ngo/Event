using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Models.Common;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class PrivateInstanceManagementHttpTests
{
    [Test]
    [Arguments(false, "setup", HttpStatusCode.OK)]
    [Arguments(true, "setup", HttpStatusCode.OK)]
    [Arguments(false, "administrator", HttpStatusCode.OK)]
    [Arguments(true, "administrator", HttpStatusCode.OK)]
    [Arguments(false, "anonymous", HttpStatusCode.Unauthorized)]
    [Arguments(true, "anonymous", HttpStatusCode.Unauthorized)]
    [Arguments(false, "ordinary", HttpStatusCode.Forbidden)]
    [Arguments(true, "ordinary", HttpStatusCode.Forbidden)]
    [Arguments(false, "expired-setup", HttpStatusCode.Gone)]
    [Arguments(true, "expired-setup", HttpStatusCode.Gone)]
    [Arguments(false, "unrelated-tenant-admin", HttpStatusCode.Forbidden)]
    [Arguments(true, "unrelated-tenant-admin", HttpStatusCode.Forbidden)]
    public async Task ExactInstanceManagementUsesItsOwnAuthorityWithoutPublicTenant(
        bool multiTenant, string authority, HttpStatusCode expected)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: authority == "setup");
        await using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            { ["Deployment:Mode"] = multiTenant ? "MultiTenant" : "SingleTenant" })));
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (authority is "administrator" or "ordinary" or "unrelated-tenant-admin")
        {
            var credential = await factory.SeedLocalUserAsync(emailConfirmed: true);
            await using (var database = factory.CreateDatabase())
            {
                var user = await database.Users.SingleAsync(user => user.Pii!.Email == credential.Identifier, token);
                if (authority == "administrator")
                    database.PlatformUserRoles.Add(new PlatformUserRole
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = user.Id,
                        User = user,
                        RoleId = (int)RoleEnum.Admin,
                        Role = null!,
                        GrantedAt = DateTime.UtcNow,
                        GrantedBy = user.Id
                    });
                if (authority == "unrelated-tenant-admin")
                {
                    var other = new Tenant
                    {
                        Id = Guid.CreateVersion7(),
                        FullName = "Other",
                        Slug = "other",
                        TenantStatusId = (int)TenantStatusEnum.Active,
                        TenantStatus = null!,
                        CreatedAt = DateTime.UtcNow
                    };
                    var member = new TenantUser
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = other.Id,
                        Tenant = other,
                        UserId = user.Id,
                        User = user,
                        StatusId = (int)TenantUserStatusEnum.Active,
                        CreatedAt = DateTime.UtcNow
                    };
                    database.Tenants.Add(other);
                    database.TenantUsers.Add(member);
                    database.Set<TenantUserRoleGrant>().Add(new TenantUserRoleGrant
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = other.Id,
                        Tenant = other,
                        TenantUserId = member.Id,
                        TenantUser = member,
                        RoleId = (int)RoleEnum.TenantAdmin,
                        Role = null!,
                        RoleScopeId = (int)RoleScopeEnum.Tenant,
                        GrantedAt = DateTime.UtcNow
                    });
                }
                await database.SaveChangesAsync(token);
            }
            using var login = await client.PostAsJsonAsync("/api/auth/local/login", credential, token);
            login.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync(token));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("token").GetString());
        }
        if (authority is "setup" or "expired-setup")
            client.DefaultRequestHeaders.Add("X-Setup-Secret", factory.SetupSecret);
        await using (var database = factory.CreateDatabase())
        {
            if (multiTenant)
            {
                var bootstrap = await database.InstanceBootstrapStates.SingleOrDefaultAsync(token);
                bootstrap?.TransitionDeploymentMode(DeploymentMode.MultiTenant);
                await database.Tenants.Where(tenant => tenant.Id == PlatformDefaults.DefaultTenantId).ExecuteDeleteAsync(token);
            }
            else
                (await database.Tenants.SingleAsync(tenant => tenant.Id == PlatformDefaults.DefaultTenantId, token)).TenantStatusId = (int)TenantStatusEnum.Provisioning;
            await database.SaveChangesAsync(token);
        }
        if (authority == "administrator")
        {
            using var journeyResponse = await client.GetAsync("/api/instanceonboarding/journey", token);
            journeyResponse.EnsureSuccessStatusCode();
            using var journey = JsonDocument.Parse(await journeyResponse.Content.ReadAsStringAsync(token));
            foreach (var relation in new[] { "manage-authentication", "manage-authorization", "manage-operator-identity" })
            {
                var href = journey.RootElement.GetProperty("_links").GetProperty(relation).GetProperty("href").GetString();
                using var linked = await client.GetAsync(href, token);
                await Assert.That(linked.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(relation);
            }
        }
        string[] paths = ["/api/instance/settings/auth-provider", "/api/instance/settings/authz-provider", "/api/instance-operator-identity"];
        foreach (var path in paths)
        {
            using var read = await client.GetAsync(path, token);
            await Assert.That(read.StatusCode).IsEqualTo(expected).Because(path);
        }
        using var authWrite = await client.PatchAsJsonAsync(paths[0], new PatchAuthProviderConfigurationDto
        { Configuration = OptionalUpdate<AuthProviderConfigurationWriteDto>.Set(new()) }, token);
        await Assert.That(authWrite.StatusCode).IsEqualTo(expected);
        using var authzWrite = await client.PatchAsJsonAsync(paths[1], new PatchAuthorizationProviderConfigurationDto
        { Configuration = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>.Set(new() { Provider = "local" }) }, token);
        await Assert.That(authzWrite.StatusCode).IsEqualTo(expected);
        using var identityWrite = await client.PutAsJsonAsync(paths[2], new SaveInstanceOperatorIdentityRequestDto { PublicName = "Private draft" }, token);
        await Assert.That(identityWrite.StatusCode).IsEqualTo(expected);
        using var publicRead = await client.GetAsync("/api/PublicExperience/settings", token);
        await Assert.That(publicRead.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        foreach (var path in new[] { paths[0] + "/status", paths[1] + "/status", paths[2] + "/details" })
        {
            using var unrelated = await client.GetAsync(path, token);
            await Assert.That(unrelated.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
        await using var persisted = factory.CreateDatabase();
        if (!multiTenant)
            await Assert.That((await persisted.Tenants.SingleAsync(tenant => tenant.Id == PlatformDefaults.DefaultTenantId, token)).TenantStatusId)
                .IsEqualTo((int)TenantStatusEnum.Provisioning);
        else
            await Assert.That(await persisted.Tenants.AnyAsync(tenant => tenant.Id == PlatformDefaults.DefaultTenantId, token)).IsFalse();
    }
}
