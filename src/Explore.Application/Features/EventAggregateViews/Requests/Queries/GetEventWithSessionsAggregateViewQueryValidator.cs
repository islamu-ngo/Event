using FluentValidation;

namespace Explore.Application.Features.EventAggregateViews.Requests.Queries;

public sealed class GetEventWithSessionsAggregateViewQueryValidator : AbstractValidator<GetEventWithSessionsAggregateViewQuery>
{
    public GetEventWithSessionsAggregateViewQueryValidator()
    {
        RuleFor(x => x.EventId)
            .NotEmpty();
    }
}
