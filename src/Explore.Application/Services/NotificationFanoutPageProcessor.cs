using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Notifications;
using Explore.Domain;

namespace Explore.Application.Services;

public enum NotificationFanoutPageProcessingOutcome
{
    Completed = 1,
    StaleClaim = 2,
    Unavailable = 3
}

public sealed record NotificationFanoutPageProcessingResult(
    NotificationFanoutPageProcessingOutcome Outcome,
    int PagesCheckpointed,
    int RecipientsMaterialized,
    int NotificationsCreated);

public sealed class NotificationFanoutPageProcessor(
    INotificationFanoutOccurrenceRepository occurrenceRepository,
    IRegistrationInventoryRepository registrationInventoryRepository,
    INotificationFanoutRunRepository runRepository,
    INotificationFanoutRecipientMaterializationService recipientMaterializationService,
    NotificationFanoutRecipientTemplateFactory templateFactory,
    TimeProvider timeProvider)
{
    public const int MaxPageSize = 1000;

    public async Task<NotificationFanoutPageProcessingResult> ProcessAsync(
        NotificationFanoutClaim claim,
        int pageSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        Validate(claim, pageSize, leaseDuration);

        NotificationFanoutOccurrence? occurrence = await occurrenceRepository.GetByPointerAsync(
            new NotificationFanoutOccurrenceRequested(
                claim.TenantId,
                claim.OccurrenceId,
                NotificationFanoutOccurrenceRequested.CurrentVersion),
            trackChanges: false,
            cancellationToken);
        if (occurrence is null
            || occurrence.Id != claim.OccurrenceId
            || occurrence.TenantId != claim.TenantId
            || occurrence.State != NotificationFanoutOccurrenceState.Pending)
        {
            return Result(NotificationFanoutPageProcessingOutcome.Unavailable, 0, 0, 0);
        }

        templateFactory.Parse(occurrence);

        int pagesCheckpointed = 0;
        int recipientsMaterialized = 0;
        int notificationsCreated = 0;
        NotificationFanoutClaim currentClaim = claim;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTime observedAt = UtcNow();
            if (!await runRepository.TryRenewClaimAsync(
                    currentClaim,
                    observedAt,
                    observedAt.Add(leaseDuration),
                    cancellationToken))
            {
                return Result(
                    NotificationFanoutPageProcessingOutcome.StaleClaim,
                    pagesCheckpointed,
                    recipientsMaterialized,
                    notificationsCreated);
            }

            IReadOnlyList<NotificationFanoutAudienceMember> page =
                await registrationInventoryRepository.GetNotificationFanoutAudienceBatchAsync(
                    occurrence.TenantId,
                    occurrence.EventId,
                    occurrence.SessionId,
                    occurrence.AudienceCutoffAt,
                    occurrence.DeliveryPolicyId,
                    currentClaim.Cursor,
                    pageSize,
                    cancellationToken)
                ?? throw new InvalidOperationException("The notification fanout audience page is unavailable.");
            ValidatePage(page, currentClaim.Cursor, pageSize);

            if (page.Count == 0)
            {
                observedAt = UtcNow();
                bool completed = await runRepository.TryCompleteAsync(
                    currentClaim,
                    observedAt,
                    cancellationToken);
                return Result(
                    completed
                        ? NotificationFanoutPageProcessingOutcome.Completed
                        : NotificationFanoutPageProcessingOutcome.StaleClaim,
                    pagesCheckpointed,
                    recipientsMaterialized,
                    notificationsCreated);
            }

            int pageNotificationCount = 0;
            foreach (NotificationFanoutAudienceMember member in page)
            {
                cancellationToken.ThrowIfCancellationRequested();
                observedAt = UtcNow();
                if (!await runRepository.TryRenewClaimAsync(
                        currentClaim,
                        observedAt,
                        observedAt.Add(leaseDuration),
                        cancellationToken))
                {
                    return Result(
                        NotificationFanoutPageProcessingOutcome.StaleClaim,
                        pagesCheckpointed,
                        recipientsMaterialized,
                        notificationsCreated);
                }

                RecipientNotificationMaterializationResult materialized;
                try
                {
                    materialized = await recipientMaterializationService.MaterializeAsync(
                        occurrence,
                        member.UserId,
                        cancellationToken);
                }
                catch (NotificationFanoutOccurrenceUnavailableException)
                {
                    return Result(
                        NotificationFanoutPageProcessingOutcome.Unavailable,
                        pagesCheckpointed,
                        recipientsMaterialized,
                        notificationsCreated);
                }
                if (materialized is { IsSkipped: true })
                {
                    continue;
                }

                ValidateMaterialization(materialized, occurrence, member.UserId);
                recipientsMaterialized = checked(recipientsMaterialized + 1);
                if (materialized.Notification is not null)
                {
                    pageNotificationCount = checked(pageNotificationCount + 1);
                    notificationsCreated = checked(notificationsCreated + 1);
                }
            }

            var nextCursor = new NotificationFanoutAudienceCursor(
                page[^1].FirstEligibleRegistrationCreatedAt,
                page[^1].UserId);
            observedAt = UtcNow();
            bool checkpointed = await runRepository.TryCheckpointAsync(
                currentClaim,
                currentClaim.Cursor,
                nextCursor,
                page.Count,
                pageNotificationCount,
                observedAt,
                cancellationToken);
            if (!checkpointed)
            {
                return Result(
                    NotificationFanoutPageProcessingOutcome.StaleClaim,
                    pagesCheckpointed,
                    recipientsMaterialized,
                    notificationsCreated);
            }

            pagesCheckpointed = checked(pagesCheckpointed + 1);
            currentClaim = currentClaim with { Cursor = nextCursor };
        }
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static void Validate(
        NotificationFanoutClaim claim,
        int pageSize,
        TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (claim.RunId == Guid.Empty
            || claim.TenantId == Guid.Empty
            || claim.OccurrenceId == Guid.Empty
            || claim.LeaseToken == Guid.Empty
            || claim.Fence <= 0
            || claim.Generation <= 0)
        {
            throw new ArgumentException("The notification fanout claim is invalid.", nameof(claim));
        }

        if (claim.Cursor is { } cursor
            && (cursor.FirstEligibleRegistrationCreatedAt.Kind != DateTimeKind.Utc
                || cursor.UserId == Guid.Empty))
        {
            throw new ArgumentException("The notification fanout claim cursor is invalid.", nameof(claim));
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        }
    }

    private static void ValidatePage(
        IReadOnlyList<NotificationFanoutAudienceMember> page,
        NotificationFanoutAudienceCursor? expectedCursor,
        int pageSize)
    {
        if (page.Count > pageSize)
        {
            throw new InvalidOperationException("The notification fanout audience page exceeded its requested bound.");
        }

        var recipientIds = new HashSet<Guid>();
        NotificationFanoutAudienceCursor? previous = expectedCursor;
        foreach (NotificationFanoutAudienceMember member in page)
        {
            var cursor = new NotificationFanoutAudienceCursor(
                member.FirstEligibleRegistrationCreatedAt,
                member.UserId);
            if (member.UserId == Guid.Empty
                || member.FirstEligibleRegistrationCreatedAt.Kind != DateTimeKind.Utc
                || !recipientIds.Add(member.UserId)
                || !IsAfter(cursor, previous))
            {
                throw new InvalidOperationException("The notification fanout audience page is not unique and strictly ordered.");
            }

            previous = cursor;
        }
    }

    private static void ValidateMaterialization(
        RecipientNotificationMaterializationResult materialized,
        NotificationFanoutOccurrence occurrence,
        Guid recipientUserId)
    {
        if (materialized is null
            || materialized.IsSkipped
            || materialized.Intent is null
            || materialized.Notification is null
            || materialized.Intent.Id == Guid.Empty
            || materialized.Intent.TenantId != occurrence.TenantId
            || materialized.Intent.FanoutOccurrenceId != occurrence.Id
            || materialized.Intent.RecipientUserId != recipientUserId
            || materialized.Notification.TenantId != occurrence.TenantId
            || materialized.Notification.UserId != recipientUserId
            || materialized.Notification.NotificationIntentId != materialized.Intent.Id)
        {
            throw new InvalidOperationException("The materialized notification authority does not match the fanout recipient.");
        }
    }

    private static bool IsAfter(
        NotificationFanoutAudienceCursor next,
        NotificationFanoutAudienceCursor? current) =>
        current is null
        || next.FirstEligibleRegistrationCreatedAt > current.Value.FirstEligibleRegistrationCreatedAt
        || (next.FirstEligibleRegistrationCreatedAt == current.Value.FirstEligibleRegistrationCreatedAt
            && next.UserId.CompareTo(current.Value.UserId) > 0);

    private static NotificationFanoutPageProcessingResult Result(
        NotificationFanoutPageProcessingOutcome outcome,
        int pagesCheckpointed,
        int recipientsMaterialized,
        int notificationsCreated) =>
        new(outcome, pagesCheckpointed, recipientsMaterialized, notificationsCreated);
}
