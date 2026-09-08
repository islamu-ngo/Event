using Explore.Application.Models.PublicExperience;

namespace Event.Application.UnitTests.Features.PublicExperience;

public sealed class PublicDiscoveryAreasConfigValidatorTests
{
    private static readonly Guid TenantLocationId = Guid.NewGuid();

    [Test]
    public async Task ValidConfigurationHasNoErrors()
    {
        var config = BuildConfig(
            new PublicDiscoveryAreaConfig(
                Guid.NewGuid(),
                "Brussels",
                "Brussels",
                "BE",
                50.85m,
                4.35m,
                [TenantLocationId],
                IsDefault: true));

        var errors = PublicDiscoveryAreasConfigValidator.Validate(config, new HashSet<Guid> { TenantLocationId });

        await Assert.That(errors).IsEmpty();
    }

    [Test]
    public async Task DuplicateIdsAndMultipleDefaultsAreRejected()
    {
        var areaId = Guid.NewGuid();
        var config = BuildConfig(
            new PublicDiscoveryAreaConfig(areaId, "Brussels", "Brussels", "BE", IsDefault: true),
            new PublicDiscoveryAreaConfig(areaId, "Antwerp", "Antwerp", "BE", IsDefault: true));

        var errors = PublicDiscoveryAreasConfigValidator.Validate(config, new HashSet<Guid>());

        await Assert.That(errors.Any(error => error.Contains("unique", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(errors.Any(error => error.Contains("one discovery area", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task InvalidOrOverPreciseCentroidsAreRejected()
    {
        var config = BuildConfig(
            new PublicDiscoveryAreaConfig(Guid.NewGuid(), "Invalid latitude", "City", "BE", 91m, 4.35m),
            new PublicDiscoveryAreaConfig(Guid.NewGuid(), "Missing longitude", "City", "BE", 50.85m),
            new PublicDiscoveryAreaConfig(Guid.NewGuid(), "Over precise", "City", "BE", 50.857m, 4.35m));

        var errors = PublicDiscoveryAreasConfigValidator.Validate(config, new HashSet<Guid>());

        await Assert.That(errors.Count(error => error.Contains("centroid", StringComparison.OrdinalIgnoreCase))).IsEqualTo(3);
    }

    [Test]
    public async Task ForeignAndRepeatedTenantLocationsAreRejected()
    {
        var foreignLocationId = Guid.NewGuid();
        var config = BuildConfig(
            new PublicDiscoveryAreaConfig(
                Guid.NewGuid(),
                "Brussels",
                "Brussels",
                "BE",
                LocationIds: [TenantLocationId, foreignLocationId]),
            new PublicDiscoveryAreaConfig(
                Guid.NewGuid(),
                "Antwerp",
                "Antwerp",
                "BE",
                LocationIds: [TenantLocationId]));

        var errors = PublicDiscoveryAreasConfigValidator.Validate(config, new HashSet<Guid> { TenantLocationId });

        await Assert.That(errors.Any(error => error.Contains("current tenant", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(errors.Any(error => error.Contains("one discovery area", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task InactiveDefaultAreaIsRejected()
    {
        var config = BuildConfig(
            new PublicDiscoveryAreaConfig(
                Guid.NewGuid(),
                "Brussels",
                "Brussels",
                "BE",
                IsActive: false,
                IsDefault: true));

        var errors = PublicDiscoveryAreasConfigValidator.Validate(config, new HashSet<Guid>());

        await Assert.That(errors.Any(error => error.Contains("default area must be active", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    private static PublicDiscoveryAreasConfig BuildConfig(params PublicDiscoveryAreaConfig[] areas) =>
        new(SchemaVersion: 1, Areas: areas);
}
