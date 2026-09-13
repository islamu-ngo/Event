using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Explore.Domain.Constants;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventDay;
using Explore.Application.Features.EventDays.Requests.Commands;
using Explore.Application.Features.EventDays.Requests.Queries;
using Explore.Application.Operations;
using Explore.Application.Operations.Decorators;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed partial class NativeEventDayHttpTests
{
    [Test]
    public async Task NativePorts_AreProtectedAndPreserveNullableDetails()
    {
        await using var factory = await DayFactory.CreateAsync();
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        await Assert.That(scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventDayCommand, BaseCommandResponse<Guid>>>()
            is AuthorizationCommandHandlerDecorator<CreateEventDayCommand, BaseCommandResponse<Guid>>).IsTrue();
        await Assert.That(scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateEventDayCommand, BaseCommandResponse<Guid>>>()
            is AuthorizationCommandHandlerDecorator<UpdateEventDayCommand, BaseCommandResponse<Guid>>).IsTrue();
        await Assert.That(scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteEventDayCommand, BaseCommandResponse<Guid>>>()
            is AuthorizationCommandHandlerDecorator<DeleteEventDayCommand, BaseCommandResponse<Guid>>).IsTrue();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventDaysByEventRequest, List<EventDayListDto>>>()
            is AuthorizationQueryHandlerDecorator<GetEventDaysByEventRequest, List<EventDayListDto>>).IsTrue();
        await Assert.That(scope.ServiceProvider.GetRequiredService<IQueryHandler<GetManagedEventDaysByEventRequest, List<EventDayListDto>>>()
            is AuthorizationQueryHandlerDecorator<GetManagedEventDaysByEventRequest, List<EventDayListDto>>).IsTrue();
        var detail = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventDayDetailRequest, EventDayDto?>>();
        await Assert.That(detail is AuthorizationQueryHandlerDecorator<GetEventDayDetailRequest, EventDayDto?>).IsTrue();
        await Assert.That(await detail.QueryAsync(new(Guid.CreateVersion7()), default)).IsNull();
        await Assert.That(await detail.QueryAsync(new(factory.ForeignDayId), default)).IsNull();
        await Assert.That((await detail.QueryAsync(new(factory.PublicDayId), default))!.Id).IsEqualTo(factory.PublicDayId);
        await factory.Services.ValidateNativeOperationsDeepAsync();
    }

    [Test]
    public async Task PublicAndManagedReads_PreservePublicationTenantAndNullableDetail()
    {
        await using var factory = await DayFactory.CreateAsync();
        using var anonymous = factory.CreateClient();
        using var owner = factory.Client(factory.OwnerId);
        using var outsider = factory.Client(factory.OutsiderId);
        var visible = await CollectionAsync(anonymous, Public(factory.PublicEventId));
        await Assert.That(Items(visible).Select(item => item.GetProperty("sortOrder").GetInt32())).IsEquivalentTo(new[] { 1, 2 });
        await Assert.That(visible.GetProperty("_links").TryGetProperty("self", out _)).IsTrue();
        // Existing public eligibility is parent-based, not a new per-day publication rule.
        using var detail = await anonymous.GetAsync(Detail(factory.PublicDayId));
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await JsonAsync(detail)).GetProperty("tenantId").GetGuid()).IsEqualTo(PlatformDefaults.DefaultTenantId);
        foreach (var hidden in new[] { factory.PrivateEventId, factory.DraftEventId, factory.ForeignEventId, factory.DeletedEventId, Guid.CreateVersion7() })
            await Assert.That(Items(await CollectionAsync(anonymous, Public(hidden)))).IsEmpty();
        foreach (var hidden in new[] { factory.ForeignDayId, factory.DeletedParentDayId, Guid.CreateVersion7() })
        {
            using var missing = await anonymous.GetAsync(Detail(hidden));
            await ProblemAsync(missing, HttpStatusCode.NotFound);
        }
        using (var unauthenticated = await anonymous.GetAsync(Managed(factory.PrivateEventId)))
            await ProblemAsync(unauthenticated, HttpStatusCode.Unauthorized);
        using (var denied = await outsider.GetAsync(Managed(factory.PrivateEventId)))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        foreach (var hidden in new[] { factory.ForeignEventId, factory.DeletedEventId, Guid.CreateVersion7() })
        {
            using var denied = await owner.GetAsync(Managed(hidden));
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        }
        await Assert.That(Items(await CollectionAsync(owner, Managed(factory.PrivateEventId))).Length).IsEqualTo(1);
        await Assert.That(Items(await CollectionAsync(owner, Managed(factory.DraftEventId))).Length).IsEqualTo(1);
    }

    [Test]
    public async Task Writes_PreserveAuthorizationIfMatchImageValidationAndDurability()
    {
        await using var factory = await DayFactory.CreateAsync();
        using var owner = factory.Client(factory.OwnerId);
        using var outsider = factory.Client(factory.OutsiderId);
        using var anonymous = factory.CreateClient();
        var input = new { eventId = factory.PublicEventId, localDate = "2027-02-01", label = "Opening", isPublished = true, sortOrder = 4, allowsDayScopeRegistration = true };
        using (var denied = await anonymous.PostAsJsonAsync("/api/eventday", input))
            await ProblemAsync(denied, HttpStatusCode.Unauthorized);
        using (var denied = await outsider.PostAsJsonAsync("/api/eventday", input))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        foreach (var parent in new[] { factory.ForeignEventId, factory.DeletedEventId, Guid.CreateVersion7() })
        {
            using var denied = await owner.PostAsJsonAsync("/api/eventday", new { eventId = parent, localDate = "2027-02-01" });
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        }
        using var created = await owner.PostAsJsonAsync("/api/eventday", input);
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var id = (await JsonAsync(created)).GetProperty("id").GetGuid();
        await Assert.That(created.Headers.Location!.AbsolutePath).IsEqualTo(Detail(id));
        var before = await DetailAsync(owner, id);
        var stamp = before.GetProperty("concurrencyStamp").GetGuid();
        await Assert.That(before.GetProperty("allowsDayScopeRegistration").GetBoolean()).IsTrue();
        foreach (var header in new string?[] { null, "invalid", "*", stamp.ToString(), $"W/\"{stamp}\"", $"\"{Guid.Empty}\"" })
        {
            using var invalid = await PatchAsync(owner, id, new { sortOrder = new { value = 8 } }, header);
            await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        }
        using (var stale = await PatchAsync(owner, id, new { sortOrder = new { value = 5 } }, $"\"{Guid.CreateVersion7()}\""))
            await ProblemAsync(stale, HttpStatusCode.Conflict);
        using (var denied = await PatchAsync(outsider, id, new { sortOrder = new { value = 5 } }, $"\"{stamp}\""))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        using (var denied = await outsider.DeleteAsync(Detail(id)))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        using (var invalid = await PatchAsync(owner, id, new { bannerImage = new { value = new { hasValue = true, value = Guid.CreateVersion7() } } }, $"\"{stamp}\""))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        await Assert.That((await DetailAsync(owner, id)).GetProperty("concurrencyStamp").GetGuid()).IsEqualTo(stamp);
        foreach (var parent in new[] { factory.UnownedEventId, factory.ForeignEventId, factory.DeletedEventId, Guid.CreateVersion7() })
        {
            using var denied = await PatchAsync(owner, id, new { @event = new { eventId = parent } }, $"\"{stamp}\"");
            await ProblemAsync(denied, HttpStatusCode.BadRequest);
        }
        // Prove the outsider can write at the destination before attacking another owner's source.
        using (var destinationCreate = await outsider.PostAsJsonAsync("/api/eventday", new
            { eventId = factory.UnownedEventId, localDate = "2027-02-15" }))
            await Assert.That(destinationCreate.StatusCode).IsEqualTo(HttpStatusCode.Created);
        using (var denied = await PatchAsync(outsider, id, new { @event = new { eventId = factory.UnownedEventId } }, $"\"{stamp}\""))
            await ProblemAsync(denied, HttpStatusCode.Forbidden);
        await Assert.That(JsonElement.DeepEquals(await DetailAsync(owner, id), before)).IsTrue();
        foreach (var hidden in new[] { factory.ForeignDayId, factory.DeletedParentDayId, Guid.CreateVersion7() })
        {
            using var deniedUpdate = await PatchAsync(owner, hidden, new { sortOrder = new { value = 5 } }, $"\"{stamp}\"");
            await ProblemAsync(deniedUpdate, HttpStatusCode.Forbidden);
            using var deniedDelete = await owner.DeleteAsync(Detail(hidden));
            await ProblemAsync(deniedDelete, HttpStatusCode.Forbidden);
        }
        using (var changed = await PatchAsync(owner, id, new { sortOrder = new { value = 5 } }, $"\"{stamp}\""))
            await Assert.That(changed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var after = await DetailAsync(owner, id);
        await Assert.That(after.GetProperty("sortOrder").GetInt32()).IsEqualTo(5);
        await Assert.That(after.GetProperty("concurrencyStamp").GetGuid()).IsNotEqualTo(stamp);
        using (var moved = await PatchAsync(owner, id,
            new { @event = new { eventId = factory.PrivateEventId } }, $"\"{after.GetProperty("concurrencyStamp").GetGuid()}\""))
            await ProblemAsync(moved, HttpStatusCode.BadRequest);
        await Assert.That(System.Text.Json.JsonElement.DeepEquals(await DetailAsync(owner, id), after)).IsTrue();
        await Assert.That(Items(await CollectionAsync(owner, Managed(factory.PrivateEventId)))
            .Any(item => item.GetProperty("id").GetGuid() == id)).IsFalse();
        using (var sameParent = await PatchAsync(owner, id,
            new { @event = new { eventId = factory.PublicEventId } }, $"\"{after.GetProperty("concurrencyStamp").GetGuid()}\""))
            await Assert.That(sameParent.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (var deleted = await owner.DeleteAsync(Detail(id)))
        {
            await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(await deleted.Content.ReadAsStringAsync()).IsEmpty();
        }
        using var missing = await owner.GetAsync(Detail(id));
        await ProblemAsync(missing, HttpStatusCode.NotFound);
    }

    private static string Public(Guid parent) => $"/api/eventday/by-event/{parent}";
    private static string Managed(Guid parent) => $"/api/eventday/management/by-event/{parent}";
    private static string Detail(Guid id) => $"/api/eventday/{id}";
    private static JsonElement[] Items(JsonElement body) => body.GetProperty("_embedded").GetProperty("items").EnumerateArray().ToArray();
    private static async Task<JsonElement> CollectionAsync(HttpClient client, string route)
    {
        using var response = await client.GetAsync(route);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await JsonAsync(response);
    }
    private static Task<JsonElement> DetailAsync(HttpClient client, Guid id) => CollectionAsync(client, Detail(id));
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        // Controller-produced ProblemDetails retain the controller's existing JSON media type.
        var mediaType = expected is HttpStatusCode.NotFound or HttpStatusCode.BadRequest
            ? "application/json"
            : "application/problem+json";
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo(mediaType);
        await Assert.That((await JsonAsync(response)).GetProperty("status").GetInt32()).IsEqualTo((int)expected);
    }
    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, object body, string? stamp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, Detail(id)) { Content = JsonContent.Create(body) };
        if (stamp is not null)
            request.Headers.TryAddWithoutValidation("If-Match", stamp);
        return await client.SendAsync(request);
    }
}
