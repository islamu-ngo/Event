using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.DTOs.ExternalApiKey.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Application.Lookups;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.ExternalApiKeys.Handlers.Commands;

public class CreateExternalApiKeyCommandHandler : ICommandHandler<CreateExternalApiKeyCommand, CreateExternalApiKeyCommandResponse>
{
    private readonly IExternalApiKeyRepository _externalApiKeyRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IGroupRepository _groupRepository;
    private readonly IAdminContext _adminContext;
    private readonly IUserContext _userContext;
    private readonly ITenantContext _tenantContext;
    private readonly BusinessMetrics _metrics;
    private readonly ILogger<CreateExternalApiKeyCommandHandler> _logger;
    private readonly IExternalApiKeyIssuanceReceiptRepository _receipts;
    private readonly IExternalApiKeyIssuanceAuthority _issuanceAuthority;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPrivacyIdentityFenceAuthority _privacyAuthority;

    public CreateExternalApiKeyCommandHandler(
        IExternalApiKeyRepository externalApiKeyRepository,
        IOrganizationRepository organizationRepository,
        IOrganizationMemberRepository organizationMemberRepository,
        IGroupMemberRepository groupMemberRepository,
        IGroupRepository groupRepository,
        IAdminContext adminContext,
        IUserContext userContext,
        ITenantContext tenantContext,
        BusinessMetrics metrics,
        ILogger<CreateExternalApiKeyCommandHandler> logger,
        IExternalApiKeyIssuanceReceiptRepository receipts,
        IExternalApiKeyIssuanceAuthority issuanceAuthority,
        IUnitOfWork unitOfWork,
        IPrivacyIdentityFenceAuthority privacyAuthority)
    {
        _externalApiKeyRepository = externalApiKeyRepository;
        _organizationRepository = organizationRepository;
        _organizationMemberRepository = organizationMemberRepository;
        _groupMemberRepository = groupMemberRepository;
        _groupRepository = groupRepository;
        _adminContext = adminContext;
        _userContext = userContext;
        _tenantContext = tenantContext;
        _metrics = metrics;
        _logger = logger;
        _receipts = receipts;
        _issuanceAuthority = issuanceAuthority;
        _unitOfWork = unitOfWork;
        _privacyAuthority = privacyAuthority;
    }

