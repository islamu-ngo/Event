using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.Features.Webhooks.Validators;

public sealed class ReconcileWebhookProviderPublicationCommandValidator
    : AbstractValidator<ReconcileWebhookProviderPublicationCommand>
{
    public ReconcileWebhookProviderPublicationCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.PublicationId).NotEmpty();
        RuleFor(command => command.ActorUserId).NotEmpty();
        RuleFor(command => command.ExpectedConcurrencyVersion).GreaterThan(0);
        RuleFor(command => command.ExternalProviderMessageId)
            .NotEmpty()
            .MaximumLength(WebhookProviderPublication.MaxExternalProviderMessageIdLength);
        RuleFor(command => command.ReasonCode)
            .NotEmpty()
            .MaximumLength(WebhookAuditEvent.MaxReasonCodeLength)
            .Matches("^[A-Za-z0-9_.:-]+$")
            .WithMessage("ReasonCode must use only letters, digits, underscore, dash, dot, or colon.");
    }
}
