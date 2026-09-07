using System.Collections.Immutable;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;

namespace Explore.Application.Services;

public sealed class FanoutAttendeeLocationAuthorizationService(
    IEventLocationRepository eventLocationRepository,
    ILocationRoomRepository roomRepository,
    IEventRegistrationRepository registrationRepository,
    IEventLocationRegistrationAccessService registrationAccessService,
    ILocationPrivacyGovernanceService governanceService,
    EventLocationDisclosureEvaluator evaluator,
    TimeProvider timeProvider) : IFanoutAttendeeLocationAuthorizationService
{
    public async Task<FanoutAttendeeLocationAuthorizationResult> AuthorizeAsync(
        FanoutAttendeeLocationAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        DateTimeOffset now = timeProvider.GetUtcNow().ToUniversalTime();
        EventLocation? eventLocation = (await eventLocationRepository.GetByIdsAsync(
                [request.EventLocationId],
                cancellationToken))
            .SingleOrDefault(item => item.TenantId == request.TenantId
                && item.EventId == request.EventId);
        LocationRoom? room = request.RoomId is { } roomId
            ? (await roomRepository.GetByIdsAsync([roomId], cancellationToken)).SingleOrDefault()
            : null;
        IReadOnlyList<EventRegistration> registrations =
            await registrationRepository.GetLocationAccessCoverageAsync(
                request.TenantId,
                request.EventId,
                request.RecipientUserId,
                cancellationToken);
        EventLocationRegistrationAccess? access = registrationAccessService.ResolveMany(
                request.TenantId,
                request.EventId,
                request.RecipientUserId,
                now,
                [request.EventLocationId],
                registrations)
            .GetValueOrDefault(request.EventLocationId);
        EffectiveLocationPrivacyGovernance governance = await governanceService.ResolveAsync(
            request.TenantId,
            cancellationToken);
        var disclosureRequest = new EventLocationDisclosureRequest(
            request.TenantId,
            request.EventId,
            request.EventLocationId,
            request.RoomId,
            request.RecipientUserId,
            EventLocationDisclosurePurpose.Attendee);
        EventLocationDisclosureAuthorityFact? authority = access is { CoversRequestedEventLocation: true }
            ? EventLocationDisclosureAuthorityFact.ForAttendee(
                request.RecipientUserId,
                request.TenantId,
                request.EventId,
                request.EventLocationId,
                access)
            : null;
        EventLocationDisclosureResult result = evaluator.Evaluate(new(
            disclosureRequest,
            eventLocation,
            eventLocation?.Location,
            room,
            new EventLocationDisclosureGovernanceFact(
                governance.IsResolved,
                governance.AllowHomeLocations,
                governance.AllowPublicExactAddress,
                governance.AllowPublicCoordinates,
                governance.MinimumHomeAudience,
                governance.DefaultRevealOffset),
            authority,
            now,
            Derivatives: null));

        return new FanoutAttendeeLocationAuthorizationResult(
            request.TenantId,
            request.EventId,
            request.RecipientUserId,
            request.EventLocationId,
            request.RoomId,
            result.State,
            result.State == EventLocationDisclosureState.Available
                ? result.DisclosedFields
                : ImmutableArray<EventLocationDisclosureField>.Empty);
    }

    private static void Validate(FanoutAttendeeLocationAuthorizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TenantId == Guid.Empty
            || request.EventId == Guid.Empty
            || request.RecipientUserId == Guid.Empty
            || request.EventLocationId == Guid.Empty
            || request.RoomId == Guid.Empty)
        {
            throw new ArgumentException("Fanout location authorization requires non-empty scoped identifiers.", nameof(request));
        }
    }
}