    public async Task<CreateExternalApiKeyCommandResponse> ExecuteAsync(CreateExternalApiKeyCommand request, CancellationToken cancellationToken)
    {
        if (!ExternalApiKeyIssuanceReceipt.IsValidOperationKey(request.OperationKey))
            return CreateExternalApiKeyCommandResponse.Failure(
                BaseCommandResponse.Validation<Guid>(["A bounded ASCII operation key is required."]));
        if (!_userContext.IsAuthenticated)
            throw new UnauthorizedAccessException("Authentication is required.");
        var currentUserId = await _adminContext.ResolveUserIdAsync(cancellationToken)
            ?? throw new UnauthorizedAccessException("The authenticated application user is unavailable.");
        if (currentUserId == Guid.Empty)
            throw new UnauthorizedAccessException("The authenticated application user is unavailable.");
        var dto = request.ExternalApiKeyDto;
        if (dto.Scopes is null || dto.Scopes.Count == 0 || dto.Scopes.Any(string.IsNullOrWhiteSpace))
            return CreateExternalApiKeyCommandResponse.Failure(
                BaseCommandResponse.Validation<Guid>(
                    ["At least one scope is required."], "External API key creation failed."));

        var ownerType = ToOwnerType(dto.ExternalApiKeyOwnerTypeId);

        var tenantId = ownerType == ExternalApiKeyOwnerType.InstanceAdmin
            ? (Guid?)null
            : _tenantContext.TenantId;

        var validator = new CreateExternalApiKeyDtoValidator(
            _externalApiKeyRepository,
            _organizationRepository,
            _groupRepository,
            currentUserId,
            tenantId);

        _receipts.RequireCleanWriteScope(tenantId);
        if (ownerType == 0
            || ownerType == ExternalApiKeyOwnerType.Organization && (dto.OrganizationId is null || dto.OrganizationId == Guid.Empty)
            || ownerType == ExternalApiKeyOwnerType.Group && (dto.GroupId is null || dto.GroupId == Guid.Empty))
        {
            var validationResult = await validator.ValidateAsync(dto, cancellationToken);
            return CreateExternalApiKeyCommandResponse.Failure(
                BaseCommandResponse.Validation<Guid>(
                    validationResult.Errors.Select(error => error.ErrorMessage),
                    "External API key creation failed."));
        }

        var authorityResult = await CheckOwnerAuthorityAsync(dto, currentUserId, cancellationToken);
        if (!authorityResult.IsAuthorized)
        {
            throw new AuthorizationException(authorityResult.DenialMessage);
        }

        Guid aggregateId = Guid.CreateVersion7();
        Guid receiptId = Guid.CreateVersion7();
        DateTime createdAt = DateTime.UtcNow;
        string? keyId = null;
        string? secret = null;
        string? rawApiKey = null;
        string operationFingerprint = ExternalApiKeyIssuanceReceipt.ComputeOperationFingerprint(
            currentUserId, tenantId, ownerType, authorityResult.OwnerId, request.OperationKey);
        string canonicalInput = JsonSerializer.Serialize(new
        {
            Name = dto.Name?.Trim(),
            Description = ExternalApiKeyInputValidation.NormalizeOptionalText(dto.Description),
            dto.ExternalApiKeyOwnerTypeId,
            dto.OrganizationId,
            dto.GroupId,
            Scopes = NormalizeScopes(dto.Scopes),
            ExpiresAt = dto.ExpiresAt?.ToUniversalTime(),
            CreditPeriodId = dto.CreditPeriodId ?? (int)ExternalApiKeyCreditPeriodEnum.None,
            dto.CreditLimit,
            dto.MaxRolloverCredits
        });
        string inputDigest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalInput)));

        try
        {
            var response = await ExecuteAtCommitAsync(async ct =>
            {
                var existing = await _receipts.FindAsync(operationFingerprint, tenantId, ct);
                if (existing is not null)
                    return await RecoverAsync(existing, ct);

                var validationResult = await validator.ValidateAsync(dto, ct);
                if (!validationResult.IsValid)
                    return CreateExternalApiKeyCommandResponse.Failure(
                        BaseCommandResponse.Validation<Guid>(validationResult.Errors.Select(error => error.ErrorMessage),
                            "External API key creation failed."));

                keyId ??= ApiKeyHashing.CreateKeyId();
                secret ??= ApiKeyHashing.CreateSecret();
                rawApiKey ??= ApiKeyHashing.FormatPersistedApiKey(keyId, secret);
                var key = new ExternalApiKey
                {
                    Id = aggregateId,
                    TenantId = tenantId,
                    Name = dto.Name.Trim(),
                    KeyId = keyId,
                    SecretHash = ApiKeyHashing.ComputeHash(secret),
                    Scopes = NormalizeScopes(dto.Scopes),
                    OwnerType = ownerType,
                    OwnerId = authorityResult.OwnerId,
                    ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
                    ExternalApiKeyStatus = null!,
                    ExternalApiKeyCreditPeriodId = dto.CreditPeriodId ?? (int)ExternalApiKeyCreditPeriodEnum.None,
                    ExternalApiKeyCreditPeriod = null!,
                    CreditLimit = dto.CreditLimit,
                    MaxRolloverCredits = dto.MaxRolloverCredits,
                    Description = ExternalApiKeyInputValidation.NormalizeOptionalText(dto.Description),
                    ExpiresAt = dto.ExpiresAt,
                    CreatedAt = createdAt,
                    CreatedBy = currentUserId
                };
                var receipt = ExternalApiKeyIssuanceReceipt.Create(
                    receiptId, operationFingerprint, inputDigest, tenantId, aggregateId, createdAt);
                await _receipts.CreateAsync(receipt, key, ct);
                return CreateExternalApiKeyCommandResponse.Issued(
                    aggregateId, "Save the secret now because it will not be shown again.", rawApiKey, keyId);
            });
            if (response.DisclosureStatus == ExternalApiKeyDisclosureStatus.Issued)
            {
                _metrics.RecordExternalApiKeyCreated(tenantId?.ToString() ?? "platform", ownerType.ToString());
                _logger.LogInformation("External API key issuance committed for owner type {OwnerType}.", ownerType);
            }
            return response;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A commit exception does not prove rollback. Re-enter both authority boundaries.
            var recovered = await ExecuteAtCommitAsync<CreateExternalApiKeyCommandResponse?>(async ct =>
            {
                var receipt = await _receipts.FindAsync(operationFingerprint, tenantId, ct);
                return receipt is null ? null : await RecoverAsync(receipt, ct);
            });
            if (recovered is not null)
                return recovered;
            throw;
        }

        Task<T> ExecuteAtCommitAsync<T>(Func<CancellationToken, Task<T>> operation) =>
            _privacyAuthority.ExecuteSerializedAsync(async ct =>
            {
                if (await _privacyAuthority.IsSubjectFencedAsync(currentUserId, ct))
                    throw new AuthorizationException("The application account is unavailable.");
                return await _unitOfWork.ExecuteSerializableAsync(async transactionToken =>
                {
                    if (!await _issuanceAuthority.IsAuthorizedForCommitAsync(
                            currentUserId, tenantId, ownerType, authorityResult.OwnerId, transactionToken))
                        throw new AuthorizationException("Current owner authority is required.");
                    return await operation(transactionToken);
                }, ct);
            }, cancellationToken);

        async Task<CreateExternalApiKeyCommandResponse> RecoverAsync(
            ExternalApiKeyIssuanceReceipt receipt, CancellationToken ct)
        {
            if (receipt.InputDigest != inputDigest)
                return CreateExternalApiKeyCommandResponse.Failure(
                    BaseCommandResponse.Conflict<Guid>(receipt.ExternalApiKeyId));
            var key = await _receipts.GetIssuedKeyAsync(
                receipt.ExternalApiKeyId, tenantId, ownerType, authorityResult.OwnerId, ct);
            return key is null
                ? CreateExternalApiKeyCommandResponse.Failure(
                    BaseCommandResponse.NotFound<Guid>("The issued key is no longer available."))
                : CreateExternalApiKeyCommandResponse.PreviouslyIssued(key.Id, key.KeyId);
        }
    }

    private async Task<OwnerAuthorityResult> CheckOwnerAuthorityAsync(
        DTOs.ExternalApiKey.CreateExternalApiKeyDto dto, Guid currentUserId, CancellationToken cancellationToken)
    {
        var ownerType = ToOwnerType(dto.ExternalApiKeyOwnerTypeId);

        switch (ownerType)
        {
            case ExternalApiKeyOwnerType.User:
                return OwnerAuthorityResult.Authorized(currentUserId);

            case ExternalApiKeyOwnerType.Organization:
                {
                    var orgId = dto.OrganizationId!.Value;
                    var hasPermission = await _organizationMemberRepository.HasPermissionInOrganization(
                        orgId, currentUserId, PermissionCodes.OrganizationManage);

                    return hasPermission
                        ? OwnerAuthorityResult.Authorized(orgId)
                        : OwnerAuthorityResult.Denied(
                            "You do not have permission to manage API keys for this organization.",
                            "Your organization role does not include organization management permission.");
                }

            case ExternalApiKeyOwnerType.Group:
                {
                    var groupId = dto.GroupId!.Value;
                    var hasPermission = await _groupMemberRepository.HasPermissionInGroup(
                        groupId, currentUserId, PermissionCodes.GroupManage);

                    return hasPermission
                        ? OwnerAuthorityResult.Authorized(groupId)
                        : OwnerAuthorityResult.Denied(
                            "You do not have permission to manage API keys for this group.",
                            "Your group role does not include group management permission.");
                }

            case ExternalApiKeyOwnerType.Tenant:
                {
                    var tenantId = _tenantContext.TenantId;
                    var isTenantAdmin = await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken);

                    return isTenantAdmin
                        ? OwnerAuthorityResult.Authorized(tenantId)
                        : OwnerAuthorityResult.Denied(
                            "You do not have permission to manage tenant-level API keys.",
                            "Only tenant administrators can create tenant-scoped API keys.");
                }

            case ExternalApiKeyOwnerType.InstanceAdmin:
                {
                    var isInstanceAdmin = await _adminContext.IsInstanceAdminAsync(cancellationToken);

                    return isInstanceAdmin
                        ? OwnerAuthorityResult.Authorized(currentUserId)
                        : OwnerAuthorityResult.Denied(
                            "You do not have permission to manage instance-level API keys.",
                            "Only instance administrators can create platform-scoped API keys.");
                }

            default:
                return OwnerAuthorityResult.Denied(
                    "Unsupported owner type.",
                    $"Owner type id '{dto.ExternalApiKeyOwnerTypeId}' is not supported.");
        }
    }

    private static ExternalApiKeyOwnerType ToOwnerType(int ownerTypeId)
    {
        if (!NormalizedLookupMetadata.IsExternalApiKeyOwnerTypeId(ownerTypeId))
        {
            return 0;
        }

        return (ExternalApiKeyOwnerType)ownerTypeId;
    }

    private static string NormalizeScopes(IEnumerable<string> scopes)
    {
        return string.Join(' ', scopes
            .Select(scope => scope.Trim().ToLowerInvariant())
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(scope => scope, StringComparer.OrdinalIgnoreCase));
    }

}
