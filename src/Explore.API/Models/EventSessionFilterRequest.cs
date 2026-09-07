using System.ComponentModel.DataAnnotations;
using Explore.Application.DTOs.CustomPropertyProjection;

namespace Explore.API.Models;

public sealed class EventSessionFilterRequest : IValidatableObject
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Indexed query-string binding, e.g.
    /// <c>?CustomPropertyFilters[0].Namespace=tenant&amp;CustomPropertyFilters[0].Key=track&amp;CustomPropertyFilters[0].Operator=Equals&amp;CustomPropertyFilters[0].Value=backend</c>.
    /// Silently ignored when tenant flag <c>custom_properties.projection_discovery_enabled</c> is off.
    /// </summary>
    public List<CustomPropertyFilterCriterion>? CustomPropertyFilters { get; set; }

    public string? CustomPropertySearchTerm { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in QueryValidationRules.ValidatePagination(PageNumber, PageSize))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateBoundedText(
                     CustomPropertySearchTerm,
                     nameof(CustomPropertySearchTerm),
                     QueryValidationRules.MaxSearchTermLength))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateCustomPropertyFilters(
                     CustomPropertyFilters,
                     nameof(CustomPropertyFilters)))
            yield return result;
    }
}
