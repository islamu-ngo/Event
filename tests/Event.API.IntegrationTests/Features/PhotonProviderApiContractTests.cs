using System.Reflection;
using Explore.API.Controllers;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.DTOs.Geocoding;
using Explore.Application.DTOs.Location;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using TUnit.Assertions.Enums;

namespace Event.Api.IntegrationTests.Features;

public sealed class PhotonProviderApiContractTests
{
    private static readonly Assembly ApplicationAssembly = typeof(AddressSuggestionDto).Assembly;

    [Test]
    public async Task AddressSuggestionsRemainsOneAuthenticatedPrivateRateLimitedPost()
    {
        Type response = RequireApplicationType(
            "Explore.Application.DTOs.Geocoding.AddressSuggestionsResponseDto",
            "the endpoint must return merged suggestions and typed provider outcome");
        MethodInfo action = typeof(GeocodingController).GetMethod(
            nameof(GeocodingController.GetAddressSuggestions))
            ?? throw Red("the address-suggestion POST action is missing");
        var post = action.GetCustomAttribute<HttpPostAttribute>();
        Type? successType = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == StatusCodes.Status200OK)
            .Type;

        await Assert.That(post?.Template).IsEqualTo("address-suggestions");
        await Assert.That(post?.Name).IsEqualTo(RouteNames.GetAddressSuggestions);
        await Assert.That(typeof(GeocodingController).GetCustomAttribute<AuthorizeAttribute>())
            .IsNotNull();
        await Assert.That(action.GetCustomAttribute<PrivateNoStoreAttribute>()).IsNotNull();
        await Assert.That(action.GetCustomAttribute<OutputCacheAttribute>()).IsNull();
        await Assert.That(action.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo("AddressSuggestions");
        await Assert.That(successType?.GenericTypeArguments).Contains(response);
    }

    [Test]
    public async Task ProviderOutcomeIsTypedResponseDataAlongsideMergedSuggestions()
    {
        Type response = RequireApplicationType(
            "Explore.Application.DTOs.Geocoding.AddressSuggestionsResponseDto",
            "provider degradation must not become an HTTP or UI exception protocol");
        PropertyInfo suggestions = RequireProperty(response, "Suggestions");
        PropertyInfo outcome = RequireProperty(response, "ProviderOutcome");

        await Assert.That(suggestions.PropertyType.IsAssignableTo(typeof(System.Collections.IEnumerable)))
            .IsTrue();
        await Assert.That(Enum.GetNames(outcome.PropertyType)).IsEquivalentTo(
            ["None", "Ready", "Timeout", "Unavailable", "Limited"],
            CollectionOrdering.Matching);
    }

    [Test]
    public async Task BrowserSuggestionContainsOpaqueSelectionWithoutProviderInternals()
    {
        string[] required = ["Source", "SelectionToken", "SelectionExpiresAt", "Attribution"];
        string[] forbidden =
        [
            "Latitude", "Longitude", "Coordinates", "Provider", "ProviderRecord",
            "ProviderRecordId", "ProviderResponse", "ProviderQuery", "ProviderConfigVersion",
            "ConfigurationFingerprint", "PersistenceProfile", "DatasetKey", "DatasetVersion"
        ];

        foreach (string field in required)
        {
            _ = RequireProperty(typeof(AddressSuggestionDto), field);
        }

        await Assert.That(typeof(AddressSuggestionDto).GetProperties()
                .Select(property => property.Name)
                .Intersect(forbidden, StringComparer.Ordinal))
            .IsEmpty();
        await Assert.That(typeof(AddressSuggestionDto).GetProperty("LocationId")?.PropertyType)
            .IsEqualTo(typeof(Guid?));
    }

    [Test]
    public async Task ProviderFailurePreservesSuggestionsAndExposesNoUpstreamDetails()
    {
        Type response = RequireApplicationType(
            "Explore.Application.DTOs.Geocoding.AddressSuggestionsResponseDto",
            "provider failure must preserve eligible local suggestions");
        string[] forbidden =
        [
            "Query", "RequestUri", "ProviderUri", "ProviderBody", "ProviderResponse",
            "RawError", "Exception", "RetryAfterText"
        ];

        _ = RequireProperty(response, "Suggestions");
        _ = RequireProperty(response, "ProviderOutcome");
        await Assert.That(response.GetProperties().Select(property => property.Name)
                .Intersect(forbidden, StringComparer.Ordinal))
            .IsEmpty();
    }

