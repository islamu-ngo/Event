
using Explore.Application.Contracts.Identity;
using Explore.Application.Notifications;
using Explore.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Authentication;

public sealed class LocalIdentityLifecycleEmailService(
    ILocalIdentityExecutionScopeFactory scopes) : IAccountAuthorityLifecycleEmailProvider
{
    public AccountAuthorityKind Kind => AccountAuthorityKind.LocalIdentity;

    public async Task<AccountAuthorityLifecycleEmailResult> RequestAsync(AccountAuthorityLifecycleEmailAction action,
        AccountAuthorityLifecycleEmailRequest request, ResolvedAccountAuthority authority, CancellationToken cancellationToken = default)
    {
        if (authority.Kind != Kind || authority.UserId != request.UserId || authority.ExternalLoginId != request.ExternalLoginId
            || authority.AccountKey.ProviderKind != AuthenticationProviderKind.Local
            || !string.Equals(authority.AccountKey.Value, request.UserId.ToString("D"), StringComparison.Ordinal))
            return new(AccountAuthorityLifecycleEmailStatus.AccountNotLinked, action, Kind, ReasonCode: "local_account_not_linked");
        var purpose = action switch
        {
            AccountAuthorityLifecycleEmailAction.EmailVerification => LocalIdentityLifecyclePurpose.EmailVerification,
            AccountAuthorityLifecycleEmailAction.EmailUpdateVerification => LocalIdentityLifecyclePurpose.EmailChange,
            AccountAuthorityLifecycleEmailAction.PasswordReset => LocalIdentityLifecyclePurpose.PasswordRecovery,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        await using var identity = await scopes.CreateAsync(cancellationToken);
        var lifecycle = identity.Services.GetRequiredService<ILocalIdentityLifecycleStore>();
        var deliveries = identity.Services.GetRequiredService<ILocalIdentityLifecycleDeliveryStore>();
        LocalIdentityLifecycleRequest? nativeRequest;
        try
        {
            nativeRequest = await deliveries.ReadRequestAsync(request.UserId, request.ExternalLoginId, purpose,
                request.ProposedEmail, request.ExpectedSecurityStamp, cancellationToken);
        }
        catch (Exception exception) when (purpose == LocalIdentityLifecyclePurpose.PasswordRecovery
            && LocalIdentityIntakeFailureClassifier.IsTransientStoreFailure(exception))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(AccountAuthorityLifecycleEmailStatus.ProviderRequestFailed, action, Kind,
                ReasonCode: "local_intake_store_unavailable");
        }
        LocalIdentityLifecyclePointer? operation;
        try
        {
            operation = nativeRequest is null ? null : await lifecycle.BeginAsync(nativeRequest, cancellationToken);
        }
        catch (LocalIdentityIntakeException exception) when (purpose == LocalIdentityLifecyclePurpose.PasswordRecovery)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(AccountAuthorityLifecycleEmailStatus.ProviderRequestFailed, action, Kind,
                ReasonCode: exception.Failure == LocalIdentityIntakeFailure.Conflict
                    ? "local_intake_conflict" : "local_intake_store_unavailable");
        }
        // Public intake maps target-specific statuses uniformly; no recipient, pointer, or rate key is exposed here.
        // Local audit is the native ledger, NOT an external provider delegation in a fabricated tenant.
        return operation is null
            ? new(AccountAuthorityLifecycleEmailStatus.AccountNotLinked, action, Kind, ReasonCode: "local_request_not_eligible")
            : new(AccountAuthorityLifecycleEmailStatus.DelegationRecorded, action, Kind, ReasonCode: "local_operation_recorded");
    }
}
