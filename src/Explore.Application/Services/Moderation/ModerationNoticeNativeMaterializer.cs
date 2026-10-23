using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Services;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Moderation;

/// <summary>
/// Projects only an independently qualified native tenant user. The dedicated portal notice is
/// required; optional native channels retain their actual preference and routing policy.
/// Eligible email is only an original protected pointer; its native final-use owner independently
/// qualifies the original purpose immediately before SMTP, without acknowledging the notice.
/// </summary>
public sealed class ModerationNoticeNativeMaterializer(
    INotificationOwnershipResolver ownership,
    INotificationPreferenceResolver preferences,
    IRecipientNotificationMaterializer materializer)
{
    public async Task<bool> MaterializePreFindingAsync(
        ModerationPreFindingNotice notice, User recipient, CancellationToken token)
    {
        if (notice.TenantId == Guid.Empty || recipient.Id != notice.RecipientHumanId || recipient.IsDeleted)
            return false;
        var draft = new NotificationIntentDraft(Explore.Application.Notifications.NotificationCategory.TrustSafetyModeration,
            TenantId: notice.TenantId, UserId: recipient.Id, RecipientKind: "User",
            TemplateKey: ModerationPreFindingNoticeTemplate.Key,
            SafePayloadReference: ModerationPreFindingNoticeTemplate.Reference(notice.Id),
            DeduplicationKey: ModerationPreFindingNoticeTemplate.DeduplicationKey(notice.Id, recipient.Id));
        var route = await ownership.ResolveAsync(draft, token);
        if (!route.IsLocalIslamuDelivery)
            return false;

        var result = await materializer.MaterializeInCurrentTransactionAsync(new(
            Guid.CreateVersion7(), draft, NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired,
            "moderation_pre_finding_pointer",
            new RecipientInAppNotificationDraft(
                (int)NotificationTypeEnum.General, ModerationPreFindingNoticeTemplate.Title, null,
                (int)ActorTypeEnum.User, (int)NotificationReasonEnum.System,
                EntityId: notice.Id.ToString("D"), IsRequired: true),
            Email: null, IncludeEmailChannel: false, EmailRequired: false,
            PreferenceCategoryCode: NotificationPreferenceCategoryCodes.TrustSafety,
            LinkAllowed: false, MaterializedAt: notice.IssuedAt.UtcDateTime), token);
        return !result.IsSkipped && result.Notification is not null;
    }

    public async Task<(Guid? IntentId, ModerationNoticeDeliveryDisposition Disposition)> MaterializeAsync(
        ModerationNoticeSource source, Guid noticeId, ModerationNoticeAudience audience,
        User recipient, CancellationToken token)
    {
        if (source.TenantId is not { } tenantId || recipient.IsDeleted)
            throw new InvalidOperationException("Native notice projection requires a current tenant recipient.");
        var draft = new NotificationIntentDraft(Explore.Application.Notifications.NotificationCategory.TrustSafetyModeration,
            TenantId: tenantId, UserId: recipient.Id,
            RecipientKind: audience == ModerationNoticeAudience.Reviewer ? "Moderator" : "User",
            TemplateKey: ModerationNoticeEmailTemplate.Key,
            SafePayloadReference: ModerationNoticeEmailTemplate.Reference(noticeId),
            SafePayloadHash: source.SnapshotHash,
            DeduplicationKey: $"moderation-notice:{source.EffectId:N}:{(int)audience}:{recipient.Id:N}");
        var route = await ownership.ResolveAsync(draft, token);
        if (!route.IsLocalIslamuDelivery)
            return (null, ModerationNoticeDeliveryDisposition.NativeRoutingUnavailable);

        var preference = await preferences.ResolveAsync(new NotificationPreferenceResolveRequest(
            tenantId, recipient.Id, null, null,
            NotificationPreferenceCategoryCodes.TrustSafety, NotificationPreferenceChannelCodes.InApp), token);
        var email = RecipientEmailAddressResolver.Resolve(recipient, recipient.Id);
        var emailPreference = await preferences.ResolveAsync(new NotificationPreferenceResolveRequest(
            tenantId, recipient.Id, null, null,
            NotificationPreferenceCategoryCodes.TrustSafety, NotificationPreferenceChannelCodes.Email), token);
        var emailDraft = email.HasVerifiedEmail && emailPreference.IsEnabled ? new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId,
            Kind = EmailDispatchKind.AccountModerationOriginalNotice,
            SourceType = ModerationNoticeEmailTemplate.SourceType, SourceId = noticeId,
            RecipientUserId = recipient.Id, RecipientAddressSource = RecipientAddressSource.TenantUserVerifiedEmail,
            RecipientEmail = email.Email!, Subject = ModerationNoticeEmailTemplate.Subject,
            PlainTextBody = ModerationNoticeEmailTemplate.Body(noticeId),
            CreatedAt = source.CapturedAt.UtcDateTime
        } : null;
        var result = await materializer.MaterializeInCurrentTransactionAsync(new(
            Guid.CreateVersion7(), draft, NotificationDeliveryPolicyEnum.ModerationContextOptional,
            "moderation_portal_pointer",
            preference.IsEnabled ? new RecipientInAppNotificationDraft(
                (int)NotificationTypeEnum.General, "Moderation notice available", null,
                (int)ActorTypeEnum.User, (int)NotificationReasonEnum.System,
                EntityId: noticeId.ToString("D"), IsRequired: false) : null,
            Email: emailDraft, IncludeEmailChannel: true, EmailRequired: false,
            EmailSkipReason: email.SkipReason ?? "email_preference_disabled",
            PreferenceCategoryCode: NotificationPreferenceCategoryCodes.TrustSafety,
            EmailPreferenceEnabled: emailPreference.IsEnabled,
            LinkAllowed: false, MaterializedAt: source.CapturedAt.UtcDateTime,
            IncludeInAppChannel: true, InAppPreferenceEnabled: preference.IsEnabled), token);
        return result.IsSkipped
            ? (null, ModerationNoticeDeliveryDisposition.RecipientUnavailable)
            : (result.Intent.Id, result.Email?.Status == EmailDispatchStatus.Pending
                ? ModerationNoticeDeliveryDisposition.NativePortalEmailQueued
                : ModerationNoticeDeliveryDisposition.NativePortalEmailUnavailable);
    }
}
