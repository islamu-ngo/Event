using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Features.CustomProperties;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventSessionCustomProperties.Handlers.Commands;

public sealed class PurgeEventSessionCustomPropertyDefinitionCommandHandler : IRequestHandler<PurgeEventSessionCustomPropertyDefinitionCommand, BaseCommandResponse<CustomPropertyPurgeResultDto>>
{
    private readonly IEventSessionCustomPropertyRepository _repository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly HybridCache _cache;
    private readonly BusinessMetrics? _metrics;

    public PurgeEventSessionCustomPropertyDefinitionCommandHandler(
        IEventSessionCustomPropertyRepository repository,
        IAuditLogRepository auditLogRepository,
        ICurrentUserService currentUserService,
        IUnitOfWork unitOfWork,
        HybridCache cache,
        BusinessMetrics? metrics = null)
    {
        _repository = repository;
        _auditLogRepository = auditLogRepository;
        _currentUserService = currentUserService;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _metrics = metrics;
    }

    public async Task<BaseCommandResponse<CustomPropertyPurgeResultDto>> Handle(PurgeEventSessionCustomPropertyDefinitionCommand request, CancellationToken cancellationToken)
    {
        var reason = request.Reason.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return BaseCommandResponse.Validation<CustomPropertyPurgeResultDto>(
                ["A purge reason is required."],
                "Session custom-property definition purge failed.");
        }

        var summary = await _repository.GetPurgeDependencies(request.Id, cancellationToken);
        if (summary is null)
        {
            return BaseCommandResponse.NotFound<CustomPropertyPurgeResultDto>("Session custom-property definition not found.");
        }

        if (summary.HasBlockingDependencies)
        {
            var blockedResponse = CustomPropertyPurgeResponseFactory.ToBlockedResponse(
                summary,
                reason,
                "Session custom-property definition purge blocked.");
            RecordPurgeDecision(summary, "blocked");
            return blockedResponse;
        }

        var auditLogId = Guid.CreateVersion7();
        var result = CustomPropertyPurgeResponseFactory.ToResult(summary, true, auditLogId, reason);
        var audit = CustomPropertyPurgeResponseFactory.CreateAudit(summary, result, _currentUserService.UserId);

        var purged = await _unitOfWork.ExecuteInTransactionAsync(
            async ct =>
            {
                var deleted = await _repository.PurgeDefinition(request.Id, ct);
                if (!deleted)
                {
                    return false;
                }

                await _auditLogRepository.Create(audit);
                return true;
            },
            cancellationToken);

        if (!purged)
        {
            var latestSummary = await _repository.GetPurgeDependencies(request.Id, cancellationToken);
            if (latestSummary?.HasBlockingDependencies == true)
            {
                var blockedResponse = CustomPropertyPurgeResponseFactory.ToBlockedResponse(
                    latestSummary,
                    reason,
                    "Session custom-property definition purge blocked.");
                RecordPurgeDecision(latestSummary, "blocked_after_recheck");
                return blockedResponse;
            }
        }

        RecordPurgeDecision(summary, purged ? "purged" : "failed");

        if (purged)
        {
            await _cache.RemoveAsync($"session-custom-properties:detail:{request.Id}", cancellationToken);
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
