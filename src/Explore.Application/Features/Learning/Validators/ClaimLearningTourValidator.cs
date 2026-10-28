using Explore.Application.Features.Learning.Requests.Commands;
using FluentValidation;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning.Validators;

public sealed class ClaimLearningTourValidator : AbstractValidator<ClaimLearningTourCommand>
{
    public ClaimLearningTourValidator()
    {
        RuleFor(command => command.Input).NotNull();
        When(command => command.Input is not null, () =>
        {
            RuleFor(command => command.Input.ExpectedScopeStamp).Must(LearningInputRules.IsScopeStamp);
            RuleFor(command => command.Input.ExpectedRevision).GreaterThanOrEqualTo(0);
            RuleFor(command => command.Input.IntentId).Must(LearningInputRules.IsIdentity);
            RuleFor(command => command.Input.Family).IsInEnum();
            RuleFor(command => command.Input.MaterialVersion).Equal(LearningLessonCatalog.MaterialVersion);
            RuleFor(command => command.Input.ContextKey).Must(LearningInputRules.IsContext);
            RuleFor(command => command.Input.EventContextId).Must(LearningInputRules.IsOptionalIdentity);
            RuleFor(command => command.Input).Must(input =>
                input.Family == LearningTourFamily.EventManagement
                    ? input.EventContextId.HasValue
                    : input.EventContextId is null)
                .WithMessage("Only the event-management tour requires an event context.");
        });
    }
}
