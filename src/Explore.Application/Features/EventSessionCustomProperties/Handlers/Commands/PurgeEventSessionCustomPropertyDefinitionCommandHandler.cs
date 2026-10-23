using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Features.CustomProperties;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;

public sealed class PurgeEventSessionCustomPropertyDefinitionCommandHandler : ICommandHandler<PurgeEventSessionCustomPropertyDefinitionCommand, BaseCommandResponse<CustomPropertyPurgeResultDto>>
{
    private readonly IEventSessionCustomPropertyRepository _repository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISerializableAttemptOwner _owner;
    private readonly IHumanAdministrativeActionPreparation _preparation;
    private readonly HybridCache _cache;
    private readonly BusinessMetrics? _metrics;
    private readonly ITenantContext _tenantContext;
    private readonly IAdminContext _adminContext;
    private readonly ITenantAdministratorCommitAuthority _tenantAuthority;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly RequestAuthorization<PurgeEventSessionCustomPropertyDefinitionCommand> _authorization;
    private readonly ILogger<PurgeEventSessionCustomPropertyDefinitionCommandHandler> _logger;

    public PurgeEventSessionCustomPropertyDefinitionCommandHandler(
        IEventSessionCustomPropertyRepository repository,
        IAuditLogRepository auditLogRepository,
        ICurrentUserService currentUserService,
        ISerializableAttemptOwner owner,
        HybridCache cache,
        ITenantContext tenantContext,
        IAdminContext adminContext,
        ITenantAdministratorCommitAuthority tenantAuthority,
        IInstanceAdministratorCommitAuthority instanceAuthority,
        RequestAuthorization<PurgeEventSessionCustomPropertyDefinitionCommand> authorization,
        ILogger<PurgeEventSessionCustomPropertyDefinitionCommandHandler> logger,
        IHumanAdministrativeActionPreparation preparation,
        BusinessMetrics? metrics = null)
    {
        _repository = repository;
        _auditLogRepository = auditLogRepository;
        _currentUserService = currentUserService;
        _owner = owner;
        _preparation = preparation;
        _cache = cache;
        _metrics = metrics;
        _tenantContext = tenantContext;
        _adminContext = adminContext;
        _tenantAuthority = tenantAuthority;
        _instanceAuthority = instanceAuthority;
        _authorization = authorization;
        _logger = logger;
    }

    public async Task<BaseCommandResponse<CustomPropertyPurgeResultDto>> ExecuteAsync(PurgeEventSessionCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
    {
        Guid tenantId = _tenantContext.TenantId;
        if (!_currentUserService.IsAuthenticated || tenantId == Guid.Empty
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } userId)
            return BaseCommandResponse.Authentication<CustomPropertyPurgeResultDto>();
        await _authorization.AuthorizeAsync(request, _logger, cancellationToken);
        Guid auditLogId = Guid.CreateVersion7();
        DateTime occurredAt = DateTime.UtcNow;
        CustomPropertyPurgeDependencySummary? completedSummary = null;
        string? completedOutcome = null;
        var prepared = await _preparation.PrepareAsync(request, userId, cancellationToken);
        var response = await _owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(request, userId, token);
            completedSummary = null;
            completedOutcome = null;
            if (!await _instanceAuthority.IsInstanceAdministratorForCommitAsync(userId, token)
                && !await _tenantAuthority.IsTenantAdministratorForCommitAsync(userId, tenantId, token))
                return BaseCommandResponse.Authorization<CustomPropertyPurgeResultDto>();
            return await PurgeAuthorizedAsync(request, tenantId, userId, auditLogId, occurredAt, (summary, outcome) =>
            {
                completedSummary = summary;
                completedOutcome = outcome;
            }, token);
        }, cancellationToken);
        if (completedSummary is not null && completedOutcome is not null)
        {
            RecordPurgeDecision(completedSummary, completedOutcome);
            if (response.IsSuccess)
                await _cache.RemoveByTagAsync(
                    SessionCustomPropertyCache.ListsByTenant(completedSummary.TenantId), CancellationToken.None);
        }
        return response;
    }

    private async Task<BaseCommandResponse<CustomPropertyPurgeResultDto>> PurgeAuthorizedAsync(
        PurgeEventSessionCustomPropertyDefinitionCommand request, Guid tenantId, Guid userId, Guid auditLogId, DateTime occurredAt,
        Action<CustomPropertyPurgeDependencySummary, string> completed, CancellationToken cancellationToken)
    {
        var reason = request.Reason.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return BaseCommandResponse.Validation<CustomPropertyPurgeResultDto>(
                ["A purge reason is required."],
                "Session custom-property definition purge failed.");
        }

        var summary = await _repository.FenceDefinitionForPurgeAsync(request.Id, cancellationToken)
            ? await _repository.GetPurgeDependencies(request.Id, cancellationToken) : null;
        if (summary is null || summary.TenantId != tenantId)
        {
            return BaseCommandResponse.NotFound<CustomPropertyPurgeResultDto>("Session custom-property definition not found.");
        }

        if (summary.HasBlockingDependencies)
        {
            var blockedResponse = CustomPropertyPurgeResponseFactory.ToBlockedResponse(
                summary,
                reason,
                "Session custom-property definition purge blocked.");
            completed(summary, "blocked");
            return blockedResponse;
        }

        var result = CustomPropertyPurgeResponseFactory.ToResult(summary, true, auditLogId, reason);
        bool purged = await _repository.PurgeDefinition(request.Id, cancellationToken);
        if (purged)
        {
            var audit = CustomPropertyPurgeResponseFactory.CreateAudit(summary, result, userId);
            audit.Timestamp = occurredAt;
            await _auditLogRepository.Create(audit);
        }
        cancellationToken.ThrowIfCancellationRequested();

        if (!purged)
        {
            var latestSummary = await _repository.GetPurgeDependencies(request.Id, cancellationToken);
            if (latestSummary?.HasBlockingDependencies == true)
            {
                var blockedResponse = CustomPropertyPurgeResponseFactory.ToBlockedResponse(
                    latestSummary,
                    reason,
                    "Session custom-property definition purge blocked.");
                completed(latestSummary, "blocked_after_recheck");
                return blockedResponse;
            }
        }

        completed(summary, purged ? "purged" : "failed");

        if (purged)
        {
            return BaseCommandResponse.Success(result, "Session custom-property definition purged successfully.");
        }

        const string failureMessage = "Session custom-property definition purge failed.";
        return BaseCommandResponse.Validation(
            [failureMessage],
            failureMessage,
            CustomPropertyPurgeResponseFactory.ToResult(summary, false, null, reason));
    }

    private void RecordPurgeDecision(CustomPropertyPurgeDependencySummary summary, string outcome)
    {
        _metrics?.RecordCustomPropertyPurgeDecision(
            summary.TenantId.ToString(),
            summary.Scope,
            outcome,
            CustomPropertyPurgeResponseFactory.GetPrimaryBlockerCategory(summary));
    }
}
