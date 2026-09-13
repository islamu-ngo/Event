using System.Net;
using System.Text.Json;
using Explore.API.Mcp;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventProgram;
using Explore.Application.Features.EventPrograms.Requests.Queries;
using Explore.Application.Operations.Decorators;
using Explore.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[Category("Runtime")]
[NotInParallel("ApiTestFixture")]
public sealed partial class NativeEventProgramHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task BothSharedHandlerPorts_AreProtectedAndHttpPreservesPublicationTenantAndPrivacyBoundaries()
    {
        await using var factory = await ProgramFactory.CreateAsync();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var publicQuery = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventProgramSummaryRequest, EventProgramSummaryDto?>>();
        var managedQuery = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetManagedEventProgramSummaryRequest, EventProgramSummaryDto?>>();
        await Assert.That(publicQuery is AuthorizationQueryHandlerDecorator<GetEventProgramSummaryRequest, EventProgramSummaryDto?>).IsTrue();
        await Assert.That(managedQuery is AuthorizationQueryHandlerDecorator<GetManagedEventProgramSummaryRequest, EventProgramSummaryDto?>).IsTrue();
        await Assert.That(await publicQuery.QueryAsync(new(Guid.CreateVersion7()), default)).IsNull();
        using var anonymous = factory.CreateClient();
        using var owner = factory.Client(factory.OwnerId);
        using var outsider = factory.Client(factory.OutsiderId);
        var summary = await SummaryAsync(anonymous, Public(factory.PublicId));
        await Assert.That(Items(summary).Select(item => item.GetProperty("title").GetString())).IsEquivalentTo(new string?[]
            { "Public item 000", "Public item 001", "Unassigned public item", "Draft-group public item" });
        await Assert.That(summary.GetRawText()).DoesNotContain("Draft track");
        await Assert.That(summary.GetRawText()).DoesNotContain("Deleted track");
        var group = Groups(summary).Single(group => group.TryGetProperty("sessionGroupId", out var groupId)
            && groupId.ValueKind == JsonValueKind.String && groupId.GetGuid() == factory.GroupId);
        await Assert.That(group.GetProperty("eventLocation").GetProperty("fields").GetProperty("venueName").GetString()).IsEqualTo("Approved venue");
        foreach (var hidden in new[] { "Hidden city", "Hidden street", "Hidden postcode", factory.LocationId.ToString() })
            await Assert.That(summary.GetRawText()).DoesNotContain(hidden);
        await Assert.That(Groups(summary).Concat(Items(summary)).All(item => AbsentOrNull(item, "locationName") && AbsentOrNull(item, "roomName"))).IsTrue();
        foreach (var id in new[] { factory.PrivateId, factory.DraftId, factory.DeletedId, factory.ForeignId, Guid.CreateVersion7() })
        {
            using var response = await anonymous.GetAsync(Public(id));
            await ProblemAsync(response, HttpStatusCode.NotFound);
        }
        using (var response = await anonymous.GetAsync(Managed(factory.PublicId)))
            await ProblemAsync(response, HttpStatusCode.Unauthorized);
        using (var response = await outsider.GetAsync(Managed(factory.PublicId)))
            await ProblemAsync(response, HttpStatusCode.Forbidden);
        foreach (var id in new[] { factory.DeletedId, factory.ForeignId, Guid.CreateVersion7() })
        {
            using var response = await owner.GetAsync(Managed(id));
            await ProblemAsync(response, HttpStatusCode.Forbidden);
        }
        foreach (var id in new[] { factory.PrivateId, factory.DraftId, factory.PublicId })
        {
            using var response = await owner.GetAsync(Managed(id));
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            var managed = await JsonAsync(response);
            await Assert.That(Items(managed).Select(item => item.GetProperty("title").GetString())).Contains("Managed draft item");
            await Assert.That(Groups(managed).Concat(Items(managed)).All(item =>
                AbsentOrNull(item, "eventLocation") && AbsentOrNull(item, "locationName") && AbsentOrNull(item, "roomName"))).IsTrue();
        }
        var managedPublic = await SummaryAsync(owner, Managed(factory.PublicId));
        await Assert.That(managedPublic.GetRawText()).Contains("Draft track");
        await Assert.That(managedPublic.GetRawText()).DoesNotContain("Deleted item");
        var again = await SummaryAsync(anonymous, Public(factory.PublicId));
        await Assert.That(JsonElement.DeepEquals(again, summary)).IsTrue();
    }

    [Test]
    public async Task SqliteSummaries_PreservePublicAndManagedReadsAfterAgendaOrdering()
    {
        await using var factory = await ProgramFactory.CreateAsync(useSqlite: true);
        using var anonymous = factory.CreateClient();
        using var owner = factory.Client(factory.OwnerId);
        using var outsider = factory.Client(factory.OutsiderId);
        var summary = await SummaryAsync(anonymous, Public(factory.PublicId));
        await Assert.That(Items(summary).Count()).IsEqualTo(4);
        await Assert.That(Groups(summary).First().GetProperty("eventLocation").GetProperty("fields")
            .GetProperty("venueName").GetString()).IsEqualTo("Approved venue");
        await Assert.That(summary.GetProperty("readinessWarnings").EnumerateArray()
            .Select(warning => warning.GetProperty("path").GetString())).Contains("program.agenda[0].startTime");
        await Assert.That(summary.GetRawText()).DoesNotContain("Managed draft item");
        await Assert.That(summary.GetRawText()).DoesNotContain("Deleted item");
        foreach (var hidden in new[] { "Hidden street", "Hidden postcode", factory.LocationId.ToString() })
            await Assert.That(summary.GetRawText()).DoesNotContain(hidden);
        foreach (var id in new[] { factory.PublicId, factory.PrivateId, factory.DraftId })
        {
            using var response = await owner.GetAsync(Managed(id));
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(response.Headers.CacheControl?.Private).IsTrue();
            await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
            var managed = await JsonAsync(response);
            await Assert.That(Items(managed).Select(item => item.GetProperty("title").GetString())).Contains("Managed draft item");
            await Assert.That(Groups(managed).Concat(Items(managed)).All(item => AbsentOrNull(item, "eventLocation"))).IsTrue();
        }
        foreach (var id in new[] { factory.PrivateId, factory.DraftId, factory.DeletedId, factory.ForeignId, Guid.CreateVersion7() })
        {
            using var response = await anonymous.GetAsync(Public(id));
            await ProblemAsync(response, HttpStatusCode.NotFound);
        }
        using var unauthenticated = await anonymous.GetAsync(Managed(factory.PublicId));
        await ProblemAsync(unauthenticated, HttpStatusCode.Unauthorized);
        using var denied = await outsider.GetAsync(Managed(factory.PublicId));
        await ProblemAsync(denied, HttpStatusCode.Forbidden);
        await RequireVenuePrivacyReviewAsync(factory);
        var suppressed = await SummaryAsync(anonymous, Public(factory.PublicId));
        var suppressedLocation = Groups(suppressed).First().GetProperty("eventLocation");
        await Assert.That(suppressedLocation.GetProperty("state").GetString()).IsEqualTo("NeedsPrivacyReview");
        await Assert.That(AbsentOrNull(suppressedLocation, "fields")).IsTrue();
        await Assert.That(suppressed.GetRawText()).DoesNotContain("Approved venue");
    }

    [Test]
    public async Task SummaryGroupingAndMcp_KeepLocalDayOrderingApprovedDisclosureAndSafeNotFound()
    {
        await using var factory = await ProgramFactory.CreateAsync();
        using var anonymous = factory.CreateClient();
        var summary = await SummaryAsync(anonymous, Public(factory.PublicId));
        await Assert.That(summary.GetProperty("timeZoneId").GetString()).IsEqualTo("Europe/Brussels");
        await Assert.That(summary.GetProperty("sections").EnumerateArray().Select(section => section.GetProperty("sectionKey").GetString()))
            .IsEquivalentTo(new string?[] { factory.GroupId.ToString(), "unassigned" });
        var group = Groups(summary).First();
        await Assert.That(group.GetProperty("sessionGroupId").GetGuid()).IsEqualTo(factory.GroupId);
        var day = group.GetProperty("days").EnumerateArray().Single();
        await Assert.That(day.GetProperty("localDate").GetString()).IsEqualTo("2026-07-21");
        var items = day.GetProperty("items");
        await Assert.That(items[0].GetProperty("title").GetString()).IsEqualTo("Public item 001");
        await Assert.That(items[1].GetProperty("title").GetString()).IsEqualTo("Public item 000");
        await Assert.That(items[0].GetProperty("sortOrder").GetInt32()).IsEqualTo(1);
        await Assert.That(items[0].GetProperty("localStartTime").GetString()).IsEqualTo("01:30:00");
        await Assert.That(items[0].GetProperty("localEndTime").GetString()).IsEqualTo("02:30:00");
        await Assert.That(items[0].GetProperty("startsAtUtc").GetDateTimeOffset()).IsEqualTo(new DateTimeOffset(2026, 7, 20, 23, 30, 0, TimeSpan.Zero));
        await Assert.That(summary.GetProperty("readinessWarnings").EnumerateArray().Select(warning => warning.GetProperty("path").GetString()))
            .Contains("program.agenda[0].startTime");
        await Assert.That(items.EnumerateArray().SelectMany(item => item.GetProperty("readinessWarnings").EnumerateArray())
            .Any(warning => warning.GetProperty("path").GetString()!.EndsWith(".startTime", StringComparison.Ordinal))).IsFalse();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var tools = ActivatorUtilities.CreateInstance<EventManagementMcpTools>(scope.ServiceProvider);
        var text = await tools.GetPublicEventProgramSummaryAsync(factory.PublicId);
        var result = JsonSerializer.Deserialize<EventMcpProgramResultDescriptor>(text, JsonOptions)!;
        await Assert.That(result.Found).IsTrue();
        await Assert.That(result.Program!.ItemCount).IsEqualTo(4);
        await Assert.That(text).Contains("Approved venue");
        await Assert.That(result.Program.Sections[0].SessionGroups[0].Days[0].LocalDate).IsEqualTo(new DateOnly(2026, 7, 21));
        foreach (var hidden in new[] { "Hidden street", "Hidden postcode", "Hidden city", factory.LocationId.ToString(), "Managed draft item", "Deleted item" })
            await Assert.That(text).DoesNotContain(hidden);
        foreach (var id in new[] { factory.PrivateId, factory.DraftId, factory.DeletedId, factory.ForeignId, Guid.CreateVersion7() })
        {
            var missing = JsonSerializer.Deserialize<EventMcpProgramResultDescriptor>(await tools.GetPublicEventProgramSummaryAsync(id), JsonOptions)!;
            await Assert.That(missing.Found).IsFalse();
            await Assert.That(missing.FailureCode).IsEqualTo("not_found");
            await Assert.That(missing.Program).IsNull();
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventProgramSummaryRequest, EventProgramSummaryDto?>>();
        await Assert.ThrowsAsync<OperationCanceledException>(() => query.QueryAsync(new(factory.PublicId), cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => tools.GetPublicEventProgramSummaryAsync(factory.PublicId, cancellation.Token));
    }

    [Test]
    public async Task RealMcpSummary_BoundsItemsWithoutTruncatingHttpSummary()
    {
        await using var factory = await ProgramFactory.CreateAsync(publicItemCount: 101);
        using var client = factory.CreateClient();
        await Assert.That(Items(await SummaryAsync(client, Public(factory.PublicId))).Count()).IsEqualTo(103);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var tools = ActivatorUtilities.CreateInstance<EventManagementMcpTools>(scope.ServiceProvider);
        var result = JsonSerializer.Deserialize<EventMcpProgramResultDescriptor>(await tools.GetPublicEventProgramSummaryAsync(factory.PublicId), JsonOptions)!;
        await Assert.That(result.Found).IsTrue();
        await Assert.That(result.Program!.ItemCount).IsEqualTo(103);
        await Assert.That(result.Program.ProgramItemsWereTruncated).IsTrue();
        await Assert.That(result.Program.TruncatedFields).Contains("Program.Items");
        await Assert.That(result.Program.Sections.SelectMany(section => section.SessionGroups)
            .SelectMany(group => group.Days).SelectMany(day => day.Items).Count()).IsEqualTo(100);
    }

    private static bool AbsentOrNull(JsonElement item, string name) => !item.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;
    private static IEnumerable<JsonElement> Groups(JsonElement summary) =>
        summary.GetProperty("sections").EnumerateArray().SelectMany(section => section.GetProperty("sessionGroups").EnumerateArray());
    private static IEnumerable<JsonElement> Items(JsonElement summary) =>
        Groups(summary).SelectMany(group => group.GetProperty("days").EnumerateArray()).SelectMany(day => day.GetProperty("items").EnumerateArray());
    private static string Public(Guid id) => $"/api/event/{id}/program-summary";
    private static string Managed(Guid id) => $"/api/event/{id}/management-program-summary";
    private static async Task<JsonElement> SummaryAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await JsonAsync(response);
    }
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        await Assert.That(response.StatusCode).IsEqualTo(status);
        await Assert.That((await JsonAsync(response)).GetProperty("status").GetInt32()).IsEqualTo((int)status);
    }
}
