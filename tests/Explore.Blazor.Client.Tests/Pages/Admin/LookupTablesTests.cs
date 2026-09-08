using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Lookup;
using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using Explore.Blazor.Client.Services;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public class LookupTablesTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IEventLookupService _eventLookupService;
    private readonly IDialogService _dialogService;
    private readonly ISnackbar _snackbar;

    public LookupTablesTests()
    {
        _ctx = new BlazorTestContext();
        _eventLookupService = Substitute.For<IEventLookupService>();
        _dialogService = Substitute.For<IDialogService>();
        _snackbar = Substitute.For<ISnackbar>();

        _ctx.Services.AddSingleton(Substitute.For<ICategoryService>());
        _ctx.Services.AddSingleton(Substitute.For<ITagService>());
        _ctx.Services.AddSingleton(Substitute.For<ILocationClient>());
        _ctx.Services.AddSingleton(_eventLookupService);
        _ctx.Services.AddSingleton(Substitute.For<IDemographicLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<ICultureLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<IOrganizationLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<ISystemLookupService>());
        _ctx.Services.AddSingleton(_dialogService);
        _ctx.Services.AddSingleton(_snackbar);
        _ctx.Services.AddSingleton(Substitute.For<IAccessibilityFocusService>());

        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Admin User", "admin@example.com");

        SetupDefaultLookups();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private IRenderedComponent<TenantLookupTablesSection> RenderLookupTables() =>
        _ctx.RenderMudComponent<TenantLookupTablesSection>();

    [Test]
    public async Task LookupTables_ShowsLoadingIndicator_WhileLookupLoadPending()
    {
        // Arrange
        var pendingEventTypes = new TaskCompletionSource<ICollection<EventTypeListDto>>();
        _eventLookupService.GetEventTypesAsync().Returns(pendingEventTypes.Task);

        // Act
        var cut = RenderLookupTables();

        // Assert
        await Assert.That(cut.Markup).Contains("Lookup Tables");
        await Assert.That(cut.Markup).Contains("Loading lookup tables");

        // Cleanup
        pendingEventTypes.TrySetResult(new List<EventTypeListDto>());
    }

    [Test]
    public async Task LookupTables_ShowsLoadedContent_WhenLookupsSucceed()
    {
        // Arrange
        _eventLookupService.GetEventTypesAsync().Returns(
        [
            new EventTypeListDto
            {
                Id = 1,
                FullName = "Conference",
                Description = "Large gathering"
            }
        ]);

        // Act
        var cut = RenderLookupTables();
        cut.WaitForState(() => cut.Markup.Contains("Lookup Tables", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        // Assert
        await Assert.That(cut.Markup).Contains("Event Types");
        await Assert.That(cut.Markup).Contains("Tags");
    }

    [Test]
    public async Task LookupTables_UsesSnackbarError_WhenAnyLookupFails()
    {
        // Arrange
        _eventLookupService.GetEventFormatsAsync().ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var cut = RenderLookupTables();
        cut.WaitForState(() => cut.Markup.Contains("Failed to load lookup data", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        // Assert
        await Assert.That(cut.Markup).Contains("Failed to load lookup data: boom");
    }

    private void SetupDefaultLookups()
    {
        _eventLookupService.GetEventTypesAsync().Returns(new List<EventTypeListDto>());
        _eventLookupService.GetEventFormatsAsync().Returns(new List<EventFormatListDto>());
        _eventLookupService.GetEventStatusesAsync().Returns(new List<EventStatusListDto>());
        _eventLookupService.GetVisibilityTypesAsync().Returns(new List<VisibilityTypeListDto>());
        _eventLookupService.GetRegistrationModesAsync().Returns(new List<RegistrationModeListDto>());
    }
}
