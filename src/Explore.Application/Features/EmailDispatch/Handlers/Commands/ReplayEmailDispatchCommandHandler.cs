using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.EmailDispatch.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Handlers.Commands;

public sealed class ReplayEmailDispatchCommandHandler : IRequestHandler<ReplayEmailDispatchCommand, BaseCommandResponse<Guid>>
{
    private static readonly HashSet<EmailDispatchStatus> ReplayableStatuses =
    [
        EmailDispatchStatus.DeadLettered,
        EmailDispatchStatus.Parked,
        EmailDispatchStatus.RetryScheduled
    ];

    private readonly IEmailDispatchOutboxRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public ReplayEmailDispatchCommandHandler(IEmailDispatchOutboxRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(
        ReplayEmailDispatchCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new ReplayEmailDispatchCommandValidator();
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(
                "Email dispatch replay request failed validation.",
                null,
                validationResult.Errors.Select(error => error.ErrorMessage));
        }

        var dispatch = await _repository.GetByTenantAndId(request.TenantId, request.OutboxId, cancellationToken);
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
                "Redacted email dispatch rows cannot be replayed.",
                EmailDispatchFailureCodes.InvalidTransition,
                ["Redacted email dispatch rows cannot be replayed."]);
        }

        if (dispatch.Status == EmailDispatchStatus.Sent)
        {
            return Failure(
                "Sent email dispatch rows cannot be replayed.",
                EmailDispatchFailureCodes.InvalidTransition,
                ["Sent email dispatch rows cannot be replayed."]);
        }

        if (dispatch.Status == EmailDispatchStatus.Skipped)
        {
            return Failure(
                "Skipped email dispatch rows cannot be replayed.",
                EmailDispatchFailureCodes.InvalidTransition,
                ["Skipped email dispatch rows cannot be replayed."]);
        }

        if (dispatch.Status == EmailDispatchStatus.Processing)
        {
            return Failure(
                "Processing email dispatch rows cannot be replayed until the worker releases them.",
                EmailDispatchFailureCodes.InvalidTransition,
                ["Processing email dispatch rows cannot be replayed until the worker releases them."]);
        }

        if (dispatch.Status == EmailDispatchStatus.Pending)
        {
            return Success(dispatch.Id, "Email dispatch is already pending replay.");
        }

        if (!ReplayableStatuses.Contains(dispatch.Status))
        {
            return Failure(
                $"Email dispatch rows in {dispatch.Status} state cannot be replayed.",
                EmailDispatchFailureCodes.InvalidTransition,
                [$"Email dispatch rows in {dispatch.Status} state cannot be replayed."]);
        }

        var replayed = await _unitOfWork.ExecuteInTransactionAsync(
            ct => _repository.TryReplayForOperator(
                request.TenantId,
                request.OutboxId,
                request.ChangedBy,
                DateTime.UtcNow,
                ct),
            cancellationToken);

        return replayed
            ? Success(dispatch.Id, "Email dispatch queued for replay.")
            : Failure(
                "Email dispatch state changed before it could be replayed.",
                EmailDispatchFailureCodes.ConcurrentTransition,
                ["Email dispatch state changed before it could be replayed."]);
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
