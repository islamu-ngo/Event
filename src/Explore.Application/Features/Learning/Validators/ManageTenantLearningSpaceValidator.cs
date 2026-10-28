using Explore.Application.Features.Learning.Requests.Commands;
using FluentValidation;

namespace Explore.Application.Features.Learning.Validators;

public sealed class ManageTenantLearningSpaceValidator : AbstractValidator<ManageTenantLearningSpaceCommand>
{
    public ManageTenantLearningSpaceValidator()
    {
        RuleFor(command => command.Input).NotNull();
        When(command => command.Input is not null, () =>
        {
            RuleFor(command => command.Input.ExpectedScopeStamp).Must(LearningInputRules.IsScopeStamp);
            RuleFor(command => command.Input.ExpectedRevision).GreaterThanOrEqualTo(0);
            RuleFor(command => command.Input.Action).IsInEnum();
        });
    }
}
