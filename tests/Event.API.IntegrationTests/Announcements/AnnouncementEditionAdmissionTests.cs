using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Settings;
using Explore.Domain.Constants;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Announcements;

[NotInParallel]
public sealed class AnnouncementEditionAdmissionTests
{
    [Test]
    public async Task AuthenticatedDirectScalarDismissalMarkerIsRejected()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        string token = await fixture.SignInAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put,
            "/api/settings/user/keys/public_experience_preferences.announcement_bar.dismissed_revision")
        {
            Content = JsonContent.Create(new UpdateSettingValueDto { Value = "7" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await fixture.Client.SendAsync(
            request, LocalIdentityLifecycleHttpFixture.CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task AuthenticatedStrictBatchDismissalMarkerIsRejected()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        string token = await fixture.SignInAsync();
        using var request = new HttpRequestMessage(HttpMethod.Put,
            "/api/settings/user/PublicExperiencePreferences")
        {
            Content = JsonContent.Create(new UpdateSettingBatchDto
            {
                Mode = BatchUpdateMode.Strict,
                Values = new Dictionary<string, string>
                {
                    ["public_experience_preferences.announcement_bar.dismissed_revision"] = "7"
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await fixture.Client.SendAsync(
            request, LocalIdentityLifecycleHttpFixture.CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    [Arguments("GET", "instance")]
    [Arguments("PUT", "instance")]
    [Arguments("GET", "tenant")]
    [Arguments("PUT", "tenant")]
    [Arguments("GET", "tenants/019a3b40-0000-7000-8000-000000000001")]
    [Arguments("PUT", "tenants/019a3b40-0000-7000-8000-000000000001")]
    [Arguments("GET", "self")]
    [Arguments("POST", "self/dismiss")]
    public async Task AnnouncementEndpointsRequireAnActualAuthenticatedSubject(string method, string path)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/announcements/" + path);
        if (method is "PUT" or "POST")
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await fixture.Client.SendAsync(
            request, LocalIdentityLifecycleHttpFixture.CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    [Arguments("instance")]
    [Arguments("tenant")]
    [Arguments("tenants/019a3b40-0000-7000-8000-000000000001")]
    public async Task OrdinaryMemberCannotReadAdministratorPublicationSources(string path)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        string token = await fixture.SignInAsync();
        using var request = Authenticated(HttpMethod.Get, path, token);

        using var response = await fixture.Client.SendAsync(
            request, LocalIdentityLifecycleHttpFixture.CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task PrivateSelfStateIsNoStoreAndDoesNotAdvertiseDismissalWithoutAPublication()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        string token = await fixture.SignInAsync();
        using var request = Authenticated(HttpMethod.Get, "self", token);

        using var response = await fixture.Client.SendAsync(
            request, LocalIdentityLifecycleHttpFixture.CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        await Assert.That(response.Headers.CacheControl?.Private).IsEqualTo(true);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(
            LocalIdentityLifecycleHttpFixture.CancellationToken));
        await Assert.That(body.RootElement.GetProperty("tenantId").GetGuid())
            .IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(body.RootElement.GetProperty("currentObservation").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(body.RootElement.GetProperty("marker").ValueKind).IsEqualTo(JsonValueKind.Null);
        var links = body.RootElement.GetProperty("_links");
        await Assert.That(links.TryGetProperty("self", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("dismiss", out _)).IsFalse();
    }

    [Test]
    [Arguments("missing-marker")]
    [Arguments("null-observation")]
    [Arguments("actor-in-body")]
    [Arguments("unknown-witness-field")]
    [Arguments("foreign-owner")]
    [Arguments("foreign-tenant")]
    public async Task MalformedDismissalIsRejectedBeforeNativeAdmission(string attack)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        string token = await fixture.SignInAsync();
        Guid foreign = Guid.Parse("019a3b40-0000-7000-8000-000000000001");
        var observation = new JsonObject
        {
            ["tenantId"] = PlatformDefaults.DefaultTenantId,
            ["owner"] = new JsonObject { ["tenantId"] = null },
            ["edition"] = Guid.Parse("019a3b40-0000-7000-8000-000000000002"),
            ["sourceWitness"] = new JsonObject
            {
                ["instanceSelectionEpoch"] = null, ["instanceIsLocked"] = false,
                ["tenantSelectionEpoch"] = null, ["tenantIsLocked"] = false
            }
        };
        var input = new JsonObject { ["observed"] = observation, ["expectedMarker"] = null };
        switch (attack)
        {
            case "missing-marker": input.Remove("expectedMarker"); break;
            case "null-observation": input["observed"] = null; break;
            case "actor-in-body": input["actorUserId"] = foreign; break;
            case "unknown-witness-field": observation["sourceWitness"]!["revision"] = 7; break;
            case "foreign-owner": observation["owner"]!["tenantId"] = foreign; break;
            case "foreign-tenant": observation["tenantId"] = foreign; break;
            default: throw new ArgumentOutOfRangeException(nameof(attack));
        }
        using var request = Authenticated(HttpMethod.Post, "self/dismiss", token, input.ToJsonString());

        using var response = await fixture.Client.SendAsync(
            request, LocalIdentityLifecycleHttpFixture.CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    [Arguments("instance")]
    [Arguments("tenant")]
    [Arguments("tenants/019a3b40-0000-7000-8000-000000000001")]
    public async Task UnknownPublicationChangeIsRejectedAtEveryTypedRoute(string path)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        string token = await fixture.SignInAsync();
        Guid? owner = path == "instance" ? null
            : path == "tenant" ? PlatformDefaults.DefaultTenantId
            : Guid.Parse("019a3b40-0000-7000-8000-000000000001");
        var input = new JsonObject
        {
            ["change"] = new JsonObject { ["kind"] = "replace-everything" },
            ["expectedSource"] = new JsonObject
            {
                ["owner"] = new JsonObject { ["tenantId"] = owner },
                ["group"] = null, ["isLocked"] = false
            }
        };
        using var request = Authenticated(HttpMethod.Put, path, token, input.ToJsonString());

        using var response = await fixture.Client.SendAsync(
            request, LocalIdentityLifecycleHttpFixture.CancellationToken);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    private static HttpRequestMessage Authenticated(HttpMethod method, string path, string token, string? body = null)
    {
        var request = new HttpRequestMessage(method, "/api/announcements/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return request;
    }
}
