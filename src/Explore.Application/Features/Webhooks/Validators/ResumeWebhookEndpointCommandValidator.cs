using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.Webhooks.Validators;

public sealed class ResumeWebhookEndpointCommandValidator : AbstractValidator<ResumeWebhookEndpointCommand>
{
    public ResumeWebhookEndpointCommandValidator()
    {
        RuleFor(command => command.EndpointId).NotEmpty();
        RuleFor(command => command.ActorUserId).NotEmpty();
        RuleFor(command => command.ExpectedDeliveryStateVersion).GreaterThanOrEqualTo(0);
        RuleFor(command => command.ReasonCode)
            .NotEmpty()
            .MaximumLength(WebhookAuditEvent.MaxReasonCodeLength)
            .Matches("^[A-Za-z0-9_.:-]+$")
            .WithMessage("ReasonCode must use only letters, digits, underscore, dash, dot, or colon.");
    }
}
