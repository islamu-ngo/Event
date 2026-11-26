using Explore.Application.Features.Learning.Requests.Queries;
using FluentValidation;

namespace Explore.Application.Features.Learning.Validators;

public sealed class GetLearningPracticeValidator : AbstractValidator<GetLearningPracticeQuery>
{
    public GetLearningPracticeValidator()
    {
        RuleFor(query => query.PracticeId).Must(LearningInputRules.IsIdentity);
    }
}
