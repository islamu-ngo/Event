using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Explore.Application.Constants;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence.Seed;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Fixtures;

internal sealed partial class AgentBrowserPersonaFixture
{
    internal async Task<string> LoginAsync(AgentBrowserPersona persona, string? hostSlug = null)
    {
        using var hostClient = hostSlug is null ? null : CreateHostClient(hostSlug);
        using var response = await (hostClient ?? _client!).PostAsJsonAsync(
            "/api/auth/local/login",
            new LocalAuthRequestDto(persona.Email, _finalPassword),
            Token);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"agent_browser_persona_login_{persona.Name}_{(int)response.StatusCode}");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var root = document.RootElement;
        if (!root.GetProperty("success").GetBoolean()
            || root.GetProperty("userId").GetGuid() != persona.SubjectId
            || root.GetProperty("token").GetString() is not { Length: > 0 } token)
        {
            throw new InvalidOperationException($"agent_browser_persona_login_contract_{persona.Name}");
        }
        if (new JwtSecurityTokenHandler().ReadJwtToken(token).Subject != persona.SubjectId.ToString("D"))
            throw new InvalidOperationException($"agent_browser_persona_jwt_subject_{persona.Name}");

        return token;
    }

    internal HttpClient CreateTenantClient(string tenantSlug, string? bearerToken = null)
    {
        var client = _factory!.CreateClient();
        client.BaseAddress = new Uri($"http://{tenantSlug}.localhost");
        client.DefaultRequestHeaders.Add(TenantHeaderNames.TenantSlug, tenantSlug);
        if (bearerToken is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        return client;
    }

    internal HttpClient CreateHostClient(string tenantSlug)
    {
        var client = _factory!.CreateClient();
        client.BaseAddress = new Uri($"http://{tenantSlug}.localhost");
        return client;
    }

    internal static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
    }

    internal async Task AssertApiScopedEventReadableAsync()
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
            .SetTenant(AgentBrowserPersonaCatalog.TenantId);
        var repository = scope.ServiceProvider.GetRequiredService<IEventRepository>();
        await Assert.That(await repository.GetEventWithDetails(AgentBrowserPersonaCatalog.EventId)).IsNotNull();
    }

    internal async Task<Guid> CreateSameTenantUnrelatedEventAsync()
    {
        await using var database = CreateDatabase();
        DateTime now = TimeProvider.System.GetUtcNow().UtcDateTime;
        database.Organizations.Add(new Organization
        {
            Id = AgentBrowserPersonaCatalog.Id(330),
            Pii = new OrganizationPii { FullName = "Unrelated agent organization" },
            CreatedAt = now
        });
        database.Actors.Add(new Actor
        {
            Id = AgentBrowserPersonaCatalog.Id(331),
            OrganizationId = AgentBrowserPersonaCatalog.Id(330),
            ActorTypeId = (int)ActorTypeEnum.Organization,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Unrelated agent organization" },
            CreatedAt = now
        });
        database.Set<OrganizationTenant>().Add(new OrganizationTenant
        {
            Id = AgentBrowserPersonaCatalog.Id(332),
            TenantId = AgentBrowserPersonaCatalog.TenantId,
            Tenant = null!,
            OrganizationId = AgentBrowserPersonaCatalog.Id(330),
            Organization = null!,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!,
            IsVisible = true,
            IsOrganizerEligible = true,
            ApprovedAt = now,
            CreatedAt = now
        });

        Guid eventId = AgentBrowserPersonaCatalog.Id(333);
        var item = new Explore.Domain.Event
        {
            Id = eventId,
            TenantId = AgentBrowserPersonaCatalog.TenantId,
            Tenant = null!,
            ActorId = AgentBrowserPersonaCatalog.Id(331),
            Actor = null!,
            OrganizerActorId = AgentBrowserPersonaCatalog.Id(331),
            Title = "Another organization's event",
            Slug = "agent-unrelated",
            PublicCode = "agent0000333",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var session = new EventSession
        {
            Id = AgentBrowserPersonaCatalog.Id(334),
            EventId = eventId,
            Event = item,
            TenantId = AgentBrowserPersonaCatalog.TenantId,
            Tenant = null!,
            Title = item.Title,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        DateTimeOffset start = new(now.Date.AddDays(7), TimeSpan.Zero);
        session.Reschedule(UtcInstantRange.Create(start, start.AddHours(2)), "UTC", new EventScheduleProjectionCalculator());
        item.Sessions.Add(session);
        item.ApplyScheduleTimeZone("UTC", new EventScheduleProjectionCalculator());
        item.Publish(now);
        session.Publish(EventStatusEnum.Published, now);
        item.RecalculateScheduleSummaryFromSessions();
        database.Events.Add(item);
        await database.SaveChangesAsync(Token);
        return eventId;
    }
}
