using Explore.Application.Features.RegistrationForms.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.RegistrationForms.Validators;

public sealed class AttachRegistrationRequirementCommandValidator
    : AbstractValidator<AttachRegistrationRequirementCommand>
{
    public AttachRegistrationRequirementCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.WorkflowId).NotEmpty();
        RuleFor(command => command.RequirementId).NotEmpty();
        RuleFor(command => command.ExpectedConcurrencyStamp).NotEmpty();
        RuleFor(command => command).Must(command =>
                command.StandaloneQuestionnaire ==
                (command.RegistrationFormId.HasValue && command.RegistrationFormVersionId.HasValue))
            .WithMessage("Standalone questionnaire form and version ids must be supplied together.");
    }
}

public sealed class DetachRegistrationRequirementCommandValidator
    : AbstractValidator<DetachRegistrationRequirementCommand>
{
    public DetachRegistrationRequirementCommandValidator()
    {
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.RequirementId).NotEmpty();
        RuleFor(command => command.ExpectedConcurrencyStamp).NotEmpty();
    }
}
