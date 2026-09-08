using Event.Api.IntegrationTests.Builders;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.ValueObjects;
using Explore.Persistence;

namespace Event.Api.IntegrationTests.Seeds;

/// <summary>
/// Seeds event entities within an established tenant context.
/// Requires a prior <see cref="TenantScenarioSeed"/> call to provide ActorId and TenantId.
/// </summary>
public static class EventScenarioSeed
{
    public sealed record EventScenarioResult(Guid EventId, string Title, string PublicCode);

    /// <summary>
    /// Seeds a single published event visible to public API queries.
    /// </summary>
    public static async Task<EventScenarioResult> SeedPublishedEventAsync(
        ExploreDbContext context,
        Guid actorId,
        Guid tenantId,
        string title = "Published Test Event")
    {
        var @event = new EventBuilder()
            .WithTitle(title)
            .WithActorId(actorId)
            .WithTenantId(tenantId)
            .WithStatus(EventStatusEnum.Published)
            .WithVisibility(VisibilityTypeEnum.Public)
            .WithSessionDates(
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
                DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)))
            .Build();

        var session = CreatePublishedSession(@event, tenantId, DateTimeOffset.UtcNow.AddDays(7), sortOrder: 1);
        @event.Sessions.Add(session);
        @event.RecalculateScheduleSummaryFromSessions();

        context.Events.Add(@event);
        await context.SaveChangesAsync();

        return new EventScenarioResult(@event.Id, @event.Title, @event.PublicCode);
    }

    /// <summary>
    /// Seeds a draft event that is not publicly visible.
    /// </summary>
    public static async Task<EventScenarioResult> SeedDraftEventAsync(
        ExploreDbContext context,
        Guid actorId,
        Guid tenantId,
        string title = "Draft Test Event")
    {
        var @event = new EventBuilder()
            .WithTitle(title)
            .WithActorId(actorId)
            .WithTenantId(tenantId)
            .WithStatus(EventStatusEnum.Draft)
            .Build();

        context.Events.Add(@event);
        await context.SaveChangesAsync();

        return new EventScenarioResult(@event.Id, @event.Title, @event.PublicCode);
    }

    /// <summary>
    /// Seeds multiple published events for pagination and list testing.
    /// </summary>
    public static async Task<IReadOnlyList<EventScenarioResult>> SeedMultiplePublishedEventsAsync(
        ExploreDbContext context,
        Guid actorId,
        Guid tenantId,
        int count = 5)
    {
        var results = new List<EventScenarioResult>();

        for (var i = 0; i < count; i++)
        {
            var sessionStart = DateTimeOffset.UtcNow.AddDays(7 + i);
            var @event = new EventBuilder()
                .WithTitle($"Event {i + 1}")
                .WithActorId(actorId)
                .WithTenantId(tenantId)
                .WithStatus(EventStatusEnum.Published)
                .WithVisibility(VisibilityTypeEnum.Public)
                .WithSessionDates(
                    DateOnly.FromDateTime(sessionStart.UtcDateTime),
                    DateOnly.FromDateTime(sessionStart.UtcDateTime))
                .Build();

            var session = CreatePublishedSession(@event, tenantId, sessionStart, sortOrder: i + 1);
            @event.Sessions.Add(session);
            @event.RecalculateScheduleSummaryFromSessions();

            context.Events.Add(@event);
            results.Add(new EventScenarioResult(@event.Id, @event.Title, @event.PublicCode));
        }

        await context.SaveChangesAsync();
        return results;
    }

    private static EventSession CreatePublishedSession(
        Explore.Domain.Event @event,
        Guid tenantId,
        DateTimeOffset startUtc,
        int sortOrder)
    {
        var session = new EventSession(EventSessionStatusEnum.Published)
        {
            Id = Guid.NewGuid(),
            EventId = @event.Id,
            Event = @event,
            TenantId = tenantId,
            Tenant = null!,
            Title = @event.Title,
            SortOrder = sortOrder,
            EventSessionKindId = (int)EventSessionKindEnum.Talk,
            RegistrationModeId = 1,
            ConcurrencyStamp = Guid.NewGuid()
        };

        session.Reschedule(UtcInstantRange.Create(startUtc, startUtc.AddHours(1)), "UTC", new EventScheduleProjectionCalculator());
        return session;
    }
}
