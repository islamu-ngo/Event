
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.DTOs.Management;
using Explore.Application.DTOs.TenantSettings;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class ManagedTenantLocalAdministratorHttpTests
{
    private const string ProvisioningPath = "/api/managed-provider-provisioning/clients:ensure";
    private static CancellationToken Token => TestContext.Current!.Execution.CancellationToken;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task AuthenticatedInstanceAdministratorLinksExactLocalTargetAndRejectsInvalidOrUnauthorizedRequests()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        LocalAuthRequestDto administratorLogin = await factory.SeedLocalUserAsync(emailConfirmed: true);
        Guid administratorId;
        await using (ExploreDbContext seed = factory.CreateDatabase())
        {
            administratorId = await seed.LocalIdentityUsers.Where(user => user.Email == administratorLogin.Identifier)
                .Select(user => user.Id).SingleAsync(Token);
            Role role = await seed.Set<Role>().SingleAsync(candidate => candidate.MasterCode == "platform.admin", Token);
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = administratorId,
                User = null!,
                RoleId = role.Id,
                Role = role,
                GrantedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync(Token);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<LocalIdentityRole>>();
            await Assert.That((await roles.CreateAsync(new LocalIdentityRole("platform.admin"))).Succeeded).IsTrue();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            var administrator = (await manager.FindByIdAsync(administratorId.ToString("D")))!;
            await Assert.That((await manager.AddToRoleAsync(administrator, "platform.admin")).Succeeded).IsTrue();
        }
        string administratorBearer = await LoginAsync(client, administratorLogin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", administratorBearer);

        Guid operationId = Guid.CreateVersion7();
        using var issued = await client.PostAsJsonAsync("/api/instance/local-identities", new
        {
            operationId,
            email = $"managed-target-{operationId:N}@example.test",
            firstName = "Managed",
            lastName = "Target"
        }, Token);
        await Assert.That(issued.StatusCode).IsEqualTo(HttpStatusCode.Created);
        JsonElement target = (await ReadAsync(issued)).GetProperty("operation").GetProperty("receipt");
        Guid subjectId = target.GetProperty("localSubjectId").GetGuid();
        string operationPath = $"/api/instance/local-identity-operations/{operationId:D}";
        using var reconciled = await client.PostAsync(operationPath + "/reconcile", content: null, Token);
        await Assert.That(reconciled.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var beforeStatusResponse = await client.GetAsync(operationPath, Token);
        await Assert.That(beforeStatusResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement beforeStatus = await ReadAsync(beforeStatusResponse);

        var body = new ManagedProviderClientProvisioningDto
        {
            ProviderKey = "native-http-provider",
            ExternalSystem = "native-http",
            ExternalCustomerId = operationId.ToString("D"),
            TenantFullName = "Native HTTP managed tenant",
            TenantSlug = $"managed-http-{operationId:N}",
            ActivateTenant = true,
            LocalIdentity = new ManagementTenantLocalIdentityDto { LocalSubjectId = subjectId },
            DirectoryOperatorIdentity = new TenantDirectoryOperatorIdentityInputDto
            {
                PublicName = "HTTP Operator",
                LegalName = "HTTP Operator ASBL",
                OperatorKindCode = "registered_organization",
                JurisdictionCountryCode = "BE",
                RegistrationIdentifier = "BE 0123.456.789",
                PublicContactEmail = "contact@example.test",
                LegalNoticeUrl = "https://example.test/legal",
                TermsUrl = "https://example.test/terms",
                PrivacyUrl = "https://example.test/privacy"
            }
        };
        JsonObject malformed = JsonSerializer.SerializeToNode(body, JsonOptions)!.AsObject();
        malformed["localIdentity"]!["localSubjectId"] = "not-a-guid";
        using var malformedResponse = await client.PostAsJsonAsync(ProvisioningPath, malformed, Token);
        await Assert.That(malformedResponse.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await ReadAsync(malformedResponse)).GetProperty("status").GetInt32()).IsEqualTo(400);
        using var missingDirectory = await client.PostAsJsonAsync(ProvisioningPath, body with { DirectoryOperatorIdentity = null }, Token);
        await Assert.That(missingDirectory.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var unknownSubject = await client.PostAsJsonAsync(ProvisioningPath, body with
        {
            LocalIdentity = new ManagementTenantLocalIdentityDto { LocalSubjectId = Guid.CreateVersion7() }
        }, Token);
        await Assert.That(unknownSubject.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);

        LocalAuthRequestDto ordinaryLogin = await factory.SeedLocalUserAsync(emailConfirmed: true);
        string ordinaryBearer = await LoginAsync(client, ordinaryLogin);
        using (var identityRequest = new HttpRequestMessage(HttpMethod.Get, "/api/user"))
        {
            identityRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ordinaryBearer);
            using var identityResponse = await client.SendAsync(identityRequest, Token);
            await Assert.That(identityResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        using (var unauthorizedRequest = new HttpRequestMessage(HttpMethod.Post, ProvisioningPath))
        {
            unauthorizedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ordinaryBearer);
            unauthorizedRequest.Content = JsonContent.Create(body);
            using var unauthorized = await client.SendAsync(unauthorizedRequest, Token);
            await Assert.That(unauthorized.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await Assert.That((await ReadAsync(unauthorized)).GetProperty("status").GetInt32()).IsEqualTo(403);
        }

        using var provisioned = await client.PostAsJsonAsync(ProvisioningPath, body, Token);
        await Assert.That(provisioned.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement response = await ReadAsync(provisioned);
        await Assert.That(response.GetProperty("success").GetBoolean()).IsTrue();
        JsonElement linkage = response.GetProperty("id");
        Guid tenantId = linkage.GetProperty("tenantId").GetGuid();
        await Assert.That(tenantId).IsNotEqualTo(Guid.Empty);
        await Assert.That(linkage.GetProperty("userId").GetGuid()).IsEqualTo(subjectId);
        await Assert.That(linkage.GetProperty("userId").GetGuid()).IsNotEqualTo(administratorId);
        await Assert.That(linkage.GetProperty("userActorId").GetGuid()).IsEqualTo(target.GetProperty("personalActorId").GetGuid());
        await Assert.That(linkage.GetProperty("userExternalLoginId").GetGuid()).IsEqualTo(target.GetProperty("externalLoginId").GetGuid());
        await Assert.That(linkage.GetProperty("tenantUserId").GetGuid()).IsNotEqualTo(Guid.Empty);
        await Assert.That(linkage.GetProperty("tenantUserRoleGrantId").GetGuid()).IsNotEqualTo(Guid.Empty);
        using var replay = await client.PostAsJsonAsync(ProvisioningPath, body, Token);
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement replayLinkage = (await ReadAsync(replay)).GetProperty("id");
        foreach (string key in new[] { "tenantId", "userId", "tenantUserId", "userActorId", "userExternalLoginId", "tenantUserRoleGrantId" })
            await Assert.That(replayLinkage.GetProperty(key).GetGuid()).IsEqualTo(linkage.GetProperty(key).GetGuid());

        using var afterStatusResponse = await client.GetAsync(operationPath, Token);
        await Assert.That(afterStatusResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement afterStatus = await ReadAsync(afterStatusResponse);
        await Assert.That(afterStatus.GetProperty("credentialState").GetRawText())
            .IsEqualTo(beforeStatus.GetProperty("credentialState").GetRawText());
        await Assert.That(afterStatus.GetProperty("operationConcurrencyStamp").GetGuid())
            .IsEqualTo(beforeStatus.GetProperty("operationConcurrencyStamp").GetGuid());
        using var emailStatus = await client.GetAsync($"/api/admin/email-dispatch/status?tenantId={tenantId:D}", Token);
        await Assert.That(emailStatus.StatusCode).IsEqualTo(HttpStatusCode.OK);
        // The existing email-status controller returns an unawaited assembler Task, not its declared HAL body.
        // Keep that unrelated representation defect outside this slice; verify side effects through its native read port.
        await using var observation = factory.Services.CreateAsyncScope();
        var dispatches = await observation.ServiceProvider.GetRequiredService<IEmailDispatchOutboxRepository>()
            .GetStatusRows(tenantId, limit: 200, Token);
        await Assert.That(dispatches.Count).IsEqualTo(0);
    }

    private static async Task<string> LoginAsync(HttpClient client, LocalAuthRequestDto credentials)
    {
        using var response = await client.PostAsJsonAsync("/api/auth/local/login", credentials, Token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement login = await ReadAsync(response);
        await Assert.That(login.GetProperty("success").GetBoolean()).IsTrue();
        return login.GetProperty("token").GetString()!;
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(Token), cancellationToken: Token);
        return document.RootElement.Clone();
    }
}
