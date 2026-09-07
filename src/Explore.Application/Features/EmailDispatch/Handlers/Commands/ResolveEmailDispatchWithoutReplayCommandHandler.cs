using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.EmailDispatch.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Handlers.Commands;

public sealed class ResolveEmailDispatchWithoutReplayCommandHandler(
    IEmailDispatchOutboxRepository repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<ResolveEmailDispatchWithoutReplayCommand, BaseCommandResponse<Guid>>
{
    private static readonly HashSet<EmailDispatchStatus> ResolvableStatuses =
    [
        EmailDispatchStatus.DeadLettered,
        EmailDispatchStatus.Parked,
        EmailDispatchStatus.Unknown
    ];

    public async Task<BaseCommandResponse<Guid>> Handle(
        ResolveEmailDispatchWithoutReplayCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new ResolveEmailDispatchWithoutReplayCommandValidator();
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(
                "Email dispatch resolution request failed validation.",
                null,
                validationResult.Errors.Select(error => error.ErrorMessage));
        }

        var dispatch = await repository.GetByTenantAndId(request.TenantId, request.OutboxId, cancellationToken);
        if (dispatch is null)
        {
            return Failure(
                "Email dispatch outbox row was not found.",
                EmailDispatchFailureCodes.NotFound,
                ["Email dispatch outbox row was not found."]);
        }

        if (dispatch.ContentRedactedAt is not null)
        {
            return Failure(
                "Redacted email dispatch rows cannot be resolved again.",
                EmailDispatchFailureCodes.InvalidTransition,
                ["Redacted email dispatch rows cannot be resolved again."]);
        }

        if (!ResolvableStatuses.Contains(dispatch.Status))
        {
            return Failure(
                $"Email dispatch rows in {dispatch.Status} state cannot be resolved without replay.",
                EmailDispatchFailureCodes.InvalidTransition,
                [$"Email dispatch rows in {dispatch.Status} state cannot be resolved without replay."]);
        }

        var resolvedAt = DateTime.UtcNow;
        var resolved = await unitOfWork.ExecuteInTransactionAsync(
            ct => repository.TryResolveWithoutReplay(
                request.TenantId,
                request.OutboxId,
                request.Reason,
                request.ChangedBy,
                resolvedAt,
                ct),
            cancellationToken);

        return resolved
            ? Success(dispatch.Id, "Email dispatch resolved without replay.")
            : Failure(
                "Email dispatch state changed before it could be resolved.",
                EmailDispatchFailureCodes.ConcurrentTransition,
                ["Email dispatch state changed before it could be resolved."]);
    }

    private static BaseCommandResponse<Guid> Success(Guid id, string message) =>
        BaseCommandResponse.Success(id, message);

    private static BaseCommandResponse<Guid> Failure(
        string message,
        string? failureCode,
        IEnumerable<string> errors) => failureCode is null
            ? BaseCommandResponse.Validation<Guid>(errors, message)
            : BaseCommandResponse.Failure<Guid>(failureCode, message, errors);
}
