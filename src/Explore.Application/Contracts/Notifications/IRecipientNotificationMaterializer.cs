using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Notifications;

public interface IRecipientNotificationMaterializer
{
    Task<RecipientNotificationMaterializationResult> MaterializeAsync(
        RecipientNotificationMaterialization request,
        CancellationToken cancellationToken = default);

    Task<RecipientNotificationMaterializationResult> MaterializeInCurrentTransactionAsync(
        RecipientNotificationMaterialization request,
        CancellationToken cancellationToken = default);
}

public sealed record RecipientNotificationMaterialization(
    Guid IntentId,
    NotificationIntentDraft Intent,
    NotificationDeliveryPolicyEnum DeliveryPolicy,
    string DisclosureLevel,
    RecipientInAppNotificationDraft? InApp,
    EmailDispatchOutbox? Email,
    bool IncludeEmailChannel,
    bool EmailRequired,
    string? EmailSkipReason = null,
    string? PreferenceCategoryCode = null,
    bool? EmailPreferenceEnabled = null,
    string? ConsentPurpose = null,
    int? ConsentVersion = null,
    int PolicyVersion = 1,
    int TemplateVersion = 1,
    bool LinkAllowed = false,
    Guid? InAppNotificationId = null,
    Guid? InAppDeliveryId = null,
    Guid? EmailDeliveryId = null,
    DateTime? MaterializedAt = null,
    bool IncludeInAppChannel = false,
    bool? InAppPreferenceEnabled = null,
    string? InAppSkipReason = null);

public sealed record RecipientInAppNotificationDraft(
    int NotificationTypeId,
    string Title,
    string? Body,
    int NotificationScopeId,
    int? NotificationReasonId = null,
    int? NotificationEntityTypeId = null,
    string? EntityId = null,
    bool IsRequired = true);

public sealed record RecipientNotificationMaterializationResult(
    NotificationIntent Intent,
    IReadOnlyList<NotificationDelivery> Deliveries,
    Notification? Notification,
    EmailDispatchOutbox? Email,
    bool IsSkipped = false)
{
    public static RecipientNotificationMaterializationResult Skipped() => new(null!, [], null, null, true);
}
