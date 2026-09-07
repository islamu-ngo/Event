using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using DomainAccountAuthorityKind = Explore.Domain.Enums.AccountAuthorityKindEnum;
using DomainExternalWorkflowProviderKind = Explore.Domain.Enums.ExternalWorkflowProviderKindEnum;
using DomainNotificationCategory = Explore.Domain.Enums.NotificationCategoryEnum;
using DomainNotificationDeliveryStatus = Explore.Domain.Enums.NotificationDeliveryStatusEnum;
using DomainNotificationExternalDelegationStatus = Explore.Domain.Enums.NotificationExternalDelegationStatusEnum;
using DomainNotificationIntentStatus = Explore.Domain.Enums.NotificationIntentStatusEnum;
using DomainNotificationOwnershipType = Explore.Domain.Enums.NotificationOwnershipTypeEnum;
using DomainNotificationRecipientKind = Explore.Domain.Enums.NotificationRecipientKindEnum;

namespace Explore.Application.Notifications;

public sealed class DefaultNotificationOrchestrator(
    INotificationOwnershipResolver ownershipResolver,
    INotificationIntentRepository notificationIntentRepository,
    IPrivacyErasureStateRepository privacyErasureStateRepository,
    IUnitOfWork unitOfWork) : INotificationOrchestrator
{
    public async Task<NotificationOrchestrationResult> EnqueueAsync(
        NotificationIntentDraft draft,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var tenantId = draft.TenantId ?? throw new InvalidOperationException("Notification tenant id is required.");
        var recipientUserId = draft.UserId ?? throw new InvalidOperationException("Notification recipient user id is required.");
        var templateKey = RequireNonEmpty(draft.TemplateKey, "Notification template key is required.");
        var deduplicationKey = RequireNonEmpty(draft.DeduplicationKey, "Notification deduplication key is required.");

        if (await IsFencedAsync(recipientUserId, cancellationToken))
        {
            return NotificationOrchestrationResult.Fenced();
        }

        var decision = await ownershipResolver.ResolveAsync(draft, cancellationToken);
        var intent = CreateIntent(draft, decision, tenantId, templateKey, deduplicationKey);

        NotificationDelivery? delivery = null;
        NotificationExternalDelegation? delegation = null;

        switch (decision.Ownership)
        {
            case NotificationOwnership.IslamuEvent:
                delivery = CreateDelivery(intent, tenantId);
                intent.Deliveries.Add(delivery);
                break;

            case NotificationOwnership.AccountAuthority:
                if (decision.RequiresLocalAudit)
                {
                    delegation = CreateAccountAuthorityDelegation(intent, draft, decision, tenantId, templateKey);
                    intent.ExternalDelegations.Add(delegation);
                }
                break;

            case NotificationOwnership.ExternalWorkflowProvider:
                if (decision.RequiresLocalAudit)
                {
                    delegation = CreateExternalWorkflowDelegation(intent, draft, decision, tenantId, templateKey);
                    intent.ExternalDelegations.Add(delegation);
                }
                break;

            case NotificationOwnership.Disabled:
                break;

            default:
                throw new InvalidOperationException($"Unsupported notification ownership '{decision.Ownership}'.");
        }

        return await unitOfWork.ExecuteSerializableAsync(async token =>
        {
            if (await IsFencedAsync(recipientUserId, token))
            {
                return NotificationOrchestrationResult.Fenced();
            }

            var savedIntent = await notificationIntentRepository.CreateIntentAsync(intent, token);
            return new NotificationOrchestrationResult(savedIntent, decision, delivery, delegation);
        }, cancellationToken);
    }

    private async Task<bool> IsFencedAsync(Guid recipientUserId, CancellationToken cancellationToken) =>
        await privacyErasureStateRepository.GetBySubjectAsync(recipientUserId, cancellationToken) is not null;

    private static NotificationIntent CreateIntent(
        NotificationIntentDraft draft,
        NotificationOwnershipDecision decision,
        Guid tenantId,
        string templateKey,
        string deduplicationKey)
    {
        return new NotificationIntent
        {
            TenantId = tenantId,
            CategoryId = MapCategory(draft.Category),
            OwnershipTypeId = MapOwnership(decision.Ownership),
            RecipientKindId = MapRecipientKind(draft.RecipientKind),
            StatusId = ResolveIntentStatus(decision),
            TemplateKey = templateKey,
            DeduplicationKey = deduplicationKey,
            SafePayloadReference = BlankToNull(draft.SafePayloadReference),
            SafePayloadHash = BlankToNull(draft.SafePayloadHash),
            CorrelationId = BlankToNull(draft.CorrelationId),
            RecipientUserId = draft.UserId!.Value,
            EventId = draft.EventId,
            ReportId = draft.ReportId,
            ReportDecisionId = draft.ReportDecisionId,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static NotificationDelivery CreateDelivery(NotificationIntent intent, Guid tenantId)
    {
        return new NotificationDelivery
        {
            TenantId = tenantId,
            NotificationIntentId = intent.Id,
            ChannelId = (int)Explore.Domain.Enums.NotificationPreferenceChannelEnum.InApp,
            DeliveryPolicyId = ResolveDeliveryPolicy(intent.CategoryId),
            IsRequired = true,
            PolicyVersion = 1,
            DisclosureLevel = "generic",
            TemplateKey = intent.TemplateKey,
            TemplateVersion = 1,
            StatusId = (int)DomainNotificationDeliveryStatus.Pending,
            QueuedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static int ResolveDeliveryPolicy(int categoryId)
    {
        return categoryId switch
        {
            (int)DomainNotificationCategory.RegistrationLifecycle =>
                (int)Explore.Domain.Enums.NotificationDeliveryPolicyEnum.RegistrationStatusOptional,
            (int)DomainNotificationCategory.EventLifecycle =>
                (int)Explore.Domain.Enums.NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional,
            (int)DomainNotificationCategory.TrustSafetyReporting =>
                (int)Explore.Domain.Enums.NotificationDeliveryPolicyEnum.ReportCaseUpdate,
            (int)DomainNotificationCategory.TrustSafetyModeration =>
                (int)Explore.Domain.Enums.NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired,
            _ => (int)Explore.Domain.Enums.NotificationDeliveryPolicyEnum.RegistrationStatusOptional
        };
    }

    private static NotificationExternalDelegation CreateAccountAuthorityDelegation(
        NotificationIntent intent,
        NotificationIntentDraft draft,
        NotificationOwnershipDecision decision,
        Guid tenantId,
        string templateKey)
    {
        if (decision.AccountAuthorityKind == AccountAuthorityKind.None)
        {
            throw new InvalidOperationException("Account-authority delegation requires a concrete account authority kind.");
        }

        return CreateDelegation(
            intent,
            draft,
            tenantId,
            templateKey,
            (int)DomainExternalWorkflowProviderKind.None,
            MapAccountAuthorityKind(decision.AccountAuthorityKind));
    }

    private static NotificationExternalDelegation CreateExternalWorkflowDelegation(
        NotificationIntent intent,
        NotificationIntentDraft draft,
        NotificationOwnershipDecision decision,
        Guid tenantId,
        string templateKey)
    {
        if (decision.ExternalWorkflowProviderKind == ExternalWorkflowProviderKind.None)
        {
            throw new InvalidOperationException("External workflow delegation requires a concrete provider kind.");
        }

        return CreateDelegation(
            intent,
            draft,
            tenantId,
            templateKey,
            MapExternalWorkflowProviderKind(decision.ExternalWorkflowProviderKind),
            accountAuthorityKindId: null);
    }

    private static NotificationExternalDelegation CreateDelegation(
        NotificationIntent intent,
        NotificationIntentDraft draft,
        Guid tenantId,
        string templateKey,
        int providerKindId,
        int? accountAuthorityKindId)
    {
        return new NotificationExternalDelegation
        {
            TenantId = tenantId,
            NotificationIntentId = intent.Id,
            ProviderKindId = providerKindId,
            AccountAuthorityKindId = accountAuthorityKindId,
            StatusId = (int)DomainNotificationExternalDelegationStatus.Requested,
            RecipientKindId = MapRecipientKind(draft.RecipientKind),
            TemplateKey = templateKey,
            SafePayloadHash = BlankToNull(draft.SafePayloadHash),
            ExternalProviderId = BlankToNull(draft.ExternalProviderId),
            ExternalCorrelationId = BlankToNull(draft.ExternalCorrelationId),
            ReportId = draft.ReportId,
            ReportDecisionId = draft.ReportDecisionId,
            RequestedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static int ResolveIntentStatus(NotificationOwnershipDecision decision)
    {
        return decision.Ownership switch
        {
            NotificationOwnership.IslamuEvent => (int)DomainNotificationIntentStatus.Pending,
            NotificationOwnership.Disabled => (int)DomainNotificationIntentStatus.Skipped,
            NotificationOwnership.AccountAuthority or NotificationOwnership.ExternalWorkflowProvider =>
                decision.RequiresLocalAudit
                    ? (int)DomainNotificationIntentStatus.Delegated
                    : (int)DomainNotificationIntentStatus.Resolved,
            _ => throw new InvalidOperationException($"Unsupported notification ownership '{decision.Ownership}'.")
        };
    }

    private static int MapCategory(NotificationCategory category)
    {
        return Enum.TryParse<DomainNotificationCategory>(category.ToString(), out var mapped)
            ? (int)mapped
            : throw new InvalidOperationException($"Unsupported notification category '{category}'.");
    }

    private static int MapOwnership(NotificationOwnership ownership)
    {
        return Enum.TryParse<DomainNotificationOwnershipType>(ownership.ToString(), out var mapped)
            ? (int)mapped
            : throw new InvalidOperationException($"Unsupported notification ownership '{ownership}'.");
    }

    private static int MapRecipientKind(string? recipientKind)
    {
        if (string.IsNullOrWhiteSpace(recipientKind)) return (int)DomainNotificationRecipientKind.User;

        return Enum.TryParse<DomainNotificationRecipientKind>(recipientKind, ignoreCase: true, out var mapped)
            ? (int)mapped
            : (int)DomainNotificationRecipientKind.Other;
    }

    private static int MapExternalWorkflowProviderKind(ExternalWorkflowProviderKind providerKind)
    {
        return Enum.TryParse<DomainExternalWorkflowProviderKind>(providerKind.ToString(), out var mapped)
            ? (int)mapped
            : throw new InvalidOperationException($"Unsupported external workflow provider '{providerKind}'.");
    }

    private static int MapAccountAuthorityKind(AccountAuthorityKind accountAuthorityKind)
    {
        return Enum.TryParse<DomainAccountAuthorityKind>(accountAuthorityKind.ToString(), out var mapped)
            ? (int)mapped
            : throw new InvalidOperationException($"Unsupported account authority '{accountAuthorityKind}'.");
    }

    private static string RequireNonEmpty(string? value, string message)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(message) : value.Trim();
    }

    private static string? BlankToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
