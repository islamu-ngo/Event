using Explore.Application.Features.AiAssistant.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.AiAssistant.Validators;

public sealed class GrantAiConsentCommandValidator : AbstractValidator<GrantAiConsentCommand>
{
    public GrantAiConsentCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.SubjectUserId).NotEmpty();
        RuleFor(x => x.EntityName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.FieldName).NotEmpty().MaximumLength(120);
        RuleFor(x => x.GrantedByUserId).NotEmpty();
        RuleFor(x => x.Purpose).MaximumLength(512);
    }
}
