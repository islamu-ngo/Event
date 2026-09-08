using System.Reflection;
using AngleSharp.Dom;
using Explore.Blazor.Client.Pages.Admin.Dialogs;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class CreateLocationDialogTests : IDisposable
{
    private static readonly string[] ExpectedInputIds =
    [
        "create-location-address",
        "create-location-city",
        "create-location-country",
        "create-location-full-name",
        "create-location-postcode",
        "create-location-timezone"
    ];

    private readonly BlazorTestContext _ctx = new();
    private readonly IAddressSuggestionService _suggestions =
        Substitute.For<IAddressSuggestionService>();

    public CreateLocationDialogTests()
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
    public async Task Render_ExposesOnlyManualCoordinateFreeInputs()
    {
        var (host, _) = await RenderDialogAsync();
        string[] inputIds = host.FindAll("[data-testid]")
            .Select(element => element.GetAttribute("data-testid"))
            .Where(id => id is not null
                && id.StartsWith("create-location-", StringComparison.Ordinal)
                && id != "create-location-submit")
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(inputIds).IsEquivalentTo(ExpectedInputIds);
        await Assert.That(inputIds.Any(IsRawCoordinateName)).IsFalse();
        await Assert.That(RawCoordinateMembers(typeof(CreateLocationDto))).IsEmpty();
        await Assert.That(host.FindAll("[data-testid='address-suggestion-input']")).HasSingleItem();
        await Assert.That(host.FindAll("div.create-location-dialog__manual-fields > form")).HasSingleItem();
    }

    [Test]
    public async Task SavedAddressSelection_PopulatesAvailableFieldsAndKeepsManualInputs()
    {
        _suggestions.SearchAsync(
                "hall",
                null,
                null,
                null,
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

        await Assert.That(InputValue(host, "create-location-full-name"))
            .IsEqualTo("Saved Hall");
        await Assert.That(InputValue(host, "create-location-address"))
            .IsEqualTo("Saved Address");
        await Assert.That(InputValue(host, "create-location-postcode"))
            .IsEqualTo("0000");
        await Assert.That(host.FindAll("[data-testid^='create-location-']").Count)
            .IsGreaterThanOrEqualTo(7);
    }

    [Test]
    public async Task Submit_PreservesSafeManualFieldsOnCoordinateFreeResult()
    {
        var (host, dialogReference) = await RenderDialogAsync();
        SetInput(host, "create-location-full-name", "Community Hall");
        SetInput(host, "create-location-address", "10 Safe Street");
        SetInput(host, "create-location-city", "Brussels");
        SetInput(host, "create-location-postcode", "1000");
        SetInput(host, "create-location-country", "Belgium");
        SetInput(host, "create-location-timezone", "Europe/Brussels");

        Task<DialogResult?> resultSignal = dialogReference.Result;
        host.Find("[data-testid='create-location-submit']").Click();
        CreateLocationDto result = DialogData<CreateLocationDto>(
            await resultSignal.WaitAsync(TimeSpan.FromSeconds(3)));

        await Assert.That(result.FullName).IsEqualTo("Community Hall");
        await Assert.That(result.Address).IsEqualTo("10 Safe Street");
        await Assert.That(result.City).IsEqualTo("Brussels");
        await Assert.That(result.Postcode).IsEqualTo("1000");
        await Assert.That(result.Country).IsEqualTo("Belgium");
        await Assert.That(result.Timezone).IsEqualTo("Europe/Brussels");
        await Assert.That(RawCoordinateMembers(result.GetType())).IsEmpty();
    }

    [Test]
    public async Task EscapeFromOpenSuggestions_ClosesPopupWithoutClosingDialog()
    {
        _suggestions.SearchAsync(
                "hall",
                null,
                null,
                null,
                10,
                Arg.Any<HalLink>(),
                Arg.Any<CancellationToken>())
            .Returns(SearchResult(
                Suggestion("Community Hall", "10 Safe Street", "1000")));
        var (host, reference) = await RenderDialogAsync();
        IElement input = host.Find("[data-testid='address-suggestion-input']");
        await input.InputAsync(new ChangeEventArgs { Value = "hall" });

        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });

        await Assert.That(input.GetAttribute("aria-expanded")).IsEqualTo("false");
        await Assert.That(reference.Result.IsCompleted).IsFalse();
    }

    [Test]
    public async Task RenderedEvidence_CapturesSavedAddressResultsInCreateDialog()
    {
        _suggestions.SearchAsync(
                "hall",
                null,
                null,
                null,
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
            ".omo/evidence/address-suggestion-ui/create-dialog.html",
            "Create location address suggestions",
            host.Markup);

        await Assert.That(File.Exists(path)).IsTrue();
        await Assert.That(host.FindAll("[role='option']")).Count().IsEqualTo(2);
    }

    public void Dispose() => _ctx.Dispose();

    private async Task<(IRenderedComponent<MudDialogProvider> Host, IDialogReference Reference)> RenderDialogAsync()
    {
        var host = _ctx.Render<MudDialogProvider>();
        IDialogReference reference = await _ctx.Services.GetRequiredService<IDialogService>()
            .ShowAsync<CreateLocationDialog>(
                "Create location",
                new DialogParameters<CreateLocationDialog>
                {
                    {
                        component => component.SearchLink,
                        AddressSuggestionLink()
                    }
                },
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
