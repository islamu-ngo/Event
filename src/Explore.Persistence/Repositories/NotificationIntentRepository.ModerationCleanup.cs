using Explore.Application.Contracts.Persistence;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class NotificationIntentRepository
{
    public async Task<bool> StageAsync(
        ModerationNoticeNotificationCleanupRequest request, CancellationToken cancellationToken)
    {
        if (!OwnsNoticeCleanup(request)) return false;
        var prefix = $"moderation-notice:{request.EffectId:N}:";
        var targetIds = request.Targets.Select(row => row.IntentId).ToArray();
        var discovered = await _dbContext.NotificationIntents.AsNoTracking().IgnoreQueryFilters(["SoftDelete"])
            .Where(row => row.TenantId == request.TenantId
                && (row.DeduplicationKey.StartsWith(prefix) || targetIds.Contains(row.Id)))
            .OrderBy(row => row.Id).Select(row => row.Id).Take(1025).ToArrayAsync(cancellationToken);
        if (discovered.Length > 1024 || targetIds.Distinct().Count() != targetIds.Length
            || targetIds.Except(discovered).Any()) return false;
        foreach (var id in discovered)
            await RelationalEntityRowFence.AcquireAsync<NotificationIntent>(
                _dbContext, request.TenantId, row => row.Id, id, cancellationToken);
        var intents = await _dbContext.NotificationIntents.IgnoreQueryFilters(["SoftDelete"])
            .Where(row => row.TenantId == request.TenantId && discovered.Contains(row.Id))
            .OrderBy(row => row.Id).ToArrayAsync(cancellationToken);
        foreach (var intent in intents)
        {
            var parts = intent.DeduplicationKey.Split(':');
            if (intent.CategoryId != (int)NotificationCategoryEnum.TrustSafetyModeration
                || intent.TemplateKey != "moderation.notice.portal.v1" || parts.Length != 4
                || parts[0] != "moderation-notice" || parts[1] != request.EffectId.ToString("N")
                || parts[2] is not ("1" or "2" or "3")
                || !Guid.TryParseExact(parts[3], "N", out var recipient) || recipient != intent.RecipientUserId
                || intent.SafePayloadReference is not { } pointer || !pointer.StartsWith("moderation-notice:", StringComparison.Ordinal)
                || !Guid.TryParseExact(pointer["moderation-notice:".Length..], "D", out var noticeId)
                || noticeId.Version != 7 || noticeId.Variant is < 8 or > 11
                || intent.FanoutOccurrenceId is not null || intent.EventId is not null
                || intent.ReportId is not null || intent.ReportDecisionId is not null)
                return false;
            var target = request.Targets.SingleOrDefault(row => row.IntentId == intent.Id);
            if (target is not null && (target.NoticeId != noticeId || target.RecipientId != recipient
                || target.Audience.ToString(System.Globalization.CultureInfo.InvariantCulture) != parts[2]))
                return false;
        }

        // Only this original-notice SMTP kind belongs to the finite source. The source fence
        // held by the cleanup owner also fences final SMTP; provider settlement cannot recreate it.
        if (await _dbContext.Set<NotificationExternalDelegation>()
                .AnyAsync(row => row.TenantId == request.TenantId && discovered.Contains(row.NotificationIntentId), cancellationToken))
            return false;
        var emails = await _dbContext.EmailDispatchOutbox.IgnoreQueryFilters(["SoftDelete"])
            .Where(row => row.TenantId == request.TenantId && discovered.Contains(row.NotificationIntentId))
            .OrderBy(row => row.Id).ToArrayAsync(cancellationToken);
        foreach (var email in emails)
        {
            await RelationalEntityRowFence.AcquireAsync<EmailDispatchOutbox>(
                _dbContext, request.TenantId, row => row.Id, email.Id, cancellationToken);
            var intent = intents.Single(row => row.Id == email.NotificationIntentId);
            var noticeId = Guid.ParseExact(intent.SafePayloadReference!["moderation-notice:".Length..], "D");
            if (email.Kind != EmailDispatchKind.AccountModerationOriginalNotice
                || email.SourceType != ModerationNoticeEmailTemplate.SourceType || email.SourceId != noticeId
                || email.RecipientUserId != intent.RecipientUserId
                || email.RecipientAddressSource != RecipientAddressSource.TenantUserVerifiedEmail
                || email.EventId is not null || email.RegistrationOrderId is not null
                || email.ManagedTenantProvisioningOperationId is not null)
                return false;
        }
        var deliveries = await _dbContext.NotificationDeliveries
            .Where(row => row.TenantId == request.TenantId && discovered.Contains(row.NotificationIntentId))
            .OrderBy(row => row.Id).ToArrayAsync(cancellationToken);
        foreach (var row in deliveries)
        {
            await RelationalEntityRowFence.AcquireAsync<NotificationDelivery>(
                _dbContext, request.TenantId, value => value.Id, row.Id, cancellationToken);
            if (row.TemplateKey != "moderation.notice.portal.v1"
                || row.ChannelId is not ((int)NotificationPreferenceChannelEnum.InApp) and not ((int)NotificationPreferenceChannelEnum.Email)
                || row.ChannelId == (int)NotificationPreferenceChannelEnum.InApp && row.EmailDispatchOutboxId is not null
                || row.ChannelId == (int)NotificationPreferenceChannelEnum.Email
                    && (row.NotificationId is not null
                        || row.EmailDispatchOutboxId is { } emailId
                            && !emails.Any(email => email.Id == emailId && email.NotificationIntentId == row.NotificationIntentId)
                        || row.EmailDispatchOutboxId is null && row.StatusId != (int)NotificationDeliveryStatusEnum.Skipped))
                return false;
        }
        var notifications = await _dbContext.Notifications.IgnoreQueryFilters(["SoftDelete"])
            .Where(row => row.TenantId == request.TenantId && row.NotificationIntentId != null
                && discovered.Contains(row.NotificationIntentId.Value))
            .OrderBy(row => row.Id).ToArrayAsync(cancellationToken);
        foreach (var row in notifications)
        {
            await RelationalEntityRowFence.AcquireAsync<Notification>(
                _dbContext, request.TenantId, value => value.Id, row.Id, cancellationToken);
            var intent = intents.Single(value => value.Id == row.NotificationIntentId);
            if (row.UserId != intent.RecipientUserId
                || row.EntityId != intent.SafePayloadReference!["moderation-notice:".Length..])
                return false;
        }
        if (deliveries.Any(row => row.NotificationId is { } id
                && !notifications.Any(notification => notification.Id == id && notification.NotificationIntentId == row.NotificationIntentId)))
            return false;
        _dbContext.RemoveRange(deliveries);
        _dbContext.RemoveRange(emails);
        _dbContext.RemoveRange(notifications);
        _dbContext.RemoveRange(intents);
        return OwnsNoticeCleanup(request);
    }

    public async Task<bool> VerifyRemovedAsync(
        ModerationNoticeNotificationCleanupRequest request, CancellationToken cancellationToken)
    {
        if (!OwnsNoticeCleanup(request)) return false;
        var prefix = $"moderation-notice:{request.EffectId:N}:";
        var intentIds = request.Targets.Select(row => row.IntentId).ToArray();
        return !await _dbContext.NotificationIntents.AsNoTracking().IgnoreQueryFilters(["SoftDelete"])
            .AnyAsync(row => row.TenantId == request.TenantId
                && (row.DeduplicationKey.StartsWith(prefix) || intentIds.Contains(row.Id)), cancellationToken)
            && !await _dbContext.Notifications.AsNoTracking().IgnoreQueryFilters(["SoftDelete"])
                .AnyAsync(row => row.TenantId == request.TenantId && row.NotificationIntentId != null
                    && intentIds.Contains(row.NotificationIntentId.Value), cancellationToken)
            && !await _dbContext.NotificationDeliveries.AsNoTracking()
                .AnyAsync(row => row.TenantId == request.TenantId && intentIds.Contains(row.NotificationIntentId), cancellationToken)
            && !await _dbContext.EmailDispatchOutbox.AsNoTracking().IgnoreQueryFilters(["SoftDelete"])
                .AnyAsync(row => row.TenantId == request.TenantId && intentIds.Contains(row.NotificationIntentId), cancellationToken);
    }

    private bool OwnsNoticeCleanup(ModerationNoticeNotificationCleanupRequest request) =>
        _dbContext.Database.CurrentTransaction?.TransactionId == request.TransactionId
        && !_dbContext.IsTenantFilterBypassed && _dbContext.TenantFilterTenantId == request.TenantId
        && _dbContext.ModerationTrustDomainId == request.TrustDomainId
        && request.HostBindingId != Guid.Empty && request.EffectId != Guid.Empty
        && request.RetainUntil.Offset == TimeSpan.Zero && request.RetainUntil != DateTimeOffset.MaxValue
        && _clock.GetUtcNow() >= request.RetainUntil;

    private async Task RequireOriginalModerationNoticeAsync(NotificationIntent intent, CancellationToken token)
    {
        if (intent.TemplateKey != "moderation.notice.portal.v1") return;
        var parts = intent.DeduplicationKey.Split(':');
        if (parts.Length != 4 || parts[0] != "moderation-notice"
            || !Guid.TryParseExact(parts[1], "N", out var effectId))
            throw new InvalidOperationException("Original moderation notice coordinates are unavailable.");
        await using var fence = await RelationalNamedLock.AcquireTransactionAsync(
            _dbContext, $"moderation.notice:{effectId:D}", token);
        var entry = _dbContext.ChangeTracker.Entries<ModerationNoticeSource>()
            .SingleOrDefault(row => row.Entity.EffectId == effectId);
        var source = entry?.Entity ?? await _dbContext.ModerationNoticeSources.AsNoTracking()
            .SingleOrDefaultAsync(row => row.EffectId == effectId, token);
        var effect = _dbContext.ModerationEffectIntents.Local.SingleOrDefault(row => row.Id == effectId)
            ?? await _dbContext.ModerationEffectIntents.AsNoTracking().SingleOrDefaultAsync(row => row.Id == effectId, token);
        if (source is null || entry?.State == EntityState.Deleted
            || effect is null || !source.Matches(effect)
            || source.TrustDomainId != _dbContext.ModerationTrustDomainId
            || source.TenantId != intent.TenantId || intent.SafePayloadHash != source.SnapshotHash
            || source.RetainUntil is not { } ceiling || _clock.GetUtcNow() >= ceiling)
            throw new InvalidOperationException("The original moderation notice is unavailable for materialization or repair.");
        if (parts[2] == "3" && !source.HasContentRepresentative(intent.RecipientUserId))
            throw new InvalidOperationException("The original Content representative is unavailable for materialization or repair.");
    }
}
