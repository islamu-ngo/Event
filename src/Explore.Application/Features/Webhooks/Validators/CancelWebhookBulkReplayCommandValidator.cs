using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.Webhooks.Validators;

public sealed class CancelWebhookBulkReplayCommandValidator : AbstractValidator<CancelWebhookBulkReplayCommand>
{
    public CancelWebhookBulkReplayCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.ActorUserId).NotEmpty();
        RuleFor(command => command.OperationId).NotEmpty();
        RuleFor(command => command.ExpectedConcurrencyVersion).GreaterThanOrEqualTo(1);
        RuleFor(command => command.ReasonCode)
            .NotEmpty()
            .MaximumLength(WebhookBulkReplayOperation.MaxReasonCodeLength)
            .Matches("^[A-Za-z0-9_.:-]+$")
            .WithMessage("ReasonCode must use only letters, digits, underscore, dash, dot, or colon.");
    }
}
