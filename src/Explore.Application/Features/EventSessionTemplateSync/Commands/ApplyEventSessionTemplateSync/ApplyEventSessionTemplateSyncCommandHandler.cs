using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionTemplateSync;
using Explore.Application.DTOs.EventSessionTemplateSync.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EventSessionTemplateSync.Commands.ApplyEventSessionTemplateSync;

public sealed class ApplyEventSessionTemplateSyncCommandHandler
    : IRequestHandler<ApplyEventSessionTemplateSyncCommand, BaseCommandResponse<TemplateSyncOutcomeDto>>
{
    private readonly IEventSessionTemplateSyncService _syncService;

    public ApplyEventSessionTemplateSyncCommandHandler(IEventSessionTemplateSyncService syncService)
    {
        _syncService = syncService;
    }

    public async Task<BaseCommandResponse<TemplateSyncOutcomeDto>> Handle(
        ApplyEventSessionTemplateSyncCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new TemplateSyncPlanDtoValidator();
        var validationResult = await validator.ValidateAsync(request.Plan, cancellationToken);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult);

        var outcome = await _syncService.ApplySyncAsync(
            request.EventSessionId,
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
                    : "The event session was updated concurrently. Recompute the diff and try again.",
                nameof(EventSession),
                request.EventSessionId.ToString());
        }

        if (outcome.Conflicts.Count == 0)
        {
            return BaseCommandResponse.Success(outcome, "Event session template sync applied successfully.");
        }

        return BaseCommandResponse.Failure(
            outcome.Conflicts[0].Reason,
            "Event session template sync completed with conflicts.",
            outcome.Conflicts.Select(x => $"{x.Key}:{x.Reason}"),
            outcome);
    }
}
