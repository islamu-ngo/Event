using Explore.Application.Features.Learning.Requests.Commands;
using Explore.Domain.Learning;
using FluentValidation;

namespace Explore.Application.Features.Learning.Validators;

public sealed class AcknowledgeLearningRequirementValidator
    : AbstractValidator<AcknowledgeLearningRequirementCommand>
{
    public AcknowledgeLearningRequirementValidator()
    {
        RuleFor(command => command.RequirementKey).Equal(LearningRequirementDefinition.Current.RequirementKey);
        RuleFor(command => command.Input).NotNull();
        When(command => command.Input is not null, () =>
        {
            RuleFor(command => command.Input.IntentId).Must(LearningInputRules.IsIdentity);
            RuleFor(command => command.Input.ExpectedScopeStamp).Must(LearningInputRules.IsScopeStamp);
            RuleFor(command => command.Input.ExpectedRevision).GreaterThanOrEqualTo(0);
            RuleFor(command => command.Input.ExpectedPolicySourceStamp).Must(LearningInputRules.IsScopeStamp);
            RuleFor(command => command.Input.MaterialVersion).Equal(LearningRequirementDefinition.Current.MaterialVersion);
        });
    }
}
