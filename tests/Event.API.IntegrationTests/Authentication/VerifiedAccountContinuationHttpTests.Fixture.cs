using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.API.IntegrationTests.Authentication;

public sealed partial class VerifiedAccountContinuationHttpTests
{
    private static HttpClient CreateClient(LocalAdmissionWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static async Task<Guid> ArrangeMembershipsAsync(
        LocalIdentityLifecycleHttpFixture fixture, bool multiTenant)
    {
        await using var database = fixture.Native.CreateDatabase();
        database.BindTenantLease();
        var user = await database.Users.SingleAsync(row => row.Id == fixture.Binding.LocalSubjectId);
        var actor = await database.Actors.SingleAsync(row => row.UserId == user.Id);
        var initial = await database.Tenants.SingleAsync();
        var preferred = NewTenant("continuation-preferred");
        var unjoined = NewTenant("continuation-unjoined");
        var inactive = NewTenant("continuation-inactive");
        inactive.TenantStatusId = (int)TenantStatusEnum.Suspended;
        database.Tenants.AddRange(preferred, unjoined, inactive);
        foreach (var tenant in new[] { initial, preferred })
        {
            database.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = tenant,
                UserId = user.Id,
                User = user,
                ActorId = actor.Id,
                Actor = actor,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }
        user.LastActiveTenantId = preferred.Id;
        if (multiTenant)
            (await database.InstanceBootstrapStates.SingleAsync()).TransitionDeploymentMode(DeploymentMode.MultiTenant);
        await database.SaveChangesAsync();
        await fixture.Host.Services.GetRequiredService<IDeploymentModeProvider>().InvalidateCacheAsync();
        return preferred.Id;
    }

    private static Tenant NewTenant(string slug) => new()
    {
        Id = Guid.CreateVersion7(),
        Slug = slug,
        FullName = slug,
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!,
        CreatedAt = DateTime.UtcNow
    };

    private static async Task SetMultiTenantAsync(LocalIdentityLifecycleHttpFixture fixture)
    {
        await using var database = fixture.Native.CreateDatabase();
        database.BindTenantLease();
        (await database.InstanceBootstrapStates.SingleAsync()).TransitionDeploymentMode(DeploymentMode.MultiTenant);
        await database.SaveChangesAsync();
        await fixture.Host.Services.GetRequiredService<IDeploymentModeProvider>().InvalidateCacheAsync();
    }

    private static async Task<HttpResponseMessage> ContinueAsync(
        HttpClient client, string token, string? slug = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (slug is not null)
            request.Headers.TryAddWithoutValidation("X-Tenant-Slug", slug);
        return await client.SendAsync(request);
    }

    private static async Task AssertDestinationAsync(
        HttpResponseMessage response, Guid tenant, string slug, bool multiple)
    {
        await AssertOutcomeAsync(response, "Destination");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("tenantId").GetGuid()).IsEqualTo(tenant);
        await Assert.That(body.RootElement.GetProperty("tenantSlug").GetString()).IsEqualTo(slug);
        await Assert.That(body.RootElement.GetProperty("hasMultipleTenants").GetBoolean()).IsEqualTo(multiple);
    }

    private static async Task AssertOutcomeAsync(HttpResponseMessage response, string outcome)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertPrivateAsync(response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("outcome").GetString()).IsEqualTo(outcome);
        await Assert.That(body.RootElement.TryGetProperty("token", out _)).IsFalse();
        await Assert.That(body.RootElement.TryGetProperty("refreshToken", out _)).IsFalse();
    }

    private static async Task AssertPrivateAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        await Assert.That(response.Headers.CacheControl?.Private).IsEqualTo(true);
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single()).IsEqualTo("no-referrer");
    }

    private static async Task<string> ReadRowsAsync(LocalAdmissionWebApplicationFactory factory)
    {
        await using var database = factory.CreateDatabase();
        database.BindTenantLease();
        return JsonSerializer.Serialize(new
        {
            Users = await database.Users.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.IsDeleted, row.LastActiveTenantId, row.UpdatedAt }).ToArrayAsync(),
            Logins = await database.UserExternalLogins.AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.UserId, row.ProviderKey, row.AuthenticationProviderId }).ToArrayAsync(),
            Memberships = await database.TenantUsers.IgnoreQueryFilters().AsNoTracking().OrderBy(row => row.Id)
                .Select(row => new { row.Id, row.UserId, row.TenantId, row.StatusId, row.IsDeleted }).ToArrayAsync(),
            Actors = await database.Actors.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync()
        });
    }

}
