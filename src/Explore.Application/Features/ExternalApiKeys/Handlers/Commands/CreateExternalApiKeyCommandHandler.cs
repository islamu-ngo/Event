using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
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
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.ExternalApiKeys.Handlers.Commands;

public class CreateExternalApiKeyCommandHandler : IRequestHandler<CreateExternalApiKeyCommand, CreateExternalApiKeyCommandResponse>
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
        ILogger<CreateExternalApiKeyCommandHandler> logger)
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
    }

    public async Task<CreateExternalApiKeyCommandResponse> Handle(CreateExternalApiKeyCommand request, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.GetRequiredUserId();
        var dto = request.ExternalApiKeyDto;

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

        var validationResult = await validator.ValidateAsync(dto, cancellationToken);
        if (!validationResult.IsValid)
        {
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

        var keyId = ApiKeyHashing.CreateKeyId();
        var secret = ApiKeyHashing.CreateSecret();
        var rawApiKey = ApiKeyHashing.FormatPersistedApiKey(keyId, secret);

        var externalApiKey = new ExternalApiKey
        {
            TenantId = tenantId,
            Tenant = null,
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
            CreatedAt = DateTime.UtcNow,
            CreatedBy = currentUserId
        };

        externalApiKey = await _externalApiKeyRepository.Create(externalApiKey);

        _metrics.RecordExternalApiKeyCreated(
            externalApiKey.TenantId?.ToString() ?? "platform",
            externalApiKey.OwnerType.ToString());

        _logger.LogInformation(
            "External API key {KeyId} created for tenant {TenantId} with owner type {OwnerType} and owner {OwnerId}.",
            externalApiKey.KeyId,
            externalApiKey.TenantId?.ToString() ?? "platform",
            externalApiKey.OwnerType,
            externalApiKey.OwnerId);

        return CreateExternalApiKeyCommandResponse.Success(
            externalApiKey.Id,
            "External API key created successfully. Save the secret now because it will not be shown again.",
            rawApiKey,
            externalApiKey.KeyId);
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
            .Select(scope => scope.Trim())
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(scope => scope, StringComparer.OrdinalIgnoreCase));
    }

}
