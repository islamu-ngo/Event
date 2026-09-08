using System.Reflection;
using AngleSharp.Dom;
using Explore.Blazor.Client.Components.Locations;

namespace Explore.Blazor.Client.Tests.Components.Location;

public sealed class PhotonAutocompleteUiContractTests : IDisposable
{
    private const string Attribution = "required-attribution-canary";
    private readonly BlazorTestContext _ctx = new();
    private readonly PhotonTypedSuggestionServiceProxy _proxy;

    public PhotonAutocompleteUiContractTests()
    {
        object service = PhotonTypedSuggestionServiceProxy.Create(out _proxy);
        _ctx.Services.AddSingleton(typeof(IAddressSuggestionService), service);
    }

    [Test]
    public async Task GeneratedSuggestionContainsOpaqueSelectionWithoutProviderInternals()
    {
        Type dto = typeof(HalResourceOfAddressSuggestionDto);
        string[] required = ["Source", "SelectionToken", "SelectionExpiresAt", "Attribution"];
        string[] forbidden =
        [
            "Latitude", "Longitude", "Coordinates", "Provider", "ProviderRecord",
            "ProviderRecordId", "ProviderResponse", "ProviderQuery", "ProviderConfigVersion",
            "ConfigurationFingerprint", "PersistenceProfile", "DatasetKey", "DatasetVersion"
        ];

        foreach (string field in required)
        {
            _ = RequireProperty(dto, field, "generated suggestions need browser-safe selection data");
        }

        await Assert.That(dto.GetProperties().Select(property => property.Name)
                .Intersect(forbidden, StringComparer.Ordinal))
            .IsEmpty();
    }

    [Test]
    public async Task SuggestionServiceReturnsTypedSuggestionsAndProviderOutcome()
    {
        MethodInfo search = typeof(IAddressSuggestionService).GetMethod(
            nameof(IAddressSuggestionService.SearchAsync))
            ?? throw Red("address suggestion service search is missing");
        Type result = RequireTaskResult(search.ReturnType);
        _ = RequireProperty(result, "Suggestions", "UI search result must preserve merged rows");
        PropertyInfo outcome = RequireProperty(
            result,
            "ProviderOutcome",
            "provider state must be typed response data instead of exception text");

        await Assert.That(outcome.PropertyType.IsEnum).IsTrue();
    }

    [Test]
    public async Task ProviderSuggestionRendersResponseAttribution()
    {
        HalResourceOfAddressSuggestionDto suggestion = ProviderSuggestion();
        SetRequired(suggestion, "Attribution", Attribution);
        _proxy.Configure([suggestion], "Ready");
        IRenderedComponent<AddressSuggestionCombobox> cut = Render();

        await cut.Find("[data-testid='address-suggestion-input']")
            .InputAsync(new ChangeEventArgs { Value = "provider" });

        IElement attribution = RequireElement(
            cut,
            "[data-provider-attribution]",
            "provider suggestions must render response attribution");
        await Assert.That(attribution.GetAttribute("data-provider-attribution"))
            .IsEqualTo(Attribution);

        cut.Find("[role='option']").Click();

        IElement selectedAttribution = RequireElement(
            cut,
            "[data-selected-provider-attribution]",
            "selected provider address must retain visible attribution");
        await Assert.That(
                selectedAttribution.GetAttribute(
                    "data-selected-provider-attribution"))
            .IsEqualTo(Attribution);
    }

    [Test]
    [Arguments("Unavailable", "unavailable")]
    [Arguments("Limited", "limited")]
    public async Task ProviderFailurePreservesTypedInputAndRendersTypedOutcome(
        string responseOutcome,
        string expectedOutcome)
    {
        const string TypedInput = "typed-private-address";
        _proxy.Configure([], responseOutcome);
        IRenderedComponent<AddressSuggestionCombobox> cut = Render();
        IElement input = cut.Find("[data-testid='address-suggestion-input']");

        await input.InputAsync(new ChangeEventArgs { Value = TypedInput });

        await Assert.That(input.GetAttribute("value")).IsEqualTo(TypedInput);
        IElement status = RequireElement(
            cut,
            "[data-provider-outcome]",
            $"provider {expectedOutcome} must be rendered from typed response state");
        await Assert.That(status.GetAttribute("data-provider-outcome"))
            .IsEqualTo(expectedOutcome);
    }

