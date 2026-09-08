using Explore.Blazor.Client.Constants;
using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Services;

public class LocationServiceTests
{
    private readonly ILocationClient _apiClient;
    private readonly Microsoft.Extensions.Logging.ILogger<LocationService> _logger;
    private readonly LocationService _service;

    public LocationServiceTests()
    {
        _apiClient = Substitute.For<ILocationClient>();
        _logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<LocationService>>();
        _service = new LocationService(_apiClient, _logger);
    }

    // ========== GetAllLocationsAsync ==========

    #region GetAllLocationsAsync Tests

    [Test]
    public async Task GetAllLocationsAsync_ReturnsLocations_WhenApiSucceeds()
    {
        // Arrange
        var locations = ComponentDataBuilder.LocationListDto.Generate(3);
        var halResponse = CreateLocationCollectionResponse(locations);

        _apiClient.GetLocationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        var result = await _service.GetAllLocationsAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result.First().FullName).IsEqualTo(locations.First().FullName);
    }

    [Test]
    public async Task GetAllLocationsAsync_ReturnsEmptyList_WhenApiReturnsNull()
    {
        // Arrange
        _apiClient.GetLocationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((HalCollectionResourceOfLocationListDto?)null);

        // Act
        var result = await _service.GetAllLocationsAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetAllLocationsAsync_ReturnsEmptyList_WhenApiThrows()
    {
        // Arrange
        _apiClient.GetLocationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act
        var result = await _service.GetAllLocationsAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetAllLocationsAsync_CallsApiWithCorrectPagination()
    {
        // Arrange
        var halResponse = CreateLocationCollectionResponse(new List<LocationListDto>());
        _apiClient.GetLocationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        await _service.GetAllLocationsAsync();

        // Assert
        await _apiClient.Received(1).GetLocationsAsync(
            ApiConstants.FirstPage,
            ApiConstants.DefaultPageSize,
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    #endregion

    // ========== GetLocationByIdAsync ==========

    #region GetLocationByIdAsync Tests

    [Test]
    public async Task GetLocationByIdAsync_ReturnsLocation_WhenFound()
    {
        // Arrange
        var locationId = Guid.NewGuid();
        var halResponse = new HalResourceOfLocationDto
        {
            Id = locationId,
            FullName = "Main Hall",
            Address = "123 Street",
            City = "London",
            Country = "United Kingdom",
            Timezone = "Europe/London",
            Postcode = "SW1A 1AA"
        };

        _apiClient.GetLocationByIdAsync(locationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        var result = await _service.GetLocationByIdAsync(locationId);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(locationId);
        await Assert.That(result.FullName).IsEqualTo("Main Hall");
    }

    [Test]
    public async Task GetLocationByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var locationId = Guid.NewGuid();
        _apiClient.GetLocationByIdAsync(locationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Not Found", 404, null, null, null));

        // Act
        var result = await _service.GetLocationByIdAsync(locationId);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetLocationByIdAsync_ReturnsNull_WhenApiThrows()
    {
        // Arrange
        var locationId = Guid.NewGuid();
        _apiClient.GetLocationByIdAsync(locationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Server Error", 500, null, null, null));

        // Act
        var result = await _service.GetLocationByIdAsync(locationId);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    // ========== CreateLocationAsync ==========

    #region CreateLocationAsync Tests

    [Test]
    public async Task CreateLocationAsync_ReturnsResponse_WhenApiSucceeds()
    {
        // Arrange
        var dto = new CreateLocationDto
        {
            FullName = "New Location",
            Address = "456 Road",
            City = "Paris",
            Country = "France",
            Timezone = "Europe/Paris"
        };
        var expectedResponse = ComponentDataBuilder.SuccessResponse();

        _apiClient.CreateLocationAsync(Arg.Any<CreateLocationDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        // Act
        var result = await _service.CreateLocationAsync(dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
    }

    [Test]
    public async Task CreateLocationAsync_ReturnsFailureResponse_WhenApiThrows()
    {
        // Arrange
        var dto = new CreateLocationDto
        {
            FullName = "New Location",
            Address = "456 Road",
            City = "Paris",
            Country = "France",
            Timezone = "Europe/Paris"
        };

        _apiClient.CreateLocationAsync(Arg.Any<CreateLocationDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Bad Request", 400, "validation error", null, null));

        // Act
        var result = await _service.CreateLocationAsync(dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
        await Assert.That(result.Message).Contains("API error");
    }

    #endregion

    // ========== UpdateLocationAsync ==========

    #region UpdateLocationAsync Tests

    [Test]
    public async Task UpdateLocationAsync_ReturnsResponse_WhenApiSucceeds()
    {
        // Arrange
        var locationId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        var dto = new UpdateLocationDto
        {
            FullName = new UpdateLocationFullNameDto { Value = "Updated Location" }
        };
        var expectedResponse = ComponentDataBuilder.SuccessResponse(locationId);

        _apiClient.UpdateLocationAsync(Arg.Any<Guid>(), Arg.Any<UpdateLocationDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        // Act
        var result = await _service.UpdateLocationAsync(locationId, concurrencyStamp, dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await Assert.That(result.Id).IsEqualTo(locationId);
        await _apiClient.Received(1).UpdateLocationAsync(
            locationId,
            dto,
            $"\"{concurrencyStamp:D}\"",
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateLocationAsync_ReturnsFailureResponse_WhenApiThrows()
    {
        // Arrange
        var locationId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        var dto = new UpdateLocationDto
        {
            FullName = new UpdateLocationFullNameDto { Value = "Updated Location" }
        };

        _apiClient.UpdateLocationAsync(Arg.Any<Guid>(), Arg.Any<UpdateLocationDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Bad Request", 400, "validation error", null, null));

        // Act
        var result = await _service.UpdateLocationAsync(locationId, concurrencyStamp, dto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
        await Assert.That(result.Message).Contains("API error");
    }

    #endregion

    // ========== DeleteLocationAsync ==========

    #region DeleteLocationAsync Tests

    [Test]
    public async Task DeleteLocationAsync_ReturnsTrue_WhenApiSucceeds()
    {
        // Arrange
        var locationId = Guid.NewGuid();
        _apiClient.DeleteLocationAsync(locationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.DeleteLocationAsync(locationId);

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task DeleteLocationAsync_ReturnsFalse_WhenApiThrows()
    {
        // Arrange
        var locationId = Guid.NewGuid();
        _apiClient.DeleteLocationAsync(locationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException("Forbidden", 403, null, null, null));

        // Act
        var result = await _service.DeleteLocationAsync(locationId);

        // Assert
        await Assert.That(result).IsFalse();
    }

    #endregion

    // ========== GetLocationsByCityAsync ==========

    // ========== HAL Response Helpers ==========

    #region HAL Response Helpers

    private static HalCollectionResourceOfLocationListDto CreateLocationCollectionResponse(
        IList<LocationListDto> items)
    {
        return new HalCollectionResourceOfLocationListDto
        {
            _embedded = new HalCollectionEmbeddedOfLocationListDto
            {
                Items = items.Select(ToHalResource).ToList()
            }
        };
    }

    private static HalResourceOfLocationListDto ToHalResource(LocationListDto item)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(item);
        return System.Text.Json.JsonSerializer.Deserialize<HalResourceOfLocationListDto>(json)
               ?? new HalResourceOfLocationListDto();
    }

    #endregion
}
