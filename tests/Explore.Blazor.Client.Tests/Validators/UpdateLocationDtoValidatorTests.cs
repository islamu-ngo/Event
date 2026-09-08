using Explore.Blazor.Client.Validators;

namespace Explore.Blazor.Client.Tests.Validators;

public sealed class UpdateLocationDtoValidatorTests
{
    private readonly UpdateLocationDtoValidator _validator = new();

    [Test]
    public async Task EmptyUpdate_Fails()
    {
        var result = await _validator.ValidateAsync(new UpdateLocationDto());

        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task FullNameGroup_Passes() =>
        await AssertValid(new UpdateLocationDto
        {
            FullName = new UpdateLocationFullNameDto { Value = "Hall" }
        });

    [Test]
    public async Task AddressGroup_Passes() =>
        await AssertValid(new UpdateLocationDto
        {
            Address = new UpdateLocationAddressDto { Value = "10 Safe Street" }
        });

    [Test]
    public async Task PostcodeGroup_Passes() =>
        await AssertValid(new UpdateLocationDto
        {
            Postcode = new UpdateLocationPostcodeDto { Value = "1000" }
        });

    [Test]
    public async Task CountryGroup_Passes() =>
        await AssertValid(new UpdateLocationDto
        {
            Country = new UpdateLocationCountryDto { Value = "Belgium" }
        });

    [Test]
    public async Task CityGroup_Passes() =>
        await AssertValid(new UpdateLocationDto
        {
            City = new UpdateLocationCityDto { Value = "Brussels" }
        });

    [Test]
    public async Task TimezoneGroup_Passes() =>
        await AssertValid(new UpdateLocationDto
        {
            Timezone = new UpdateLocationTimezoneDto
            {
                Value = new OptionalUpdateOfstring { HasValue = true, Value = "Europe/Brussels" }
            }
        });

    private async Task AssertValid(UpdateLocationDto dto)
    {
        var result = await _validator.ValidateAsync(dto);

        await Assert.That(result.IsValid).IsTrue();
    }
}
