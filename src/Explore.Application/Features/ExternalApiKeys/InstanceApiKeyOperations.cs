using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.DTOs.ExternalApiKey.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.ExternalApiKeys;

/// <summary>Selected instance policy and native effects share each dedicated transaction owner.</summary>
public sealed class InstanceApiKeyOperations(
    IInstanceApiKeyExecutionScopeFactory scopes,
    BusinessMetrics metrics, ILogger<InstanceApiKeyOperations> logger)
{
    public async Task<IReadOnlyList<ExternalApiKey>> ReadAsync(
        Guid actorId, Guid? keyId, CancellationToken cancellationToken)
    {
        await using var scope = await scopes.CreateReadAsync(actorId, keyId, cancellationToken);
        return await scope.ExecuteAsync(InstanceApiKeyAction.Read, scope.ReadAsync, cancellationToken);
    }

    public async Task<BaseCommandResponse<Guid>> UpdateAsync(
        Guid actorId, Guid keyId, UpdateExternalApiKeyPolicyDto patch, CancellationToken cancellationToken)
    {
        DateTime changedAt = DateTime.UtcNow;
        await using var scope = await scopes.CreateWriteAsync(actorId, keyId, cancellationToken);
        var response = await scope.ExecuteAsync(InstanceApiKeyAction.Update, async token =>
        {
            var key = (await scope.ReadAsync(token)).SingleOrDefault();
            if (key is null)
                return BaseCommandResponse.NotFound<Guid>("External API key not found.");
            var validation = await new UpdateExternalApiKeyPolicyDtoValidator(scope.NameExistsAsync, key)
                .ValidateAsync(patch, token);
            if (!validation.IsValid)
                return BaseCommandResponse.Validation<Guid>(
                    validation.Errors.Select(error => error.ErrorMessage), "External API key update failed.");
            string name = patch.Metadata?.Name.Trim() ?? key.Name;
            string grantedScopes = patch.AccessPolicy is { } policy
                ? string.Join(' ', policy.Scopes.Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                : key.Scopes;
            DateTime? expiresAt = patch.AccessPolicy is { } access ? access.ExpiresAt : key.ExpiresAt;
            await scope.UpdatePolicyAsync(name, grantedScopes, expiresAt, changedAt, token);
            return BaseCommandResponse.Success(key.Id, "External API key updated successfully.");
        }, cancellationToken);
        if (response.IsSuccess)
        {
            metrics.RecordExternalApiKeyPolicyUpdated("platform", nameof(ExternalApiKeyOwnerType.InstanceAdmin));
            logger.LogInformation("Instance API key policy updated.");
        }
        return response;
    }

    public async Task<bool> RevokeAsync(Guid actorId, Guid keyId, CancellationToken cancellationToken)
    {
        DateTime changedAt = DateTime.UtcNow;
        await using var scope = await scopes.CreateWriteAsync(actorId, keyId, cancellationToken);
        bool changed = false;
        bool result = await scope.ExecuteAsync(InstanceApiKeyAction.Revoke, async token =>
        {
            changed = false;
            var key = (await scope.ReadAsync(token)).SingleOrDefault();
            if (key is null)
                return false;
            if (key.ExternalApiKeyStatusId == (int)ExternalApiKeyStatusEnum.Revoked)
                return true;
            changed = await scope.RevokeAsync(changedAt, token);
            return changed;
        }, cancellationToken);
        if (changed)
        {
            metrics.RecordExternalApiKeyRevoked("platform", nameof(ExternalApiKeyOwnerType.InstanceAdmin));
            logger.LogInformation("Instance API key revoked.");
        }
        return result;
    }

    public async Task<IReadOnlyList<TenantApiKeyUsageSummary>> ReportAsync(
        Guid actorId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        await using var scope = await scopes.CreateReportAsync(actorId, cancellationToken);
        return await scope.ReadAsync(from, to, cancellationToken);
    }

    internal static async Task AuthorizeAsync(
        IAuthorizationProvider authorization, string resourceId, string action, CancellationToken cancellationToken)
    {
        var decision = await authorization.AuthorizeAsync(
            new AuthorizationRequest(ResourceKinds.InstanceSetting, resourceId, action), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (decision.IsAllowed)
            return;
        if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
            throw new AuthorizationProviderUnavailableException(ResourceKinds.InstanceSetting, action);
        throw new AuthorizationException(ResourceKinds.InstanceSetting, action);
    }

}
