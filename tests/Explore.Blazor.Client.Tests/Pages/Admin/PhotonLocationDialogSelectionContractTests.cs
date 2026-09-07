using System.Reflection;
using AngleSharp.Dom;
using Explore.Blazor.Client.Pages.Admin.Dialogs;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class PhotonLocationDialogSelectionContractTests : IDisposable
{
    private const string Token = "opaque-selection-token-canary";
    private static readonly Guid OrganizationId = Guid.CreateVersion7();
    private readonly BlazorTestContext _ctx = new();
    private readonly PhotonTypedSuggestionServiceProxy _suggestions;

    public PhotonLocationDialogSelectionContractTests()
    {
        object service = PhotonTypedSuggestionServiceProxy.Create(out _suggestions);
        _ctx.Services.AddSingleton(typeof(IAddressSuggestionService), service);
    }

    [Test]
    public async Task GeneratedWriteDtosExposeOnlyOpaqueSelectionToken()
    {
        Type[] writeDtos = [typeof(CreateLocationDto), typeof(UpdateLocationDto)];
        string[] forbidden =
        [
            "Provider", "ProviderRecord", "ProviderRecordId", "ProviderConfigVersion",
            "ConfigurationFingerprint", "PersistenceProfile", "DatasetKey", "DatasetVersion",
            "Attribution", "Latitude", "Longitude", "Coordinates", "Provenance"
        ];

        foreach (Type dto in writeDtos)
        {
            PropertyInfo token = RequireProperty(
                dto,
                "AddressSelectionToken",
                "generated Location writes must carry opaque protected selection authority");
            await Assert.That(token.PropertyType).IsEqualTo(typeof(string));
        }

        await Assert.That(writeDtos.SelectMany(type => type.GetProperties())
                .Select(property => property.Name)
                .Intersect(forbidden, StringComparer.Ordinal))
            .IsEmpty();
    }

    [Test]
    public async Task CreateDialogUnchangedSelectionSubmitsOpaqueToken()
    {
        var (host, reference) = await RenderCreateAsync(ProviderSuggestion());

        await SelectAsync(host, "create-location-address-suggestion");
        Task<DialogResult?> resultSignal = reference.Result;
        host.Find("[data-testid='create-location-submit']").Click();
        CreateLocationDto result = DialogData<CreateLocationDto>(
            await resultSignal.WaitAsync(TimeSpan.FromSeconds(3)));

        await Assert.That(ReadToken(result)).IsEqualTo(Token);
        await Assert.That(result.OrganizationId).IsEqualTo(OrganizationId);
    }

    [Test]
    [Arguments("full-name", "Manual venue")]
    [Arguments("address", "Manual address")]
    [Arguments("postcode", "9999")]
    [Arguments("city", "Manual city")]
    [Arguments("country", "Manual country")]
    [Arguments("timezone", "Etc/UTC")]
    public async Task CreateDialogVisibleTokenBoundChangeRemovesStaleToken(
        string field,
        string replacement)
    {
        var (host, reference) = await RenderCreateAsync(ProviderSuggestion());
        await SelectAsync(host, "create-location-address-suggestion");
        host.Find($"[data-testid='create-location-{field}']").Change(replacement);

        Task<DialogResult?> resultSignal = reference.Result;
        host.Find("[data-testid='create-location-submit']").Click();
        CreateLocationDto result = DialogData<CreateLocationDto>(
            await resultSignal.WaitAsync(TimeSpan.FromSeconds(3)));

        await Assert.That(ReadToken(result)).IsNull();
    }

    [Test]
    public async Task EditDialogUnchangedSelectionSubmitsOpaqueToken()
    {
        var (host, reference) = await RenderEditAsync(ProviderSuggestion());

        await SelectAsync(host, "edit-location-address-suggestion");
        Task<DialogResult?> resultSignal = reference.Result;
        host.Find("[data-testid='edit-location-submit']").Click();
        UpdateLocationDto result = DialogData<UpdateLocationDto>(
            await resultSignal.WaitAsync(TimeSpan.FromSeconds(3)));

        await Assert.That(ReadToken(result)).IsEqualTo(Token);
        await Assert.That(result.OrganizationId).IsEqualTo(OrganizationId);
    }

    [Test]
    [Arguments("full-name", "Manual venue")]
    [Arguments("address", "Manual address")]
    [Arguments("postcode", "9999")]
    [Arguments("city", "Manual city")]
    [Arguments("country", "Manual country")]
    [Arguments("timezone", "Etc/UTC")]
    public async Task EditDialogVisibleTokenBoundChangeRemovesStaleToken(
        string field,
        string replacement)
    {
        var (host, reference) = await RenderEditAsync(ProviderSuggestion());
        await SelectAsync(host, "edit-location-address-suggestion");
        host.Find($"[data-testid='edit-location-{field}']").Change(replacement);

        Task<DialogResult?> resultSignal = reference.Result;
        host.Find("[data-testid='edit-location-submit']").Click();
        UpdateLocationDto result = DialogData<UpdateLocationDto>(
            await resultSignal.WaitAsync(TimeSpan.FromSeconds(3)));

        await Assert.That(ReadToken(result)).IsNull();
    }

    public void Dispose() => _ctx.Dispose();

    private async Task<(IRenderedComponent<MudDialogProvider>, IDialogReference)> RenderCreateAsync(
        HalResourceOfAddressSuggestionDto suggestion)
    {
        ConfigureSuggestion(suggestion);
        var host = _ctx.Render<MudDialogProvider>();
        var parameters = new DialogParameters<CreateLocationDialog>
        {
            { component => component.SearchLink, AddressSuggestionLink() },
            { component => component.OrganizationId, OrganizationId }
        };
        IDialogReference reference = await _ctx.Services.GetRequiredService<IDialogService>()
            .ShowAsync<CreateLocationDialog>("Create location", parameters);
        return (host, reference);
    }

    private async Task<(IRenderedComponent<MudDialogProvider>, IDialogReference)> RenderEditAsync(
        HalResourceOfAddressSuggestionDto suggestion)
    {
        ConfigureSuggestion(suggestion);
        var parameters = new DialogParameters
        {
            ["ExistingLocation"] = ExistingLocation(),
            ["SearchLink"] = AddressSuggestionLink(),
            ["OrganizationId"] = OrganizationId
        };
        var host = _ctx.Render<MudDialogProvider>();
        IDialogReference reference = await _ctx.Services.GetRequiredService<IDialogService>()
            .ShowAsync<EditLocationDialog>("Edit location", parameters);
        return (host, reference);
    }

    private void ConfigureSuggestion(HalResourceOfAddressSuggestionDto suggestion) =>
        _suggestions.Configure([suggestion], "Ready");

    private static async Task SelectAsync(
        IRenderedComponent<MudDialogProvider> host,
        string componentId)
    {
        IElement input = host.Find($"#{componentId}-input");
        await input.InputAsync(new ChangeEventArgs { Value = "provider" });
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
    }

    private static HalResourceOfAddressSuggestionDto ProviderSuggestion()
    {
        var suggestion = new HalResourceOfAddressSuggestionDto
        {
            LocationId = Guid.Empty,
            ConcurrencyStamp = Guid.Empty,
            DisplayName = "Provider venue",
            Address = "Provider address",
            Postcode = "1000",
            Source = LocationAddressSourceEnum.ProviderSelection,
            Visibility = LocationAddressVisibilityEnum.Quarantined,
            _links = new Dictionary<string, HalLink>()
        };
        SetRequired(suggestion, "SelectionToken", Token);
        SetRequired(suggestion, "SelectionExpiresAt", new DateTimeOffset(2026, 8, 26, 12, 5, 0, TimeSpan.Zero));
        SetRequired(suggestion, "Attribution", "required-attribution-canary");
        SetRequired(suggestion, "City", "Brussels");
        SetRequired(suggestion, "Country", "Belgium");
        SetRequired(suggestion, "Timezone", "Europe/Brussels");
        return suggestion;
    }

    private static LocationDto ExistingLocation() => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = "Existing venue",
        Address = "Existing address",
        Postcode = "1000",
        City = "Brussels",
        Country = "Belgium",
        Timezone = "Europe/Brussels",
        ConcurrencyStamp = Guid.CreateVersion7(),
        LocationKindId = 1
    };

    private static HalLink AddressSuggestionLink() => new()
    {
        Href = "/api/geocoding/address-suggestions",
        Method = "POST"
    };

    private static string? ReadToken(object dto) =>
        RequireProperty(dto.GetType(), "AddressSelectionToken", "dialog result needs a token field")
            .GetValue(dto) as string;

    private static void SetRequired(object target, string name, object value) =>
        RequireProperty(target.GetType(), name, $"generated suggestion must expose {name}")
            .SetValue(target, value);

    private static PropertyInfo RequireProperty(Type type, string name, string reason) =>
        type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
        ?? throw Red($"{reason}; {type.FullName} is missing '{name}'");

    private static T DialogData<T>(DialogResult? result) =>
        (T)(result?.Data ?? throw Red("dialog result contained no submitted DTO"));

    private static InvalidOperationException Red(string reason) =>
        new($"RED - absent protected-selection dialog integration: {reason}.");
}
