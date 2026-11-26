using Explore.Application.Features.Learning.Requests.Queries;
using FluentValidation;

namespace Explore.Application.Features.Learning.Validators;

public sealed class GetLearningContextValidator : AbstractValidator<GetLearningContextQuery>
{
    public GetLearningContextValidator()
    {
        RuleFor(query => query.ContextKey).Must(LearningInputRules.IsContext);
        RuleFor(query => query.EventContextId).Must(LearningInputRules.IsOptionalIdentity);
    }
}
