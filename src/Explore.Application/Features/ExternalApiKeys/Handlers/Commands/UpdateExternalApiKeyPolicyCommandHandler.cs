using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ExternalApiKey.Validators;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.ExternalApiKeys.Handlers.Commands;

public class UpdateExternalApiKeyPolicyCommandHandler : ICommandHandler<UpdateExternalApiKeyPolicyCommand, BaseCommandResponse<Guid>>
{
    private readonly IExternalApiKeyRepository _externalApiKeyRepository;
    private readonly IExternalApiKeyIssuanceAuthority _ownerAuthority;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IHumanApiKeyManagementPreparation _preparation;
    private readonly ISerializableAttemptOwner _attemptOwner;
    private readonly IHumanActionCommitFinalizer _finalizer;
    private readonly IAdminContext _adminContext;
    private readonly IUserContext _userContext;
    private readonly InstanceApiKeyOperations _instanceOperations;
    private readonly BusinessMetrics _metrics;
    private readonly ILogger<UpdateExternalApiKeyPolicyCommandHandler> _logger;

    public UpdateExternalApiKeyPolicyCommandHandler(
        IExternalApiKeyRepository externalApiKeyRepository,
        IExternalApiKeyIssuanceAuthority ownerAuthority,
        IInstanceAdministratorCommitAuthority instanceAuthority,
        IUnitOfWork unitOfWork,
        IAdminContext adminContext,
        IUserContext userContext,
        InstanceApiKeyOperations instanceOperations,
        BusinessMetrics metrics,
        ILogger<UpdateExternalApiKeyPolicyCommandHandler> logger,
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

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(UpdateExternalApiKeyPolicyCommand request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } currentUserId)
            return BaseCommandResponse.Authentication<Guid>();
        if (request.ExternalApiKeyId == Guid.Empty)
            return BaseCommandResponse.NotFound<Guid>("External API key not found.");
        if (await _externalApiKeyRepository.GetForTenantAsync(request.ExternalApiKeyId, cancellationToken) is null)
        {
            if (!await _adminContext.IsInstanceAdminAsync(cancellationToken))
                return BaseCommandResponse.NotFound<Guid>("External API key not found.");
            return await _instanceOperations.UpdateAsync(
                currentUserId, request.ExternalApiKeyId, request.ExternalApiKeyPolicyDto, cancellationToken);
        }
        var prepared = await _preparation.PrepareAsync(request, currentUserId, cancellationToken);
        Explore.Domain.ExternalApiKey? updatedKey = null;
        async Task<BaseCommandResponse<Guid>> ExecuteOwnerAsync(CancellationToken token)
        {
            updatedKey = null;
            if (prepared is not null) await prepared.AcquireAsync(request, currentUserId, token);
            var before = await _externalApiKeyRepository.GetForTenantAsync(request.ExternalApiKeyId, token);
            if (before is null || !await ExternalApiKeyManagementAuthority.IsAuthorizedForCommitAsync(
                    before, currentUserId, _ownerAuthority, _instanceAuthority, token))
                return BaseCommandResponse.NotFound<Guid>("External API key not found.");
            var externalApiKey = await _externalApiKeyRepository.GetForManagementAsync(request.ExternalApiKeyId, token);
            if (externalApiKey is null || !ExternalApiKeyManagementAuthority.HasSameOwner(before, externalApiKey))
                return BaseCommandResponse.NotFound<Guid>("External API key not found.");
            if (prepared is not null)
                ExternalApiKeyManagementAuthority.RegisterFinalAdmission(before, currentUserId,
                    _externalApiKeyRepository, _ownerAuthority, _instanceAuthority, _finalizer);
            var validator = new UpdateExternalApiKeyPolicyDtoValidator(
                (name, ct) => _externalApiKeyRepository.ExistsByOwnerAndName(
                    externalApiKey.OwnerType, externalApiKey.OwnerId, name, ct), externalApiKey);
            var validationResult = await validator.ValidateAsync(request.ExternalApiKeyPolicyDto, token);
            if (!validationResult.IsValid)
                return BaseCommandResponse.Validation<Guid>(
                    validationResult.Errors.Select(error => error.ErrorMessage), "External API key update failed.");
            if (request.ExternalApiKeyPolicyDto.Metadata is { } metadata)
                externalApiKey.Name = metadata.Name.Trim();
            if (request.ExternalApiKeyPolicyDto.AccessPolicy is { } accessPolicy)
            {
                externalApiKey.Scopes = NormalizeScopes(accessPolicy.Scopes);
                externalApiKey.ExpiresAt = accessPolicy.ExpiresAt;
            }
            externalApiKey.UpdatedAt = DateTime.UtcNow;
            externalApiKey.UpdatedBy = currentUserId;
            await _externalApiKeyRepository.Update(externalApiKey, token);
            token.ThrowIfCancellationRequested();
            updatedKey = externalApiKey;
            return BaseCommandResponse.Success(externalApiKey.Id, "External API key updated successfully.");
        }
        var response = prepared is null
            ? await _unitOfWork.ExecuteSerializableAsync(ExecuteOwnerAsync, cancellationToken)
            : await _attemptOwner.ExecuteAsync(ExecuteOwnerAsync, cancellationToken);
        if (!response.IsSuccess || updatedKey is null)
            return response;

        _metrics.RecordExternalApiKeyPolicyUpdated(
            updatedKey.TenantId?.ToString() ?? "platform",
            updatedKey.OwnerType.ToString());

        _logger.LogInformation("External API key policy updated.");

        return response;
    }

    private static string NormalizeScopes(IEnumerable<string> scopes)
    {
        return string.Join(' ', scopes
            .Select(scope => scope.Trim())
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(scope => scope, StringComparer.OrdinalIgnoreCase));
    }
}
