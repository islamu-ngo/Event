using System.Reflection;
using AngleSharp.Dom;
using Explore.Blazor.Client.Pages.Admin.Dialogs;
using Explore.Blazor.Client.Pages.Events.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class EditLocationDialogTests : IDisposable
{
    private static readonly string[] ExpectedInputIds =
    [
        "edit-location-address",
        "edit-location-city",
        "edit-location-country",
        "edit-location-full-name",
        "edit-location-postcode",
        "edit-location-timezone"
    ];

    private readonly BlazorTestContext _ctx = new();
    private readonly IAddressSuggestionService _suggestions =
        Substitute.For<IAddressSuggestionService>();

    public EditLocationDialogTests()
    {
        _suggestions.SearchAsync(
                Arg.Any<string>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                Arg.Any<int>(),
                Arg.Any<HalLink>(),
                Arg.Any<CancellationToken>())
            .Returns(SearchResult());
        _ctx.Services.AddSingleton(_suggestions);
    }

    [Test]
    public async Task RenderAndSubmit_UsesOnlyTrimmedManualGroups()
    {
        var (host, reference) = await RenderDialogAsync();
        string[] inputIds = host.FindAll("[data-testid]")
            .Select(element => element.GetAttribute("data-testid"))
            .Where(id => id is not null
                && id.StartsWith("edit-location-", StringComparison.Ordinal)
                && id != "edit-location-private-home"
                && id != "edit-location-submit")
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(inputIds).IsEquivalentTo(ExpectedInputIds);
        await Assert.That(inputIds.Any(IsRawCoordinateName)).IsFalse();
        await Assert.That(host.FindAll("[data-testid='address-suggestion-input']")).HasSingleItem();
        await Assert.That(host.FindAll("div.edit-location-dialog__manual-fields > form")).HasSingleItem();

        SetInput(host, "edit-location-full-name", "  Updated Hall  ");
        SetInput(host, "edit-location-address", "  20 Manual Avenue  ");
        SetInput(host, "edit-location-city", "  Ghent  ");
        SetInput(host, "edit-location-postcode", "  9000  ");
        SetInput(host, "edit-location-country", "  Belgium  ");
        SetInput(host, "edit-location-timezone", "  Europe/Brussels  ");
        Task<DialogResult?> resultSignal = reference.Result;
        host.Find("[data-testid='edit-location-submit']").Click();

        UpdateLocationDto result = DialogData<UpdateLocationDto>(
            await resultSignal.WaitAsync(TimeSpan.FromSeconds(3)));
        await Assert.That(result.FullName!.Value).IsEqualTo("Updated Hall");
        await Assert.That(result.Address!.Value).IsEqualTo("20 Manual Avenue");
        await Assert.That(result.City!.Value).IsEqualTo("Ghent");
        await Assert.That(result.Postcode!.Value).IsEqualTo("9000");
        await Assert.That(result.Country!.Value).IsEqualTo("Belgium");
        UpdateLocationTimezoneDto timezone = result.Timezone
            ?? throw new InvalidOperationException("Timezone update group was not submitted.");
        OptionalUpdateOfstring timezoneValue = timezone.Value
            ?? throw new InvalidOperationException("Timezone update value was not submitted.");
        await Assert.That(timezoneValue.HasValue).IsTrue();
        await Assert.That(timezoneValue.Value).IsEqualTo("Europe/Brussels");
        await Assert.That(RawCoordinateMembers(result.GetType())).IsEmpty();
    }

    [Test]
    public async Task SavedAddressSelection_UpdatesAvailableFieldsAndPreservesManualEditing()
    {
        _suggestions.SearchAsync(
                "hall",
                Arg.Is<Guid?>(value => value == null),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                10,
                Arg.Any<HalLink>(),
                Arg.Any<CancellationToken>())
            .Returns(SearchResult(
                Suggestion("Saved Hall", "Saved Address", "0000")));
        var (host, _) = await RenderDialogAsync();
        IElement search = host.Find("[data-testid='address-suggestion-input']");

        await search.InputAsync(new ChangeEventArgs { Value = "hall" });
        await search.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await search.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        SetInput(host, "edit-location-city", "Manual City");

        await Assert.That(InputValue(host, "edit-location-full-name"))
            .IsEqualTo("Saved Hall");
        await Assert.That(InputValue(host, "edit-location-address"))
            .IsEqualTo("Saved Address");
        await Assert.That(InputValue(host, "edit-location-postcode"))
            .IsEqualTo("0000");
        await Assert.That(InputValue(host, "edit-location-city"))
            .IsEqualTo("Manual City");
    }

    [Test]
    public async Task PrivateHomeAction_OpensSeparateConsentDialogWithoutSubmittingAddressEdit()
    {
        _ctx.Services.AddSingleton(Substitute.For<IPrivateHomeOwnershipService>());
        var (host, reference) = await RenderDialogAsync();

        host.Find("[data-testid='edit-location-private-home']").Click();

        await Assert.That(host.FindComponents<HomeOwnerConsentDialog>().Count).IsEqualTo(1);
        await Assert.That(reference.Result.IsCompleted).IsFalse();
    }

    [Test]
    public async Task RenderedEvidence_CapturesSavedAddressResultsInEditDialog()
    {
        _suggestions.SearchAsync(
                "hall",
                Arg.Is<Guid?>(value => value == null),
                Arg.Any<Guid?>(),
                Arg.Any<Guid?>(),
                10,
                Arg.Any<HalLink>(),
                Arg.Any<CancellationToken>())
            .Returns(SearchResult(
                Suggestion("Community Hall", "10 Safe Street", "1000"),
                Suggestion("Neighbourhood Centre", "20 Safe Avenue", "2000")
            ));
        var (host, _) = await RenderDialogAsync();

        await host.Find("[data-testid='address-suggestion-input']")
            .InputAsync(new ChangeEventArgs { Value = "hall" });
        string path = await VisualEvidenceDocument.WriteAsync(
            ".omo/evidence/address-suggestion-ui/edit-dialog.html",
            "Edit location address suggestions",
            host.Markup);

        await Assert.That(File.Exists(path)).IsTrue();
        await Assert.That(host.FindAll("[role='option']")).Count().IsEqualTo(2);
    }

    public void Dispose() => _ctx.Dispose();

    private async Task<(IRenderedComponent<MudDialogProvider> Host, IDialogReference Reference)> RenderDialogAsync()
    {
        var existing = new LocationDto
        {
            Id = Guid.CreateVersion7(),
            FullName = "Existing Hall",
            Address = "1 Existing Street",
            City = "Brussels",
            Postcode = "1000",
            Country = "Belgium",
            Timezone = "UTC",
            ConcurrencyStamp = Guid.CreateVersion7(),
            LocationKindId = 1
        };
        var parameters = new DialogParameters<EditLocationDialog>
        {
            { component => component.ExistingLocation, existing },
            { component => component.SearchLink, AddressSuggestionLink() }
        };
        var host = _ctx.Render<MudDialogProvider>();
        IDialogReference reference = await _ctx.Services.GetRequiredService<IDialogService>()
            .ShowAsync<EditLocationDialog>(
                "Edit location",
                parameters,
                DialogOptionsFactory.Small());
        return (host, reference);
    }

    private static void SetInput(IRenderedComponent<MudDialogProvider> host, string testId, string value) =>
        host.Find($"[data-testid='{testId}']").Change(value);

    private static string? InputValue(
        IRenderedComponent<MudDialogProvider> host,
        string testId) =>
        host.Find($"[data-testid='{testId}']").GetAttribute("value");

    private static HalResourceOfAddressSuggestionDto Suggestion(
        string name,
        string address,
        string postcode) =>
        new()
        {
            LocationId = Guid.CreateVersion7(),
            ConcurrencyStamp = Guid.CreateVersion7(),
            DisplayName = name,
            Address = address,
            Postcode = postcode,
            Source = LocationAddressSourceEnum.Manual,
            Visibility = LocationAddressVisibilityEnum.TenantApproved,
            _links = new Dictionary<string, HalLink>()
        };

    private static AddressSuggestionSearchResult SearchResult(
        params HalResourceOfAddressSuggestionDto[] suggestions) => new()
        {
            Suggestions = suggestions,
            ProviderOutcome = AddressProviderOutcome.None
        };

    private static HalLink AddressSuggestionLink() => new()
    {
        Href = "/api/geocoding/address-suggestions",
        Method = "POST"
    };

    private static T DialogData<T>(DialogResult? result) =>
        (T)(result?.Data ?? throw new InvalidOperationException("Dialog result contained no data."));

    private static string[] RawCoordinateMembers(Type type) => type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(property => property.Name)
        .Where(IsRawCoordinateName)
        .ToArray();

    private static bool IsRawCoordinateName(string? name) =>
        string.Equals(name, "latitude", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "longitude", StringComparison.OrdinalIgnoreCase);
}
