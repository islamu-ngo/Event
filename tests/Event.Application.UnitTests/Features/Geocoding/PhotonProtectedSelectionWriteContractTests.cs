using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.Locations.Handlers.Commands;

namespace Event.Application.UnitTests.Features.Geocoding;

public sealed class PhotonProtectedSelectionWriteContractTests
{
    private const string ContractNamespace = "Contracts.Infrastructure.Geocoding.";

    [Test]
    public async Task CreateAndPatchAcceptOneOpaqueAddressSelectionToken()
    {
        var createToken = PhotonApplicationContractAssertions.RequireProperty(
            typeof(CreateLocationDto),
            "AddressSelectionToken",
            "Location create must accept optional protected selection authority");
        var patchToken = PhotonApplicationContractAssertions.RequireProperty(
            typeof(UpdateLocationDto),
            "AddressSelectionToken",
            "Location PATCH must accept optional protected selection authority");

        await Assert.That(createToken.PropertyType).IsEqualTo(typeof(string));
        await Assert.That(patchToken.PropertyType).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task BrowserWriteDtosNeverAcceptProviderFactsOrCoordinates()
    {
        Type[] browserDtos = [typeof(CreateLocationDto), typeof(UpdateLocationDto)];
        string[] forbidden =
        [
            "Provider", "ProviderRecord", "ProviderRecordId", "ProviderConfigVersion",
            "ConfigurationFingerprint", "PersistenceProfile", "DatasetKey", "DatasetVersion",
            "Attribution", "Latitude", "Longitude", "Coordinates", "Provenance"
        ];
        string[] violations = browserDtos
            .SelectMany(type => PhotonApplicationContractAssertions.PublicPropertyNames(type)
                .Intersect(forbidden, StringComparer.Ordinal)
                .Select(name => $"{type.Name}.{name}"))
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task UnprotectContextBindsTrustedActorScopePurposeTargetAndConfiguration()
    {
        Type protector = Contract(
            "IAddressSelectionProtector",
            "selection tokens must be unprotected through one Application-owned boundary");
        Type context = Contract(
            "AddressSelectionContext",
            "unprotect must receive server-trusted binding context");
        var unprotect = PhotonApplicationContractAssertions.RequireMethod(
            protector,
            "UnprotectAsync",
            "selection unprotect must be cancellable and complete before persistence");
        Type target = PhotonApplicationContractAssertions.RequireProperty(
            context,
            "Target",
            "selection context must bind create/update target semantics").PropertyType;
        string[] contextFields =
        [
            "TenantId", "ActorId", "OrganizationId", "Purpose", "ConfigurationFingerprint"
        ];

        foreach (string field in contextFields)
        {
            _ = PhotonApplicationContractAssertions.RequireProperty(
                context,
                field,
                "selection context must reject cross-context token use");
        }
        _ = PhotonApplicationContractAssertions.RequireProperty(
            target,
            "LocationId",
            "update selections must bind the target Location");
        _ = PhotonApplicationContractAssertions.RequireProperty(
            target,
            "ExpectedConcurrencyStamp",
            "update selections must bind the expected Location version");
        await Assert.That(unprotect.GetParameters().Any(parameter => parameter.ParameterType == context))
            .IsTrue();
        await Assert.That(unprotect.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(CancellationToken)))
            .IsTrue();
    }

    [Test]
    public async Task SelectionFailuresRemainBoundedAndFailClosedWithoutMismatchOracle()
    {
        Type failures = Contract(
            "AddressSelectionFailureCode",
            "protected selection rejection must use bounded internal categories");
        string[] names = PhotonApplicationContractAssertions.EnumNames(
            failures,
            "protected selection failure codes must be machine values");

        await Assert.That(names).Contains("Invalid");
        await Assert.That(names).Contains("Expired");
        await Assert.That(names.Length).IsLessThanOrEqualTo(12);
    }

    [Test]
    public void CreateAndPatchUnprotectBeforeRepositoryWrite()
    {
        Type protector = Contract(
            "IAddressSelectionProtector",
            "Location writes must validate protected selections before persistence");

        PhotonApplicationContractAssertions.RequireConstructorDependency(
            typeof(CreateLocationCommandHandler),
            protector,
            "Location create must own protected selection validation");
        PhotonApplicationContractAssertions.RequireConstructorDependency(
            typeof(UpdateLocationCommandHandler),
            protector,
            "Location PATCH must own protected selection validation");
        PhotonApplicationContractAssertions.RequireAsyncCallBefore(
            typeof(CreateLocationCommandHandler),
            protector,
            "UnprotectAsync",
            typeof(ILocationRepository),
            "Create",
            "token validation must complete before the create write");
        PhotonApplicationContractAssertions.RequireAsyncCallBefore(
            typeof(UpdateLocationCommandHandler),
            protector,
            "UnprotectAsync",
            typeof(ILocationRepository),
            "Update",
            "token validation must complete before the update write");
    }

    [Test]
    public async Task ProtectedSelectionCarriesAtomicBundleAndOpaquePrivateProvenance()
    {
        Type selection = Contract(
            "ProtectedAddressSelection",
            "the unprotected value must be one normalized provider selection");
        string[] required =
        [
            "DisplayName", "Address", "Postcode", "City", "Country", "Timezone",
            "Latitude", "Longitude", "Attribution", "Provenance"
        ];
        string[] forbiddenTopLevel =
        [
            "ProviderRecord", "ProviderRecordId", "DatasetKey", "DatasetVersion",
            "ProviderConfigVersion", "PersistenceProfile"
        ];

        foreach (string field in required)
        {
            _ = PhotonApplicationContractAssertions.RequireProperty(
                selection,
                field,
                "provider selection persistence must be complete and atomic");
        }

        await Assert.That(PhotonApplicationContractAssertions.PublicPropertyNames(selection)
                .Intersect(forbiddenTopLevel, StringComparer.Ordinal))
            .IsEmpty();
    }

    [Test]
    public async Task ManualWritesNeverDependOnOutboundGeocoder()
    {
        Type protector = Contract(
            "IAddressSelectionProtector",
            "manual and protected writes must be explicitly distinguishable");
        Type gateway = Contract(
            "IAddressSuggestionProviderGateway",
            "provider search must remain outside Location writes");
        Type[] handlers = [typeof(CreateLocationCommandHandler), typeof(UpdateLocationCommandHandler)];

        foreach (Type handler in handlers)
        {
            bool directlyCallsProvider = handler.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => parameter.ParameterType == gateway);
            await Assert.That(directlyCallsProvider).IsFalse();
            PhotonApplicationContractAssertions.RequireConstructorDependency(
                handler,
                protector,
                "the handler must distinguish token omission from a protected selection");
        }
    }

    private static Type Contract(string name, string behavior) =>
        PhotonApplicationContractAssertions.RequireType($"{ContractNamespace}{name}", behavior);
}
