using System.Reflection;
using AutoMapper;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Geocoding;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.Geocoding;
using Explore.Application.Features.Locations.Handlers.Commands;
using Explore.Application.Features.Locations.Requests.Commands;
using Explore.Application.Profiles;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using NSubstitute;

using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.Locations.Commands;

public sealed class LocationAddressWriteContractTests
{
    private const string ProviderTransitionName = "SetProviderAddress";

    [Test]
    public async Task DirectLocationWriteContractsDoNotExposeRawCoordinateOrTenantMembers()
    {
        string[] forbiddenMembers =
        [
            .. NamedPublicProperties(typeof(CreateLocationDto), "TenantId", "Latitude", "Longitude"),
            .. NamedPublicProperties(typeof(UpdateLocationDto), "TenantId", "Latitude", "Longitude"),
            .. typeof(Location).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.Name is "Latitude" or "Longitude" && property.SetMethod?.IsPublic == true)
                .Select(property => $"{nameof(Location)}.{property.Name} setter"),
            .. PublicSetterViolations(typeof(Location), nameof(Location.Pii)),
            .. PublicMethodViolations(typeof(Location), "AttachPii"),
            .. PublicSetterViolations(
                typeof(LocationPii),
                nameof(LocationPii.Address),
                nameof(LocationPii.Postcode),
                nameof(LocationPii.Latitude),
                nameof(LocationPii.Longitude),
                nameof(LocationPii.LocationId),
                nameof(LocationPii.Location))
        ];

