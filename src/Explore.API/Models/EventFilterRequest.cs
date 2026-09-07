using System.ComponentModel.DataAnnotations;
using Explore.Application.DTOs.CustomPropertyProjection;

namespace Explore.API.Models;

public sealed class EventFilterRequest : IValidatableObject
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public string? SearchTerm { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? CategoryId { get; set; }
    public List<Guid>? IncludedCategoryIds { get; set; }
    public List<Guid>? ExcludedCategoryIds { get; set; }
    public string? CategoryInclusionMode { get; set; }
    public string? CategoryExclusionMode { get; set; }
    public List<Guid>? IncludedTagIds { get; set; }
    public List<Guid>? ExcludedTagIds { get; set; }
    public string? InclusionMode { get; set; }
    public string? ExclusionMode { get; set; }
    public List<int>? FormatIds { get; set; }
    public List<int>? MadhabIds { get; set; }
    public List<int>? RegistrationModeIds { get; set; }
    public List<int>? LanguageIds { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public List<int>? EventTypeIds { get; set; }
    public List<int>? AudienceGenderIds { get; set; }
    public List<int>? AudienceAgeIds { get; set; }
    public List<int>? EventStatusIds { get; set; }

    public List<int>? GenderModeIds { get; set; }
    public bool? IncludesQuranRecitation { get; set; }
    public List<int>? ReferencePrayerIds { get; set; }
    public List<int>? IslamicPrimaryLanguageIds { get; set; }
    public bool? HasIslamicAspect { get; set; }

    public int? SkillLevelId { get; set; }
    public bool? IsCodingCompetition { get; set; }
    public bool? IsHackathon { get; set; }
    public bool? RequiresLaptop { get; set; }
    public string? TechStackTag { get; set; }
    public bool? HasTechAspect { get; set; }

    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = true;
    public string? View { get; set; }

    /// <summary>
    /// Indexed query-string binding, e.g.
    /// <c>?CustomPropertyFilters[0].Namespace=tenant&amp;CustomPropertyFilters[0].Key=region&amp;CustomPropertyFilters[0].Operator=Equals&amp;CustomPropertyFilters[0].Value=west</c>.
    /// Silently ignored when tenant flag <c>custom_properties.projection_discovery_enabled</c> is off.
    /// </summary>
    public List<CustomPropertyFilterCriterion>? CustomPropertyFilters { get; set; }

    public string? CustomPropertySearchTerm { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in QueryValidationRules.ValidatePagination(PageNumber, PageSize))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateBoundedText(
                     SearchTerm,
                     nameof(SearchTerm),
                     QueryValidationRules.MaxSearchTermLength))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateBoundedText(
                     TechStackTag,
                     nameof(TechStackTag),
                     QueryValidationRules.MaxShortTextLength))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateBoundedText(
                     CustomPropertySearchTerm,
                     nameof(CustomPropertySearchTerm),
                     QueryValidationRules.MaxSearchTermLength))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateSortBy(SortBy))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateTemporalView(View, nameof(View)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateFilterMode(CategoryInclusionMode, nameof(CategoryInclusionMode)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateFilterMode(CategoryExclusionMode, nameof(CategoryExclusionMode)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateFilterMode(InclusionMode, nameof(InclusionMode)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateFilterMode(ExclusionMode, nameof(ExclusionMode)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateDateRange(DateFrom, DateTo))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateOptionalGuid(ActorId, nameof(ActorId)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateOptionalGuid(OrganizationId, nameof(OrganizationId)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateOptionalGuid(GroupId, nameof(GroupId)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateOptionalGuid(CategoryId, nameof(CategoryId)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateGuidList(IncludedCategoryIds, nameof(IncludedCategoryIds)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateGuidList(ExcludedCategoryIds, nameof(ExcludedCategoryIds)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateGuidList(IncludedTagIds, nameof(IncludedTagIds)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidateGuidList(ExcludedTagIds, nameof(ExcludedTagIds)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidatePositiveIntList(FormatIds, nameof(FormatIds)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidatePositiveIntList(MadhabIds, nameof(MadhabIds)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidatePositiveIntList(RegistrationModeIds, nameof(RegistrationModeIds)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidatePositiveIntList(LanguageIds, nameof(LanguageIds)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidatePositiveIntList(EventTypeIds, nameof(EventTypeIds)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidatePositiveIntList(AudienceGenderIds, nameof(AudienceGenderIds)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidatePositiveIntList(AudienceAgeIds, nameof(AudienceAgeIds)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidatePositiveIntList(EventStatusIds, nameof(EventStatusIds)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidatePositiveIntList(GenderModeIds, nameof(GenderModeIds)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidatePositiveIntList(ReferencePrayerIds, nameof(ReferencePrayerIds)))
            yield return result;

        foreach (var result in QueryValidationRules.ValidatePositiveIntList(IslamicPrimaryLanguageIds, nameof(IslamicPrimaryLanguageIds)))
            yield return result;

        if (SkillLevelId is <= 0)
        {
            yield return new ValidationResult(
                "SkillLevelId must be greater than 0.",
                [nameof(SkillLevelId)]);
        }

        foreach (var result in QueryValidationRules.ValidateCustomPropertyFilters(
                     CustomPropertyFilters,
                     nameof(CustomPropertyFilters)))
            yield return result;
    }
}
