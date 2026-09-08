using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventTemplateSync;
using Explore.Application.DTOs.EventTemplateSync.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventTemplateSync.Commands.ApplyEventTemplateSync;

public sealed class ApplyEventTemplateSyncCommandHandler
    : IRequestHandler<ApplyEventTemplateSyncCommand, BaseCommandResponse<TemplateSyncOutcomeDto>>
{
    private readonly IEventTemplateSyncService _syncService;

    public ApplyEventTemplateSyncCommandHandler(IEventTemplateSyncService syncService)
    {
        _syncService = syncService;
    }

    public async Task<BaseCommandResponse<TemplateSyncOutcomeDto>> Handle(
        ApplyEventTemplateSyncCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new TemplateSyncPlanDtoValidator();
        var validationResult = await validator.ValidateAsync(request.Plan, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult);

        var outcome = await _syncService.ApplySyncAsync(
            request.EventId,
            request.Plan,
            request.BaseProvenanceVersion,
            cancellationToken);

        string? failureCode = outcome.Conflicts.FirstOrDefault()?.Reason;
        if (failureCode is ConcurrencyConflictException.StaleSyncBase or ConcurrencyConflictException.ConcurrentUpdate)
        {
            throw new ConcurrencyConflictException(
                failureCode,
                failureCode == ConcurrencyConflictException.StaleSyncBase
                    ? "The template sync base is stale. Recompute the diff and try again."
                    : "The event was updated concurrently. Recompute the diff and try again.",
                nameof(Event),
                request.EventId.ToString());
        }

        if (outcome.Conflicts.Count == 0)
        {
            return BaseCommandResponse.Success(outcome, "Event template sync applied successfully.");
        }

        return BaseCommandResponse.Failure(
            outcome.Conflicts[0].Reason,
            "Event template sync completed with conflicts.",
            outcome.Conflicts.Select(x => $"{x.Key}:{x.Reason}"),
            outcome);
    }
}
