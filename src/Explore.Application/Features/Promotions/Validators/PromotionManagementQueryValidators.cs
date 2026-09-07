using Explore.Application.Features.Promotions.Requests.Queries;
using FluentValidation;

namespace Explore.Application.Features.Promotions.Validators;

public sealed class ListPromotionManagementQueryValidator : AbstractValidator<ListPromotionManagementQuery>
{
    public ListPromotionManagementQueryValidator()
    {
        RuleFor(query => query.EventId).NotEmpty();
        RuleFor(query => query.TicketCatalogVersionId).NotEmpty();
    }
}

public sealed class GetPromotionManagementQueryValidator : AbstractValidator<GetPromotionManagementQuery>
{
    public GetPromotionManagementQueryValidator()
    {
        RuleFor(query => query.EventId).NotEmpty();
        RuleFor(query => query.PromotionDefinitionId).NotEmpty();
    }
}
