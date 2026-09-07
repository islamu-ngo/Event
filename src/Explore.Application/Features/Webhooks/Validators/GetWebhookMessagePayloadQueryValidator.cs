using Explore.Application.Features.Webhooks.Requests.Queries;
using FluentValidation;

namespace Explore.Application.Features.Webhooks.Validators;

public sealed class GetWebhookMessagePayloadQueryValidator : AbstractValidator<GetWebhookMessagePayloadQuery>
{
    public GetWebhookMessagePayloadQueryValidator()
    {
        RuleFor(query => query.MessageId).NotEmpty();
    }
}
