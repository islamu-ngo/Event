using Explore.Application.Features.Learning.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Learning.Validators;

public sealed class UpdateLearningPreferencesValidator : AbstractValidator<UpdateLearningPreferencesCommand>
{
    public UpdateLearningPreferencesValidator()
    {
        RuleFor(command => command.Input).NotNull();
        When(command => command.Input is not null, () =>
        {
            RuleFor(command => command.Input.ExpectedScopeStamp).Must(LearningInputRules.IsScopeStamp);
            RuleFor(command => command.Input.ExpectedRevision).GreaterThanOrEqualTo(0);
        });
    }
}
