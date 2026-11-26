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
            RuleFor(command => command.Input.MaterialVersion).Equal(LearningExperienceCatalog.MaterialVersion);
            RuleFor(command => command.Input.ExpectedPlanFingerprint).Must(LearningInputRules.IsScopeStamp);
            RuleFor(command => command.Input.ExpectedPolicySourceStamp).Must(LearningInputRules.IsScopeStamp);
            RuleFor(command => command.Input.ContextKey).Must(LearningInputRules.IsContext);
            RuleFor(command => command.Input.EventContextId).Must(LearningInputRules.IsOptionalIdentity);
            RuleFor(command => command.Input).Must(input =>
                (input.Family == LearningTourFamily.EventManagement) == input.EventContextId.HasValue)
                .WithMessage("Only the event-management tour requires an event context.");
            RuleFor(command => command.Input.Action).IsInEnum();
            RuleFor(command => command.Input).Must(input => input.Action == LearningTourAction.Progress
                    ? LearningInputRules.IsStepKey(input.StepKey) && input.StepVersion is > 0
                    : input.StepKey is null && input.StepVersion is null)
                .WithMessage("Only Progress accepts a semantic step key and positive version.");
            RuleFor(command => command.Input).Must(input => input.Action == LearningTourAction.Snooze
                    ? input.SnoozeDuration.HasValue && Enum.IsDefined(input.SnoozeDuration.Value)
                    : input.SnoozeDuration is null)
                .WithMessage("Only Snooze accepts one registered duration.");
            RuleFor(command => command.Input).Must(input =>
                input.ClaimToken.HasValue || input.Action is LearningTourAction.Skip or LearningTourAction.Snooze)
                .WithMessage("An unclaimed invitation can only be skipped or snoozed.");
        });
    }
}
