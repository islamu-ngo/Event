using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Notifications;
using Microsoft.Extensions.Options;

namespace Explore.Application.Services;

public sealed class DefaultAccountAuthorityLifecycleEmailService(
    INotificationOrchestrator notificationOrchestrator,
    IOptions<AccountAuthorityLifecycleEmailOptions> options) : IAccountAuthorityLifecycleEmailService
{
    private const string RecipientKind = "User";

    public Task<AccountAuthorityLifecycleEmailResult> RequestEmailVerificationAsync(
        AccountAuthorityLifecycleEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        return RequestAsync(AccountAuthorityLifecycleEmailAction.EmailVerification, request, cancellationToken);
    }

    public Task<AccountAuthorityLifecycleEmailResult> RequestPasswordResetAsync(
        AccountAuthorityLifecycleEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        return RequestAsync(AccountAuthorityLifecycleEmailAction.PasswordReset, request, cancellationToken);
    }

    public Task<AccountAuthorityLifecycleEmailResult> RequestEmailUpdateVerificationAsync(
        AccountAuthorityLifecycleEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        return RequestAsync(AccountAuthorityLifecycleEmailAction.EmailUpdateVerification, request, cancellationToken);
    }

    private async Task<AccountAuthorityLifecycleEmailResult> RequestAsync(
        AccountAuthorityLifecycleEmailAction action,
        AccountAuthorityLifecycleEmailRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = options.Value;
        if (!settings.Enabled)
        {
            return CreateResult(
                AccountAuthorityLifecycleEmailStatus.Disabled,
                action,
                settings.AccountAuthorityKind,
                ReasonCode: "account_authority_lifecycle_email_disabled");
        }

        if (!settings.ProviderConfigured || settings.AccountAuthorityKind == AccountAuthorityKind.None)
        {
            return CreateResult(
                AccountAuthorityLifecycleEmailStatus.ProviderNotConfigured,
                action,
                settings.AccountAuthorityKind,
                ReasonCode: "account_authority_provider_not_configured");
        }

        var draft = CreateDraft(action, request, settings.AccountAuthorityKind);
        var orchestration = await notificationOrchestrator.EnqueueAsync(draft, cancellationToken);
        if (orchestration.IsFenced)
        {
            return CreateResult(
                AccountAuthorityLifecycleEmailStatus.Disabled,
                action,
                settings.AccountAuthorityKind,
                ReasonCode: "account_authority_lifecycle_email_unavailable");
        }

        return CreateResult(
            AccountAuthorityLifecycleEmailStatus.DelegationRecorded,
            action,
            orchestration.Decision.AccountAuthorityKind,
            orchestration.Intent.Id,
            orchestration.ExternalDelegation?.Id,
            "delegation_recorded");
    }

    private static NotificationIntentDraft CreateDraft(
        AccountAuthorityLifecycleEmailAction action,
        AccountAuthorityLifecycleEmailRequest request,
        AccountAuthorityKind accountAuthorityKind)
    {
        var externalUserId = RequireNonEmpty(request.AccountAuthorityUserId, "Account-authority user id is required.");
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.CreateVersion7().ToString("N")
            : request.CorrelationId.Trim();
        var templateKey = GetTemplateKey(action);
        var actionKey = GetActionKey(action);
        var safePayloadReference = $"account-authority:{accountAuthorityKind}:user:{externalUserId}";

        return new NotificationIntentDraft(
            NotificationCategory.IdentityLifecycle,
            TenantId: request.TenantId,
            RecipientKind: RecipientKind,
            TemplateKey: templateKey,
            SafePayloadReference: safePayloadReference,
            IsUserFacing: true,
            IsIslamuInitiated: true,
            DeduplicationKey: $"identity-lifecycle:{actionKey}:{request.UserId}:{correlationId}",
            CorrelationId: correlationId,
            UserId: request.UserId,
            ExternalProviderId: externalUserId,
            ExternalCorrelationId: correlationId);
    }

    private static AccountAuthorityLifecycleEmailResult CreateResult(
        AccountAuthorityLifecycleEmailStatus status,
        AccountAuthorityLifecycleEmailAction action,
        AccountAuthorityKind accountAuthorityKind,
        Guid? notificationIntentId = null,
        Guid? localDelegationId = null,
        string? ReasonCode = null)
    {
        return new AccountAuthorityLifecycleEmailResult(
            status,
            action,
            accountAuthorityKind,
            notificationIntentId,
            localDelegationId,
            ReasonCode);
    }

    private static string GetTemplateKey(AccountAuthorityLifecycleEmailAction action) => action switch
    {
        AccountAuthorityLifecycleEmailAction.EmailVerification => "identity.email.verify",
        AccountAuthorityLifecycleEmailAction.PasswordReset => "identity.password.reset",
        AccountAuthorityLifecycleEmailAction.EmailUpdateVerification => "identity.email-update.verify",
        _ => throw new InvalidOperationException($"Unsupported account-authority lifecycle email action '{action}'.")
    };

    private static string GetActionKey(AccountAuthorityLifecycleEmailAction action) => action switch
    {
        AccountAuthorityLifecycleEmailAction.EmailVerification => "email-verification",
        AccountAuthorityLifecycleEmailAction.PasswordReset => "password-reset",
        AccountAuthorityLifecycleEmailAction.EmailUpdateVerification => "email-update-verification",
        _ => throw new InvalidOperationException($"Unsupported account-authority lifecycle email action '{action}'.")
    };

    private static string RequireNonEmpty(string value, string message)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException(message) : value.Trim();
    }
}
