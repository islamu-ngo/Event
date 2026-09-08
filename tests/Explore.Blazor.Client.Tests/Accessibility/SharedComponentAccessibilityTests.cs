using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Shared;
using Explore.Blazor.Client.Tests.Common;
using MudBlazor;
using NSubstitute;

namespace Explore.Blazor.Client.Tests.Accessibility;

/// <summary>
/// Accessibility tests for shared UI components.
/// Verifies WCAG 2.2 AA compliance in rendered markup:
/// role="alert" on error containers, alt text on images, ARIA attributes.
/// </summary>
public class SharedComponentAccessibilityTests : IDisposable
{
    private readonly BlazorTestContext _ctx;

    public SharedComponentAccessibilityTests()
    {
        _ctx = new BlazorTestContext();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    #region ErrorState Component

    [Test]
    public async Task ErrorState_RendersRoleAlert_WhenErrorMessagePresent()
    {
        // Arrange & Act
        var cut = _ctx.RenderMudComponent<ErrorState>(p =>
            p.Add(x => x.ErrorMessage, "Something went wrong"));

        // Assert — error container must have role="alert" for screen reader announcement
        await Assert.That(cut.Markup).Contains("role=\"alert\"");
    }

    [Test]
    public async Task ErrorState_RendersErrorMessage_InsideAlertRole()
    {
        // Arrange & Act
        var cut = _ctx.RenderMudComponent<ErrorState>(p =>
            p.Add(x => x.ErrorMessage, "Network error occurred"));

        // Assert — the error text must be inside the role="alert" container
        var alertDiv = cut.Find("[role='alert']");
        await Assert.That(alertDiv.InnerHtml).Contains("Network error occurred");
    }

    [Test]
    public async Task ErrorState_RendersNothing_WhenErrorMessageNull()
    {
        // Arrange & Act
        var cut = _ctx.RenderMudComponent<ErrorState>(p =>
            p.Add(x => x.ErrorMessage, (string?)null));

        // Assert — no role="alert" container when there's no error
        await Assert.That(cut.Markup).DoesNotContain("role=\"alert\"");
    }

    [Test]
    public async Task ErrorState_RendersNothing_WhenErrorMessageEmpty()
    {
        // Arrange & Act
        var cut = _ctx.RenderMudComponent<ErrorState>(p =>
            p.Add(x => x.ErrorMessage, ""));

        // Assert
        await Assert.That(cut.Markup).DoesNotContain("role=\"alert\"");
    }

    [Test]
    [Skip("Category: Component accessibility. Removal: enable after the AppButton wrapper handles OnClick as EventCallback<MouseEventArgs> under MudBlazor v9.")]
    public async Task ErrorState_RendersRetryButton_WhenOnRetryProvided()
    {
        // Arrange & Act
        var retryClicked = false;
        var cut = _ctx.RenderMudComponent<ErrorState>(p =>
            p.Add(x => x.ErrorMessage, "Error")
             .Add(x => x.OnRetry, () => { retryClicked = true; }));

        // Assert — retry button must be present and accessible
        await Assert.That(cut.Markup).Contains("Try Again");
    }

    [Test]
    public async Task ErrorState_HidesRetryButton_WhenOnRetryNotProvided()
    {
        // Arrange & Act
        var cut = _ctx.RenderMudComponent<ErrorState>(p =>
            p.Add(x => x.ErrorMessage, "Error"));

        // Assert — no retry button when callback not provided
        await Assert.That(cut.Markup).DoesNotContain("Try Again");
    }

    #endregion

    #region ImageUpload Component

    [Test]
    public async Task ImageUpload_RendersPolicyDrivenHintAndIdleBusyState()
    {
        // Arrange
        var imageService = Substitute.For<IImageStorageService>();
        _ctx.Services.AddSingleton(imageService);

        // Act
        var cut = _ctx.RenderMudComponent<ImageUpload>(p =>
            p.Add(x => x.AcceptedFormats, ".png,.webp")
             .Add(x => x.AllowedContentTypes, ["image/png", "image/webp"])
             .Add(x => x.MaxFileSize, 2 * 1024 * 1024));

        // Assert
        await Assert.That(cut.Markup).Contains("Max 2 MB", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("aria-busy=\"false\"");
    }

    [Test]
    public async Task ImageUpload_RendersPreviewAlt_WhenPreviewExists()
    {
        // Arrange
        var imageService = Substitute.For<IImageStorageService>();
        _ctx.Services.AddSingleton(imageService);

        // Act
        var cut = _ctx.RenderMudComponent<ImageUpload>(p =>
            p.Add(x => x.PreviewUrl, "/api/storageobject/00000000-0000-0000-0000-000000000001/public")
             .Add(x => x.Alt, "Event banner preview"));

        // Assert
        await Assert.That(cut.Markup).Contains("alt=\"Event banner preview\"");
    }

    #endregion

    #region StorageImage Component

    [Test]
    public async Task StorageImage_DefaultAlt_IsEmpty_ForDecorativeImages()
    {
        // Arrange — mock image service to return a URL
        var imageService = Substitute.For<IImageStorageService>();
        imageService.GetImageUrlAsync(Arg.Any<string>())
            .Returns("https://example.com/image.jpg");
        _ctx.Services.AddSingleton(imageService);

        // Act
        var cut = _ctx.Render<StorageImage>(p =>
            p.Add(x => x.ImageUrl, "https://example.com/image.jpg"));

        // Assert — default alt should be empty string (decorative image per WCAG)
        await Assert.That(cut.Markup).Contains("alt=\"\"");
    }

    [Test]
    public async Task StorageImage_RendersCustomAlt_WhenProvided()
    {
        // Arrange
        var imageService = Substitute.For<IImageStorageService>();
        _ctx.Services.AddSingleton(imageService);

        // Act
        var cut = _ctx.Render<StorageImage>(p =>
            p.Add(x => x.ImageUrl, "https://example.com/photo.jpg")
             .Add(x => x.Alt, "Community gathering at sunset"));

        // Assert — custom alt text rendered on img element
        await Assert.That(cut.Markup).Contains("alt=\"Community gathering at sunset\"");
    }

    [Test]
    public async Task StorageImage_RendersCustomAltAsAttributeOnly_WhenMetadataContainsMarkup()
    {
        // Arrange
        const string altText = "poster \" onerror=\"alert(1)";
        var imageService = Substitute.For<IImageStorageService>();
        _ctx.Services.AddSingleton(imageService);

        // Act
        var cut = _ctx.Render<StorageImage>(p =>
            p.Add(x => x.ImageUrl, "https://example.com/photo.jpg")
             .Add(x => x.Alt, altText));

        // Assert
        var image = cut.Find("img.storage-image__media");
        await Assert.That(image.GetAttribute("alt")).IsEqualTo(altText);
        await Assert.That(image.HasAttribute("onerror")).IsFalse();
    }

    [Test]
    public async Task StorageImage_RendersErrorTextAsEncodedText_WhenMetadataContainsMarkup()
    {
        // Arrange
        const string errorText = "<img src=x onerror=alert(1)>";
        var imageService = Substitute.For<IImageStorageService>();
        imageService.GetPresignedUrlByIdAsync(Arg.Any<Guid>())
            .Returns((string?)null);
        _ctx.Services.AddSingleton(imageService);

        // Act
        var cut = _ctx.Render<StorageImage>(p =>
            p.Add(x => x.StorageObjectId, Guid.NewGuid())
             .Add(x => x.ErrorText, errorText));

        // Assert
        var errorContainer = cut.Find(".storage-image__error");
        await Assert.That(errorContainer.TextContent).Contains(errorText);
        await Assert.That(errorContainer.InnerHtml).Contains("&lt;img src=x onerror=alert(1)&gt;");
        await Assert.That(cut.FindAll("img[src='x']")).IsEmpty();
        await Assert.That(cut.FindAll("script")).IsEmpty();
    }

    [Test]
    public async Task StorageImage_ShowsPlaceholder_WhenNoImageReferenceExists()
    {
        // Arrange — no image URL or key = placeholder state
        var imageService = Substitute.For<IImageStorageService>();
        _ctx.Services.AddSingleton(imageService);

        // Act — render with no image data
        var cut = _ctx.Render<StorageImage>();

        // Assert — placeholder should be visible (not an img with missing alt)
        await Assert.That(cut.Markup).DoesNotContain("<img");
    }

    #endregion
}
