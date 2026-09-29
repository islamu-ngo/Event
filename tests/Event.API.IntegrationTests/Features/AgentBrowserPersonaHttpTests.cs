using System.Net;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class AgentBrowserPersonaHttpTests
{
    private const string DefaultTenantSlug = "default";
    private const string NegativeTenantSlug = "agent-negative";

    [Test]
    public async Task LocalAdministratorLoginNeedsNoTenantButProtectedReadsStillDo()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.RunAsync();

        string token = await fixture.LoginAsync(AgentBrowserPersonaCatalog.Administrator, "admin");

        using var adminHost = fixture.CreateHostClient("admin");
        adminHost.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using (var currentUser = await adminHost.GetAsync("/api/user"))
            await Assert.That(currentUser.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (var authority = await adminHost.GetAsync("/api/user/admin-authority"))
        {
            await Assert.That(authority.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var document = await AgentBrowserPersonaFixture.ReadJsonAsync(authority);
            await Assert.That(document.RootElement.GetProperty("isInstanceAdmin").GetBoolean()).IsTrue();
        }
        using var protectedRead = await adminHost.GetAsync("/api/event/my");
        await Assert.That(protectedRead.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var protectedDeletion = await adminHost.DeleteAsync("/api/user");
        await Assert.That(protectedDeletion.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task SixRealLocalLoginsAndAnonymousRequestsExposeOnlyTheirHalAuthority()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.RunAsync();
        await using (var database = fixture.CreateDatabase())
        {
            await Assert.That(await new EventRepository(database).IsPubliclyEligibleAsync(
                AgentBrowserPersonaCatalog.TenantId,
                AgentBrowserPersonaCatalog.EventId,
                CancellationToken.None)).IsTrue();
            string[] managerPermissions = await database.RolePermissions
                .Where(row => row.RoleId == (int)RoleEnum.EventManager
                    || row.RoleId == (int)RoleEnum.RegistrationManager)
                .Select(row => row.Permission.MasterCode)
                .ToArrayAsync();
            await Assert.That(managerPermissions).DoesNotContain(PermissionCodes.EventDelete);
            await Assert.That(await database.OrganizationMembers
                .AnyAsync(row => row.UserId == AgentBrowserPersonaCatalog.Manager.SubjectId)).IsFalse();
            var managerSnapshot = await new EventAuthoritySnapshotService(database).GetForUserAndEventsAsync(
                AgentBrowserPersonaCatalog.TenantId,
                AgentBrowserPersonaCatalog.Manager.SubjectId,
                [AgentBrowserPersonaCatalog.EventId],
                DateTime.UtcNow,
                CancellationToken.None);
            await Assert.That(managerSnapshot.Events[AgentBrowserPersonaCatalog.EventId].PermissionCodes)
                .DoesNotContain(PermissionCodes.EventDelete);
        }
        await fixture.AssertApiScopedEventReadableAsync();
        using (var hostOnly = fixture.CreateHostClient(DefaultTenantSlug))
        using (var eventDetail = await hostOnly.GetAsync($"/api/event/{AgentBrowserPersonaCatalog.EventId:D}"))
            await Assert.That(eventDetail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (var negativeHostOnly = fixture.CreateHostClient(NegativeTenantSlug))
        using (var eventDetail = await negativeHostOnly.GetAsync($"/api/event/{AgentBrowserPersonaCatalog.NegativeEventId:D}"))
            await Assert.That(eventDetail.StatusCode).IsEqualTo(HttpStatusCode.OK);

        string managerToken = await fixture.LoginAsync(AgentBrowserPersonaCatalog.Manager);
        using (var manager = fixture.CreateTenantClient(DefaultTenantSlug, managerToken))
        using (var detail = await manager.GetAsync($"/api/event/{AgentBrowserPersonaCatalog.EventId:D}"))
        {
            await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var document = await AgentBrowserPersonaFixture.ReadJsonAsync(detail);
            JsonElement links = document.RootElement.GetProperty("_links");
            await AssertLinkAsync(links, "edit", expected: true, "manager before other identities");
            await AssertLinkAsync(links, "delete", expected: false, "manager before other identities");
        }

        var expected = new[]
        {
            new PersonaAuthority(AgentBrowserPersonaCatalog.Administrator, Edit: false, Delete: false, Moderate: true),
            new PersonaAuthority(AgentBrowserPersonaCatalog.TenantAdministrator, Edit: false, Delete: false, Moderate: true),
            new PersonaAuthority(AgentBrowserPersonaCatalog.Organizer, Edit: true, Delete: true, Moderate: false),
            new PersonaAuthority(AgentBrowserPersonaCatalog.Manager, Edit: true, Delete: false, Moderate: false),
            new PersonaAuthority(AgentBrowserPersonaCatalog.Attendee, Edit: false, Delete: false, Moderate: false),
            new PersonaAuthority(AgentBrowserPersonaCatalog.Moderator, Edit: false, Delete: false, Moderate: false)
        };

        var mismatches = new List<string>();
        foreach (var authority in expected)
        {
            string token = await fixture.LoginAsync(authority.Persona);
            using var client = fixture.CreateTenantClient(DefaultTenantSlug, token);
            using var response = await client.GetAsync($"/api/event/{AgentBrowserPersonaCatalog.EventId:D}");
            string? failureTitle = null;
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                using var failure = await AgentBrowserPersonaFixture.ReadJsonAsync(response);
                failureTitle = failure.RootElement.GetProperty("title").GetString();
            }
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
                .Because($"{authority.Persona.Name}: {failureTitle}");
            using var document = await AgentBrowserPersonaFixture.ReadJsonAsync(response);
            JsonElement links = document.RootElement.GetProperty("_links");
            RecordLink(links, "edit", authority.Edit, authority.Persona.Name, mismatches);
            RecordLink(links, "delete", authority.Delete, authority.Persona.Name, mismatches);
            RecordLink(links, "moderate-light", authority.Moderate, authority.Persona.Name, mismatches);
        }
        await Assert.That(mismatches).IsEmpty();

        using var anonymous = fixture.CreateTenantClient(DefaultTenantSlug);
        using (var detail = await anonymous.GetAsync($"/api/event/{AgentBrowserPersonaCatalog.EventId:D}"))
        {
            await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var document = await AgentBrowserPersonaFixture.ReadJsonAsync(detail);
            JsonElement links = document.RootElement.GetProperty("_links");
            await AssertLinkAsync(links, "edit", expected: false, "anonymous");
            await AssertLinkAsync(links, "delete", expected: false, "anonymous");
            await AssertLinkAsync(links, "moderate-light", expected: false, "anonymous");
        }
        using (var protectedResponse = await anonymous.GetAsync("/api/event/my"))
            await Assert.That(protectedResponse.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task DeniedMutationAndPrimaryTenantRoutesDoNotCrossEventBoundaries()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.RunAsync();
        Guid unrelatedEventId = await fixture.CreateSameTenantUnrelatedEventAsync();
        string attendeeToken = await fixture.LoginAsync(AgentBrowserPersonaCatalog.Attendee);
        string organizerToken = await fixture.LoginAsync(AgentBrowserPersonaCatalog.Organizer);

        using (var attendee = fixture.CreateTenantClient(DefaultTenantSlug, attendeeToken))
        {
            Guid stamp = await GetConcurrencyStampAsync(attendee, AgentBrowserPersonaCatalog.EventId);
            using var denied = UpdateRequest(AgentBrowserPersonaCatalog.EventId, stamp);
            using var response = await attendee.SendAsync(denied);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await AssertEventTitleAsync(fixture, DefaultTenantSlug, AgentBrowserPersonaCatalog.EventId, "Agent browser event");
        }

        string managerToken = await fixture.LoginAsync(AgentBrowserPersonaCatalog.Manager);
        using (var manager = fixture.CreateTenantClient(DefaultTenantSlug, managerToken))
        using (var response = await manager.DeleteAsync($"/api/event/{AgentBrowserPersonaCatalog.EventId:D}"))
        {
            await using var database = fixture.CreateDatabase();
            await Assert.That((await database.Events.IgnoreQueryFilters()
                .SingleAsync(row => row.Id == AgentBrowserPersonaCatalog.EventId)).IsDeleted).IsFalse();
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
        await AssertEventTitleAsync(fixture, DefaultTenantSlug, AgentBrowserPersonaCatalog.EventId, "Agent browser event");

        using var defaultTenant = fixture.CreateTenantClient(DefaultTenantSlug, organizerToken);
        Guid unrelatedStamp = await GetConcurrencyStampAsync(defaultTenant, unrelatedEventId);
        using (var unrelated = await defaultTenant.GetAsync($"/api/event/{unrelatedEventId:D}"))
        {
            using var document = await AgentBrowserPersonaFixture.ReadJsonAsync(unrelated);
            JsonElement links = document.RootElement.GetProperty("_links");
            await AssertLinkAsync(links, "edit", expected: false, "organizer for another organization");
            await AssertLinkAsync(links, "delete", expected: false, "organizer for another organization");
        }
        using (var denied = UpdateRequest(unrelatedEventId, unrelatedStamp))
        using (var response = await defaultTenant.SendAsync(denied))
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using (var denied = await defaultTenant.DeleteAsync($"/api/event/{unrelatedEventId:D}"))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await AssertEventTitleAsync(fixture, DefaultTenantSlug, unrelatedEventId, "Another organization's event");

        using (var foreignEvent = await defaultTenant.GetAsync($"/api/event/{AgentBrowserPersonaCatalog.NegativeEventId:D}"))
            await Assert.That(foreignEvent.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        await AssertEventTitleAsync(fixture, NegativeTenantSlug, AgentBrowserPersonaCatalog.NegativeEventId, "Agent negative event");

        using var negativeTenant = fixture.CreateTenantClient(NegativeTenantSlug, organizerToken);
        using (var foreignEvent = await negativeTenant.GetAsync($"/api/event/{AgentBrowserPersonaCatalog.EventId:D}"))
            await Assert.That(foreignEvent.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using (var publicEvent = await negativeTenant.GetAsync($"/api/event/{AgentBrowserPersonaCatalog.NegativeEventId:D}"))
            await Assert.That(publicEvent.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertEventTitleAsync(fixture, NegativeTenantSlug, AgentBrowserPersonaCatalog.NegativeEventId, "Agent negative event");

        Guid negativeStamp;
        await using (var database = fixture.CreateDatabase())
            negativeStamp = await database.Events
                .Where(row => row.Id == AgentBrowserPersonaCatalog.NegativeEventId)
                .Select(row => row.ConcurrencyStamp)
                .SingleAsync();
        using (var denied = UpdateRequest(AgentBrowserPersonaCatalog.NegativeEventId, negativeStamp))
        using (var response = await negativeTenant.SendAsync(denied))
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await AssertEventTitleAsync(fixture, NegativeTenantSlug, AgentBrowserPersonaCatalog.NegativeEventId, "Agent negative event");
    }

    [Test]
    public async Task AuthorizedUpdateInvalidatesTenantScopedEventDetails()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.RunAsync();

        await AssertEventTitleAsync(
            fixture, DefaultTenantSlug, AgentBrowserPersonaCatalog.EventId, "Agent browser event");

        string organizerToken = await fixture.LoginAsync(AgentBrowserPersonaCatalog.Organizer);
        using var organizer = fixture.CreateTenantClient(DefaultTenantSlug, organizerToken);
        Guid stamp = await GetConcurrencyStampAsync(organizer, AgentBrowserPersonaCatalog.EventId);
        using var update = UpdateRequest(AgentBrowserPersonaCatalog.EventId, stamp);
        update.Content = new StringContent(
            "{\"title\":{\"value\":\"Updated agent browser event\"}}", Encoding.UTF8, "application/json");
        using var response = await organizer.SendAsync(update);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        await AssertEventTitleAsync(
            fixture, DefaultTenantSlug, AgentBrowserPersonaCatalog.EventId, "Updated agent browser event");
    }

    [Test]
    public async Task NativeGrantRevocationDeniesTheSameTenantAdministratorToken()
    {
        await using var fixture = await AgentBrowserPersonaFixture.CreateAsync();
        await fixture.RunAsync();
        string administratorToken = await fixture.LoginAsync(AgentBrowserPersonaCatalog.Administrator);
        string tenantAdministratorToken = await fixture.LoginAsync(AgentBrowserPersonaCatalog.TenantAdministrator);

        using var tenantAdministrator = fixture.CreateTenantClient(DefaultTenantSlug, tenantAdministratorToken);
        using (var before = await tenantAdministrator.GetAsync($"/api/event/{AgentBrowserPersonaCatalog.EventId:D}"))
        {
            await Assert.That(before.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var document = await AgentBrowserPersonaFixture.ReadJsonAsync(before);
            await AssertLinkAsync(document.RootElement.GetProperty("_links"), "moderate-light", expected: true, "tenant administrator before revocation");
        }

        Guid grantId;
        await using (var database = fixture.CreateDatabase())
        {
            grantId = await database.TenantUserRoleGrants
                .Where(row => row.TenantId == AgentBrowserPersonaCatalog.TenantId
                    && row.RoleId == (int)RoleEnum.TenantAdmin
                    && row.TenantUser.UserId == AgentBrowserPersonaCatalog.TenantAdministrator.SubjectId
                    && row.RevokedAt == null)
                .Select(row => row.Id)
                .SingleAsync();
        }

        using (var administrator = fixture.CreateTenantClient(DefaultTenantSlug, administratorToken))
        using (var revoked = await administrator.DeleteAsync($"/api/tenant-user-role-grants/{grantId:D}"))
            await Assert.That(revoked.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

        using (var after = await tenantAdministrator.GetAsync($"/api/event/{AgentBrowserPersonaCatalog.EventId:D}"))
        {
            await Assert.That(after.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var document = await AgentBrowserPersonaFixture.ReadJsonAsync(after);
            await AssertLinkAsync(document.RootElement.GetProperty("_links"), "moderate-light", expected: false, "same token after revocation");
        }

        using var mutation = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/event/{AgentBrowserPersonaCatalog.EventId:D}/moderation/light")
        {
            Content = new StringContent("{\"reasonCode\":\"agent-browser-test\"}", Encoding.UTF8, "application/json")
        };
        using var denied = await tenantAdministrator.SendAsync(mutation);
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    private static async Task<Guid> GetConcurrencyStampAsync(HttpClient client, Guid eventId)
    {
        using var response = await client.GetAsync($"/api/event/{eventId:D}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = await AgentBrowserPersonaFixture.ReadJsonAsync(response);
        return document.RootElement.GetProperty("concurrencyStamp").GetGuid();
    }

    private static HttpRequestMessage UpdateRequest(Guid eventId, Guid concurrencyStamp)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/event/{eventId:D}")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{concurrencyStamp:D}\"");
        return request;
    }

    private static async Task AssertEventTitleAsync(
        AgentBrowserPersonaFixture fixture,
        string tenantSlug,
        Guid eventId,
        string expectedTitle)
    {
        using var client = fixture.CreateTenantClient(tenantSlug);
        using var response = await client.GetAsync($"/api/event/{eventId:D}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = await AgentBrowserPersonaFixture.ReadJsonAsync(response);
        await Assert.That(document.RootElement.GetProperty("title").GetString()).IsEqualTo(expectedTitle);
    }

    private static async Task AssertLinkAsync(JsonElement links, string relation, bool expected, string persona)
    {
        await Assert.That(links.TryGetProperty(relation, out _)).IsEqualTo(expected)
            .Because($"{persona} {(expected ? "must" : "must not")} receive the {relation} affordance");
    }

    private static void RecordLink(JsonElement links, string relation, bool expected, string persona, List<string> mismatches)
    {
        bool present = links.TryGetProperty(relation, out _);
        if (present != expected)
            mismatches.Add($"{persona}:{relation}:expected={expected}:actual={present}");
    }

    private sealed record PersonaAuthority(
        AgentBrowserPersona Persona,
        bool Edit,
        bool Delete,
        bool Moderate);
}
