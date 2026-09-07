using Explore.Application.DTOs.Organization;
using Explore.Application.DTOs.Organization.Validators;

namespace Event.Application.UnitTests.Features.Organizations.Validators;

public sealed class CreateOrganizationDtoValidatorTests
{
    private readonly CreateOrganizationDtoValidator _validator = new();

    [Test]
    public async Task CompleteOrganizationIsValid()
    {
        var result = await _validator.ValidateAsync(CreateDto());

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task EveryRequiredTextPartitionRejectsBlankInput()
    {
        (string Property, CreateOrganizationDto Dto)[] cases =
        [
            (nameof(CreateOrganizationDto.FullName), CreateDto(fullName: "")),
            (nameof(CreateOrganizationDto.Email), CreateDto(email: "")),
            (nameof(CreateOrganizationDto.Country), CreateDto(country: "")),
            (nameof(CreateOrganizationDto.City), CreateDto(city: "")),
            (nameof(CreateOrganizationDto.Address), CreateDto(address: ""))
        ];

        foreach ((string property, CreateOrganizationDto dto) in cases)
        {
            var result = await _validator.ValidateAsync(dto);

            await Assert.That(result.IsValid).IsFalse();
            await Assert.That(result.Errors.Any(error =>
                error.PropertyName == property)).IsTrue();
        }
    }

    [Test]
    public async Task TextLengthBoundariesRejectOnlyOverflow()
    {
        var fullNameOverflow = await _validator.ValidateAsync(
            CreateDto(fullName: new string('a', 101)));
        var addressOverflow = await _validator.ValidateAsync(
            CreateDto(address: new string('a', 201)));

        await Assert.That(fullNameOverflow.Errors.Any(error =>
            error.PropertyName == nameof(CreateOrganizationDto.FullName))).IsTrue();
        await Assert.That(addressOverflow.Errors.Any(error =>
            error.PropertyName == nameof(CreateOrganizationDto.Address))).IsTrue();
    }

    [Test]
    [Arguments("invalid-email")]
    [Arguments("invalid@")]
    [Arguments("@invalid.com")]
    public async Task InvalidEmailShapesAreRejected(string email)
    {
        var result = await _validator.ValidateAsync(CreateDto(email: email));

        await Assert.That(result.Errors.Any(error =>
            error.PropertyName == nameof(CreateOrganizationDto.Email))).IsTrue();
    }

    [Test]
    [Arguments("", true)]
    [Arguments("https://example.com", true)]
    [Arguments("http://example.org", true)]
    [Arguments("not-a-url", false)]
    [Arguments("www.example.com", false)]
    public async Task OptionalWebsiteUsesAbsoluteHttpUrlPartition(
        string website,
        bool expectedValid)
    {
        var result = await _validator.ValidateAsync(
            CreateDto(websiteUrl: website));

        await Assert.That(result.IsValid).IsEqualTo(expectedValid);
    }

    private static CreateOrganizationDto CreateDto(
        string fullName = "Test Organization",
        string email = "test@example.com",
        string country = "Belgium",
        string city = "Brussels",
        string address = "123 Test Street",
        string websiteUrl = "https://example.com") =>
        new()
        {
            FullName = fullName,
            Email = email,
            Country = country,
            City = city,
            Address = address,
            Postcode = 1000,
            WebsiteUrl = websiteUrl
        };
}
