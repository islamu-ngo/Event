using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.CustomPropertyDefinition.Validators;
using Explore.Domain.Enums;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.CustomPropertyDefinitions.Validators;

public class CreateCustomPropertyDefinitionDtoValidatorTests
{
    private readonly CreateCustomPropertyDefinitionDtoValidator _validator = new();

    [Test]
    public async Task Validate_WithOptionTypeWithoutOptions_ReturnsError()
    {
        var dto = new CreateCustomPropertyDefinitionDto
        {
            EntityTypeName = EntityTypeName.Organization,
            Namespace = "tenant.community",
            Key = "format",
            DisplayName = "Format",
            PropertyType = PropertyType.Option,
            ExposureLevel = ExposureLevel.Internal,
        };

        var result = await _validator.ValidateAsync(dto);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.ErrorMessage.Contains("At least one option", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Validate_WithUnsupportedEntityType_ReturnsError()
    {
        var dto = new CreateCustomPropertyDefinitionDto
        {
            EntityTypeName = EntityTypeName.Event,
            Namespace = "tenant.community",
            Key = "notes",
            DisplayName = "Notes",
            PropertyType = PropertyType.Text,
            ExposureLevel = ExposureLevel.Internal,
        };

        var result = await _validator.ValidateAsync(dto);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(e => e.ErrorMessage.Contains("Organization and Group", StringComparison.Ordinal))).IsTrue();
    }
}
