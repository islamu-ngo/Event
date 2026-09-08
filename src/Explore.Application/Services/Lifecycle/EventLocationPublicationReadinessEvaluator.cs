using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Lifecycle;

public static class EventLocationPublicationReadinessEvaluator
{
    public static LifecycleReadinessResult Include(
        LifecycleReadinessResult eventReadiness,
        IReadOnlyCollection<EventLocation> eventLocations)
    {
        ArgumentNullException.ThrowIfNull(eventReadiness);
        ArgumentNullException.ThrowIfNull(eventLocations);

        var errors = eventReadiness.Errors.ToList();
        bool hasReviewRequired = eventLocations.Any(location =>
            !location.IsToBeAnnounced && location.NeedsPrivacyReview);
        bool hasUnusablePhysicalLocation = eventLocations.Any(location =>
            !location.IsToBeAnnounced
            && !location.NeedsPrivacyReview
            && !location.SatisfiesPublicationVenueRequirement(location.Location));
        bool hasInvalidShape = eventLocations.Any(location => !location.HasValidLocationOrTbaShape);

        if (hasReviewRequired)
        {
            errors.Add(CreateError(
                "event_location_privacy_review_required",
                "One or more physical event locations require privacy remediation before publication.",
                eventReadiness.Profile));
        }

        if (hasUnusablePhysicalLocation || hasInvalidShape)
        {
            errors.Add(CreateError(
                "event_location_physical_location_unusable",
                "Replace each unusable physical event location or explicitly select TBA before publication.",
                eventReadiness.Profile));
        }

        return errors.Count == 0
            ? LifecycleReadinessResult.Success(eventReadiness.Profile)
            : LifecycleReadinessResult.Failure(eventReadiness.Profile, errors);
    }

    private static LifecycleReadinessError CreateError(
        string code,
        string message,
        ValidationProfile profile) => new(
        Code: code,
        FieldKey: EventFieldKey.Location,
        FieldPath: "locations",
        Message: message,
        Severity: ReadinessErrorSeverity.Error,
        Source: ReadinessErrorSource.DomainRule,
        Profile: profile);
}
