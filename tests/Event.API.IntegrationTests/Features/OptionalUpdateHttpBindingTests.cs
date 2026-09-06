// ABOUTME: Raw HTTP integration tests for OptionalUpdate<T> API binding semantics.
// ABOUTME: Proves omitted, explicit set, nullable clear, and invalid wrapper payloads remain distinct.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Instance;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class OptionalUpdateHttpBindingTests
{
    private static readonly string SetupSecret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    private const string BrandingUrl = "/api/instance/settings/branding";
    private string? _previousSetupSecret;

    [Before(Test)]
    public void CaptureSetupSecret() => _previousSetupSecret = Environment.GetEnvironmentVariable("SETUP_SECRET");

    [After(Test)]
    public void RestoreSetupSecret() => Environment.SetEnvironmentVariable("SETUP_SECRET", _previousSetupSecret);

    [Test]
    public async Task ConcreteSet_BindsStringAndBooleanWrappers()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var userId = await CreateInstanceAdminAsync(factory);

        using var request = CreatePatchRequest(userId,
            """{"defaultBrandDisplayName":{"hasValue":true,"value":"Configured Instance"},"lockTenantBrandDisplayName":{"hasValue":true,"value":true}}""");
        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var branding = await GetBrandingAsync(client, userId);
        await Assert.That(branding.DefaultBrandDisplayName).IsEqualTo("Configured Instance");
        await Assert.That(branding.LockTenantBrandDisplayName).IsTrue();
    }

    [Test]
    public async Task WrapperNull_IsUnspecifiedAndLeavesThatFieldUnchanged()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var userId = await CreateInstanceAdminAsync(factory);

        await SendSuccessfulPatchAsync(client, userId,
            """{"defaultBrandDisplayName":{"hasValue":true,"value":"Before"}}""");

        using var request = CreatePatchRequest(userId,
            """{"defaultBrandDisplayName":null,"defaultBrandLogoUrl":{"hasValue":true,"value":"https://cdn.example.test/logo.svg"}}""");
        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var branding = await GetBrandingAsync(client, userId);
        await Assert.That(branding.DefaultBrandDisplayName).IsEqualTo("Before");
        await Assert.That(branding.DefaultBrandLogoUrl).IsEqualTo("https://cdn.example.test/logo.svg");
    }

    [Test]
    public async Task ExplicitClearObject_ClearsNullableLeaf()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var userId = await CreateInstanceAdminAsync(factory);

        await SendSuccessfulPatchAsync(client, userId,
            """{"defaultBrandLogoUrl":{"hasValue":true,"value":"https://cdn.example.test/logo.svg"}}""");

        using var request = CreatePatchRequest(userId,
            """{"defaultBrandLogoUrl":{"hasValue":true,"value":null}}""");
        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var branding = await GetBrandingAsync(client, userId);
        await Assert.That(branding.DefaultBrandLogoUrl).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task MalformedOptionalMissingValue_ReturnsBadRequestWithoutMutation()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var userId = await CreateInstanceAdminAsync(factory);

        await SendSuccessfulPatchAsync(client, userId,
            """{"defaultBrandDisplayName":{"hasValue":true,"value":"Before"}}""");

        using var request = CreatePatchRequest(userId,
            """{"defaultBrandDisplayName":{"hasValue":true}}""");
        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var branding = await GetBrandingAsync(client, userId);
        await Assert.That(branding.DefaultBrandDisplayName).IsEqualTo("Before");
    }

    [Test]
    public async Task NonNullableNull_ReturnsBadRequestWithoutMutation()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var userId = await CreateInstanceAdminAsync(factory);

        await SendSuccessfulPatchAsync(client, userId,
            """{"lockTenantBrandDisplayName":{"hasValue":true,"value":true}}""");

        using var request = CreatePatchRequest(userId,
            """{"lockTenantBrandDisplayName":{"hasValue":true,"value":null}}""");
        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var branding = await GetBrandingAsync(client, userId);
        await Assert.That(branding.LockTenantBrandDisplayName).IsTrue();
    }

    private static AuthenticatedWebApplicationFactory CreateFactory()
    {
        Environment.SetEnvironmentVariable("SETUP_SECRET", SetupSecret);
        return new AuthenticatedWebApplicationFactory();
    }

    private static async Task<Guid> CreateInstanceAdminAsync(AuthenticatedWebApplicationFactory factory)
    {
        var userId = Guid.CreateVersion7();
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

        dbContext.Users.Add(new User { Id = userId, CreatedAt = DateTime.UtcNow,
        CreatedBy = userId,
        Pii = new UserPii
        {
            UserId = userId,
            Email = $"{userId:N}@integration.test",
            FirstName = "Instance",
            LastName = "Admin"
        } });
        var completedAt = DateTime.UtcNow;
        var bootstrap = InstanceBootstrapState.CreateInteractivePending(
            Guid.CreateVersion7(),
            Explore.Domain.Enums.DeploymentMode.SingleTenant,
            completedAt);
        bootstrap.CompleteInteractive(userId, completedAt);
        dbContext.InstanceBootstrapStates.Add(bootstrap);
        Role? platformAdministratorRole = dbContext.Roles
            .SingleOrDefault(role => role.Id == (int)RoleEnum.Admin);
        if (platformAdministratorRole is null)
        {
            platformAdministratorRole = new Role
            {
                Id = (int)RoleEnum.Admin,
                MasterCode = "platform.admin",
                FullName = "Platform Administrator",
                Scope = RoleScopeEnum.Platform,
                RoleScope = null!,
                IsSystem = true
            };
            dbContext.Roles.Add(platformAdministratorRole);
        }
        dbContext.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            User = null!,
            RoleId = platformAdministratorRole.Id,
            Role = platformAdministratorRole,
            GrantedAt = completedAt,
            GrantedBy = userId
        });

        await dbContext.SaveChangesAsync();
        return userId;
    }

    private static HttpRequestMessage CreatePatchRequest(Guid userId, string json)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, BrandingUrl);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateInstanceAdminHeaderValue(userId));
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return request;
    }

    private static async Task SendSuccessfulPatchAsync(HttpClient client, Guid userId, string json)
    {
        using var request = CreatePatchRequest(userId, json);
        var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static async Task<BrandingSettingsDto> GetBrandingAsync(HttpClient client, Guid userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BrandingUrl);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateInstanceAdminHeaderValue(userId));
        var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        var branding = await response.Content.ReadFromJsonAsync<BrandingSettingsDto>();
        await Assert.That(branding).IsNotNull();
        return branding!;
    }
}
