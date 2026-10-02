using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Domain.Services.Scheduling;
using Explore.Application.Models.PublicExperience;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed class EventDiscoveryDisclosureRaceTests
{
    [Test]
    public async Task CommittedRestrictionPrecedesConditionalDiscoveryReadWithoutCachedMembership()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        string marker = $"disclosure-{Guid.CreateVersion7():N}";
        Guid eventId = (await SeedAsync(factory, marker)).EventId;

        string route = $"/api/event?searchTerm={marker}&pageSize=20";
        using var initial = await client.GetAsync(route);
        await Assert.That(initial.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(initial.Headers.CacheControl?.NoStore).IsEqualTo(true);
        await Assert.That(await initial.Content.ReadAsStringAsync()).Contains(marker);

        var restrictionCommitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<HttpResponseMessage> heldRead = ReadAfterRestrictionAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var entity = await context.Events.FindAsync([eventId], deadline.Token);
            entity!.VisibilityTypeId = (int)VisibilityTypeEnum.Private;
            await context.SaveChangesAsync(deadline.Token);
        }
        restrictionCommitted.SetResult();

        using var response = await heldRead;
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync(deadline.Token);
        await Assert.That(body).DoesNotContain(marker);
        await Assert.That(body).DoesNotContain(eventId.ToString());

        async Task<HttpResponseMessage> ReadAfterRestrictionAsync()
        {
            await restrictionCommitted.Task.WaitAsync(deadline.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.IfNoneMatch.Add(
                initial.Headers.ETag ?? new EntityTagHeaderValue("\"previous-public-frame\""));
            return await client.SendAsync(request, deadline.Token);
        }
    }

    [Test]
    public async Task UnknownGovernedAreaDoesNotBecomeAnUnfilteredSearch()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        string marker = $"area-{Guid.CreateVersion7():N}";
        Guid eventId = (await SeedAsync(factory, marker)).EventId;

        using var control = await client.GetAsync($"/api/event?searchTerm={marker}");
        await Assert.That(control.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await control.Content.ReadAsStringAsync()).Contains(marker);

        using var response = await client.GetAsync(
            $"/api/event?searchTerm={marker}&areaId={Guid.CreateVersion7():D}");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain(marker);
        await Assert.That(body).DoesNotContain(eventId.ToString());
    }

    [Test]
    public async Task CommittedRedactionDoesNotReplayCachedPublicDetailFields()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        string marker = $"detail-{Guid.CreateVersion7():N}";
        string replacement = $"sanitized-{Guid.CreateVersion7():N}";
        var seeded = await SeedAsync(factory, marker);
        string route = $"/api/event/{seeded.EventId:D}";
        using var initial = await client.GetAsync(route);
        await Assert.That(initial.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await initial.Content.ReadAsStringAsync()).Contains(marker);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var entity = await context.Events.FindAsync(seeded.EventId);
            entity!.Title = replacement;
            await context.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        request.Headers.IfNoneMatch.Add(
            initial.Headers.ETag ?? new EntityTagHeaderValue("\"previous-public-detail\""));
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).DoesNotContain(marker);
        await Assert.That(body).Contains(replacement);
    }

    [Test]
    public async Task HomeDoesNotReplayARegionalEventAfterCommittedRestriction()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        string marker = $"home-{Guid.CreateVersion7():N}";
        Guid areaId = Guid.CreateVersion7();
        var seeded = await SeedAsync(factory, marker, areaId);
        string route = $"/api/public-experience/home?areaId={areaId:D}";
        using var initial = await client.GetAsync(route);
        await Assert.That(initial.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await initial.Content.ReadAsStringAsync()).Contains(marker);

        var committed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task<HttpResponseMessage> read = ReadAfterRestrictionAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var entity = await context.Events.FindAsync([seeded.EventId], deadline.Token);
            entity!.VisibilityTypeId = (int)VisibilityTypeEnum.Private;
            await context.SaveChangesAsync(deadline.Token);
        }
        committed.SetResult();

        using var response = await read;
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync(deadline.Token);
        await Assert.That(body).DoesNotContain(marker);
        await Assert.That(body).DoesNotContain(seeded.EventId.ToString());

        async Task<HttpResponseMessage> ReadAfterRestrictionAsync()
        {
            await committed.Task.WaitAsync(deadline.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.IfNoneMatch.Add(
                initial.Headers.ETag ?? new EntityTagHeaderValue("\"previous-home-frame\""));
            return await client.SendAsync(request, deadline.Token);
        }
    }

    [Test]
    public async Task RegionalMembershipAndCountDisappearAfterCityDisclosureIsRevoked()
    {
        await using var factory = new NativeEventTagsFactory(relational: true);
        using var client = factory.CreateClient();
        string marker = $"regional-{Guid.CreateVersion7():N}";
        Guid areaId = Guid.CreateVersion7();
        var seeded = await SeedAsync(factory, marker, areaId);
        string route = $"/api/event?searchTerm={marker}&areaId={areaId:D}";
        using var initial = await client.GetAsync(route);
        await Assert.That(initial.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var initialBody = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
        await Assert.That(initialBody.RootElement.GetProperty("totalCount").GetInt32()).IsEqualTo(1);
        var matching = initialBody.RootElement.GetProperty("_embedded")
            .GetProperty("items")[0].GetProperty("event").GetProperty("matchingSession");
        await Assert.That(matching.GetProperty("city").GetString()).IsEqualTo("Brussels");
        await Assert.That(matching.GetProperty("country").GetString()).IsEqualTo("BE");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var carrier = await context.EventLocations.FindAsync(seeded.EventLocationId);
            var audit = carrier!.ChangeDisclosurePolicy(
                EventLocationDisclosureFields.Country,
                (LocationDisclosureAudienceEnum)carrier.FullDetailsAudienceId,
                carrier.RevealFullDetailsFromUtc,
                carrier.PolicyVersion,
                seeded.UserId,
                EventLocationDisclosureAuditReasonEnum.GovernanceTightening,
                DateTime.UtcNow);
            context.Set<EventLocationDisclosureAudit>().Add(audit);
            await context.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        if (initial.Headers.ETag is not null)
            request.Headers.IfNoneMatch.Add(initial.Headers.ETag);
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string body = await response.Content.ReadAsStringAsync();
        using var parsed = JsonDocument.Parse(body);
        await Assert.That(parsed.RootElement.GetProperty("totalCount").GetInt32()).IsEqualTo(0);
        await Assert.That(body).DoesNotContain(marker);
        await Assert.That(body).DoesNotContain(seeded.EventId.ToString());
        await Assert.That(body).DoesNotContain("Brussels");
    }

    private static async Task<(Guid EventId, Guid? EventLocationId, Guid UserId)> SeedAsync(
        NativeEventTagsFactory factory, string marker, Guid? areaId = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var tenant = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context);
        var entity = new EventBuilder()
            .WithId(Guid.CreateVersion7())
            .WithTitle(marker)
            .WithActorId(tenant.ActorId)
            .WithTenantId(tenant.TenantId)
            .WithStatus(EventStatusEnum.Published)
            .WithFormat(areaId.HasValue ? EventFormatEnum.Local : EventFormatEnum.Digital)
            .Build();
        var now = DateTimeOffset.UtcNow;
        var start = now.AddDays(2);
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            EventId = entity.Id,
            Event = entity,
            TenantId = tenant.TenantId,
            Tenant = null!,
            StartTime = start,
            EndTime = start.AddHours(1)
        };
        session.ReprojectLocalTimes("UTC", new EventScheduleProjectionCalculator());
        EventLocation? carrier = null;
        if (areaId.HasValue)
        {
            var location = new Location
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.TenantId,
                FullName = "Public venue",
                City = "Brussels",
                Country = "BE"
            };
            location.SetManualAddress(Guid.CreateVersion7().ToString("N"), "0000");
            context.Locations.Add(location);
            carrier = EventLocation.CreatePhysical(
                tenant.TenantId, entity.Id, location.Id, tenant.UserId, now.UtcDateTime);
            var audit = carrier.ChangeDisclosurePolicy(
                EventLocationDisclosureFields.City | EventLocationDisclosureFields.Country,
                LocationDisclosureAudienceEnum.Never,
                null,
                carrier.PolicyVersion,
                tenant.UserId,
                EventLocationDisclosureAuditReasonEnum.GovernanceTightening,
                now.UtcDateTime,
                needsPrivacyReview: false);
            context.EventLocations.Add(carrier);
            context.Set<EventLocationDisclosureAudit>().Add(audit);
            session.AssignEventLocation(carrier);
            var config = new PublicDiscoveryAreasConfig(Areas:
            [
                new PublicDiscoveryAreaConfig(
                    areaId.Value, "Brussels", "Brussels", "BE", LocationIds: [location.Id])
            ]);
            context.Set<TenantSetting>().Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.TenantId,
                Tenant = null!,
                SettingKey = GovernanceSettingKeys.PublicExperience.DiscoveryAreas,
                Value = JsonSerializer.Serialize(JsonSerializer.Serialize(config, JsonSerializerOptions.Web))
            });
        }
        entity.Sessions.Add(session);
        entity.RecalculateScheduleSummaryFromSessions();
        context.Events.Add(entity);
        await context.SaveChangesAsync();
        return (entity.Id, carrier?.Id, tenant.UserId);
    }
}
