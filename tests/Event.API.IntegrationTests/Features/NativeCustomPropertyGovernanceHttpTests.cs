using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.DTOs.CustomPropertyGovernance;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions.Enums;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed partial class NativeCustomPropertyGovernanceHttpTests
{
    private const string Root = "/api/admin/custom-property-definitions/governance-report";
    private static readonly DateTime LastUsed = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Test]
    public async Task Report_ReturnsOnlyActiveAmbientDefinitionsWithTruthfulUsageAndDisclosure()
    {
        await using var factory = await NativeCustomPropertyGovernanceFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.AdminId);
        var report = await ReportAsync(client);
        await Assert.That(report.TotalCount).IsEqualTo(4);
        await Assert.That(report.Items.Select(row => row.Key)).IsEquivalentTo(
            new[] { "a-none", "b-search", "c-moderation", "d-session" }, CollectionOrdering.Matching);
        await Assert.That(report.Items.All(row => row.TenantId == PlatformDefaults.DefaultTenantId)).IsTrue();
        var used = report.Items.Single(row => row.Key == "b-search");
        await Assert.That(used.ActiveInstanceCount).IsEqualTo(1);
        await Assert.That(used.LastUsedAt).IsEqualTo(LastUsed);
        await Assert.That(used.ExposureLevel).IsEqualTo(ExposureLevel.TenantAdminOnly);
        await Assert.That(used.PropertyType).IsEqualTo("Text");
        await Assert.That(used.IsSearchable && used.IsFilterable && used.IsExportable).IsTrue();
        await Assert.That(used.IsAnalyticsRelevant).IsFalse();
        await Assert.That(used.IsSystemOwned).IsTrue();
        await Assert.That(used.Recommendation).IsEqualTo(PromotionRecommendation.ConsiderProjectionFirst);
        await Assert.That(report.Items[0].LastUsedAt).IsNull();
        await Assert.That(report.Items[0].ActiveInstanceCount).IsEqualTo(0);
        var moderation = report.Items[2];
        await Assert.That(moderation.Recommendation).IsEqualTo(PromotionRecommendation.ConsiderLayer2Promotion);
        await Assert.That(moderation.ExposureLevel).IsEqualTo(ExposureLevel.TenantAdminOnly);
        await Assert.That(moderation.IsSearchable && moderation.IsFilterable && moderation.IsExportable
            && moderation.IsModerationRelevant && moderation.IsAnalyticsRelevant).IsTrue();
        using var response = await client.GetAsync(Url());
        var json = await response.Content.ReadAsStringAsync();
        await Assert.That(json).DoesNotContain("private-value-sentinel");
        await Assert.That(json).DoesNotContain("foreign-definition-sentinel");
        await Assert.That(json).DoesNotContain("deleted-definition-sentinel");
    }

    [Test]
    public async Task FiltersAndPages_CountMatchingRowsBeforePagingIncludingMatchesBeyondTheFirstPage()
    {
        await using var factory = await NativeCustomPropertyGovernanceFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var client = Client(factory, data.AdminId);
        var filtered = await ReportAsync(client, "&recommendation=ConsiderProjectionFirst&pageSize=1");
        await Assert.That(filtered.TotalCount).IsEqualTo(2);
        await Assert.That(filtered.TotalPages).IsEqualTo(2);
        await Assert.That(filtered.HasNextPage).IsTrue();
        await Assert.That(filtered.Items.Single().Key).IsEqualTo("b-search");
        var second = await ReportAsync(client, "&recommendation=ConsiderProjectionFirst&pageSize=1&pageNumber=2");
        await Assert.That(second.TotalCount).IsEqualTo(2);
        await Assert.That(second.Items.Single().Key).IsEqualTo("d-session");
        await Assert.That(second.HasNextPage).IsFalse();
        var scoped = await ReportAsync(client, "&scope=EventSession&recommendation=ConsiderProjectionFirst");
        await Assert.That(scoped.TotalCount).IsEqualTo(1);
        await Assert.That(scoped.Items.Single().EntityScope).IsEqualTo("EventSession");
        var empty = await ReportAsync(client, "&scope=Unknown");
        await Assert.That(empty.TotalCount).IsEqualTo(0);
        await Assert.That(empty.Items).IsEmpty();
        var beyond = await ReportAsync(client, "&pageNumber=10&pageSize=1");
        await Assert.That(beyond.TotalCount).IsEqualTo(4);
        await Assert.That(beyond.Items).IsEmpty();
    }

    [Test]
    public async Task Denials_RequirePersistedAdministrativeAuthorityAndRejectForeignTargetTenants()
    {
        await using var factory = await NativeCustomPropertyGovernanceFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var anonymous = factory.CreateClient();
        using var member = Client(factory, data.MemberId);
        using var foreignAdmin = Client(factory, data.ForeignAdminId);
        using var admin = Client(factory, data.AdminId);
        using (var response = await anonymous.GetAsync(Url()))
            await ProblemAsync(response, HttpStatusCode.Unauthorized);
        foreach (var client in new[] { member, foreignAdmin })
        {
            using var response = await client.GetAsync(Url());
            await ProblemAsync(response, HttpStatusCode.Forbidden);
        }
        using var foreign = await admin.GetAsync(Root + $"?tenantId={data.ForeignTenantId}");
        await ProblemAsync(foreign, HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task InvalidTransportFilters_RemainValidationProblems()
    {
        await using var factory = await NativeCustomPropertyGovernanceFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var admin = Client(factory, data.AdminId);
        foreach (var url in new[] { Root, Url("&pageSize=0"), Url("&pageNumber=0"), Url("&recommendation=999") })
        {
            using var response = await admin.GetAsync(url);
            await ProblemAsync(response, HttpStatusCode.BadRequest);
        }
    }

    private static string Url(string filter = "") => Root + $"?tenantId={PlatformDefaults.DefaultTenantId}" + filter;

    private static HttpClient Client(NativeCustomPropertyGovernanceFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        // Claims deliberately claim admin even for an ordinary persisted member.
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(userId, PlatformDefaults.DefaultTenantId));
        return client;
    }

    private static async Task<PaginatedResult<CustomPropertyGovernanceRowDto>> ReportAsync(HttpClient client, string filter = "")
    {
        using var response = await client.GetAsync(Url(filter));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PaginatedResult<CustomPropertyGovernanceRowDto>>(JsonOptions))!;
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        await Assert.That(response.StatusCode).IsEqualTo(status);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)status);
        await Assert.That(json.RootElement.TryGetProperty("items", out _)).IsFalse();
    }

    private static async Task<SeedData> SeedAsync(NativeCustomPropertyGovernanceFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var admin = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var member = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var other = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(db);
        var membership = await db.TenantUsers.Include(row => row.Tenant).SingleAsync(row => row.UserId == admin.UserId);
        GrantAdmin(db, membership);
        var foreignMembership = new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = other.TenantId,
            Tenant = null!,
            UserId = other.UserId,
            User = null!,
            ActorId = other.ActorId,
            Actor = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = LastUsed,
            CreatedAt = LastUsed
        };
        db.TenantUsers.Add(foreignMembership);
        GrantAdmin(db, foreignMembership);
        var ownEvent = await EventScenarioSeed.SeedPublishedEventAsync(db, admin.ActorId, admin.TenantId);
        var foreignEvent = await EventScenarioSeed.SeedPublishedEventAsync(db, other.ActorId, other.TenantId);
        var none = Definition(admin.TenantId, ownEvent.EventId, "a-none");
        var search = Definition(admin.TenantId, ownEvent.EventId, "b-search");
        search.IsSearchable = search.IsFilterable = search.IsExportable = search.IsSystemOwned = true;
        search.ExposureLevel = ExposureLevel.TenantAdminOnly;
        var moderation = Definition(admin.TenantId, ownEvent.EventId, "c-moderation");
        moderation.IsModerationRelevant = moderation.IsAnalyticsRelevant = moderation.IsSearchable
            = moderation.IsFilterable = moderation.IsExportable = true;
        moderation.ExposureLevel = ExposureLevel.TenantAdminOnly;
        var inactive = Definition(admin.TenantId, ownEvent.EventId, "inactive-definition-sentinel");
        inactive.IsActive = false;
        var deleted = Definition(admin.TenantId, ownEvent.EventId, "deleted-definition-sentinel");
        deleted.IsDeleted = true;
        db.EventCustomPropertyDefinitions.AddRange(none, search, moderation, inactive, deleted,
            Definition(other.TenantId, foreignEvent.EventId, "foreign-definition-sentinel"));
        var session = await db.EventSessions.SingleAsync(row => row.EventId == ownEvent.EventId);
        db.EventSessionCustomPropertyDefinitions.Add(new EventSessionCustomPropertyDefinition
        {
            Id = Guid.CreateVersion7(),
            TenantId = admin.TenantId,
            EventSessionId = session.Id,
            Namespace = "tenant.custom",
            Key = "d-session",
            DisplayName = "Session searchable",
            IsActive = true,
            IsSearchable = true,
            PropertyType = PropertyType.Text,
            ExposureLevel = ExposureLevel.Public,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        db.EventCustomPropertyValues.AddRange(new EventCustomPropertyValue
        {
            Id = Guid.CreateVersion7(),
            TenantId = admin.TenantId,
            EventId = ownEvent.EventId,
            EventCustomPropertyDefinitionId = search.Id,
            TextValue = "private-value-sentinel",
            UpdatedAt = LastUsed,
            ConcurrencyStamp = Guid.CreateVersion7()
        }, new EventCustomPropertyValue
        {
            Id = Guid.CreateVersion7(),
            TenantId = admin.TenantId,
            EventId = ownEvent.EventId,
            EventCustomPropertyDefinitionId = search.Id,
            Ordinal = 1,
            TextValue = "deleted-value-sentinel",
            UpdatedAt = LastUsed.AddDays(1),
            IsDeleted = true,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        await db.SaveChangesAsync();
        return new(admin.UserId, member.UserId, other.UserId, other.TenantId);
    }

    private static void GrantAdmin(ExploreDbContext db, TenantUser membership) => db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
    {
        Id = Guid.CreateVersion7(),
        TenantId = membership.TenantId,
        Tenant = membership.Tenant,
        TenantUserId = membership.Id,
        TenantUser = membership,
        RoleId = (int)RoleEnum.TenantAdmin,
        Role = null!,
        RoleScopeId = (int)RoleScopeEnum.Tenant
    });

    private static EventCustomPropertyDefinition Definition(Guid tenantId, Guid eventId, string key) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        EventId = eventId,
        Namespace = "tenant.custom",
        Key = key,
        DisplayName = key,
        IsActive = true,
        PropertyType = PropertyType.Text,
        ExposureLevel = ExposureLevel.Public,
        ConcurrencyStamp = Guid.CreateVersion7()
    };

    private sealed record SeedData(Guid AdminId, Guid MemberId, Guid ForeignAdminId, Guid ForeignTenantId);
}
