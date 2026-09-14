using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.Features.EventAgendaItems.Requests.Commands;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Application.Operations.Decorators;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed partial class NativeEventAgendaItemHttpTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 20, 23, 30, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task RegisteredPorts_AreProtectedAndPublicDetailsRemainNullable()
    {
        await using var factory = await AgendaFactory.CreateAsync();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        await Assert.That(scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventAgendaItemCommand, BaseCommandResponse<Guid>>>()
            is AuthorizationCommandHandlerDecorator<CreateEventAgendaItemCommand, BaseCommandResponse<Guid>>).IsTrue();
        await Assert.That(scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateEventAgendaItemCommand, BaseCommandResponse<Guid>>>()
            is AuthorizationCommandHandlerDecorator<UpdateEventAgendaItemCommand, BaseCommandResponse<Guid>>).IsTrue();
        await Assert.That(scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteEventAgendaItemCommand, BaseCommandResponse<Guid>>>()
            is AuthorizationCommandHandlerDecorator<DeleteEventAgendaItemCommand, BaseCommandResponse<Guid>>).IsTrue();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventAgendaItemsByEventRequest, List<EventAgendaItemListDto>>>()
            is AuthorizationQueryHandlerDecorator<GetEventAgendaItemsByEventRequest, List<EventAgendaItemListDto>>).IsTrue();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IQueryHandler<GetManagedEventAgendaItemsByEventRequest, List<EventAgendaItemListDto>>>()
            is AuthorizationQueryHandlerDecorator<GetManagedEventAgendaItemsByEventRequest, List<EventAgendaItemListDto>>).IsTrue();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IQueryHandler<GetManagedEventAgendaItemDetailRequest, EventAgendaItemDto?>>()
            is AuthorizationQueryHandlerDecorator<GetManagedEventAgendaItemDetailRequest, EventAgendaItemDto?>).IsTrue();
        var detail = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventAgendaItemDetailRequest, EventAgendaItemDto?>>();
        await Assert.That(detail is AuthorizationQueryHandlerDecorator<GetEventAgendaItemDetailRequest, EventAgendaItemDto?>).IsTrue();
        await Assert.That(await detail.QueryAsync(new(Guid.CreateVersion7()), default)).IsNull();
        await Assert.That(await detail.QueryAsync(new(factory.ForeignItemId), default)).IsNull();
        await Assert.That((await detail.QueryAsync(new(factory.ItemId), default))!.Id).IsEqualTo(factory.ItemId);
    }

    [Test]
    public async Task PublicAndManagedReads_PreserveEligibilityApprovedVenueAndParentBinding()
    {
        await using var factory = await AgendaFactory.CreateAsync();
        using var anonymous = factory.CreateClient();
        using var owner = factory.Client(factory.OwnerId);
        using var outsider = factory.Client(factory.OutsiderId);
        var visible = await GetAsync(anonymous, Public(factory.PublicId));
        await Assert.That(Items(visible).Length).IsEqualTo(1);
        var detail = await GetAsync(anonymous, Detail(factory.ItemId));
        await Assert.That(detail.GetProperty("eventLocation").GetProperty("fields").GetProperty("venueName").GetString())
            .IsEqualTo("Approved agenda venue");
        foreach (var body in new[] { visible, detail })
            foreach (var hidden in new[] { "Hidden street", "Hidden postcode", "Hidden city", factory.LocationId.ToString() })
                await Assert.That(body.GetRawText()).DoesNotContain(hidden);
        foreach (var hidden in new[] { factory.PrivateId, factory.DraftId, factory.DeletedId, factory.ForeignId, Guid.CreateVersion7() })
            await Assert.That(Items(await GetAsync(anonymous, Public(hidden)))).IsEmpty();
        foreach (var hidden in new[] { factory.DeletedParentItemId, factory.ForeignItemId, Guid.CreateVersion7() })
        {
            using var missing = await anonymous.GetAsync(Detail(hidden));
            await ProblemAsync(missing, HttpStatusCode.NotFound);
        }
        using (var unauthenticated = await anonymous.GetAsync(Managed(factory.PublicId)))
            await ProblemAsync(unauthenticated, HttpStatusCode.Unauthorized);
        using (var denied = await outsider.GetAsync(Managed(factory.PublicId)))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        foreach (var parent in new[] { factory.PublicId, factory.PrivateId, factory.DraftId })
        {
            using var managed = await owner.GetAsync(Managed(parent));
            await Assert.That(managed.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(managed.Headers.CacheControl?.Private).IsTrue();
            await Assert.That(managed.Headers.CacheControl?.NoStore).IsTrue();
            await Assert.That(Items(await JsonAsync(managed)).Length).IsEqualTo(1);
        }
        var exact = await GetAsync(owner, ManagedDetail(factory.PublicId, factory.ItemId));
        await Assert.That(exact.GetProperty("locationId").GetGuid()).IsEqualTo(factory.LocationId);
        using (var forged = await owner.GetAsync(ManagedDetail(factory.PrivateId, factory.ItemId)))
            await ProblemAsync(forged, HttpStatusCode.NotFound);
        foreach (var parent in new[] { factory.ForeignId, factory.DeletedId, Guid.CreateVersion7() })
        {
            using var denied = await owner.GetAsync(Managed(parent));
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        }
        await Assert.That(JsonElement.DeepEquals(await GetAsync(anonymous, Detail(factory.ItemId)), detail)).IsTrue();

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
            var repository = scope.ServiceProvider.GetRequiredService<IEventLocationRepository>();
            var placement = (await repository.GetForUpdateAsync(factory.PlacementId, default))!;
            var audit = placement.ApplyGovernanceTightening(true, factory.OwnerId,
                new DateTime(2026, 8, 2, 10, 15, 0, DateTimeKind.Utc));
            scope.ServiceProvider.GetRequiredService<ExploreDbContext>().EventLocationDisclosureAudits.Add(audit);
            await repository.SaveChangesAsync(default);
        }
        var suppressed = await GetAsync(anonymous, Detail(factory.ItemId));
        await Assert.That(suppressed.GetProperty("eventLocation").GetProperty("state").GetString()).IsEqualTo("NeedsPrivacyReview");
        await Assert.That(suppressed.GetRawText()).DoesNotContain("Approved agenda venue");
        await Assert.That((await GetAsync(anonymous, Public(factory.PublicId))).GetRawText()).DoesNotContain("Approved agenda venue");
        await Assert.That((await GetAsync(owner, ManagedDetail(factory.PublicId, factory.ItemId))).GetProperty("locationId").GetGuid())
            .IsEqualTo(factory.LocationId);
    }

    [Test]
    public async Task OwnerCreate_UsesPersistedParentAuthorityAndResolvesLocalDay()
    {
        await using var factory = await AgendaFactory.CreateAsync();
        using var owner = factory.Client(factory.OwnerId);
        using var created = await owner.PostAsJsonAsync("/api/eventagendaitem", new
        {
            eventId = factory.PublicId, title = "Opening", startTime = Start, endTime = Start.AddHours(1), sortOrder = 4
        });
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var id = (await JsonAsync(created)).GetProperty("id").GetGuid();
        var item = await GetAsync(owner, ManagedDetail(factory.PublicId, id));
        await Assert.That(item.GetProperty("eventDayId").GetGuid()).IsEqualTo(factory.DayId);
        await Assert.That(item.GetProperty("localStartDate").GetString()).IsEqualTo("2026-07-21");
        await Assert.That(item.GetProperty("localStartTime").GetString()).IsEqualTo("01:30:00");
    }

    private static string Public(Guid parent) => $"/api/eventagendaitem/by-event/{parent}";
    private static string Managed(Guid parent) => $"/api/eventagendaitem/management/by-event/{parent}";
    private static string Detail(Guid id) => $"/api/eventagendaitem/{id}";
    private static string ManagedDetail(Guid parent, Guid id) => $"{Managed(parent)}/{id}";
    private static JsonElement[] Items(JsonElement body) => body.GetProperty("_embedded").GetProperty("items").EnumerateArray().ToArray();
    private static async Task<JsonElement> GetAsync(HttpClient client, string route)
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
    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, object body, string? stamp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, Detail(id)) { Content = JsonContent.Create(body) };
        if (stamp is not null)
            request.Headers.TryAddWithoutValidation("If-Match", stamp);
        return await client.SendAsync(request);
    }
}
