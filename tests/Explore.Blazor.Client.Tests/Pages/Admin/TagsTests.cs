using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Lookup;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public class TagsTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly ITagService _tagService;
    private readonly ISnackbar _snackbar;

    public TagsTests()
    {
        _ctx = new BlazorTestContext();
        _tagService = Substitute.For<ITagService>();
        _snackbar = Substitute.For<ISnackbar>();

        _ctx.Services.AddSingleton(Substitute.For<ICategoryService>());
        _ctx.Services.AddSingleton(_tagService);
        _ctx.Services.AddSingleton(Substitute.For<ILocationClient>());
        _ctx.Services.AddSingleton(Substitute.For<IEventLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<IDemographicLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<ICultureLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<IOrganizationLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<ISystemLookupService>());
        _ctx.Services.AddSingleton(_snackbar);
        _ctx.Services.AddSingleton(Substitute.For<IDialogService>());
        _ctx.Services.AddSingleton(Substitute.For<IAccessibilityFocusService>());

        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Admin User", "admin@example.com");

        SetupDefaultLookups();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private IRenderedComponent<TenantLookupTablesSection> RenderTags() =>
        _ctx.RenderMudComponent<TenantLookupTablesSection>();

    private static void SelectTab(IRenderedComponent<TenantLookupTablesSection> cut, string tabName)
    {
        var tab = cut.FindAll("[role='tab']").First(x => x.TextContent.Contains(tabName, StringComparison.OrdinalIgnoreCase));
        tab.Click();
    }

    [Test]
    public async Task Tags_ShowsLoadingState_WhileFetchIsPending()
    {
        // Arrange
        var pending = new TaskCompletionSource<ICollection<TagListDto>>();
        _tagService.GetTagsAsync().Returns(pending.Task);

        // Act
        var cut = RenderTags();

        // Assert
        await Assert.That(cut.Markup).Contains("Loading lookup tables");

        // Cleanup
        pending.TrySetResult(new List<TagListDto>());
    }

    [Test]
    public async Task Tags_ShowsEmptyState_WhenNoTagsReturned()
    {
        // Arrange
        _tagService.GetTagsAsync().Returns(new List<TagListDto>());

        // Act
        var cut = RenderTags();
        cut.WaitForState(() => cut.Markup.Contains("Lookup Tables", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));
        SelectTab(cut, "Tags");

        // Assert
        await Assert.That(cut.Markup).Contains("Tags");
        await Assert.That(cut.Markup).Contains("Search tags");
    }

    [Test]
    public async Task Tags_ShowsTagRows_WhenDataExists()
    {
        // Arrange
        _tagService.GetTagsAsync().Returns(
        [
            new TagListDto
            {
                Id = Guid.NewGuid(),
                FullName = "Community",
                MasterCode = "COMM"
            }
        ]);

        // Act
        var cut = RenderTags();
        cut.WaitForState(() => cut.Markup.Contains("Lookup Tables", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));
        SelectTab(cut, "Tags");
        cut.WaitForState(() => cut.Markup.Contains("Community", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        // Assert
        await Assert.That(cut.Markup).Contains("Community");
        await Assert.That(cut.Markup).Contains("COMM");
    }

    [Test]
    public async Task Tags_UsesSnackbarError_WhenLoadFails()
    {
        // Arrange
        _tagService.GetTagsAsync().ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var cut = RenderTags();
        cut.WaitForState(() => cut.Markup.Contains("Failed to load lookup data", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        // Assert
        await Assert.That(cut.Markup).Contains("Failed to load lookup data: boom");
    }

    private void SetupDefaultLookups()
    {
        _tagService.GetTagsAsync().Returns(new List<TagListDto>());
    }
}
