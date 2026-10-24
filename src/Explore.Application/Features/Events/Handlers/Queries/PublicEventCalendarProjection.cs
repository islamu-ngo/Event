using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;

namespace Explore.Application.Features.Events.Handlers.Queries;

/// <summary>
/// Single-use anonymous calendar projection. No DTO survives the query handler;
/// native acquisition and its fences cover mapping, private byte preparation and
/// terminal delivery. Source administrators never become the location requester.
/// </summary>
public sealed class PublicEventCalendarProjection : IAsyncDisposable
{
    private IModerationPublicEventCalendarRepository? owner;
    private readonly ModerationPublicEventCalendarRequest request;
    private readonly IEventLocationDisclosureService locations;
    private readonly TimeProvider clock;
    private readonly CancellationToken originCancellation;

    internal PublicEventCalendarProjection(IModerationPublicEventCalendarRepository owner,
        ModerationPublicEventCalendarRequest request, IEventLocationDisclosureService locations,
        TimeProvider clock, CancellationToken originCancellation) =>
        (this.owner, this.request, this.locations, this.clock, this.originCancellation) =
            (owner, request, locations, clock, originCancellation);

    public async Task<ModerationPublicEventCalendarOutcome> DiscloseAsync<TRepresentation>(
        Func<EventCalendarExportDto, CancellationToken, Task<TRepresentation>> prepare,
        Func<TRepresentation, CancellationToken, Task> deliver, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(deliver);
        var repository = Interlocked.Exchange(ref owner, null);
        if (repository is null) return ModerationPublicEventCalendarOutcome.NotFound;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(originCancellation, cancellationToken);
        var entered = false;
        return await repository.ExecuteSerializedAsync(async token =>
        {
            // An execution-strategy callback may be retried, but output never is.
            if (entered) return ModerationPublicEventCalendarOutcome.Unavailable;
            entered = true;
            token.ThrowIfCancellationRequested();
            var acquisition = await repository.AcquireAsync(request, token);
            await using var lease = acquisition.Lease;
            if (acquisition.Outcome != ModerationPublicEventCalendarOutcome.Allowed) return acquisition.Outcome;
            if (lease is null) return ModerationPublicEventCalendarOutcome.Unavailable;
            var validityCancellation = lease.ValidityCancellation;
            using var deliveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, validityCancellation);
            var deliveryToken = deliveryCancellation.Token;
            try
            {
                deliveryToken.ThrowIfCancellationRequested();
                if (clock.GetUtcNow() >= lease.ValidUntil) return ModerationPublicEventCalendarOutcome.Unavailable;
                var current = await lease.RevalidateAsync(deliveryToken);
                if (current != ModerationPublicEventCalendarOutcome.Allowed) return current;
                var item = lease.Event;
                var session = lease.PrimarySession;
                if (item.Id != request.EventId || item.TenantId != request.TenantId
                    || session.EventId != request.EventId || session.TenantId != request.TenantId
                    || session.StartTime is not { } start || session.EndTime is not { } end || end <= start)
                    return ModerationPublicEventCalendarOutcome.Unavailable;
                var location = await ResolvePublicLocationAsync(session, deliveryToken);
                var view = new EventCalendarExportDto(item.Id, item.Title,
                    item.Content ?? item.Description, item.Slug,
                    start.ToUniversalTime(), end.ToUniversalTime(), location);
                var representation = await prepare(view, deliveryToken);
                deliveryToken.ThrowIfCancellationRequested();
                if (clock.GetUtcNow() >= lease.ValidUntil) return ModerationPublicEventCalendarOutcome.Unavailable;
                current = await lease.RevalidateAsync(deliveryToken);
                deliveryToken.ThrowIfCancellationRequested();
                if (current != ModerationPublicEventCalendarOutcome.Allowed) return current;
                if (clock.GetUtcNow() >= lease.ValidUntil) return ModerationPublicEventCalendarOutcome.Unavailable;
                await deliver(representation, deliveryToken);
                deliveryToken.ThrowIfCancellationRequested();
                if (clock.GetUtcNow() >= lease.ValidUntil) return ModerationPublicEventCalendarOutcome.Unavailable;
                return ModerationPublicEventCalendarOutcome.Allowed;
            }
            catch (OperationCanceledException) when (validityCancellation.IsCancellationRequested)
            {
                return ModerationPublicEventCalendarOutcome.Unavailable;
            }
            catch (ObjectDisposedException) when (validityCancellation.IsCancellationRequested
                || clock.GetUtcNow() >= lease.ValidUntil)
            {
                return ModerationPublicEventCalendarOutcome.Unavailable;
            }
        }, cancellation.Token);
    }

    private async Task<string?> ResolvePublicLocationAsync(Explore.Domain.EventSession session, CancellationToken token)
    {
        if (session.EventLocationId is not { } eventLocationId) return null;
        var results = await locations.ResolveManyAsync(
            [new(session.TenantId, session.EventId, eventLocationId, session.RoomId, RequesterUserId: null,
                EventLocationDisclosurePurpose.Public)], token);
        if (!results.TryGetValue(eventLocationId, out var result) || result.EventLocationId != eventLocationId
            || result.Purpose != EventLocationDisclosurePurpose.Public
            || result.State is not (EventLocationDisclosureState.Available or EventLocationDisclosureState.PrivateVenue)
            || result.Values is not { } values) return null;
        string?[] parts =
        [
            values.VenueName, values.RoomName, values.StreetAddress, values.Postcode, values.City, values.Country
        ];
        var location = string.Join(", ", parts.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(location) ? null : location;
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref owner, null);
        return ValueTask.CompletedTask;
    }

    public override string ToString() => nameof(PublicEventCalendarProjection);
}
