using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Geocoding.Requests.Queries;
using FluentValidation;

namespace Explore.Application.Features.Geocoding.Validators;

public sealed class GetAddressSuggestionsQueryValidator
    : AbstractValidator<GetAddressSuggestionsQuery>
{
    public GetAddressSuggestionsQueryValidator()
    {
        RuleFor(query => query.TenantId).NotEmpty();
        RuleFor(query => query.Request).NotNull();
        When(query => query.Request is not null, () =>
        {
            RuleFor(query => query.Request.SearchText)
                .NotEmpty()
                .MinimumLength(LocalAddressSuggestionBounds.MinimumSearchLength)
                .MaximumLength(LocalAddressSuggestionBounds.MaximumSearchLength);
            RuleFor(query => query.Request.Limit)
                .InclusiveBetween(
                    LocalAddressSuggestionBounds.MinimumLimit,
                    LocalAddressSuggestionBounds.MaximumLimit);
            RuleFor(query => query.Request.OrganizationId)
                .Must(organizationId => organizationId is null || organizationId != Guid.Empty)
                .WithMessage("OrganizationId must be omitted or contain a non-empty identifier.");
            RuleFor(query => query.Request)
                .Must(request => HasValidTargetBinding(
                    request.LocationId,
                    request.ExpectedConcurrencyStamp))
                .WithMessage(
                    "LocationId and ExpectedConcurrencyStamp must either both be omitted or both be non-empty.");
        });
    }

    private static bool HasValidTargetBinding(Guid? locationId, Guid? concurrencyStamp) =>
        (locationId, concurrencyStamp) switch
        {
            (null, null) => true,
            ({ } location, { } stamp) => location != Guid.Empty && stamp != Guid.Empty,
            _ => false
        };
}
