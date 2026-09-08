using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Helpers;

public class ImageHelperTests
{
    [Test]
    public async Task GetEventImageUrl_ReturnsFeaturedImage_WhenProvided()
    {
        const string featuredImageUri = "https://cdn.example.test/event.jpg";

        var result = ImageHelper.GetEventImageUrl(featuredImageUri, "Annual Summit");

        await Assert.That(result).IsEqualTo(featuredImageUri);
    }

    [Test]
    public async Task GetEventImageUrl_ReturnsLocalSvgFallback_WhenImageMissing()
    {
        var result = ImageHelper.GetEventImageUrl(null, "Annual Summit", "4a90e2");
        var svg = DecodeSvg(result);

        await Assert.That(result).StartsWith("data:image/svg+xml;utf8,");
        await Assert.That(svg).Contains("linearGradient");
        await Assert.That(svg).Contains("radialGradient");
        await Assert.That(svg).Contains("@media (prefers-color-scheme: dark)");
        await Assert.That(svg).Contains("event-gradient-light");
        await Assert.That(svg).Contains("event-gradient-dark");
        await Assert.That(svg).DoesNotContain("Annual Summit");
        await Assert.That(result).DoesNotContain("placehold.co");
    }

    [Test]
    public async Task GetOrganizationPlaceholder_ReturnsLocalSvgFallback_WhenImageMissing()
    {
        var result = ImageHelper.GetOrganizationPlaceholder(null, "Community");

        await Assert.That(result).StartsWith("data:image/svg+xml;utf8,");
        await Assert.That(result).Contains(Uri.EscapeDataString("Community"));
        await Assert.That(result).DoesNotContain("placehold.co");
    }

    [Test]
    public async Task GetEventImageUrl_IsDeterministicForTheSameTitle()
    {
        var first = ImageHelper.GetEventImageUrl(null, "Annual Summit", "not-a-color");
        var second = ImageHelper.GetEventImageUrl(null, "Annual Summit", "4a90e2");

        await Assert.That(first).IsEqualTo(second);
    }

    [Test]
    public async Task GetEventImageUrl_UsesDifferentGradientForDifferentTitle()
    {
        var first = ImageHelper.GetEventImageUrl(null, "Annual Summit");
        var second = ImageHelper.GetEventImageUrl(null, "Community Workshop");

        await Assert.That(first).IsNotEqualTo(second);
    }

    private static string DecodeSvg(string dataUri) =>
        Uri.UnescapeDataString(dataUri["data:image/svg+xml;utf8,".Length..]);
}