        ConstructorInfo[] handlerConstructors = typeof(CreateLocationCommandHandler)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public);
        string[] mapperConstructorParameters = handlerConstructors
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => parameter.ParameterType.FullName == "AutoMapper.IMapper")
            .Select(parameter => $"{nameof(CreateLocationCommandHandler)} constructor parameter {parameter.Name}")
            .ToArray();

        await Assert.That(forbiddenMembers.Concat(mapperConstructorParameters)).IsEmpty();
    }

    [Test]
    public async Task CreateWithRepositoryNativeRealMapperConstructsTheLocationPiiAggregate()
    {
        Guid tenantId = Guid.CreateVersion7();
        var locations = Substitute.For<ILocationRepository>();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        Location? persisted = null;
        locations.Create(Arg.Do<Location>(location =>
            {
                persisted = location;
                location.Id = Guid.CreateVersion7();
            }), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Location>()
                ?? throw new InvalidOperationException("The location repository received a null entity."));
        var handler = CreateLocationHandler(locations, tenantContext);
        Exception? constructionFailure = null;

        try
        {
            await handler.Handle(new CreateLocationCommand
            {
                TenantId = tenantId,
                LocationDto = ManualCreateDto()
            }, CancellationToken.None);
        }
        catch (Exception exception)
        {
            constructionFailure = exception;
        }

        var violations = new List<string>();
        if (constructionFailure is not null)
        {
            violations.Add($"real aggregate construction threw {constructionFailure.GetType().Name}");
        }
        if (persisted?.Pii is not { Address: "Rue Manual 20", Postcode: "1000" })
        {
            violations.Add("the persisted Location was not constructed with its required PII child");
        }
        if (persisted?.TenantId != tenantId)
        {
            violations.Add("the persisted Location did not use the trusted tenant");
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task CreateWhenTrustedCommandTenantDiffersFromContextFailsClosedBeforePersistence()
    {
        Guid contextTenantId = Guid.CreateVersion7();
        var locations = Substitute.For<ILocationRepository>();
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(contextTenantId);
        locations.Create(Arg.Any<Location>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<Location>()
            ?? throw new InvalidOperationException("The location repository received a null entity."));
        var handler = CreateLocationHandler(locations, tenantContext);

        var response = await handler.Handle(new CreateLocationCommand
        {
            TenantId = Guid.CreateVersion7(),
            LocationDto = ManualCreateDto()
        }, CancellationToken.None);

        var violations = new List<string>();
        if (response.IsSuccess)
        {
            violations.Add("a command tenant that disagreed with trusted context was accepted");
        }
        if (locations.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(ILocationRepository.Create)))
        {
            violations.Add("the mismatched-tenant request reached persistence");
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task PublicAddressTransitionContractExposesNoUnauthorizedProviderOverloads()
    {
        var violations = new List<string>();
        MethodInfo[] providerTransitions = FindPublicProviderTransitions();

        MethodInfo[] expectedProviderTransitions = providerTransitions
            .Where(IsExpectedProviderTransition)
            .ToArray();
        if (expectedProviderTransitions.Length != 1)
        {
            violations.Add($"Location must expose exactly one public {ProviderTransitionName}(string, string, {nameof(GeoCoordinate)}); found {expectedProviderTransitions.Length}");
        }

        violations.AddRange(providerTransitions
            .Where(method => !IsExpectedProviderTransition(method))
            .Select(method => $"Location exposes unauthorized public provider transition {FormatMethodSignature(method)}"));

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task ManualAddressTransitionOnActivePrivateHomeReplacesManualFieldsAndClearsStaleCoordinates()
    {
        Guid ownerId = Guid.CreateVersion7();
        Location location = NewLocation(Guid.CreateVersion7(), 50.8503, 4.3517);
        location.ClassifyAsPrivateHome(ownerId);

        location.SetManualAddress("Rue Manual 21", "2000");

        await Assert.That(location.Pii?.Address).IsEqualTo("Rue Manual 21");
        await Assert.That(location.Pii?.Postcode).IsEqualTo("2000");
        await Assert.That(location.Pii?.Latitude).IsNull();
        await Assert.That(location.Pii?.Longitude).IsNull();
        await Assert.That(location.LocationPrivacyStateId).IsEqualTo((int)LocationPrivacyStateEnum.Active);
        await Assert.That(location.LocationKindId).IsEqualTo((int)LocationKindEnum.PrivateHome);
        await Assert.That(location.OwnerUserId).IsEqualTo(ownerId);
    }

    [Test]
    public async Task ProviderAddressTransitionWithNormalFiniteCoordinatePersistsAllFieldsExactly()
    {
        Location location = NewLocation(Guid.CreateVersion7());
        GeoCoordinate coordinate = GeoCoordinate.Create(50.8503, 4.3517);

        location.SetProviderAddress("Rue Provider 30", "1000", coordinate);

        await Assert.That(location.Pii?.Address).IsEqualTo("Rue Provider 30");
        await Assert.That(location.Pii?.Postcode).IsEqualTo("1000");
        await Assert.That(location.Pii?.Latitude).IsEqualTo(50.8503);
        await Assert.That(location.Pii?.Longitude).IsEqualTo(4.3517);
    }

    [Test]
    public async Task GeoCoordinateFactoryRejectsNonFiniteAndGeographicallyOutOfRangeValues()
    {
        (double Latitude, double Longitude, string Case)[] invalidCoordinates =
        [
            (double.NaN, 4.3517, "NaN latitude"),
            (50.8503, double.NaN, "NaN longitude"),
            (double.PositiveInfinity, 4.3517, "positive infinity latitude"),
            (50.8503, double.PositiveInfinity, "positive infinity longitude"),
            (double.NegativeInfinity, 4.3517, "negative infinity latitude"),
            (50.8503, double.NegativeInfinity, "negative infinity longitude"),
            (90.0001, 4.3517, "latitude above 90"),
            (-90.0001, 4.3517, "latitude below -90"),
            (50.8503, 180.0001, "longitude above 180"),
            (50.8503, -180.0001, "longitude below -180")
        ];
        var violations = new List<string>();

        foreach (var coordinate in invalidCoordinates)
        {
            bool rejected = CoordinateRejected(coordinate.Latitude, coordinate.Longitude);
            if (!rejected)
            {
                violations.Add($"{coordinate.Case} was accepted by the coordinate factory");
            }
        }

        const string sensitiveCoordinateCanary = "190.123456789";
        try
        {
            GeoCoordinate.Create(50.8503, 190.123456789);
            violations.Add("the sensitive coordinate canary was accepted");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            if (exception.ToString().Contains(sensitiveCoordinateCanary, StringComparison.Ordinal))
            {
                violations.Add("the coordinate exception disclosed its exact sensitive value");
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task PublicPiiBypassesAreImpossibleAndManualTransitionRemainsAtomic()
    {
        PropertyInfo? piiProperty = typeof(Location).GetProperty(nameof(Location.Pii));
        MethodInfo? attachPii = typeof(Location).GetMethod("AttachPii", BindingFlags.Instance | BindingFlags.Public);
        PropertyInfo[] mutablePiiFields = typeof(LocationPii)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.Name is nameof(LocationPii.Address)
                or nameof(LocationPii.Postcode)
                or nameof(LocationPii.Latitude)
                or nameof(LocationPii.Longitude))
            .Where(property => property.SetMethod?.IsPublic == true)
            .ToArray();
        Location location = NewLocation(Guid.CreateVersion7(), 50.8503, 4.3517);

        location.SetManualAddress("Rue Atomic 40", "4000");

        var violations = new List<string>();
        if (piiProperty?.SetMethod?.IsPublic == true)
        {
            violations.Add("Location.Pii exposes a public setter");
        }
        if (attachPii is not null)
        {
            violations.Add("Location.AttachPii remains public");
        }
        violations.AddRange(mutablePiiFields.Select(property => $"LocationPii.{property.Name} exposes a public setter"));
        if (location.Pii is not { Address: "Rue Atomic 40", Postcode: "4000", Latitude: null, Longitude: null })
        {
            violations.Add("the authorized manual transition did not replace both address fields and clear coordinates atomically");
        }
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task PatchWhenManualAddressChangesClearsStaleCoordinatesAndPreservesPrivateHomeConsent()
    {
        Guid ownerId = Guid.CreateVersion7();
        Location location = NewLocation(Guid.CreateVersion7(), 50.8503, 4.3517);
        location.ClassifyAsPrivateHome(ownerId);
        var locations = Substitute.For<ILocationRepository>();
        locations.GetById(location.Id, Arg.Any<CancellationToken>()).Returns(location);
        var handler = CreateUpdateLocationHandler(locations, location.TenantId);

        var response = await handler.Handle(new UpdateLocationCommand
        {
            LocationId = location.Id,
            ExpectedConcurrencyStamp = location.ConcurrencyStamp,
            UpdateLocationDto = new UpdateLocationDto
            {
                Address = new UpdateLocationAddressDto { Value = "Rue Manual 21" }
            }
        }, CancellationToken.None);

        var violations = new List<string>();
        if (!response.IsSuccess)
        {
            violations.Add("the valid manual address PATCH was rejected");
        }
        if (location.Pii?.Address != "Rue Manual 21")
        {
            violations.Add("the manual address PATCH did not persist the exact requested address");
        }
        if (location.Pii?.Postcode != "1000")
        {
            violations.Add("the address-only PATCH did not retain the existing postcode");
        }
        if (location.Pii?.Latitude is not null || location.Pii?.Longitude is not null)
        {
            violations.Add("manual address mutation retained stale coordinates");
        }
        if (location.LocationKindId != (int)LocationKindEnum.PrivateHome || location.OwnerUserId != ownerId)
        {
            violations.Add("manual address mutation changed consent-backed Private Home ownership");
        }
        var updateCalls = locations.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILocationRepository.Update))
            .ToArray();
        if (updateCalls.Length != 1 || !ReferenceEquals(updateCalls[0].GetArguments()[0], location))
        {
            violations.Add("the manual address PATCH did not update the same aggregate exactly once");
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task ErasureRemainsAuthoritativeAgainstAllPublicAddressTransitions()
    {
        Location location = NewLocation(Guid.CreateVersion7(), 50.8503, 4.3517);
        location.ClassifyAsPrivateHome(Guid.CreateVersion7());
        location.EraseOwnedPii(DateTime.UtcNow, LocationPrivacyErasureReasonEnum.OwnerErasureRequest);
        var violations = new List<string>();

        try
        {
            location.SetManualAddress("Resurrected address", "9999");
            violations.Add("manual address transition resurrected erased PII");
        }
        catch (InvalidOperationException)
        {
        }
        try
        {
            location.SetProviderAddress(
                "Resurrected address",
                "9999",
                GeoCoordinate.Create(50.8503, 4.3517));
            violations.Add("provider address transition resurrected erased PII");
        }
        catch (InvalidOperationException)
        {
        }
        if (location.Pii is not null || location.LocationPrivacyStateId != (int)LocationPrivacyStateEnum.Erased)
        {
            violations.Add("erasure tombstone or PII absence changed during a public address transition");
        }

        await Assert.That(violations).IsEmpty();
    }

    private static IEnumerable<string> NamedPublicProperties(Type type, params string[] names) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => names.Contains(property.Name, StringComparer.Ordinal))
            .Select(property => $"{type.Name}.{property.Name}");

    private static IEnumerable<string> PublicSetterViolations(Type type, params string[] names) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => names.Contains(property.Name, StringComparer.Ordinal))
            .Where(property => property.SetMethod?.IsPublic == true)
            .Select(property => $"{type.Name}.{property.Name} setter");

    private static IEnumerable<string> PublicMethodViolations(Type type, string name) =>
        type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
            .Where(method => method.Name == name)
            .Select(method => $"{type.Name}.{FormatMethodSignature(method)}");

    private static MethodInfo[] FindPublicProviderTransitions() => typeof(Location)
        .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
        .Where(method => method.Name == ProviderTransitionName)
        .ToArray();

    private static bool IsExpectedProviderTransition(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        return !method.IsStatic
            && parameters.Length == 3
            && parameters[0].ParameterType == typeof(string)
            && parameters[1].ParameterType == typeof(string)
            && parameters[2].ParameterType == typeof(GeoCoordinate);
    }

    private static string FormatMethodSignature(MethodInfo method) =>
        $"{method.Name}({string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name))})";

    private static bool CoordinateRejected(double latitude, double longitude)
    {
        try
        {
            GeoCoordinate.Create(latitude, longitude);
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static CreateLocationCommandHandler CreateLocationHandler(
        ILocationRepository locations,
        ITenantContext tenantContext)
    {
        var userContext = Substitute.For<IUserContext>();
        Guid userId = Guid.CreateVersion7();
        userContext.GetRequiredUserId().Returns(userId);
        var governance = Substitute.For<IAddressGovernancePolicyResolver>();
        governance.ResolveAsync(Arg.Any<AddressGovernancePolicyRequest>(), Arg.Any<CancellationToken>())
            .Returns(AddressGovernancePolicyDecision.Allowed(
                AddressCreationMode.OpenWithModeration,
                LocationAddressVisibilityEnum.CreatorPrivate));
        return new CreateLocationCommandHandler(
            locations,
            Substitute.For<IAddressSelectionProtector>(),
            tenantContext,
            userContext,
            governance,
            TimeProvider.System);
    }

    private static UpdateLocationCommandHandler CreateUpdateLocationHandler(
        ILocationRepository locations,
        Guid tenantId)
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(tenantId);
        var userContext = Substitute.For<IUserContext>();
        Guid userId = Guid.CreateVersion7();
        userContext.GetRequiredUserId().Returns(userId);
        var governance = Substitute.For<IAddressGovernancePolicyResolver>();
        governance.ResolveAsync(Arg.Any<AddressGovernancePolicyRequest>(), Arg.Any<CancellationToken>())
            .Returns(AddressGovernancePolicyDecision.Allowed(
                AddressCreationMode.OpenWithModeration,
                LocationAddressVisibilityEnum.CreatorPrivate));
        return new UpdateLocationCommandHandler(
            locations,
            Substitute.For<IAddressSelectionProtector>(),
            tenantContext,
            userContext,
            governance,
            TimeProvider.System);
    }

    private static CreateLocationDto ManualCreateDto() => new()
    {
        FullName = "Manual venue",
        Address = "Rue Manual 20",
        Postcode = "1000",
        Country = "Belgium",
        City = "Brussels"
    };

    private static Location NewLocation(Guid tenantId, double? latitude = null, double? longitude = null)
    {
        var location = new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = NewTenant(tenantId),
            FullName = "Manual venue",
            Country = "Belgium",
            City = "Brussels",
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        if (latitude is { } exactLatitude && longitude is { } exactLongitude)
        {
            location.SetProviderAddress(
                "Rue Existing 10",
                "1000",
                Explore.Domain.ValueObjects.GeoCoordinate.Create(exactLatitude, exactLongitude));
        }
        else
        {
            location.SetManualAddress("Rue Existing 10", "1000");
        }
        return location;
    }

    private static IMapper CreateRealMapper()
    {
#if USE_COMMERCIAL_LUCKYPENNY_LIBS
        var configuration = new MapperConfiguration(
            expression => expression.AddProfile<LookupMappingProfile>(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
#else
        var configuration = new MapperConfiguration(expression => expression.AddProfile<LookupMappingProfile>());
#endif
        return configuration.CreateMapper();
    }

    private static Tenant NewTenant(Guid tenantId) => new()
    {
        Id = tenantId,
        FullName = "Test tenant",
        Slug = $"test-tenant-{tenantId:N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = new TenantStatus
        {
            Id = (int)TenantStatusEnum.Active,
            MasterCode = "ACTIVE",
            FullName = "Active",
            IsActiveState = true
        }
    };
}
