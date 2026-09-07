using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.EmailDispatch.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Handlers.Commands;

public sealed class ParkEmailDispatchCommandHandler : IRequestHandler<ParkEmailDispatchCommand, BaseCommandResponse<Guid>>
{
    private readonly IEmailDispatchOutboxRepository _repository;

    public ParkEmailDispatchCommandHandler(IEmailDispatchOutboxRepository repository)
    {
        _repository = repository;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(
        ParkEmailDispatchCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new ParkEmailDispatchCommandValidator();
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return Failure(
                "Email dispatch park request failed validation.",
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
                "Redacted email dispatch rows cannot be parked.",
                EmailDispatchFailureCodes.InvalidTransition,
                ["Redacted email dispatch rows cannot be parked."]);
        }

        if (dispatch.Status == EmailDispatchStatus.Sent)
        {
            return Failure(
                "Sent email dispatch rows cannot be parked.",
                EmailDispatchFailureCodes.InvalidTransition,
                ["Sent email dispatch rows cannot be parked."]);
        }

        if (dispatch.Status == EmailDispatchStatus.Skipped)
        {
            return Failure(
                "Skipped email dispatch rows cannot be parked.",
                EmailDispatchFailureCodes.InvalidTransition,
                ["Skipped email dispatch rows cannot be parked."]);
        }

        if (dispatch.Status == EmailDispatchStatus.Unknown)
        {
            return Failure(
                "Unknown email dispatch rows must be reconciled or resolved without replay.",
                EmailDispatchFailureCodes.InvalidTransition,
                ["Unknown email dispatch rows must be reconciled or resolved without replay."]);
        }

        if (dispatch.Status == EmailDispatchStatus.Parked)
        {
            return Success(dispatch.Id, "Email dispatch is already parked.");
        }

        var parked = await _repository.TryParkForOperator(
            request.TenantId,
            request.OutboxId,
            request.Reason,
            request.ChangedBy,
            DateTime.UtcNow,
            cancellationToken);

        return parked
            ? Success(dispatch.Id, "Email dispatch parked for operator review.")
            : Failure(
                "Email dispatch state changed before it could be parked.",
                EmailDispatchFailureCodes.ConcurrentTransition,
                ["Email dispatch state changed before it could be parked."]);
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
