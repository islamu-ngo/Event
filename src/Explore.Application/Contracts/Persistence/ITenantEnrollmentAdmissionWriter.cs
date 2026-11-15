using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Owns the originally selected provider account and tenant storage scope.
/// The writer retains the complete enrollment-setting, lifecycle and privacy fences
/// through commit and invokes authorization only when allocating a new membership.
/// Existing active membership is reconciled against that same original account.
/// </summary>
public interface ITenantEnrollmentAdmissionWriter
{
    Guid TenantId { get; }
    AuthenticationProviderKind Provider { get; }
    Task<TenantUser> AdmitAsync(
        Func<CancellationToken, Task> authorizeNewMembership,
        CancellationToken cancellationToken);
}