    [Test]
    public async Task ProviderNoneKeepsLocalResultsAndHealthyMachineState()
    {
        _proxy.Configure([LocalSuggestion()], "None");
        IRenderedComponent<AddressSuggestionCombobox> cut = Render();

        await cut.Find("[data-testid='address-suggestion-input']")
            .InputAsync(new ChangeEventArgs { Value = "local" });

        await Assert.That(cut.FindAll("[role='option']")).HasSingleItem();
        IElement status = RequireElement(
            cut,
            "[data-provider-outcome='none']",
            "Provider=None must remain healthy while local results are visible");
        await Assert.That(status.GetAttribute("data-provider-outcome")).IsEqualTo("none");
    }

    [Test]
    public async Task AutocompleteInvocationRequiresTheSingleAdvertisedHalRelation()
    {
        PropertyInfo searchLink = RequireProperty(
            typeof(AddressSuggestionCombobox),
            "SearchLink",
            "autocomplete must receive the server-advertised address-suggestions relation");
        MethodInfo search = typeof(IAddressSuggestionService).GetMethod(
            nameof(IAddressSuggestionService.SearchAsync))
            ?? throw Red("address suggestion service search is missing");

        await Assert.That(searchLink.PropertyType).IsEqualTo(typeof(HalLink));
        await Assert.That(search.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(HalLink)))
            .IsTrue();
    }

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<AddressSuggestionCombobox> Render()
    {
        RenderFragment fragment = builder =>
        {
            builder.OpenComponent<AddressSuggestionCombobox>(0);
            builder.AddAttribute(
                1,
                "SearchLink",
                new HalLink { Href = "/api/geocoding/address-suggestions", Method = "POST" });
            builder.CloseComponent();
        };
        return _ctx.Render<AddressSuggestionCombobox>(fragment);
    }

    private static HalResourceOfAddressSuggestionDto ProviderSuggestion() => new()
    {
        LocationId = Guid.Empty,
        ConcurrencyStamp = Guid.Empty,
        DisplayName = "Provider venue",
        Address = "Provider address",
        Postcode = "0000",
        Source = LocationAddressSourceEnum.ProviderSelection,
        Visibility = LocationAddressVisibilityEnum.Quarantined,
        _links = new Dictionary<string, HalLink>()
    };

    private static HalResourceOfAddressSuggestionDto LocalSuggestion() => new()
    {
        LocationId = Guid.CreateVersion7(),
        ConcurrencyStamp = Guid.CreateVersion7(),
        DisplayName = "Local venue",
        Address = "Local address",
        Postcode = "0000",
        Source = LocationAddressSourceEnum.Manual,
        Visibility = LocationAddressVisibilityEnum.TenantApproved,
        _links = new Dictionary<string, HalLink>()
    };

    private static Type RequireTaskResult(Type taskType) =>
        taskType.IsGenericType && taskType.GetGenericTypeDefinition() == typeof(Task<>)
            ? taskType.GenericTypeArguments[0]
            : throw Red("address suggestion search must return Task<TypedResult>");

    private static PropertyInfo RequireProperty(Type type, string name, string reason) =>
        type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
        ?? throw Red($"{reason}; {type.FullName} is missing '{name}'");

    private static void SetRequired(object target, string name, object value) =>
        RequireProperty(target.GetType(), name, $"provider UI cannot consume {name}")
            .SetValue(target, value);

    private static IElement RequireElement(
        IRenderedComponent<AddressSuggestionCombobox> cut,
        string selector,
        string reason) => cut.FindAll(selector).SingleOrDefault() ?? throw Red(reason);

    private static InvalidOperationException Red(string reason) =>
        new($"RED - absent typed optional-provider Blazor integration: {reason}.");
}
