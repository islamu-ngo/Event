using System.Net;
using System.Text.Json;
using Explore.API.Controllers;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Agenda;
using Explore.Application.Features.Agenda.Requests.Queries;
using Explore.Application.Operations.Decorators;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Api.IntegrationTests.Features;

[Category("Runtime")]
[NotInParallel("ApiTestFixture")]
public sealed partial class NativeAgendaProjectionHttpTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task PublishedProjection_UsesRegisteredPublicPortAndMergesActualSqliteSchedule()
    {
        await using var factory = await ProjectionFactory.CreateAsync();
        using var client = factory.CreateClient();
        var projection = await ProjectionAsync(factory, client, factory.PublicId);
        await Assert.That(projection.EventId).IsEqualTo(factory.PublicId);
        await Assert.That(projection.EventTitle).IsEqualTo("Projection event");
        await Assert.That(projection.Timezone).IsEqualTo("Europe/Brussels");
        await Assert.That(projection.Days.Select(day => day.LocalDate).SequenceEqual(new[]
            { new DateOnly(2026, 7, 23), new DateOnly(2026, 7, 22), new DateOnly(2026, 7, 21) })).IsTrue();
        var empty = projection.Days[0];
        await Assert.That(empty.EventDayId).IsEqualTo(factory.EmptyDayId);
        await Assert.That(empty.Entries).IsEmpty();
        await Assert.That(empty.Label).IsEqualTo("Opening day");
        await Assert.That(empty.Description).IsEqualTo("Published description");
        await Assert.That(empty.AllowsDayScopeRegistration).IsTrue();
        await Assert.That(projection.Days[1].EventDayId).IsNull();
        await Assert.That(projection.Days[1].IsPublished).IsTrue();
        await Assert.That(projection.Days[1].Entries.Single().Title).IsEqualTo("Unlabelled day");
        var schedule = projection.Days[2];
        await Assert.That(schedule.EventDayId).IsEqualTo(factory.ScheduleDayId);
        await Assert.That(schedule.Entries.Select(entry => entry.Title).SequenceEqual(new[]
            { "Local early", "Tie agenda", "Tie session", "Local later" })).IsTrue();
        await Assert.That(schedule.Entries.Select(entry => entry.EntryType).SequenceEqual(new[]
            { "Session", "AgendaItem", "Session", "AgendaItem" })).IsTrue();
        var early = schedule.Entries[0];
        await Assert.That(early.StartTime).IsEqualTo(new DateTimeOffset(2026, 7, 20, 23, 30, 0, TimeSpan.Zero));
        await Assert.That(early.EndTime).IsEqualTo(new DateTimeOffset(2026, 7, 21, 0, 30, 0, TimeSpan.Zero));
        await Assert.That(early.LocalStartDate).IsEqualTo(new DateOnly(2026, 7, 21));
        await Assert.That(early.LocalStartTime).IsEqualTo(new TimeOnly(1, 30));
        await Assert.That(early.LocalEndTime).IsEqualTo(new TimeOnly(2, 30));
        await Assert.That(early.LocalStartMinuteOfDay).IsEqualTo(90);
        await Assert.That(early.LocalEndMinuteOfDay).IsEqualTo(150);
        foreach (var entry in new[] { early, schedule.Entries[1] })
        {
            await Assert.That(entry.EventLocation!.EventLocationId).IsEqualTo(factory.PlacementId);
            await Assert.That(entry.EventLocation.Fields!.VenueName).IsEqualTo("Approved projection venue");
        }
        await AssertRedactedAsync(projection, factory);

        using var scope = Scope(factory);
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventAgendaProjectionRequest, EventAgendaProjectionDto?>>();
        await Assert.That(query is AuthorizationQueryHandlerDecorator<GetEventAgendaProjectionRequest, EventAgendaProjectionDto?>).IsTrue();
        var parameters = typeof(EventAgendaItemController).GetConstructors().Single().GetParameters();
        await Assert.That(parameters.Any(parameter => parameter.ParameterType.Namespace == "MediatR")).IsFalse();
        await Assert.That(parameters.Count(parameter => parameter.ParameterType.IsGenericType
            && (parameter.ParameterType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)
                || parameter.ParameterType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))).IsEqualTo(8);
    }

    [Test]
    public async Task UnavailableEvents_ReturnNullAnd404ProblemEvenForOwner()
    {
        await using var factory = await ProjectionFactory.CreateAsync();
        using var anonymous = factory.CreateClient();
        using var owner = factory.OwnerClient();
        using var scope = Scope(factory);
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventAgendaProjectionRequest, EventAgendaProjectionDto?>>();
        foreach (var id in new[] { factory.PrivateId, factory.DraftId, factory.ForeignId, factory.DeletedId, Guid.CreateVersion7() })
        {
            await Assert.That(await query.QueryAsync(new(id), default)).IsNull();
            foreach (var client in new[] { anonymous, owner })
            {
                using var response = await client.GetAsync(Route(id));
                await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
                await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/json");
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                await Assert.That(body.RootElement.GetProperty("status").GetInt32()).IsEqualTo(404);
                await Assert.That(body.RootElement.GetProperty("code").GetString()).IsEqualTo("resource_not_found");
                await Assert.That(body.RootElement.TryGetProperty("days", out _)).IsFalse();
            }
        }
    }

    [Test]
    public async Task RenewedPrivacyReview_SuppressesPreviouslyApprovedVenueWithoutHidingSchedule()
    {
        await using var factory = await ProjectionFactory.CreateAsync();
        using var client = factory.CreateClient();
        var before = await ProjectionAsync(factory, client, factory.PublicId);
        var physicalEntries = new[]
        {
            before.Days.SelectMany(day => day.Entries).Single(entry => entry.EntryType == "Session" && entry.Title == "Local early"),
            before.Days.SelectMany(day => day.Entries).Single(entry => entry.EntryType == "AgendaItem" && entry.Title == "Tie agenda")
        };
        foreach (var entry in physicalEntries)
        {
            await Assert.That(entry.EventLocation).IsNotNull();
            await Assert.That(entry.EventLocation!.EventLocationId).IsEqualTo(factory.PlacementId);
            await Assert.That(entry.EventLocation.State).IsEqualTo(EventLocationDisclosureState.Available);
            await Assert.That(entry.EventLocation.Fields).IsNotNull();
            await Assert.That(entry.EventLocation.Fields!.VenueName).IsEqualTo("Approved projection venue");
        }
        using (var scope = Scope(factory))
        {
            var repository = scope.ServiceProvider.GetRequiredService<IEventLocationRepository>();
            var placement = await repository.GetForUpdateAsync(factory.PlacementId, default)
                ?? throw new InvalidOperationException("Seeded placement is missing.");
            var audit = placement.ApplyGovernanceTightening(true, factory.OwnerId,
                new DateTime(2026, 8, 2, 10, 15, 0, DateTimeKind.Utc));
            scope.ServiceProvider.GetRequiredService<ExploreDbContext>().EventLocationDisclosureAudits.Add(audit);
            await repository.SaveChangesAsync(default);
        }
        var after = await ProjectionAsync(factory, client, factory.PublicId);
        await Assert.That(after.Days.SelectMany(day => day.Entries).Select(entry => entry.Id)
            .SequenceEqual(before.Days.SelectMany(day => day.Entries).Select(entry => entry.Id))).IsTrue();
        foreach (var previousEntry in physicalEntries)
        {
            var entry = after.Days.SelectMany(day => day.Entries).Single(entry => entry.Id == previousEntry.Id);
            await Assert.That(entry.EventLocation).IsNotNull();
            await Assert.That(entry.EventLocation!.EventLocationId).IsEqualTo(previousEntry.EventLocation!.EventLocationId);
            await Assert.That(entry.EventLocation!.State).IsEqualTo(EventLocationDisclosureState.NeedsPrivacyReview);
            await Assert.That(entry.EventLocation.Fields).IsNull();
        }
        await Assert.That(JsonSerializer.Serialize(after, JsonOptions)).DoesNotContain("Approved projection venue");
        await AssertRedactedAsync(after, factory);
    }

    [Test]
    public async Task CanonicalEligibility_RechecksActorSuspensionInsteadOfTrustingPublishedStatus()
    {
        await using var factory = await ProjectionFactory.CreateAsync();
        using var client = factory.CreateClient();
        await ProjectionAsync(factory, client, factory.PublicId);
        using (var scope = Scope(factory))
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var actor = await context.Actors.SingleAsync(actor => actor.UserId == factory.OwnerId);
            actor.IsSuspended = true;
            await context.SaveChangesAsync();
        }
        using var response = await client.GetAsync(Route(factory.PublicId));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var readScope = Scope(factory);
        var query = readScope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventAgendaProjectionRequest, EventAgendaProjectionDto?>>();
        await Assert.That(await query.QueryAsync(new(factory.PublicId), default)).IsNull();
    }

    [Test]
    public async Task NativeQuery_PreservesTokenAtRealDatabaseReadsAndCancelsAtReadBoundary()
    {
        await using var factory = await ProjectionFactory.CreateAsync();
        using var scope = Scope(factory);
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventAgendaProjectionRequest, EventAgendaProjectionDto?>>();
        using var cancellation = new CancellationTokenSource();
        factory.Tokens.Enabled = true;
        var result = await query.QueryAsync(new(factory.PublicId), cancellation.Token);
        factory.Tokens.Enabled = false;
        await Assert.That(result!.Days.SelectMany(day => day.Entries).Count()).IsEqualTo(5);
        await Assert.That(factory.Tokens.Observed.Count(token => token == cancellation.Token)).IsGreaterThanOrEqualTo(4);
        await Assert.That(factory.Tokens.Observed.Where(token => token.CanBeCanceled).All(token => token == cancellation.Token)).IsTrue();
        // The inherited parent GetById remains tokenless; this does not claim full read cancellation.
        await Assert.That(factory.Tokens.Observed.Any(token => !token.CanBeCanceled)).IsTrue();
        factory.Tokens.Observed.Clear();
        factory.Tokens.CancelAtTokenBearingRead = cancellation;
        factory.Tokens.Enabled = true;
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => query.QueryAsync(new(factory.PublicId), cancellation.Token));
        }
        finally
        {
            factory.Tokens.Enabled = false;
            factory.Tokens.CancelAtTokenBearingRead = null;
        }
        await Assert.That(cancellation.IsCancellationRequested).IsTrue();
        await Assert.That(factory.Tokens.Observed.Count(token => token.CanBeCanceled)).IsEqualTo(1);
        await Assert.ThrowsAsync<OperationCanceledException>(() => query.QueryAsync(new(factory.PublicId), cancellation.Token));
    }

    private static IServiceScope Scope(ProjectionFactory factory)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        return scope;
    }

    private static string Route(Guid id) => $"/api/eventagendaitem/agenda-projection/{id}";

    private static async Task<EventAgendaProjectionDto> ProjectionAsync(ProjectionFactory factory, HttpClient client, Guid id)
    {
        using var response = await client.GetAsync(Route(id));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        using var scope = Scope(factory);
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventAgendaProjectionRequest, EventAgendaProjectionDto?>>();
        var projection = await query.QueryAsync(new(id), default)
            ?? throw new InvalidOperationException("Projection query was empty.");
        // The disclosure DTO is output-only. Compare the complete wire document with the
        // independently executed native result rather than inventing a deserialization shim.
        var options = scope.ServiceProvider.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
        await Assert.That(JsonElement.DeepEquals(body.RootElement, JsonSerializer.SerializeToElement(projection, options))).IsTrue();
        return projection;
    }

    private static async Task AssertRedactedAsync(EventAgendaProjectionDto projection, ProjectionFactory factory)
    {
        await Assert.That(projection.Days.SelectMany(day => day.Entries).All(entry => entry.LocationId is null && entry.RoomId is null)).IsTrue();
        var json = JsonSerializer.Serialize(projection, JsonOptions);
        foreach (var hidden in new[] { factory.LocationId.ToString(), factory.RoomId.ToString(), "canary" })
            await Assert.That(json).DoesNotContain(hidden);
    }
}
