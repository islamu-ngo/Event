using Explore.Application.Notifications;

namespace Explore.Application.Contracts.Identity;

public interface IAccountAuthorityLifecycleEmailService
{
    Task<AccountAuthorityLifecycleEmailResult> RequestEmailVerificationAsync(
        AccountAuthorityLifecycleEmailRequest request,
        CancellationToken cancellationToken = default);

    Task<AccountAuthorityLifecycleEmailResult> RequestPasswordResetAsync(
        AccountAuthorityLifecycleEmailRequest request,
        CancellationToken cancellationToken = default);

    Task<AccountAuthorityLifecycleEmailResult> RequestEmailUpdateVerificationAsync(
        AccountAuthorityLifecycleEmailRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AccountAuthorityLifecycleEmailRequest(
    Guid TenantId,
    Guid UserId,
    string AccountAuthorityUserId,
    string? CurrentEmail = null,
    string? ProposedEmail = null,
    string? ClientId = null,
    string? RedirectUri = null,
    int? LifespanSeconds = null,
    string? CorrelationId = null);

public sealed record AccountAuthorityLifecycleEmailResult(
    AccountAuthorityLifecycleEmailStatus Status,
    AccountAuthorityLifecycleEmailAction Action,
    AccountAuthorityKind AccountAuthorityKind,
    Guid? NotificationIntentId = null,
    Guid? LocalDelegationId = null,
    string? ReasonCode = null)
{
    public bool DelegationRecorded => Status == AccountAuthorityLifecycleEmailStatus.DelegationRecorded;
}

public enum AccountAuthorityLifecycleEmailAction
{
    EmailVerification = 1,
    PasswordReset = 2,
    EmailUpdateVerification = 3
}

public enum AccountAuthorityLifecycleEmailStatus
{
    Disabled = 0,
    ProviderNotConfigured = 1,
    DelegationRecorded = 2,
    ProviderRequestFailed = 3
}
