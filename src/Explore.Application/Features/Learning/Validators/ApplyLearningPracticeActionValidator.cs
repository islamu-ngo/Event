using Explore.Application.Features.Learning.Requests.Commands;
using FluentValidation;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning.Validators;

public sealed class ApplyLearningPracticeActionValidator : AbstractValidator<ApplyLearningPracticeActionCommand>
{
    public ApplyLearningPracticeActionValidator()
    {
        RuleFor(command => command.Input).NotNull();
        When(command => command.Input is not null, () =>
        {
            RuleFor(command => command.Input.ExpectedScopeStamp).Must(LearningInputRules.IsScopeStamp);
            RuleFor(command => command.Input.ExpectedRevision).GreaterThanOrEqualTo(0);
            RuleFor(command => command.Input.ExpectedPracticeRevision).GreaterThanOrEqualTo(0);
            RuleFor(command => command.Input.IntentId).Must(LearningInputRules.IsIdentity);
            RuleFor(command => command.Input.PracticeId).Must(LearningInputRules.IsOptionalIdentity);
            RuleFor(command => command.Input.ContextKey).Must(LearningInputRules.IsContext);
            RuleFor(command => command.Input.Action).IsInEnum();
            RuleFor(command => command.Input.MaterialVersion).Equal(LearningLessonCatalog.MaterialVersion);
            RuleFor(command => command.Input.RecipeKey).Must(key =>
                LearningLessonCatalog.TryGetLesson(key, LearningLessonCatalog.MaterialVersion, out _))
                .WithMessage("Choose a recipe from the current learning catalogue.");
            RuleFor(command => command.Input).Must(input =>
                input.Action == LearningPracticeAction.Start || input.PracticeId.HasValue)
                .WithMessage("An existing practice identity is required for this action.");
            RuleFor(command => command.Input).Must(input => input.Action == LearningPracticeAction.Choose
                ? LearningLessonCatalog.TryGetLesson(input.RecipeKey, input.MaterialVersion, out var lesson)
                    && lesson.Choices.Any(choice => string.Equals(choice.Key, input.ChoiceKey, StringComparison.Ordinal))
                : input.ChoiceKey is null)
                .WithMessage("Only Choose accepts an exact authored choice key for this recipe.");
        });
    }
}
