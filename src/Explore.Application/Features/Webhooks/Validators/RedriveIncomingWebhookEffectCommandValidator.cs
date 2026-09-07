using Explore.Application.Features.Webhooks.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Webhooks.Validators;

public sealed class RedriveIncomingWebhookEffectCommandValidator
    : AbstractValidator<RedriveIncomingWebhookEffectCommand>
{
    public RedriveIncomingWebhookEffectCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.EffectOutboxId).NotEmpty();
        RuleFor(command => command.ExpectedProcessingGeneration).GreaterThan(0);
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(500);
    }
}