    [Test]
    public async Task HalPublishesExactlyOneExecutableAddressSuggestionsRelation()
    {
        FieldInfo relation = typeof(LinkRelations).GetField(
            "AddressSuggestions",
            BindingFlags.Public | BindingFlags.Static)
            ?? throw Red("HAL must expose the merged private address-suggestions operation");
        FieldInfo? providerRelation = typeof(LinkRelations).GetField(
            "ProviderAddressSuggestions",
            BindingFlags.Public | BindingFlags.Static);
        LinkDefinition[] advertised = new AddressSuggestionCollectionLinkPolicy()
            .GetCollectionLinks(user: null)
            .Where(link => link.Rel == "address-suggestions")
            .ToArray();

        await Assert.That(relation.GetRawConstantValue()).IsEqualTo("address-suggestions");
        await Assert.That(advertised).HasSingleItem();
        await Assert.That(advertised[0].RouteName).IsEqualTo(RouteNames.GetAddressSuggestions);
        await Assert.That(advertised[0].Method).IsEqualTo("POST");
        await Assert.That(providerRelation).IsNull();
    }

    [Test]
    public async Task LocationCollectionPublishesAddressSuggestionEntrypoint()
    {
        LinkDefinition[] advertised = new LocationCollectionLinkPolicy()
            .GetCollectionLinks(user: null)
            .Where(link => link.Rel == "address-suggestions")
            .ToArray();

        await Assert.That(advertised).HasSingleItem();
        await Assert.That(advertised[0].RouteName)
            .IsEqualTo(RouteNames.GetAddressSuggestions);
        await Assert.That(advertised[0].Method).IsEqualTo("POST");
        await Assert.That(advertised[0].RequiresAuth).IsTrue();
    }

    [Test]
    public async Task LocationCollectionItemsPublishAuthorizedWriteCapabilities()
    {
        var item = new LocationListDto
        {
            Id = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7(),
            FullName = "Synthetic Hall",
            Address = "Synthetic address",
            City = "Synthetic city",
            Country = "Synthetic country"
        };
        LinkDefinition[] links = new LocationCollectionLinkPolicy()
            .GetItemLinks(item, user: null)
            .Where(link => link.Rel is "edit" or "delete")
            .ToArray();

        await Assert.That(links).Count().IsEqualTo(2);
        await Assert.That(links.All(link => link.RequiresAuth)).IsTrue();
        await Assert.That(links.All(link => link.PermissionAction is not null))
            .IsTrue();
    }

    [Test]
    public async Task HalSearchPolicyDoesNotDependOnConcreteProviderAvailability()
    {
        Type policy = typeof(AddressSuggestionCollectionLinkPolicy);
        Type[] dependencies = policy.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        string[] dtoAuthorityFlags = typeof(AddressSuggestionDto).GetProperties()
            .Select(property => property.Name)
            .Intersect(
                ["ProviderReady", "ProviderEnabled", "CanUseProvider", "CanSearch"],
                StringComparer.Ordinal)
            .ToArray();

        await Assert.That(dependencies.Any(type =>
                type.Name.Contains("ProviderAvailability", StringComparison.Ordinal)))
            .IsFalse();
        await Assert.That(dtoAuthorityFlags).IsEmpty();
    }

    [Test]
    public async Task RequestBodyContainsSearchScopeButNoProviderAuthority()
    {
        string[] fields = typeof(AddressSuggestionsRequestDto).GetProperties()
            .Select(property => property.Name)
            .ToArray();
        string[] forbidden =
        [
            "Provider", "ProviderMode", "Endpoint", "Credential", "ApiKey", "TenantId",
            "Latitude", "Longitude", "Coordinates"
        ];

        await Assert.That(fields).Contains("SearchText");
        await Assert.That(fields).Contains("Limit");
        await Assert.That(fields).Contains("OrganizationId");
        await Assert.That(fields.Intersect(forbidden, StringComparer.Ordinal)).IsEmpty();
    }

    private static Type RequireApplicationType(string fullName, string reason) =>
        ApplicationAssembly.GetType(fullName, throwOnError: false)
        ?? throw Red($"{reason}; missing production contract '{fullName}'");

    private static PropertyInfo RequireProperty(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
        ?? throw Red($"{type.FullName} must expose machine field '{name}'");

    private static InvalidOperationException Red(string reason) =>
        new($"RED - absent optional-provider API integration: {reason}.");
}
