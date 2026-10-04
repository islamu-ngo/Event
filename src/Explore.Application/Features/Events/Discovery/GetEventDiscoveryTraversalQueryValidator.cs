using FluentValidation;

namespace Explore.Application.Features.Events.Discovery;

public sealed class GetEventDiscoveryTraversalQueryValidator : AbstractValidator<GetEventDiscoveryTraversalQuery>
{
    public GetEventDiscoveryTraversalQueryValidator()
    {
        RuleFor(request => request.Criteria.PageNumber).Equal(1);
        RuleFor(request => request.Criteria.PageSize).InclusiveBetween(1, 100);
        RuleFor(request => request.Cursor).MaximumLength(4096);
        RuleFor(request => request.Criteria.SearchTerm).MaximumLength(200);
        RuleFor(request => request.Criteria.CustomPropertySearchTerm).MaximumLength(200);
        RuleFor(request => request.Criteria.TechStackTag).MaximumLength(100);
        RuleFor(request => request.Criteria.SortBy)
            .Must(value => value is null || new[] { "date", "title", "views", "createdAt" }
                .Contains(value, StringComparer.OrdinalIgnoreCase));
        RuleFor(request => request.Criteria.LocationIds).Must(values => values is null || values.Count == 0);
        RuleFor(request => request.Criteria.DateTo)
            .Must((request, to) => to is null || request.Criteria.DateFrom is null || to >= request.Criteria.DateFrom);
        RuleFor(request => request.Criteria).Custom((criteria, context) =>
        {
            int?[] counts =
            [
                criteria.IncludedCategoryIds?.Count, criteria.ExcludedCategoryIds?.Count,
                criteria.IncludedTagIds?.Count, criteria.ExcludedTagIds?.Count,
                criteria.FormatIds?.Count, criteria.MadhabIds?.Count, criteria.RegistrationModeIds?.Count,
                criteria.LanguageIds?.Count, criteria.EventTypeIds?.Count, criteria.AudienceGenderIds?.Count,
                criteria.AudienceAgeIds?.Count, criteria.EventStatusIds?.Count, criteria.GenderModeIds?.Count,
                criteria.ReferencePrayerIds?.Count, criteria.IslamicPrimaryLanguageIds?.Count
            ];
            if (counts.Any(count => count > 50))
                context.AddFailure("Criteria", "Discovery filter collections cannot exceed 50 values.");
            if (criteria.CustomPropertyFilters?.Count > 10)
                context.AddFailure("Criteria", "Discovery cannot exceed 10 custom property filters.");
            if (criteria.CustomPropertyFilters?.Any(filter =>
                    filter is null || string.IsNullOrWhiteSpace(filter.Namespace) || string.IsNullOrWhiteSpace(filter.Key)
                    || filter.Namespace.Length > 100 || filter.Key.Length > 100
                    || filter.Value?.Length > 500 || filter.OptionIds?.Count > 50) == true)
                context.AddFailure("Criteria", "Custom property discovery criteria exceed their finite bounds.");
        });
    }
}
