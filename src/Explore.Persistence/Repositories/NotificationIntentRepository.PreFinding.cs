using Explore.Application.Notifications;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class NotificationIntentRepository
{
    private Task RequirePreFindingNotificationAsync(NotificationIntent intent, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (intent.TemplateKey != ModerationPreFindingNoticeTemplate.Key
            && !intent.DeduplicationKey.StartsWith(ModerationPreFindingNoticeTemplate.Prefix, StringComparison.Ordinal)
            && intent.SafePayloadReference?.StartsWith(ModerationPreFindingNoticeTemplate.Prefix, StringComparison.Ordinal) != true)
            return Task.CompletedTask;
        var pointer = intent.SafePayloadReference;
        if (pointer is null || !pointer.StartsWith(ModerationPreFindingNoticeTemplate.Prefix, StringComparison.Ordinal)
            || !Guid.TryParseExact(pointer[ModerationPreFindingNoticeTemplate.Prefix.Length..], "D", out var noticeId))
            throw new InvalidOperationException("Pre-finding notification coordinates are unavailable.");
        var entry = _dbContext.ChangeTracker.Entries<ModerationPreFindingNotice>()
            .SingleOrDefault(row => row.Entity.Id == noticeId);
        var notice = entry?.Entity;
        // Only the native issuer's freshly staged opportunity may create this graph.
        // A generic notification retry cannot reconstruct it from expired private rows.
        if (entry?.State != EntityState.Added || notice is null
            || _dbContext.Database.CurrentTransaction is null
            || _dbContext.ModerationTrustDomainId != notice.TrustDomainId
            || _dbContext.TenantFilterTenantId != notice.TenantId || _dbContext.IsTenantFilterBypassed
            || _dbContext.CurrentUserService?.UserId != notice.IssuedByHumanId
            || notice.PurgedAt is not null || notice.Withdrawn
            || notice.AcknowledgedAt is not null || notice.RespondedAt is not null
            || _clock.GetUtcNow() < notice.IssuedAt || _clock.GetUtcNow() >= notice.RetainUntil
            || !ModerationPreFindingNotificationGraph.Matches(intent, notice))
            throw new InvalidOperationException("The native pre-finding opportunity is unavailable for notification materialization.");
        notice.ValidatePersistedState();
        return Task.CompletedTask;
    }
}
