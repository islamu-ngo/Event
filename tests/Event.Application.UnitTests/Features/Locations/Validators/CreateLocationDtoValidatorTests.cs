using Explore.Application.DTOs.Location;
using Explore.Application.DTOs.Location.Validators;

namespace Event.Application.UnitTests.Features.Locations.Validators;

public sealed class CreateLocationDtoValidatorTests
{
    private readonly CreateLocationDtoValidator _validator = new();

    [Test]
    public async Task CompleteManualLocationIsValid()
    {
        var result = await _validator.ValidateAsync(CreateDto());

        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    public async Task EveryRequiredAddressPartitionRejectsBlankInput()
    {
        (string Property, CreateLocationDto Dto)[] cases =
        [
            (nameof(CreateLocationDto.FullName), CreateDto(fullName: "")),
            (nameof(CreateLocationDto.Address), CreateDto(address: "")),
            (nameof(CreateLocationDto.Postcode), CreateDto(postcode: "")),
            (nameof(CreateLocationDto.Country), CreateDto(country: "")),
            (nameof(CreateLocationDto.City), CreateDto(city: ""))
        ];

        foreach ((string property, CreateLocationDto dto) in cases)
        {
            var result = await _validator.ValidateAsync(dto);

            await Assert.That(result.IsValid).IsFalse();
            await Assert.That(result.Errors.Any(error =>
                error.PropertyName == property)).IsTrue();
        }
    }

    [Test]
    public async Task FullNameBoundaryAcceptsLimitAndRejectsOverflow()
    {
        var atLimit = await _validator.ValidateAsync(
            CreateDto(fullName: new string('a', 500)));
        var overflow = await _validator.ValidateAsync(
            CreateDto(fullName: new string('a', 501)));

        await Assert.That(atLimit.IsValid).IsTrue();
        await Assert.That(overflow.Errors.Any(error =>
            error.PropertyName == nameof(CreateLocationDto.FullName))).IsTrue();
    }

    private static CreateLocationDto CreateDto(
        string fullName = "Test Location",
        string address = "123 Test Street",
        string postcode = "12345",
        string country = "Belgium",
        string city = "Brussels") =>
        new()
        {
            FullName = fullName,
            Address = address,
            Postcode = postcode,
            Country = country,
            City = city
        };
}
