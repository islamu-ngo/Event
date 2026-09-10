using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Application.Notifications;

public sealed class RecipientNotificationMaterializer(
    IRecipientNotificationGraphRepository notificationGraphRepository,
    IUnitOfWork unitOfWork,
    ISettingMutationLock mutationLock,
    IPrivacyErasureStateRepository? privacyErasureStateRepository = null) : IRecipientNotificationMaterializer
{
    public Task<RecipientNotificationMaterializationResult> MaterializeAsync(
        RecipientNotificationMaterialization request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        return mutationLock.ExecuteOrderedGroupsAsync(
            [[GovernanceSettingKeys.Email.DeliveryEnabled]],
            token => MaterializeUnderPolicyLockAsync(request, token), cancellationToken);
    }

    private async Task<RecipientNotificationMaterializationResult> MaterializeUnderPolicyLockAsync(
        RecipientNotificationMaterialization request, CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(
                ct => MaterializeGraphAsync(request, ct),
                cancellationToken);
        }
        catch (NotificationIntentDeduplicationConflictException)
        {
            return await unitOfWork.ExecuteInTransactionAsync(
                ct => LoadWinningGraphAsync(request, ct),
                cancellationToken);
        }
    }

    public Task<RecipientNotificationMaterializationResult> MaterializeInCurrentTransactionAsync(
        RecipientNotificationMaterialization request,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        return MaterializeGraphAsync(request, cancellationToken);
    }

    private async Task<RecipientNotificationMaterializationResult> MaterializeGraphAsync(
        RecipientNotificationMaterialization request,
        CancellationToken cancellationToken)
    {
        if (await IsFencedAsync(request, cancellationToken))
        {
            return RecipientNotificationMaterializationResult.Skipped();
        }

        var graph = BuildGraph(request);
        if (await IsFencedAsync(request, cancellationToken))
        {
            return RecipientNotificationMaterializationResult.Skipped();
        }

        await notificationGraphRepository.CreateGraphAsync(graph.Intent, cancellationToken);
        return graph;
    }

    private async Task<RecipientNotificationMaterializationResult> LoadWinningGraphAsync(
        RecipientNotificationMaterialization request,
        CancellationToken cancellationToken)
    {
        if (await IsFencedAsync(request, cancellationToken))
        {
            return RecipientNotificationMaterializationResult.Skipped();
        }

        NotificationIntent? winner = await LoadWinningIntentAsync(request, cancellationToken);
        if (winner is null)
        {
            throw new InvalidOperationException("The winning notification intent was not visible after exact conflict rollback.");
        }

        RecipientNotificationMaterializationResult expected = BuildGraph(request with { IntentId = winner.Id });
        if (await IsFencedAsync(request, cancellationToken))
        {
            return RecipientNotificationMaterializationResult.Skipped();
        }

        await notificationGraphRepository.RepairMissingRecipientDeliveryRowsAsync(
            winner,
            expected.Deliveries,
            expected.Notification,
            expected.Email,
            cancellationToken);

        NotificationIntent repaired = await LoadWinningIntentAsync(request, cancellationToken)
            ?? throw new InvalidOperationException("The repaired notification graph could not be loaded.");
        return ToResult(repaired);
    }

    private Task<NotificationIntent?> LoadWinningIntentAsync(
        RecipientNotificationMaterialization request,
        CancellationToken cancellationToken)
    {
        Guid tenantId = request.Intent.TenantId!.Value;
        return request.Intent.FanoutOccurrenceId is { } occurrenceId
            ? notificationGraphRepository.GetGraphByTenantOccurrenceAndRecipientAsync(
                tenantId,
                occurrenceId,
                request.Intent.UserId!.Value,
                cancellationToken)
            : notificationGraphRepository.GetGraphByTenantAndDeduplicationKeyAsync(
                tenantId,
                request.Intent.DeduplicationKey!,
                cancellationToken);
    }

    private async Task<bool> IsFencedAsync(
        RecipientNotificationMaterialization request,
        CancellationToken cancellationToken) =>
        privacyErasureStateRepository is not null
        && await privacyErasureStateRepository.GetBySubjectAsync(
            request.Intent.UserId!.Value,
            cancellationToken) is not null;

    private static RecipientNotificationMaterializationResult BuildGraph(RecipientNotificationMaterialization request)
    {
        NotificationIntentDraft draft = request.Intent;
        Guid tenantId = draft.TenantId!.Value;
        Guid recipientUserId = draft.UserId!.Value;
        DateTime now = request.MaterializedAt ?? DateTime.UtcNow;

        var intent = new NotificationIntent
        {
            Id = request.IntentId,
            TenantId = tenantId,
            CategoryId = MapCategory(draft.Category),
            OwnershipTypeId = (int)NotificationOwnershipTypeEnum.IslamuEvent,
            RecipientKindId = MapRecipientKind(draft.RecipientKind),
            StatusId = request.Email is null
                ? (int)NotificationIntentStatusEnum.Resolved
                : (int)NotificationIntentStatusEnum.DispatchQueued,
            TemplateKey = draft.TemplateKey!,
            DeduplicationKey = draft.DeduplicationKey!,
            SafePayloadReference = BlankToNull(draft.SafePayloadReference),
            SafePayloadHash = BlankToNull(draft.SafePayloadHash),
            CorrelationId = BlankToNull(draft.CorrelationId),
            RecipientUserId = recipientUserId,
            FanoutOccurrenceId = draft.FanoutOccurrenceId,
            EventId = draft.EventId,
            ReportId = draft.ReportId,
            ReportDecisionId = draft.ReportDecisionId,
            CreatedAt = now
        };

        Notification? notification = null;
        if (request.InApp is not null)
        {
            RecipientInAppNotificationDraft inApp = request.InApp;
            notification = new Notification
            {
                Id = request.InAppNotificationId ?? Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                NotificationIntentId = intent.Id,
                NotificationIntent = intent,
                UserId = recipientUserId,
                User = null!,
                NotificationTypeId = inApp.NotificationTypeId,
                NotificationType = null!,
                Title = inApp.Title.Trim(),
                Body = BlankToNull(inApp.Body),
                DeduplicationKey = $"{draft.DeduplicationKey}:in-app",
                NotificationEntityTypeId = inApp.NotificationEntityTypeId,
                EntityId = BlankToNull(inApp.EntityId),
                NotificationScopeId = inApp.NotificationScopeId,
                NotificationScope = null!,
                NotificationReasonId = inApp.NotificationReasonId,
                CreatedAt = now
            };
            intent.Deliveries.Add(CreateDelivery(
                request,
                request.InAppDeliveryId,
                NotificationPreferenceChannelEnum.InApp,
                inApp.IsRequired,
                NotificationDeliveryStatusEnum.Delivered,
                now,
                notification,
                null,
                null));
        }
        else if (request.IncludeInAppChannel)
        {
            intent.Deliveries.Add(CreateDelivery(
                request,
                request.InAppDeliveryId,
                NotificationPreferenceChannelEnum.InApp,
                isRequired: false,
                NotificationDeliveryStatusEnum.Skipped,
                now,
                notification: null,
                email: null,
                string.IsNullOrWhiteSpace(request.InAppSkipReason)
                    ? "in_app_preference_disabled"
                    : request.InAppSkipReason.Trim()));
        }

        EmailDispatchOutbox? email = request.Email is { } draftEmail ? CreateEmailDraft(draftEmail) : null;
        if (request.IncludeEmailChannel)
        {
            if (email is not null)
            {
                if (email.RecipientUserId != recipientUserId || email.TenantId != tenantId)
                {
                    throw new InvalidOperationException("Email recipient authority must match the notification intent tenant and user.");
                }

                email.Id = email.Id == Guid.Empty ? Guid.CreateVersion7() : email.Id;
                email.NotificationIntentId = intent.Id;
                email.NotificationIntent = intent;
                intent.Deliveries.Add(CreateDelivery(
                    request,
                    request.EmailDeliveryId,
                    NotificationPreferenceChannelEnum.Email,
                    request.EmailRequired,
                    NotificationDeliveryStatusEnum.Queued,
                    now,
                    null,
                    email,
                    null));
            }
            else
            {
                intent.Deliveries.Add(CreateDelivery(
                    request,
                    request.EmailDeliveryId,
                    NotificationPreferenceChannelEnum.Email,
                    request.EmailRequired,
                    NotificationDeliveryStatusEnum.Skipped,
                    now,
                    null,
                    null,
                    string.IsNullOrWhiteSpace(request.EmailSkipReason) ? "email_not_eligible" : request.EmailSkipReason.Trim()));
            }
        }

        return new RecipientNotificationMaterializationResult(intent, intent.Deliveries.ToArray(), notification, email);
    }

    // Each transaction owns its email entity; rollback cannot restore a mutated request object.
    private static EmailDispatchOutbox CreateEmailDraft(EmailDispatchOutbox source) => new()
    {
        Id = source.Id,
        TenantId = source.TenantId,
        PublishEventId = source.PublishEventId,
        Kind = source.Kind,
        SourceType = source.SourceType,
        SourceId = source.SourceId,
        EventId = source.EventId,
        RegistrationOrderId = source.RegistrationOrderId,
        RecipientUserId = source.RecipientUserId,
        RecipientAddressSource = source.RecipientAddressSource,
        ManagedTenantProvisioningOperationId = source.ManagedTenantProvisioningOperationId,
        RecipientEmail = source.RecipientEmail,
        Subject = source.Subject,
        PlainTextBody = source.PlainTextBody,
        HtmlBody = source.HtmlBody,
        ReplyTo = source.ReplyTo,
        MaxAttempts = source.MaxAttempts,
        NextAttemptAt = source.NextAttemptAt,
        CorrelationId = source.CorrelationId,
        CreatedAt = source.CreatedAt,
        CreatedBy = source.CreatedBy
    };

    private static NotificationDelivery CreateDelivery(
        RecipientNotificationMaterialization request,
        Guid? deliveryId,
        NotificationPreferenceChannelEnum channel,
        bool isRequired,
        NotificationDeliveryStatusEnum status,
        DateTime now,
        Notification? notification,
        EmailDispatchOutbox? email,
        string? failureCategory)
    {
        return new NotificationDelivery
        {
            Id = deliveryId ?? Guid.CreateVersion7(),
            TenantId = request.Intent.TenantId!.Value,
            NotificationIntentId = request.IntentId,
            ChannelId = (int)channel,
            DeliveryPolicyId = (int)request.DeliveryPolicy,
            IsRequired = isRequired,
            PolicyVersion = request.PolicyVersion,
            ConsentPurpose = request.ConsentPurpose,
            ConsentVersion = request.ConsentVersion,
            PreferenceCategoryCode = request.PreferenceCategoryCode,
            PreferenceEnabled = channel switch
            {
                NotificationPreferenceChannelEnum.Email => request.EmailPreferenceEnabled,
                NotificationPreferenceChannelEnum.InApp => request.InAppPreferenceEnabled,
                _ => null
            },
            RecipientAddressSource = email?.RecipientAddressSource,
            DisclosureLevel = request.DisclosureLevel,
            TemplateKey = request.Intent.TemplateKey!,
            TemplateVersion = request.TemplateVersion,
            LinkAllowed = request.LinkAllowed,
            NotificationId = notification?.Id,
            Notification = notification,
            EmailDispatchOutboxId = email?.Id,
            EmailDispatchOutbox = email,
            StatusId = (int)status,
            FailureCategory = failureCategory,
            QueuedAt = status == NotificationDeliveryStatusEnum.Queued ? now : null,
            CompletedAt = status is NotificationDeliveryStatusEnum.Delivered or NotificationDeliveryStatusEnum.Skipped ? now : null,
            CreatedAt = now
        };
    }

    private static RecipientNotificationMaterializationResult ToResult(NotificationIntent intent)
    {
        NotificationDelivery[] deliveries = intent.Deliveries.ToArray();
        return new RecipientNotificationMaterializationResult(
            intent,
            deliveries,
            deliveries.Select(delivery => delivery.Notification).FirstOrDefault(value => value is not null),
            deliveries.Select(delivery => delivery.EmailDispatchOutbox).FirstOrDefault(value => value is not null));
    }

    private static void Validate(RecipientNotificationMaterialization request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.IntentId == Guid.Empty
            || request.Intent.TenantId is null
            || request.Intent.TenantId == Guid.Empty
            || request.Intent.UserId is null
            || request.Intent.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("A non-empty intent, tenant, and recipient user id are required.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(request.Intent.TemplateKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Intent.DeduplicationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisclosureLevel);
        if (request.Intent.FanoutOccurrenceId == Guid.Empty)
        {
            throw new InvalidOperationException("Fanout occurrence id must be non-empty when supplied.");
        }

        if (request.InAppNotificationId == Guid.Empty
            || request.InAppDeliveryId == Guid.Empty
            || request.EmailDeliveryId == Guid.Empty)
        {
            throw new InvalidOperationException("Pre-generated notification graph ids must be non-empty when supplied.");
        }

        if (request.MaterializedAt is { Kind: not DateTimeKind.Utc })
        {
            throw new InvalidOperationException("Notification materialization time must be UTC when supplied.");
        }

        if (request.IncludeInAppChannel
            && request.InAppPreferenceEnabled == true
            && request.InApp is null)
        {
            throw new InvalidOperationException(
                "An enabled optional in-app channel requires an in-app notification draft.");
        }

        if (request.InApp is not null && request.InAppPreferenceEnabled == false)
        {
            throw new InvalidOperationException(
                "A disabled optional in-app channel cannot include an in-app notification draft.");
        }
    }

    private static int MapCategory(NotificationCategory category) =>
        Enum.TryParse<NotificationCategoryEnum>(category.ToString(), out var mapped)
            ? (int)mapped
            : throw new InvalidOperationException($"Unsupported notification category '{category}'.");

    private static int MapRecipientKind(string? recipientKind) =>
        Enum.TryParse<NotificationRecipientKindEnum>(recipientKind, true, out var mapped)
            ? (int)mapped
            : (int)NotificationRecipientKindEnum.User;

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
