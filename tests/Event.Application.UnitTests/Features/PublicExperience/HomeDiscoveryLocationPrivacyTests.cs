using System.Text.Json;
using Explore.Application.DTOs.Location;
using Explore.Application.DTOs.PublicExperience;

namespace Explore.Application.UnitTests.Features.PublicExperience;

public sealed class HomeDiscoveryLocationPrivacyTests
{
    [Test]
    public async Task GenericLocationListDtoExposesNoCoordinateProperties()
    {
        var propertyNames = typeof(LocationListDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        await Assert.That(propertyNames).DoesNotContain("Latitude");
        await Assert.That(propertyNames).DoesNotContain("Longitude");
        await Assert.That(propertyNames).DoesNotContain("Coordinates");
    }

    [Test]
    public async Task HomeDiscoverySerializationContainsOnlyCoarsePublicAreaLocationData()
    {
        var home = new HomeDiscoveryDto
        {
            Context = new HomeDiscoveryContextDto
            {
                SelectedAreaId = Guid.NewGuid(),
                SelectedAreaDisplayName = "Brussels",
                AvailableAreas =
                [
                    new PublicDiscoveryAreaDto
                    {
                        Id = Guid.NewGuid(),
                        DisplayName = "Brussels",
                        City = "Brussels",
                        CountryCode = "BE",
                        CentroidLatitude = 50.85m,
                        CentroidLongitude = 4.35m
                    }
                ]
            }
        };

        var json = JsonSerializer.Serialize(home);

        await Assert.That(json).Contains("CentroidLatitude");
        await Assert.That(json).Contains("CentroidLongitude");
        await Assert.That(json).DoesNotContain("LocationIds");
        await Assert.That(json).DoesNotContain("Address");
        await Assert.That(json).DoesNotContain("Postcode");
        await Assert.That(json).DoesNotContain("Origin");
    }
}
