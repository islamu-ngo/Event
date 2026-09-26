using Explore.Application.DTOs.Tenant;
using Explore.Application.DTOs.Tenant.Validators;
using FluentValidation.Results;

namespace Event.Application.UnitTests.DTOs.Tenant.Validators;

public class CreateTenantDtoValidatorTests
{
    private readonly CreateTenantDtoValidator _validator = new();

    [Test]
    [Arguments("admin")]
    [Arguments("_framework")]
    [Arguments("api")]
    [Arguments("actors")]
    [Arguments("event-created")]
    [Arguments("group")]
    [Arguments("home")]
    [Arguments("organization")]
    [Arguments("organizations")]
    [Arguments("settings")]
    [Arguments("users")]
    public async Task Validate_WithReservedSlug_ReturnsReservedError(string slug)
    {
        ValidationResult result = await _validator.ValidateAsync(CreateDto(slug));

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(CreateTenantDto.Slug) &&
            error.ErrorMessage.Contains("reserved", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task Validate_WithTwoCharacterSlug_ReturnsMinimumLengthError()
    {
        ValidationResult result = await _validator.ValidateAsync(CreateDto("al"));

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(CreateTenantDto.Slug) &&
            error.ErrorMessage.Contains("3 characters", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    [Arguments("-invalid")]
    [Arguments("invalid-")]
    [Arguments("my--bad")]
    public async Task Validate_WithMalformedSlug_ReturnsFormatError(string slug)
    {
        ValidationResult result = await _validator.ValidateAsync(CreateDto(slug));

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(CreateTenantDto.Slug) &&
            error.ErrorMessage.Contains("lowercase letters", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Validate_WithValidSlug_ReturnsValid()
    {
        ValidationResult result = await _validator.ValidateAsync(CreateDto("al-nour"));

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_WithMissingSlug_ReturnsRequiredError()
    {
        ValidationResult result = await _validator.ValidateAsync(CreateDto(null!));

        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(CreateTenantDto.Slug) &&
            error.ErrorMessage.Contains("required", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    private static CreateTenantDto CreateDto(string slug) => new()
    {
        FullName = "Al Nour Community",
        Slug = slug
    };
}

public class UpdateTenantSlugDtoValidatorTests
{
    private readonly UpdateTenantSlugDtoValidator _validator = new();

    [Test]
    [Arguments("admin")]
    [Arguments("_framework")]
    [Arguments("api")]
    [Arguments("actors")]
    [Arguments("event-created")]
    [Arguments("group")]
    [Arguments("home")]
    [Arguments("organization")]
    [Arguments("organizations")]
    [Arguments("settings")]
    [Arguments("users")]
    public async Task Validate_WithReservedSlug_ReturnsReservedError(string slug)
    {
        ValidationResult result = await _validator.ValidateAsync(CreateDto(slug));

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(UpdateTenantSlugDto.Value) &&
            error.ErrorMessage.Contains("reserved", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    [Arguments("al")]
    [Arguments("-invalid")]
    [Arguments("invalid-")]
    [Arguments("my--bad")]
    public async Task Validate_WithShortOrMalformedSlug_ReturnsInvalid(string slug)
    {
        ValidationResult result = await _validator.ValidateAsync(CreateDto(slug));

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Validate_WithValidSlug_ReturnsValid()
    {
        ValidationResult result = await _validator.ValidateAsync(CreateDto("al-nour"));

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task Validate_WithMissingSlug_ReturnsRequiredError()
    {
        ValidationResult result = await _validator.ValidateAsync(CreateDto(null!));

        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(UpdateTenantSlugDto.Value) &&
            error.ErrorMessage.Contains("required", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    private static UpdateTenantSlugDto CreateDto(string slug) => new() { Value = slug };
}
