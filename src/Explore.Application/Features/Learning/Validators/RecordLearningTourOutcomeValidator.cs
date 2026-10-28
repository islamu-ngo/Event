using Explore.Application.Features.Learning.Requests.Commands;
using FluentValidation;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning.Validators;

public sealed class RecordLearningTourOutcomeValidator : AbstractValidator<RecordLearningTourOutcomeCommand>
{
    public RecordLearningTourOutcomeValidator()
    {
        RuleFor(command => command.Input).NotNull();
        When(command => command.Input is not null, () =>
        {
            RuleFor(command => command.Input.ExpectedScopeStamp).Must(LearningInputRules.IsScopeStamp);
            RuleFor(command => command.Input.ExpectedRevision).GreaterThanOrEqualTo(0);
            RuleFor(command => command.Input.IntentId).Must(LearningInputRules.IsIdentity);
            RuleFor(command => command.Input.ClaimToken).Must(LearningInputRules.IsOptionalIdentity);
            RuleFor(command => command.Input.Family).IsInEnum();
            RuleFor(command => command.Input.MaterialVersion).Equal(LearningLessonCatalog.MaterialVersion);
            RuleFor(command => command.Input.Outcome).IsInEnum();
            RuleFor(command => command.Input.StepIndex).InclusiveBetween(0, 32);
            RuleFor(command => command.Input).Must(input =>
                input.ClaimToken.HasValue || input.Outcome == LearningTourOutcome.Skipped && input.StepIndex == 0)
                .WithMessage("An unclaimed invitation can only be skipped at step zero.");
        });
    }
}
