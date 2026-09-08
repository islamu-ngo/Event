using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.EmailDispatch.Validators;

public sealed class ReconcileUnknownEmailDispatchCommandValidator
    : AbstractValidator<ReconcileUnknownEmailDispatchCommand>
{
    public ReconcileUnknownEmailDispatchCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty().WithMessage("TenantId is required.");
        RuleFor(command => command.OutboxId).NotEmpty().WithMessage("OutboxId is required.");
        RuleFor(command => command.Outcome)
            .IsInEnum()
            .Must(outcome => outcome is EmailDispatchUnknownReconciliationOutcome.Delivered
                or EmailDispatchUnknownReconciliationOutcome.NotDelivered)
            .WithMessage("Outcome must be Delivered or NotDelivered.");
        RuleFor(command => command.Reason)
            .NotEmpty().WithMessage("Reconciliation reason is required.")
            .MaximumLength(500).WithMessage("Reconciliation reason must be 500 characters or fewer.");
        RuleFor(command => command.ProviderMessageId)
            .MaximumLength(500)
            .WithMessage("Provider message id must be 500 characters or fewer.");
    }
}
