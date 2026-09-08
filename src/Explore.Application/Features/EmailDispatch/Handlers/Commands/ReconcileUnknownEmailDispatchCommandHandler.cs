using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.EmailDispatch.Validators;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Handlers.Commands;

public sealed class ReconcileUnknownEmailDispatchCommandHandler(
    IEmailDispatchOutboxRepository repository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<ReconcileUnknownEmailDispatchCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ReconcileUnknownEmailDispatchCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await new ReconcileUnknownEmailDispatchCommandValidator()
            .ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Failure<Guid>(
                EmailDispatchFailureCodes.ValidationFailed,
                "Email dispatch reconciliation failed validation.",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        var dispatch = await repository.GetByTenantAndId(request.TenantId, request.OutboxId, cancellationToken);
        if (dispatch is null)
        {
            return BaseCommandResponse.Failure<Guid>(
                EmailDispatchFailureCodes.NotFound,
                "Email dispatch outbox row was not found.",
                ["Email dispatch outbox row was not found."]);
        }

        if (dispatch.ContentRedactedAt is not null || dispatch.Status != EmailDispatchStatus.Unknown)
        {
            return BaseCommandResponse.Failure<Guid>(
                EmailDispatchFailureCodes.InvalidTransition,
                "Only non-redacted Unknown email dispatch rows can be reconciled.",
                ["Only non-redacted Unknown email dispatch rows can be reconciled."]);
        }

        var changed = await unitOfWork.ExecuteInTransactionAsync(
            ct => repository.TryReconcileUnknown(
                request.TenantId,
                request.OutboxId,
                request.Outcome,
                request.Reason.Trim(),
                string.IsNullOrWhiteSpace(request.ProviderMessageId) ? null : request.ProviderMessageId.Trim(),
                request.ChangedBy,
                DateTime.UtcNow,
                ct),
            cancellationToken);

        return changed
            ? BaseCommandResponse.Success(
                dispatch.Id,
                request.Outcome == EmailDispatchUnknownReconciliationOutcome.Delivered
                    ? "Unknown email dispatch reconciled as delivered."
                    : "Unknown email dispatch reconciled as not delivered and queued.")
            : BaseCommandResponse.Failure<Guid>(
                EmailDispatchFailureCodes.ConcurrentTransition,
                "Email dispatch state changed before it could be reconciled.",
                ["Email dispatch state changed before it could be reconciled."]);
    }
}
