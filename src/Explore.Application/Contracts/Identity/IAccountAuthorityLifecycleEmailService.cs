// ABOUTME: Application contract for requesting account-authority-owned identity lifecycle email actions.
// ABOUTME: Models verification and reset email delegation without exposing provider tokens or secrets.

using Explore.Application.Authentication;
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
    Guid UserId,
    Guid ExternalLoginId,
    Guid? TenantId = null,
    string? ProposedEmail = null,
    string? ClientId = null,
    string? RedirectUri = null,
    int? LifespanSeconds = null,
    string? CorrelationId = null,
    string? ExpectedSecurityStamp = null)
{
    public override string ToString() => nameof(AccountAuthorityLifecycleEmailRequest);
}

// Only the Application routing service can construct a selection from a persisted binding.
public sealed class ResolvedAccountAuthority
{
    internal ResolvedAccountAuthority(Guid externalLoginId, Guid userId, ProviderAccountKey accountKey,
        AccountAuthorityKind kind)
    {
        ExternalLoginId = externalLoginId;
        UserId = userId;
        AccountKey = accountKey;
        Kind = kind;
    }

    public Guid ExternalLoginId { get; }
    public Guid UserId { get; }
    public ProviderAccountKey AccountKey { get; }
    public AccountAuthorityKind Kind { get; }
}

// Provider adapters consume a server-resolved binding, never a caller's provider designation.
public interface IAccountAuthorityLifecycleEmailProvider
{
    AccountAuthorityKind Kind { get; }

    Task<AccountAuthorityLifecycleEmailResult> RequestAsync(
        AccountAuthorityLifecycleEmailAction action,
        AccountAuthorityLifecycleEmailRequest request,
        ResolvedAccountAuthority authority,
        CancellationToken cancellationToken = default);
}

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
    ProviderRequestFailed = 3,
    AccountNotLinked = 4,
    ProviderManaged = 5,
    ScopeUnavailable = 6
}
