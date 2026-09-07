using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Lookup;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public class CategoriesTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly ICategoryService _categoryService;

    public CategoriesTests()
    {
        _ctx = new BlazorTestContext();
        _categoryService = Substitute.For<ICategoryService>();

        _ctx.Services.AddSingleton(_categoryService);
        _ctx.Services.AddSingleton(Substitute.For<ITagService>());
        _ctx.Services.AddSingleton(Substitute.For<ILocationClient>());
        _ctx.Services.AddSingleton(Substitute.For<IEventLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<IDemographicLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<ICultureLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<IOrganizationLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<ISystemLookupService>());
        _ctx.Services.AddSingleton(Substitute.For<IDialogService>());
        _ctx.Services.AddSingleton(Substitute.For<ISnackbar>());
        _ctx.Services.AddSingleton(Substitute.For<IAccessibilityFocusService>());

        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Admin User", "admin@example.com");

        SetupDefaultLookups();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private IRenderedComponent<TenantLookupTablesSection> RenderCategories() =>
        _ctx.RenderMudComponent<TenantLookupTablesSection>();

    [Test]
    public async Task Categories_ShowsLoadingState_WhileFetchIsPending()
    {
        // Arrange
        var pending = new TaskCompletionSource<ICollection<CategoryListDto>>();
        _categoryService.GetCategoriesAsync().Returns(pending.Task);

        // Act
        var cut = RenderCategories();

        // Assert
        await Assert.That(cut.Markup).Contains("Loading lookup tables");

        // Cleanup
        pending.TrySetResult(new List<CategoryListDto>());
    }

    [Test]
    public async Task Categories_ShowsErrorAlert_WhenFetchThrows()
    {
        // Arrange
        _categoryService.GetCategoriesAsync().ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var cut = RenderCategories();
        cut.WaitForState(() => cut.Markup.Contains("Failed to load lookup data", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        // Assert
        await Assert.That(cut.Markup).Contains("Failed to load lookup data: boom");
    }

    [Test]
    public async Task Categories_ShowsEmptyState_WhenNoRecordsReturned()
    {
        // Arrange
        _categoryService.GetCategoriesAsync().Returns(new List<CategoryListDto>());

        // Act
        var cut = RenderCategories();
        cut.WaitForState(() => cut.Markup.Contains("Lookup Tables", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        // Assert
        await Assert.That(cut.Markup).Contains("Categories");
        await Assert.That(cut.Markup).Contains("Search categories");
    }

    [Test]
    public async Task Categories_ShowsCategoryRows_WhenDataExists()
    {
        // Arrange
        _categoryService.GetCategoriesAsync().Returns(
        [
            new CategoryListDto
            {
                Id = Guid.NewGuid(),
                FullName = "Faith & Spirituality",
                MasterCode = "FTH",
                ParentFullName = "Islamic"
            }
        ]);

        // Act
        var cut = RenderCategories();
        cut.WaitForState(() => cut.Markup.Contains("Spirituality", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        // Assert
        await Assert.That(cut.Markup).Contains("Spirituality");
        await Assert.That(cut.Markup).Contains("FTH");
        await Assert.That(cut.Markup).Contains("Islamic");
    }

    private void SetupDefaultLookups()
    {
        _categoryService.GetCategoriesAsync().Returns(new List<CategoryListDto>());
    }
}
