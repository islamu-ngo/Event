using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Application.Telemetry;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.ExternalApiKeys.Handlers.Commands;

public class RevokeExternalApiKeyCommandHandler : ICommandHandler<RevokeExternalApiKeyCommand, bool>
{
    private readonly IExternalApiKeyRepository _externalApiKeyRepository;
    private readonly IExternalApiKeyIssuanceAuthority _ownerAuthority;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAdminContext _adminContext;
    private readonly IUserContext _userContext;
    private readonly InstanceApiKeyOperations _instanceOperations;
    private readonly BusinessMetrics _metrics;
    private readonly ILogger<RevokeExternalApiKeyCommandHandler> _logger;
    private readonly IHumanApiKeyManagementPreparation _preparation;
    private readonly ISerializableAttemptOwner _attemptOwner;
    private readonly IHumanActionCommitFinalizer _finalizer;

    public RevokeExternalApiKeyCommandHandler(
        IExternalApiKeyRepository externalApiKeyRepository,
        IExternalApiKeyIssuanceAuthority ownerAuthority,
        IInstanceAdministratorCommitAuthority instanceAuthority,
        IUnitOfWork unitOfWork,
        IAdminContext adminContext,
        IUserContext userContext,
        InstanceApiKeyOperations instanceOperations,
        BusinessMetrics metrics,
        ILogger<RevokeExternalApiKeyCommandHandler> logger,
        IHumanApiKeyManagementPreparation preparation,
        ISerializableAttemptOwner attemptOwner,
        IHumanActionCommitFinalizer finalizer)
    {
        _externalApiKeyRepository = externalApiKeyRepository;
        _ownerAuthority = ownerAuthority;
        _instanceAuthority = instanceAuthority;
        _unitOfWork = unitOfWork;
        _adminContext = adminContext;
        _userContext = userContext;
        _instanceOperations = instanceOperations;
        _metrics = metrics;
        _logger = logger;
        _preparation = preparation;
        _attemptOwner = attemptOwner;
        _finalizer = finalizer;
    }

    /// <summary>
    /// Holds persisted owner authority and the current-tenant or platform key through idempotent revocation.
    /// </summary>
    public async Task<bool> ExecuteAsync(RevokeExternalApiKeyCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated)
            return false;
        var resolvedUserId = await _adminContext.ResolveUserIdAsync(cancellationToken);
        if (resolvedUserId is not Guid currentUserId || currentUserId == Guid.Empty)
            return false;
        if (request.Id == Guid.Empty)
            return false;
        if (await _externalApiKeyRepository.GetForTenantAsync(request.Id, cancellationToken) is null)
        {
            if (!await _adminContext.IsInstanceAdminAsync(cancellationToken))
                return false;
            return await _instanceOperations.RevokeAsync(currentUserId, request.Id, cancellationToken);
        }
        var prepared = await _preparation.PrepareAsync(request, currentUserId, cancellationToken);
        Explore.Domain.ExternalApiKey? revokedKey = null;
        async Task<bool> ExecuteOwnerAsync(CancellationToken token)
        {
            revokedKey = null;
            if (prepared is not null)
                await prepared.AcquireAsync(request, currentUserId, token);
            var before = await _externalApiKeyRepository.GetForTenantAsync(request.Id, token);
            if (before is null || !await ExternalApiKeyManagementAuthority.IsAuthorizedForCommitAsync(
                    before, currentUserId, _ownerAuthority, _instanceAuthority, token))
                return false;
            var externalApiKey = await _externalApiKeyRepository.GetForManagementAsync(request.Id, token);
            if (externalApiKey is null || !ExternalApiKeyManagementAuthority.HasSameOwner(before, externalApiKey))
                return false;
            if (prepared is not null)
                ExternalApiKeyManagementAuthority.RegisterFinalAdmission(before, currentUserId,
                    _externalApiKeyRepository, _ownerAuthority, _instanceAuthority, _finalizer);
            if (externalApiKey.ExternalApiKeyStatusId == (int)ExternalApiKeyStatusEnum.Revoked)
                return true;
            externalApiKey.ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Revoked;
            externalApiKey.UpdatedAt = DateTime.UtcNow;
            externalApiKey.UpdatedBy = currentUserId;
            await _externalApiKeyRepository.Update(externalApiKey, token);
            token.ThrowIfCancellationRequested();
            revokedKey = externalApiKey;
            return true;
        }
        var success = prepared is null
            ? await _unitOfWork.ExecuteSerializableAsync(ExecuteOwnerAsync, cancellationToken)
            : await _attemptOwner.ExecuteAsync(ExecuteOwnerAsync, cancellationToken);
        if (!success || revokedKey is null)
            return success;

        _metrics.RecordExternalApiKeyRevoked(
            revokedKey.TenantId?.ToString() ?? "platform",
            revokedKey.OwnerType.ToString());

        _logger.LogInformation(
            "External API key revoked. OwnerType: {OwnerType}.",
            revokedKey.OwnerType);

        return true;
    }

}
