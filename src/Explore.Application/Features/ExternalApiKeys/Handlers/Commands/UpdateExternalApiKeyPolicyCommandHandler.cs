using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ExternalApiKey.Validators;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.ExternalApiKeys.Handlers.Commands;

public class UpdateExternalApiKeyPolicyCommandHandler : IRequestHandler<UpdateExternalApiKeyPolicyCommand, BaseCommandResponse<Guid>>
{
    private readonly IExternalApiKeyRepository _externalApiKeyRepository;
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IGroupMemberRepository _groupMemberRepository;
    private readonly IAdminContext _adminContext;
    private readonly IUserContext _userContext;
    private readonly BusinessMetrics _metrics;
    private readonly ILogger<UpdateExternalApiKeyPolicyCommandHandler> _logger;

    public UpdateExternalApiKeyPolicyCommandHandler(
        IExternalApiKeyRepository externalApiKeyRepository,
        IOrganizationMemberRepository organizationMemberRepository,
        IGroupMemberRepository groupMemberRepository,
        IAdminContext adminContext,
        IUserContext userContext,
        BusinessMetrics metrics,
        ILogger<UpdateExternalApiKeyPolicyCommandHandler> logger)
    {
        _externalApiKeyRepository = externalApiKeyRepository;
        _organizationMemberRepository = organizationMemberRepository;
        _groupMemberRepository = groupMemberRepository;
        _adminContext = adminContext;
        _userContext = userContext;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(UpdateExternalApiKeyPolicyCommand request, CancellationToken cancellationToken)
    {
        var currentUserId = _userContext.GetRequiredUserId();
        var externalApiKey = await _externalApiKeyRepository.GetByIdIgnoringTenantFilter(request.ExternalApiKeyId, cancellationToken);

        if (externalApiKey is null || !await CanManageAsync(externalApiKey, currentUserId, cancellationToken))
        {
            return BaseCommandResponse.NotFound<Guid>("External API key not found.");
        }

        var validator = new UpdateExternalApiKeyPolicyDtoValidator(_externalApiKeyRepository, externalApiKey);
        var validationResult = await validator.ValidateAsync(request.ExternalApiKeyPolicyDto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(error => error.ErrorMessage),
                "External API key update failed.");
        }

        if (request.ExternalApiKeyPolicyDto.Metadata is { } metadata)
        {
            externalApiKey.Name = metadata.Name.Trim();
        }

        if (request.ExternalApiKeyPolicyDto.AccessPolicy is { } accessPolicy)
        {
            externalApiKey.Scopes = NormalizeScopes(accessPolicy.Scopes);
            externalApiKey.ExpiresAt = accessPolicy.ExpiresAt;
        }
        externalApiKey.UpdatedAt = DateTime.UtcNow;
        externalApiKey.UpdatedBy = currentUserId;

        await _externalApiKeyRepository.Update(externalApiKey);

        _metrics.RecordExternalApiKeyPolicyUpdated(
            externalApiKey.TenantId?.ToString() ?? "platform",
            externalApiKey.OwnerType.ToString());

        _logger.LogInformation(
            "External API key {KeyId} policy updated for tenant {TenantId} by user {UserId}.",
            externalApiKey.KeyId,
            externalApiKey.TenantId?.ToString() ?? "platform",
            currentUserId);

        return BaseCommandResponse.Success(externalApiKey.Id, "External API key updated successfully.");
    }

    private async Task<bool> CanManageAsync(Explore.Domain.ExternalApiKey externalApiKey, Guid currentUserId, CancellationToken cancellationToken)
    {
        return externalApiKey.OwnerType switch
        {
            ExternalApiKeyOwnerType.User => externalApiKey.OwnerId == currentUserId,
            ExternalApiKeyOwnerType.Organization => await _organizationMemberRepository.HasPermissionInOrganization(
                externalApiKey.OwnerId, currentUserId, PermissionCodes.OrganizationManage),
            ExternalApiKeyOwnerType.Group => await _groupMemberRepository.HasPermissionInGroup(
                externalApiKey.OwnerId, currentUserId, PermissionCodes.GroupManage),
            ExternalApiKeyOwnerType.Tenant => await _adminContext.IsTenantAdminAsync(externalApiKey.TenantId!.Value, cancellationToken),
            ExternalApiKeyOwnerType.InstanceAdmin => await _adminContext.IsInstanceAdminAsync(cancellationToken),
            _ => false
        };
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
