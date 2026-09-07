using Explore.Application.DTOs.CustomPropertyDefinition;
using FluentValidation;

namespace Explore.Application.DTOs.CustomPropertyDefinition.Validators;

public sealed class UpdateCustomPropertyDefinitionDtoValidator : AbstractValidator<UpdateCustomPropertyDefinitionDto>
{
    public UpdateCustomPropertyDefinitionDtoValidator()
    {
        RuleFor(x => x).Must(x => x.Relations is not null || x.Metadata is not null || x.Validation is not null || x.Options is not null)
            .WithMessage("At least one update group is required.");
        RuleFor(x => x.Relations).Must(x => x?.EntityTypeName is not null).When(x => x.Relations is not null)
            .WithMessage("Relations must contain at least one update.");
        RuleFor(x => x.Metadata).Must(HasMetadataUpdate).When(x => x.Metadata is not null)
            .WithMessage("Metadata must contain at least one update.");
        RuleFor(x => x.Validation).Must(HasValidationUpdate).When(x => x.Validation is not null)
            .WithMessage("Validation must contain at least one update.");
        RuleFor(x => x.Options!.Items).NotNull().When(x => x.Options is not null).WithMessage("Options.items is required when options is supplied.");
    }

    internal static bool HasMetadataUpdate(UpdateCustomPropertyDefinitionMetadataDto? value) =>
        value is not null &&
        (value.Namespace is not null || value.Key is not null || value.DisplayName is not null ||
         value.Description.HasValue || value.IsActive.HasValue || value.SortOrder.HasValue ||
         value.ExposureLevel.HasValue || value.IsSearchable.HasValue || value.IsFilterable.HasValue ||
         value.IsExportable.HasValue || value.IsModerationRelevant.HasValue ||
         value.IsAnalyticsRelevant.HasValue || value.IsSystemOwned.HasValue);

    internal static bool HasValidationUpdate(UpdateCustomPropertyDefinitionValidationDto? value) =>
        value is not null &&
        (value.PropertyType.HasValue || value.IsRequired.HasValue || value.IsMulti.HasValue ||
         value.DefaultTextValue.HasValue || value.DefaultNumberValue.HasValue ||
         value.DefaultBooleanValue.HasValue || value.DefaultDateTimeValue.HasValue ||
         value.MinLength.HasValue || value.MaxLength.HasValue || value.RegexPattern.HasValue ||
         value.MinNumber.HasValue || value.MaxNumber.HasValue || value.MinDateTime.HasValue ||
         value.MaxDateTime.HasValue || value.AllowedUrlSchemes.HasValue);
}
