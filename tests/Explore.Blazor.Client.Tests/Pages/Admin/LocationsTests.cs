using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Lookup;
using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using Explore.Blazor.Client.Services;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public class LocationsTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly ILocationClient _locationClient;

    public LocationsTests()
    {
        _ctx = new BlazorTestContext();
        _locationClient = Substitute.For<ILocationClient>();

        _ctx.Services.AddSingleton(Substitute.For<ICategoryService>());
        _ctx.Services.AddSingleton(Substitute.For<ITagService>());
        _ctx.Services.AddSingleton(_locationClient);
        _ctx.Services.AddSingleton(Substitute.For<IEventLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<IDemographicLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<ICultureLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<IOrganizationLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<ISystemLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<ISnackbar>());
        _ctx.Services.AddSingleton(Substitute.For<IDialogService>());
        _ctx.Services.AddSingleton(Substitute.For<IAccessibilityFocusService>());

        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Admin User", "admin@example.com");

        SetupDefaultLookups();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private IRenderedComponent<TenantLookupTablesSection> RenderLocations() =>
        _ctx.RenderMudComponent<TenantLookupTablesSection>();

    private static void SelectTab(IRenderedComponent<TenantLookupTablesSection> cut, string tabName)
    {
        var tab = cut.FindAll("[role='tab']").First(x => x.TextContent.Contains(tabName, StringComparison.OrdinalIgnoreCase));
        tab.Click();
    }

    [Test]
    public async Task Locations_ShowsLoadingState_WhileFetchIsPending()
    {
        // Arrange
        var pending =
            new TaskCompletionSource<HalCollectionResourceOfLocationListDto>();
        _locationClient.GetLocationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(pending.Task);

        // Act
        var cut = RenderLocations();

        // Assert
        await Assert.That(cut.Markup).Contains("Loading lookup tables");

        // Cleanup
        pending.TrySetResult(LocationCollection());
    }

    [Test]
    public async Task Locations_ShowsEmptyState_WhenNoLocationsReturned()
    {
        // Arrange
        _locationClient.GetLocationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(LocationCollection());

        // Act
        var cut = RenderLocations();
        cut.WaitForState(() => cut.Markup.Contains("Lookup Tables", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));
        SelectTab(cut, "Locations");

        // Assert
        await Assert.That(cut.Markup).Contains("Locations");
        await Assert.That(cut.Markup).Contains("Search locations");
    }

    [Test]
    public async Task Locations_ShowsLocationRows_WhenDataExists()
    {
        // Arrange
        _locationClient.GetLocationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(LocationCollection(
            new LocationListDto
            {
                Id = Guid.NewGuid(),
                FullName = "Main Mosque Hall",
                City = "Brussels",
                Country = "Belgium",
                Address = "1 Unity St"
            }
        ));

        // Act
        var cut = RenderLocations();
        SelectTab(cut, "Locations");
        cut.WaitForState(() => cut.Markup.Contains("Main Mosque Hall", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        // Assert
        await Assert.That(cut.Markup).Contains("Main Mosque Hall");
        await Assert.That(cut.Markup).Contains("Brussels");
        await Assert.That(cut.Markup).Contains("Belgium");
    }

    [Test]
    public async Task Locations_HidesWriteControlsWithoutHalCapabilities()
    {
        _locationClient.GetLocationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(LocationCollection(
            new LocationListDto
            {
                Id = Guid.CreateVersion7(),
                FullName = "Read-only Hall",
                Address = "1 Safe Street",
                City = "Brussels",
                Country = "Belgium"
            }));

        IRenderedComponent<TenantLookupTablesSection> cut = RenderLocations();
        SelectTab(cut, "Locations");
        cut.WaitForState(
            () => cut.Markup.Contains(
                "Read-only Hall",
                StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(3));

        bool hasCreate = cut.FindAll("button")
            .Any(button => button.TextContent.Trim() == "Create");
        await Assert.That(hasCreate).IsFalse();
        await Assert.That(cut.FindAll("button[aria-label^='Edit'], button[aria-label^='Delete']")).IsEmpty();
    }

    [Test]
    public async Task Locations_ShowsOnlyAdvertisedWriteControls()
    {
        Guid locationId = Guid.CreateVersion7();
        HalCollectionResourceOfLocationListDto resource = LocationCollection(
            new LocationListDto
            {
                Id = locationId,
                FullName = "Managed Hall",
                Address = "2 Safe Street",
                City = "Brussels",
                Country = "Belgium"
            });
        resource._links = new Dictionary<string, HalLink>
        {
            ["create"] = LocationLink("POST", "/api/location")
        };
        HalResourceOfLocationListDto item =
            resource._embedded!.Items!.Single();
        item._links = new Dictionary<string, HalLink>
        {
            ["self"] = LocationLink("GET", $"/api/location/{locationId:D}"),
            ["edit"] = LocationLink("PATCH", $"/api/location/{locationId:D}"),
            ["delete"] = LocationLink("DELETE", $"/api/location/{locationId:D}")
        };
        _locationClient.GetLocationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(resource);

        IRenderedComponent<TenantLookupTablesSection> cut = RenderLocations();
        SelectTab(cut, "Locations");
        cut.WaitForState(
            () => cut.Markup.Contains(
                "Managed Hall",
                StringComparison.OrdinalIgnoreCase),
            TimeSpan.FromSeconds(3));

        await Assert.That(cut.FindAll("[data-testid='location-create']"))
            .HasSingleItem();
        await Assert.That(
                cut.FindAll($"[data-testid='location-edit-{locationId:D}']"))
            .HasSingleItem();
        await Assert.That(
                cut.FindAll($"[data-testid='location-delete-{locationId:D}']"))
            .HasSingleItem();
    }

    [Test]
    public async Task Locations_UsesSnackbarError_WhenLoadFails()
    {
        // Arrange
        var snackbar = _ctx.Services.GetRequiredService<ISnackbar>();
        _locationClient.GetLocationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var cut = RenderLocations();
        cut.WaitForAssertion(() =>
            snackbar.Received().Add(
                Arg.Is<string>(message => message.Contains("Failed to load locations: boom", StringComparison.OrdinalIgnoreCase)),
                Severity.Error,
                Arg.Any<Action<SnackbarOptions>>(),
                Arg.Any<string>()));

        await Assert.That(cut.Markup).DoesNotContain("Failed to load lookup data: boom");
    }

    private void SetupDefaultLookups()
    {
        _locationClient.GetLocationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(LocationCollection());
    }

    private static HalCollectionResourceOfLocationListDto LocationCollection(
        params LocationListDto[] items) => new()
        {
            _embedded = new HalCollectionEmbeddedOfLocationListDto
            {
                Items = items.Select(item =>
                {
                    string json = System.Text.Json.JsonSerializer.Serialize(item);
                    return System.Text.Json.JsonSerializer
                        .Deserialize<HalResourceOfLocationListDto>(json)
                        ?? new HalResourceOfLocationListDto();
                }).ToArray()
            }
        };

    private static HalLink LocationLink(string method, string href) => new()
    {
        Method = method,
        Href = href
    };
}
