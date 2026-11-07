using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Domain.Enums;

namespace Event.API.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class VerifiedTenantJoinHttpTests
{
    private const string Route = "/api/auth/tenant-join";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExactVerifiedAccountJoinsOnlySelectedOpenTenantAndRepeatPreservesGraph(bool keycloak)
    {
        await using var fixture = await JoinFixture.CreateAsync(keycloak);
        string identities = await fixture.ReadIdentitiesAsync();
        using var first = await fixture.JoinAsync();
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        Guid membership = await fixture.AssertJoinedAsync();
        await Assert.That(await fixture.ReadIdentitiesAsync()).IsEqualTo(identities);
        string committed = await fixture.ReadGraphAsync();

        using var repeat = await fixture.JoinAsync();

        await Assert.That(repeat.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await fixture.AssertJoinedAsync()).IsEqualTo(membership);
        await Assert.That(await fixture.ReadGraphAsync()).IsEqualTo(committed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ClosedRegistrationRejectsMissingMembershipWithoutGraphWrites(bool keycloak)
    {
        await using var fixture = await JoinFixture.CreateAsync(keycloak, registrationOpen: false);
        string before = await fixture.ReadGraphAsync();

        using var response = await fixture.JoinAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(await fixture.ReadGraphAsync()).IsEqualTo(before);
    }

    [Test]
    [Arguments(false, TenantUserStatusEnum.Removed)]
    [Arguments(false, TenantUserStatusEnum.Suspended)]
    [Arguments(true, TenantUserStatusEnum.Removed)]
    [Arguments(true, TenantUserStatusEnum.Suspended)]
    public async Task RemovedOrSuspendedMembershipCannotBeReactivated(
        bool keycloak, TenantUserStatusEnum status)
    {
        await using var fixture = await JoinFixture.CreateAsync(keycloak, membershipStatus: status);
        string before = await fixture.ReadGraphAsync();

        using var response = await fixture.JoinAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
        await Assert.That(await fixture.ReadGraphAsync()).IsEqualTo(before);
    }

    [Test]
    [Arguments(null, HttpStatusCode.Conflict)]
    [Arguments("missing-tenant", HttpStatusCode.NotFound)]
    [Arguments("join-inactive", HttpStatusCode.NotFound)]
    public async Task AbsentOrInvalidExplicitTargetCannotUseIncidentalDefault(
        string? slug, HttpStatusCode expected)
    {
        await using var fixture = await JoinFixture.CreateAsync(false);
        string before = await fixture.ReadGraphAsync();

        using var response = await fixture.JoinAsync(slug, new
        {
            targetTenantId = fixture.TargetId, tenantSlug = JoinFixture.TargetSlug
        });

        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await Assert.That(await fixture.ReadGraphAsync()).IsEqualTo(before);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BodyIdentityCannotEnrollAnotherAccountOrOverrideSelectedTenant(bool keycloak)
    {
        await using var fixture = await JoinFixture.CreateAsync(keycloak);
        string identities = await fixture.ReadIdentitiesAsync();

        using var response = await fixture.JoinAsync(JoinFixture.TargetSlug, new
        {
            userId = fixture.OtherUserId, subject = fixture.OtherUserId.ToString("D"),
            issuer = "https://forged.example.test", accountKey = fixture.OtherUserId.ToString("D"),
            email = "forged@example.test", targetTenantId = fixture.DefaultTenantId
        });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await fixture.AssertJoinedAsync();
        await Assert.That(await fixture.ReadIdentitiesAsync()).IsEqualTo(identities);
    }

    [Test]
    [Arguments("api-key")]
    [Arguments("ambient")]
    [Arguments("anonymous")]
    public async Task ApiKeyOrAmbientIdentityCannotReplaceVerifiedPrincipal(string kind)
    {
        await using var fixture = await JoinFixture.CreateAsync(
            kind == "ambient", initialMembership: kind == "api-key");
        string? key = null;
        if (kind == "api-key")
        {
            using var issuance = new HttpRequestMessage(HttpMethod.Post, "/api/externalapikey")
            {
                Content = JsonContent.Create(new CreateExternalApiKeyDto
                {
                    Name = "Tenant join rejection witness", Scopes = ["events:read"],
                    ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.User
                })
            };
            issuance.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.Token);
            issuance.Headers.Add("X-Tenant-Slug", "local-admission");
            issuance.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
            using var issued = await fixture.Client.SendAsync(issuance);
            await Assert.That(issued.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var body = JsonDocument.Parse(await issued.Content.ReadAsStringAsync());
            key = body.RootElement.GetProperty("apiKey").GetString()!;
        }
        string before = await fixture.ReadGraphAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, Route);
        request.Headers.Add("X-Tenant-Slug", JoinFixture.TargetSlug);
        if (key is not null)
            request.Headers.Add("X-API-Key", key);
        if (kind == "ambient")
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
                fixture.Native.CreateExternalProviderToken(
                [
                    new Claim("sub", "did:plc:abcdefghijklmnopqrstuvwx"),
                    new Claim("typ", "Bearer"), new Claim("auth_provider", "atproto"),
                    new Claim("internal_user_id", fixture.UserId.ToString("D"))
                ], fixture.Native.ExternalIssuer));

        using var response = await fixture.Client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(
            kind == "anonymous" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden);
        await Assert.That(await fixture.ReadGraphAsync()).IsEqualTo(before);
    }
}
