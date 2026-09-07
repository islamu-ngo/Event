using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.Webhooks.Validators;

public sealed class ScheduleWebhookBulkReplayCommandValidator : AbstractValidator<ScheduleWebhookBulkReplayCommand>
{
    public ScheduleWebhookBulkReplayCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.ActorUserId).NotEmpty();
        RuleFor(command => command.OperationKey).NotEmpty();
        RuleFor(command => command.FromUtc).Must(BeUtc).WithMessage("FromUtc must use UTC kind.");
        RuleFor(command => command.ToUtc).Must(BeUtc).WithMessage("ToUtc must use UTC kind.");
        RuleFor(command => command.ToUtc).GreaterThan(command => command.FromUtc);
        RuleFor(command => command.WebhookConsumerId)
            .Must(value => value is null || value != Guid.Empty)
            .WithMessage("WebhookConsumerId cannot be empty when supplied.");
        RuleFor(command => command.WebhookEndpointId)
            .Must(value => value is null || value != Guid.Empty)
            .WithMessage("WebhookEndpointId cannot be empty when supplied.");
        RuleFor(command => command.EventType)
            .MaximumLength(WebhookMessage.MaxEventTypeLength)
            .When(command => !string.IsNullOrWhiteSpace(command.EventType));
        RuleFor(command => command.MaxItems).InclusiveBetween(1, WebhookBulkReplayOperation.HardMaximumItems);
        RuleFor(command => command.ReasonCode)
            .NotEmpty()
            .MaximumLength(WebhookBulkReplayOperation.MaxReasonCodeLength)
            .Matches("^[A-Za-z0-9_.:-]+$")
            .WithMessage("ReasonCode must use only letters, digits, underscore, dash, dot, or colon.");
    }

    private static bool BeUtc(DateTime value) => value.Kind == DateTimeKind.Utc;
}
