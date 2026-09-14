using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventPublicActions.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    public async Task PublicSurface_OnlyEligibleActionsRedirectAndEmitBoundedMetrics()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var client = Client(factory);
        using var meter = factory.Services.GetRequiredService<IMeterFactory>().Create(BusinessMetrics.MeterName);
        using var capture = new EngagementCapture(meter);
        using var list = await client.GetAsync(Route(data.PublicId));
        await Assert.That(list.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("_embedded").GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())).IsEquivalentTo(new[] { data.EarlierId, data.ActionId });
        await Assert.That(json.RootElement.GetProperty("_embedded").GetProperty("items")[0].GetProperty("id").GetGuid())
            .IsEqualTo(data.EarlierId);
        using var detail = await client.GetAsync(Route(data.PublicId, data.ActionId));
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        await Assert.That(detailJson.RootElement.GetProperty("_links").TryGetProperty("edit", out _)).IsFalse();
        await Assert.That(detailJson.RootElement.GetProperty("destinationDomain").GetString()).IsEqualTo("example.test");
        await Assert.That(detailJson.RootElement.GetProperty("rel").GetString()).IsEqualTo("noopener noreferrer");

        var denied = data.HiddenParents.Concat(data.HiddenActions.Select(id => (data.PublicId, id)))
            .Concat(new[] { (data.PublicId, data.OtherActionId), (data.PublicId, data.ForeignActionId),
                (data.ForeignId, data.ForeignActionId), (data.PublicId, Guid.CreateVersion7()),
                (Guid.CreateVersion7(), data.ActionId) });
        foreach (var (eventId, actionId) in denied)
        {
            using var missing = await client.GetAsync(Route(eventId, actionId));
            await Assert.That(missing.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            using var redirect = await client.GetAsync(Route(eventId, actionId) + "/redirect?surface=event_detail");
            await Assert.That(redirect.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
            using var problem = JsonDocument.Parse(await redirect.Content.ReadAsStringAsync());
            await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo(404);
            await Assert.That(problem.RootElement.GetProperty("code").GetString()).IsEqualTo("resource_not_found");
        }
        foreach (var (eventId, _) in data.HiddenParents.Append((data.ForeignId, data.ForeignActionId)))
        {
            using var hiddenList = await client.GetAsync(Route(eventId));
            await Assert.That(hiddenList.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var hiddenJson = JsonDocument.Parse(await hiddenList.Content.ReadAsStringAsync());
            await Assert.That(hiddenJson.RootElement.GetProperty("_embedded").GetProperty("items").GetArrayLength()).IsEqualTo(0);
        }
        await Assert.That(capture.Snapshot()).IsEmpty();
        foreach (var surface in new[] { "event_detail", "private-contact@example.test" })
        {
            using var redirect = await client.GetAsync(Route(data.ExternalId, data.RegistrationId) + "/redirect?surface=" + Uri.EscapeDataString(surface));
            await Assert.That(redirect.StatusCode).IsEqualTo(HttpStatusCode.Found);
            await Assert.That(redirect.Headers.Location?.OriginalString).IsEqualTo("https://example.test/original?ref=public");
            await Assert.That(redirect.Headers.CacheControl?.NoStore).IsTrue();
        }
        var measurements = capture.Snapshot();
        await Assert.That(measurements.Length).IsEqualTo(2);
        await Assert.That(measurements.Select(item => item.Tags["surface"])).IsEquivalentTo(new object?[] { "event_detail", "other" });
        foreach (var measurement in measurements)
        {
            await Assert.That(measurement.Value).IsEqualTo(1);
            await Assert.That(measurement.Tags.Keys).IsEquivalentTo(new[] { "action_kind", "surface", "outcome" });
            await Assert.That(measurement.Tags["action_kind"]).IsEqualTo("external_registration");
            await Assert.That(measurement.Tags["outcome"]).IsEqualTo("redirect_issued");
        }
    }

    [Test]
    public async Task OwnerHttpWrites_PreservePendingReviewValidationAndConditionalDelete()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        using var owner = Client(factory, data.OwnerId);
        using var anonymous = Client(factory);
        using var unauthorized = await anonymous.PostAsJsonAsync(Route(data.PublicId), Input());
        await Assert.That(unauthorized.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        foreach (var eventId in new[] { data.PublicId, data.PrivateId, data.DraftId })
        {
            using var create = await owner.PostAsJsonAsync(Route(eventId), Input());
            await Assert.That(create.StatusCode).IsEqualTo(HttpStatusCode.Created);
            var created = (await create.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!;
            await Assert.That(create.Headers.Location?.AbsolutePath).IsEqualTo(Route(eventId, created.Id));
            var initial = (await State(factory, created.Id))!;
            await Assert.That(initial.HealthStateId).IsEqualTo((int)EventPublicActionHealthStateEnum.PendingReview);
            await Assert.That(initial.Label).IsEqualTo("Source");
            await Assert.That(await Detail(factory, eventId, created.Id)).IsNull();
            using var update = await owner.PutAsJsonAsync(Route(eventId, created.Id), Input(initial.Stamp) with { Url = "https://example.test/revised" });
            await Assert.That(update.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var updated = (await State(factory, created.Id))!;
            await Assert.That(updated.Url).IsEqualTo("https://example.test/revised");
            await Assert.That(updated.Stamp).IsNotEqualTo(initial.Stamp);
            using var stale = await owner.PutAsJsonAsync(Route(eventId, created.Id), Input(initial.Stamp));
            await Assert.That(stale.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            foreach (var header in new string?[] { null, "invalid", initial.Stamp.ToString(), $"\"{initial.Stamp}\"" })
            {
                using var rejected = await Delete(owner, Route(eventId, created.Id), header);
                await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
                using var problem = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
                await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo(400);
                await Assert.That(problem.RootElement.GetProperty("errors").EnumerateObject().Any()).IsTrue();
                await Assert.That(await State(factory, created.Id)).IsEqualTo(updated);
            }
            using var deleted = await Delete(owner, Route(eventId, created.Id), $"\"{updated.Stamp}\"");
            await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await State(factory, created.Id)).IsNull();
        }
        var active = (await Detail(factory, data.PublicId, data.ActionId))!;
        using var editActive = await owner.PutAsJsonAsync(Route(data.PublicId, data.ActionId), Input(active.ConcurrencyStamp));
        await Assert.That(editActive.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await State(factory, data.ActionId))!.HealthStateId).IsEqualTo((int)EventPublicActionHealthStateEnum.PendingReview);
        using var deniedRedirect = await anonymous.GetAsync(Route(data.PublicId, data.ActionId) + "/redirect");
        await Assert.That(deniedRedirect.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ProtectedPorts_RejectForgedForeignAndMismatchedAuthorityWithoutMutation()
    {
        await using var factory = new NativeEventSeriesFactory();
        var data = await Seed(factory);
        var original = (await State(factory, data.ActionId))!;
        var other = await State(factory, data.OtherActionId);
        var foreign = await State(factory, data.ForeignActionId, data.ForeignTenantId);
        var actionIds = await ActionIds(factory, data.PublicId);
        using (var outsider = Scope(factory, data.OutsiderId))
        {
            var create = outsider.ServiceProvider.GetRequiredService<ICommandHandler<CreateEventPublicActionCommand, BaseCommandResponse<Guid>>>();
            var update = outsider.ServiceProvider.GetRequiredService<ICommandHandler<UpdateEventPublicActionCommand, BaseCommandResponse<Guid>>>();
            var delete = outsider.ServiceProvider.GetRequiredService<ICommandHandler<DeleteEventPublicActionCommand, BaseCommandResponse<Guid>>>();
            await Assert.That(async () => await create.ExecuteAsync(new() { EventId = data.PublicId, Action = Input() }, default)).Throws<AuthorizationException>();
            await Assert.That(async () => await update.ExecuteAsync(new() { EventId = data.PublicId, ActionId = data.ActionId, Action = Input(original.Stamp) }, default)).Throws<AuthorizationException>();
            await Assert.That(async () => await delete.ExecuteAsync(new() { EventId = data.PublicId, ActionId = data.ActionId, ExpectedConcurrencyStamp = original.Stamp }, default)).Throws<AuthorizationException>();
        }
        using (var scope = Scope(factory, data.OwnerId))
        {
            var update = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateEventPublicActionCommand, BaseCommandResponse<Guid>>>();
            var delete = scope.ServiceProvider.GetRequiredService<ICommandHandler<DeleteEventPublicActionCommand, BaseCommandResponse<Guid>>>();
            foreach (var actionId in new[] { data.OtherActionId, data.ForeignActionId, Guid.CreateVersion7() })
            {
                await Assert.That((await update.ExecuteAsync(new() { EventId = data.PublicId, ActionId = actionId, Action = Input(original.Stamp) }, default)).IsSuccess).IsFalse();
                await Assert.That((await delete.ExecuteAsync(new() { EventId = data.PublicId, ActionId = actionId, ExpectedConcurrencyStamp = original.Stamp }, default)).IsSuccess).IsFalse();
            }
            foreach (var eventId in new[] { data.OtherId, data.ForeignId, Guid.CreateVersion7() })
                await Assert.That(async () => await update.ExecuteAsync(new() { EventId = eventId, ActionId = data.ActionId, Action = Input(original.Stamp) }, default)).Throws<AuthorizationException>();
        }
        using var attacker = Client(factory, data.OutsiderId);
        using var forged = await attacker.PostAsJsonAsync(Route(data.PublicId), new
        {
            kindId = 1, url = "https://example.test/forged", userId = data.OwnerId,
            tenantId = data.ForeignTenantId, eventId = data.OtherId, authorizationFacts = new { userId = data.OwnerId }
        });
        await Assert.That(forged.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using var unknownMembers = JsonDocument.Parse(await forged.Content.ReadAsStringAsync());
        await Assert.That(unknownMembers.RootElement.GetProperty("errors").EnumerateObject().Any()).IsTrue();
        using var denied = await attacker.PostAsJsonAsync(Route(data.PublicId), Input());
        await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(await State(factory, data.ActionId)).IsEqualTo(original);
        await Assert.That(await State(factory, data.OtherActionId)).IsEqualTo(other);
        await Assert.That(await State(factory, data.ForeignActionId, data.ForeignTenantId)).IsEqualTo(foreign);
        await Assert.That(await ActionIds(factory, data.PublicId)).IsEquivalentTo(actionIds);
    }

    private static async Task<HttpResponseMessage> Delete(HttpClient client, string route, string? stamp)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, route);
        if (stamp is not null) request.Headers.TryAddWithoutValidation("If-Match", stamp);
        return await client.SendAsync(request);
    }

    private sealed class EngagementCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly Lock _gate = new();
        private readonly List<Engagement> _items = [];
        public EngagementCapture(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter) && instrument.Name == "explore.event_public_actions.engagements")
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
            {
                lock (_gate) _items.Add(new(value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
            });
            _listener.Start();
        }
        public Engagement[] Snapshot() { lock (_gate) return [.. _items]; }
        public void Dispose() => _listener.Dispose();
    }
    private sealed record Engagement(long Value, IReadOnlyDictionary<string, object?> Tags);
}
