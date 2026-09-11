using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Operations.Decorators;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeIdentityQueryControllerTests
{
    private static readonly Guid LinkedUser = Guid.Parse("018e4e5c-7f00-7000-8000-000000000081");
    private static readonly Guid Subject = Guid.Parse("018e4e5c-7f00-7000-8000-000000000082");
    private const string Issuer = "https://accounts.google.com";

    [Test]
    public async Task HttpProviderIdentityAndLegacyAdminQueryCoexistWithoutClaimOrEmailFallback()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        factory.AdditionalConfiguration["Diagnostics:EnableAdminCacheInvalidation"] = "true";
        using var client = factory.CreateClient();
        await SeedAsync(factory);

        using var linked = Request(HttpMethod.Post, "/api/_internal/admin-cache/current-user/snapshot", Issuer);
        using var linkedResponse = await client.SendAsync(linked);
        linkedResponse.EnsureSuccessStatusCode();
        var snapshot = await linkedResponse.Content.ReadFromJsonAsync<AdminCacheDiagnosticsController.AdminCacheCurrentUserDiagnostics>();
        await Assert.That(snapshot!.ResolvedUserId).IsEqualTo(LinkedUser);
        await Assert.That(snapshot.SubjectClaim).IsEqualTo(Subject.ToString("D"));

        using var unlinked = Request(HttpMethod.Post, "/api/_internal/admin-cache/current-user/snapshot", "https://other.example.test");
        using var unlinkedResponse = await client.SendAsync(unlinked);
        unlinkedResponse.EnsureSuccessStatusCode();
        var missing = await unlinkedResponse.Content.ReadFromJsonAsync<AdminCacheDiagnosticsController.AdminCacheCurrentUserDiagnostics>();
        await Assert.That(missing!.ResolvedUserId).IsNull();

        // UserController resolves the native identity, then executes the real, still-MediatR admin query.
        using var legacy = Request(HttpMethod.Get, "/api/user/admin-authority", Issuer);
        using var legacyResponse = await client.SendAsync(legacy);
        legacyResponse.EnsureSuccessStatusCode();
        var authority = await legacyResponse.Content.ReadFromJsonAsync<AdminAuthorityDto>();
        await Assert.That(authority!.HasAnyAuthority).IsFalse();

        using var missingUser = Request(HttpMethod.Get, "/api/user/admin-authority", "https://other.example.test");
        using var missingUserResponse = await client.SendAsync(missingUser);
        await Assert.That(missingUserResponse.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var anonymous = await client.GetAsync("/api/user/admin-authority");
        await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ActualHostConstructsEveryClosedCallerAndResolvesProtectedIdentityInIndependentScopes()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        await SeedAsync(factory);
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        var query = first.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>();
        var other = second.ServiceProvider.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>();
        await Assert.That(query).IsTypeOf<AuthorizationQueryHandlerDecorator<ResolveCurrentUserIdByIdentityRequest, Guid?>>();
        await Assert.That(ReferenceEquals(query, other)).IsFalse();
        var request = new ResolveCurrentUserIdByIdentityRequest
        {
            Provider = "google", ProviderId = $"oidc:27:{Issuer}:{Subject:D}",
            Email = "conflicting@example.invalid", EmailVerified = true
        };
        await Assert.That(await query.QueryAsync(request, CancellationToken.None)).IsEqualTo(LinkedUser);
        await Assert.That(await other.QueryAsync(request with { Provider = "keycloak" }, CancellationToken.None)).IsNull();
        await Assert.That(await query.QueryAsync(request, CancellationToken.None)).IsEqualTo(LinkedUser);

        Type[] callers = [
            typeof(AdminCacheDiagnosticsController), typeof(ControlPlaneTenantConfigurationController), typeof(GroupController),
            typeof(InstanceAuthenticationSettingsController), typeof(InstanceAuthorizationSettingsController),
            typeof(InstanceGovernanceSettingsController), typeof(InstanceMessagingSettingsController),
            typeof(InstanceModerationReportingSettingsController), typeof(InstanceOnboardingController),
            typeof(InstancePresentationSettingsController), typeof(InstanceStorageSettingsController),
            typeof(ModerationReportingRoutingController), typeof(OrganizationController), typeof(SetupTargetEnrollmentsController),
            typeof(TenantOnboardingController), typeof(TenantStorageSettingsController), typeof(UserController)
        ];
        foreach (var caller in callers)
            await Assert.That(ActivatorUtilities.CreateInstance(first.ServiceProvider, caller)).IsNotNull();
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string issuer)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Subject, "Provider user",
            ("iss", issuer), ("auth_provider", "google"), ("internal_user_id", Subject.ToString("D")),
            ("email", "conflicting@example.invalid"), ("email_verified", "true")));
        return request;
    }

    private static async Task SeedAsync(AuthenticatedWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var user = new User
        {
            Id = LinkedUser,
            Pii = new UserPii { UserId = LinkedUser, Email = "linked@example.invalid", FirstName = "Linked", LastName = "User" }
        };
        db.Users.Add(user);
        db.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = Guid.Parse("018e4e5c-7f00-7000-8000-000000000083"),
            User = user, UserId = LinkedUser, AuthenticationProvider = null!,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Google,
            ProviderKey = $"oidc:27:{Issuer}:{Subject:D}"
        });
        await db.SaveChangesAsync();
    }
}
