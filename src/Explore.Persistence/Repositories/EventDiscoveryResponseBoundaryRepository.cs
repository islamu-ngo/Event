using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Services.Federation;
using Explore.Domain.Enums;
using Explore.Persistence.Extensions;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Scalar public-source clock projection, independent of response filters and take.
/// This also fences empty responses and candidates omitted from a home section.
/// </summary>
public sealed class EventDiscoveryResponseBoundaryRepository(
    ExploreDbContext context,
    ILocationPrivacyGovernanceService privacy,
    AtprotoEventGovernanceResolver federation) : IEventDiscoveryResponseBoundaryRepository
{
    public async Task<DateTimeOffset?> GetNextAsync(
        Guid tenantId, DateTimeOffset observedAtUtc, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || context.TenantFilterTenantId != tenantId || context.IsTenantFilterBypassed)
            throw new EventDiscoveryUnavailableException();

        var publicEvents = context.Events.WherePubliclyEligible(context)
            .Where(value => value.TenantId == tenantId).Select(value => value.Id);
        var sessions = context.EventSessions.AsNoTracking()
            .Where(value => value.TenantId == tenantId && publicEvents.Contains(value.EventId)
                && value.EventSessionStatusId == (int)EventSessionStatusEnum.Published
                && (value.EventDayId == null || value.EventDay!.IsPublished));
        var nextInstantReader = EventDirectoryTemporalQuery.CreateNextInstantReader(
            context, observedAtUtc, cancellationToken);
        DateTimeOffset? next = await NextInstantAsync(sessions.Select(value => value.StartTime));
        Include(await NextInstantAsync(sessions.Select(value => value.EndTime)));

        if ((await federation.ResolveAsync(tenantId, null, cancellationToken)).EventsEnabled)
        {
            var remote = new AtprotoEventProjectionRepository(context).VisibleQuery(includeLocalEchoes: true);
            Include(await NextInstantAsync(remote.Select(value => value.StartsAt)));
            Include(await NextInstantAsync(remote.Select(value => value.EndsAt)));
        }

        var locations = context.EventLocations.AsNoTracking()
            .Where(value => value.TenantId == tenantId && publicEvents.Contains(value.EventId)
                && !value.IsToBeAnnounced && !value.NeedsPrivacyReview && value.Location != null
                && value.Location.Pii != null
                && value.Location.LocationKindId != (int)LocationKindEnum.PrivateHome
                && value.Location.LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Active);
        if (await locations.AnyAsync(cancellationToken))
        {
            var governance = await privacy.ResolveAsync(tenantId, cancellationToken);
            if (!governance.IsResolved)
                throw new EventDiscoveryUnavailableException();
            locations = locations.Where(value =>
                governance.AllowPublicExactAddress && (value.ShowStreetAddress || value.ShowPostcode)
                || governance.AllowPublicCoordinates && value.ShowCoordinates);
            DateTime now = observedAtUtc.UtcDateTime;
            DateTime threshold = now.Subtract(governance.DefaultRevealOffset);
            DateTime? created = await locations.Where(value => value.CreatedAt > threshold)
                .OrderBy(value => value.CreatedAt).Select(value => (DateTime?)value.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            DateTime? explicitReveal = await locations.Where(value => value.RevealFullDetailsFromUtc > now)
                .OrderBy(value => value.RevealFullDetailsFromUtc).Select(value => value.RevealFullDetailsFromUtc)
                .FirstOrDefaultAsync(cancellationToken);
            // The evaluator opens at max(created + governed offset, explicit reveal).
            // The lower envelope of its future components is conservative and cannot
            // miss that opening. Do arithmetic in .NET to preserve subsecond offsets
            // instead of rounding a provider DATEADD before the future predicate.
            if (created is { } creation)
                Include(new DateTimeOffset(DateTime.SpecifyKind(creation, DateTimeKind.Utc)
                    .Add(governance.DefaultRevealOffset)));
            if (explicitReveal is { } reveal)
                Include(new DateTimeOffset(DateTime.SpecifyKind(reveal, DateTimeKind.Utc)));
        }
        return next;

        Task<DateTimeOffset?> NextInstantAsync(IQueryable<DateTimeOffset?> values) =>
            nextInstantReader(values);

        void Include(DateTimeOffset? boundary)
        {
            if (boundary > observedAtUtc && (next is null || boundary < next))
                next = boundary;
        }
    }
}
