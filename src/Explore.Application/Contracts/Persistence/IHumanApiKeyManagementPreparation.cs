using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Application.Features.ExternalApiKeys.Requests.Queries;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Captures original human sources outside the owning transaction. A null result is reserved
/// for an authenticated machine; absent human credential evidence must fail closed.
/// Key ownership remains the existing handler-owned predicate, not a new outer policy.
/// </summary>
public interface IHumanApiKeyManagementPreparation
{
    Task<IPreparedHumanApiKeyManagement?> PrepareAsync(UpdateExternalApiKeyPolicyCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanApiKeyManagement?> PrepareAsync(RevokeExternalApiKeyCommand request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanApiKeyManagement?> PrepareAsync(GetExternalApiKeyDetailsRequest request,
        Guid principalId, CancellationToken cancellationToken = default);
    Task<IPreparedHumanApiKeyManagement?> PrepareAsync(GetExternalApiKeyUsageReportRequest request,
        Guid principalId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Single-use original-request custody on the selected data owner. Acquisition registers
/// terminal source/time admission; it does not replace the owner's management authorization.
/// </summary>
public interface IPreparedHumanApiKeyManagement
{
    Task AcquireAsync(object originalRequest, Guid principalId, CancellationToken cancellationToken = default);
}
