using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.Location;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.PublicExperience.Handlers.Queries;
using Explore.Application.Models.PublicExperience;
using Explore.Application.Serialization;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Event.Architecture.Tests;

[Category("EventLocationPrivacy")]
public sealed class DiscoveryPostgisSeparationArchitectureTests
{
    [Test]
    public async Task AreaOnlyPublicContractsRemainCoordinateFreeByDefault()
    {
        var area = new PublicDiscoveryAreaDto
        {
            Id = Guid.NewGuid(),
            DisplayName = "Brussels",
            City = "Brussels",
            CountryCode = "BE",
            CentroidLatitude = 50.85m,
            CentroidLongitude = 4.35m
        };
        var sourceItem = new EventDiscoveryItemDto
        {
            Event = new EventListDto
            {
                Id = Guid.NewGuid(),
                Title = "Area-only event",
                EventTypeFullName = "Community",
                AudienceGenderFullName = "All",
                AudienceAgeFullName = "All",
                ActorDisplayName = "Organizer",
                ActorTypeFullName = "Organization",
                EventStatusFullName = "Published",
                VisibilityTypeFullName = "Public",
                EventFormatFullName = "In-Person"
            },
            DistanceMeters = 125,
            NearestSessionId = Guid.NewGuid(),
            NearestLocationId = Guid.NewGuid(),
            NearestLocationName = "Exact venue",
            NearestOccurrenceStartsAtUtc = DateTimeOffset.Parse("2026-07-20T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture)
        };
        var mapMethod = typeof(GetHomeDiscoveryQueryHandler).GetMethod(
            "MapDiscoveryItem",
            BindingFlags.NonPublic | BindingFlags.Static);
        var item = (EventDiscoveryItemDto)mapMethod!.Invoke(null, [sourceItem])!;
        var home = new HomeDiscoveryDto
        {
            Context = new HomeDiscoveryContextDto
            {
                Mode = HomeDiscoveryMode.Area,
                AvailableAreas = [area]
            },
            Hero = [item]
        };

        var locationProperties = typeof(LocationListDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        var areaProperties = typeof(PublicDiscoveryAreaDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        var json = JsonSerializer.Serialize(home, ExploreJsonContext.Default.HomeDiscoveryDto);

        await Assert.That(locationProperties).DoesNotContain("Latitude");
        await Assert.That(locationProperties).DoesNotContain("Longitude");
        await Assert.That(locationProperties).DoesNotContain("Coordinates");
        await Assert.That(areaProperties).DoesNotContain("LocationIds");
        await Assert.That(areaProperties).DoesNotContain("Address");
        await Assert.That(areaProperties).DoesNotContain("Postcode");
        await Assert.That(json).Contains("\"centroidLatitude\"");
        await Assert.That(json).Contains("\"centroidLongitude\"");
        await Assert.That(json).DoesNotContain("\"latitude\"");
        await Assert.That(json).DoesNotContain("\"longitude\"");
        await Assert.That(json).DoesNotContain("\"address\"");
        await Assert.That(json).DoesNotContain("\"postcode\"");
        await Assert.That(item.DistanceMeters).IsNull();
        await Assert.That(item.NearestSessionId).IsNull();
        await Assert.That(item.NearestLocationId).IsNull();
        await Assert.That(item.NearestLocationName).IsNull();
        await Assert.That(item.NearestOccurrenceStartsAtUtc).IsNull();
        await Assert.That(json).DoesNotContain("\"distanceMeters\"");
        await Assert.That(json).DoesNotContain("\"nearestSessionId\"");
        await Assert.That(json).DoesNotContain("\"nearestLocationId\"");
        await Assert.That(json).DoesNotContain("\"nearestLocationName\"");
        await Assert.That(json).DoesNotContain("\"nearestOccurrenceStartsAtUtc\"");
    }

    [Test]
    public async Task DiscoveryAreaValidationUsesBoundedScalarLocationIds()
    {
        var repositoryMethod = typeof(ILocationRepository).GetMethod(
            "GetExistingTenantLocationIdsAsync",
            [typeof(Guid), typeof(IReadOnlyCollection<Guid>), typeof(CancellationToken)]);
        var handlerSource = await File.ReadAllTextAsync(ContextSystemHelpers.RepoPath(
            "Explore.Application",
            "Features",
            "PublicExperience",
            "Handlers",
            "Queries",
            "GetHomeDiscoveryQueryHandler.cs"));
        var repositorySource = await File.ReadAllTextAsync(ContextSystemHelpers.RepoPath(
            "Explore.Persistence",
            "Repositories",
            "LocationRepository.cs"));

        await Assert.That(repositoryMethod).IsNotNull();
        await Assert.That(repositoryMethod!.ReturnType).IsEqualTo(typeof(Task<IReadOnlyList<Guid>>));
        await Assert.That(handlerSource).Contains("GetExistingTenantLocationIdsAsync");
        await Assert.That(handlerSource).DoesNotContain("GetLocationsByTenant");
        await Assert.That(repositorySource).Contains("IgnoreAutoIncludes()");
        await Assert.That(repositorySource).Contains(".Select(location => location.Id)");
    }

    [Test]
    public async Task PrivateHomeWithPiiHasNoDiscoveryPointByDefault()
    {
        var locationId = Guid.NewGuid();
        var privateHome = new Location
        {
            Id = locationId,
            TenantId = Guid.NewGuid(),
            Tenant = null!,
            FullName = "Private home",
            Country = "BE",
            City = "Brussels"
        };
        privateHome.ClassifyAsPrivateHome(Guid.NewGuid());
        privateHome.SetProviderAddress(
            "Private address",
            "1000",
            Explore.Domain.ValueObjects.GeoCoordinate.Create(50.8466, 4.3528));

        var propertyNames = privateHome.GetType()
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        await Assert.That(propertyNames).DoesNotContain("DiscoveryPoint");
        await Assert.That(propertyNames).DoesNotContain("PublicPoint");
        await Assert.That(propertyNames).DoesNotContain("IsPubliclyDiscoverable");
    }

    [Test]
    public async Task PostgisDiscoveryRuntimeSurfaceRemainsAbsent()
    {
        var forbiddenPatterns = new (string Surface, string Pattern)[]
        {
            ("domain entity", @"\bLocationDiscoveryPoint\b"),
            ("table or index", @"\blocation_discovery_points?\b"),
            ("spatial radius query", @"\bST_DWithin\b"),
            ("spatial distance query", @"\bST_Distance\b"),
            ("spatial geography mapping", @"geography\s*\(\s*Point"),
            ("spatial package", @"\bNetTopologySuite\b"),
            ("PostGIS dependency", @"\bPostGIS\b"),
            ("proximity endpoint or service", @"\b(?:LocationProximity|ProximityDiscovery)\w*\b")
        };
        var sourceFiles = Directory
            .EnumerateFiles(ContextSystemHelpers.RepoPath("src"), "*", SearchOption.AllDirectories)
            .Where(IsProductionContractSource)
            .Append(ContextSystemHelpers.RepoPath("Directory.Packages.props"))
            .Append(ContextSystemHelpers.RepoPath("docker-compose.yml"));
        var violations = new List<string>();

        foreach (var sourceFile in sourceFiles)
        {
            var source = await File.ReadAllTextAsync(sourceFile);
            foreach (var (surface, pattern) in forbiddenPatterns)
            {
                if (Regex.IsMatch(source, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    violations.Add(
                        $"{Path.GetRelativePath(ContextSystemHelpers.RepoRoot, sourceFile)}:{surface}");
                }
            }
        }

        var domainTypeNames = typeof(Location).Assembly
            .GetTypes()
            .Select(type => type.Name)
            .ToArray();
        var dbSetEntityNames = typeof(ExploreDbContext)
            .GetProperties()
            .Where(property => property.PropertyType.IsGenericType &&
                               property.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(property => property.PropertyType.GetGenericArguments()[0].Name)
            .ToArray();

        await Assert.That(violations).IsEmpty();
        await Assert.That(domainTypeNames).DoesNotContain("LocationDiscoveryPoint");
        await Assert.That(dbSetEntityNames).DoesNotContain("LocationDiscoveryPoint");
    }

    [Test]
    public async Task PublicDiscoverySourceMustNotReadExactLocationPii()
    {
        var sourcePaths = new[]
        {
            ContextSystemHelpers.RepoPath("Explore.Application", "DTOs", "PublicExperience"),
            ContextSystemHelpers.RepoPath("Explore.Application", "Features", "PublicExperience"),
            ContextSystemHelpers.RepoPath(
                "Explore.Application",
                "Features",
                "Federation",
                "Atproto",
                "Handlers",
                "Queries",
                "GetPublicEventDiscoveryRequestHandler.cs"),
            ContextSystemHelpers.RepoPath(
                "Explore.API",
                "Controllers",
                "PublicExperienceController.cs")
        };
        var exactPiiPatterns = new[]
        {
            @"\bLocationPii\b",
            @"\.Pii\b",
            @"\.(?:Latitude|Longitude|Address|Postcode)\b"
        };
        var violations = new List<string>();

        foreach (var sourceFile in sourcePaths.SelectMany(EnumerateCSharpFiles))
        {
            var source = await File.ReadAllTextAsync(sourceFile);
            foreach (var pattern in exactPiiPatterns)
            {
                if (Regex.IsMatch(source, pattern, RegexOptions.CultureInvariant))
                {
                    violations.Add(
                        $"{Path.GetRelativePath(ContextSystemHelpers.RepoRoot, sourceFile)}:{pattern}");
                }
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    private static bool IsProductionContractSource(string path)
    {
        var relativePath = Path.GetRelativePath(ContextSystemHelpers.RepoPath("src"), path);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (segments.Contains("bin", StringComparer.OrdinalIgnoreCase) ||
            segments.Contains("obj", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return Path.GetExtension(path) is ".cs" or ".csproj" or ".props" or ".targets" or
            ".json" or ".yml" or ".yaml" or ".razor" or ".js";
    }

    private static IEnumerable<string> EnumerateCSharpFiles(string path) =>
        File.Exists(path)
            ? [path]
            : Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories);
}
