using Explore.Application.DTOs.Geocoding;
using Explore.Application.Features.Geocoding.Handlers.Queries;
using TUnit.Assertions.Enums;

namespace Event.Application.UnitTests.Features.Geocoding;

public sealed class PhotonProviderQueryContractTests
{
    private const string ContractNamespace = "Contracts.Infrastructure.Geocoding.";

    [Test]
    public async Task ApplicationUsesOneSemanticOptionalProviderGatewayWithoutConcreteMode()
    {
        Type gateway = Contract(
            "IAddressSuggestionProviderGateway",
            "Application must own one semantic optional-provider boundary");
        var search = PhotonApplicationContractAssertions.RequireMethod(
            gateway,
            "SearchAsync",
            "the optional provider gateway must expose one search operation");
        Type? concreteMode = typeof(AddressSuggestionDto).Assembly.GetType(
            $"Explore.Application.{ContractNamespace}GeocodingProvider",
            throwOnError: false);

        PhotonApplicationContractAssertions.RequireConstructorDependency(
            typeof(GetAddressSuggestionsQueryHandler),
            gateway,
            "the query handler must compose local rows with the semantic optional provider gateway");
        await Assert.That(concreteMode).IsNull();
        await Assert.That(search.GetParameters().Any(parameter => parameter.ParameterType.IsEnum))
            .IsFalse();
        await Assert.That(search.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(CancellationToken)))
            .IsTrue();
    }

    [Test]
    public async Task MergedResponseCarriesSuggestionsAndTypedProviderOutcome()
    {
        Type response = PhotonApplicationContractAssertions.RequireType(
            "DTOs.Geocoding.AddressSuggestionsResponseDto",
            "local and optional-provider rows must merge at the Application response boundary");
        Type outcome = PhotonApplicationContractAssertions.RequireType(
            "DTOs.Geocoding.AddressProviderOutcome",
            "provider state must be typed response data");
        var suggestions = PhotonApplicationContractAssertions.RequireProperty(
            response,
            "Suggestions",
            "the response must retain local rows when the provider is disabled or degraded");
        var providerOutcome = PhotonApplicationContractAssertions.RequireProperty(
            response,
            "ProviderOutcome",
            "provider state must remain independent from the merged collection");

        await Assert.That(suggestions.PropertyType.IsAssignableTo(typeof(System.Collections.IEnumerable)))
            .IsTrue();
        await Assert.That(providerOutcome.PropertyType).IsEqualTo(outcome);
        await Assert.That(PhotonApplicationContractAssertions.EnumNames(
                outcome,
                "provider outcomes must be bounded machine values"))
            .IsEquivalentTo(
                ["None", "Ready", "Timeout", "Unavailable", "Limited"],
                CollectionOrdering.Matching);
    }

    [Test]
    public async Task BrowserSuggestionKeepsSemanticSourceAndOpaqueSelectionOnly()
    {
        Type suggestion = typeof(AddressSuggestionDto);
        string[] required =
        [
            "Source", "Attribution", "SelectionToken", "SelectionExpiresAt"
        ];
        string[] forbidden =
        [
            "Latitude", "Longitude", "Coordinates", "Provider", "ProviderRecord",
            "ProviderRecordId", "RawProviderRecord", "ProviderConfigVersion",
            "ConfigurationFingerprint", "PersistenceProfile", "DatasetKey", "DatasetVersion",
            "ProviderQuery", "ProviderResponse"
        ];

        foreach (string name in required)
        {
            _ = PhotonApplicationContractAssertions.RequireProperty(
                suggestion,
                name,
                "browser suggestions need semantic source, attribution, and opaque selection authority");
        }

        await Assert.That(PhotonApplicationContractAssertions.PublicPropertyNames(suggestion)
                .Intersect(forbidden, StringComparer.Ordinal))
            .IsEmpty();
        await Assert.That(suggestion.GetProperty("LocationId")?.PropertyType)
            .IsEqualTo(typeof(Guid?));
    }

    [Test]
    public async Task ProviderGatewayAcceptsOnlyCurrentSearchIntentAndNeverLocalRows()
    {
        Type gateway = Contract(
            "IAddressSuggestionProviderGateway",
            "Application must own one provider-neutral optional gateway");
        Type request = Contract(
            "AddressGeocoderRequest",
            "outbound search must contain only current explicit intent");
        var search = PhotonApplicationContractAssertions.RequireMethod(
            gateway,
            "SearchAsync",
            "the provider gateway must expose one cancellable search operation");
        string[] forbidden =
        [
            "LocalSuggestions", "Locations", "LocationIds", "StoredAddresses",
            "TenantAddresses", "OrganizationAddresses"
        ];

        await Assert.That(search.GetParameters().Any(parameter => parameter.ParameterType == request))
            .IsTrue();
        await Assert.That(search.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(CancellationToken)))
            .IsTrue();
        await Assert.That(PhotonApplicationContractAssertions.PublicPropertyNames(request)
                .Intersect(forbidden, StringComparer.Ordinal))
            .IsEmpty();
    }

    private static Type Contract(string name, string behavior) =>
        PhotonApplicationContractAssertions.RequireType($"{ContractNamespace}{name}", behavior);
}
