using Event.Web.BffHosting.Options;
using Event.Web.BffHosting.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class EventBffHostClassifierTests
{
    [Test]
    public async Task IsAdminHost_WithConfiguredOriginAndRequestHost_ReturnsTrue()
    {
        var classifier = CreateClassifier("https://admin.example.org");
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString("admin.example.org");

        var isAdminHost = classifier.IsAdminHost(httpContext);

        await Assert.That(isAdminHost).IsTrue();
    }

    [Test]
    public async Task IsAdminHost_WithPublicHost_ReturnsFalse()
    {
        var classifier = CreateClassifier("admin.example.org");

        var isAdminHost = classifier.IsAdminHost("events.example.org");

        await Assert.That(isAdminHost).IsFalse();
    }

    [Test]
    public async Task Validate_WithWildcardAdminHost_FailsClearly()
    {
        var validator = new EventBffHostingOptionsValidator();
        var options = new EventBffHostingOptions
        {
            AdminHosts = ["*.example.org"]
        };

        var result = validator.Validate(null, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains("must be an exact host");
    }

    [Test]
    public async Task Validate_WithDuplicateAdminHost_FailsClearly()
    {
        var validator = new EventBffHostingOptionsValidator();
        var options = new EventBffHostingOptions
        {
            AdminHosts = ["admin.example.org", "https://admin.example.org"]
        };

        var result = validator.Validate(null, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains("duplicate host 'admin.example.org'");
    }

    [Test]
    public async Task Validate_WithInvalidAdminHostAllowedIpRange_FailsClearly()
    {
        var validator = new EventBffHostingOptionsValidator();
        var options = new EventBffHostingOptions
        {
            AdminHosts = ["admin.example.org"],
            AdminHostAllowedIpRanges = ["not-a-cidr"]
        };

        var result = validator.Validate(null, options);

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains("must be an IP address or CIDR range");
    }

    private static EventBffHostClassifier CreateClassifier(params string[] adminHosts)
    {
        return new EventBffHostClassifier(Options.Create(new EventBffHostingOptions
        {
            AdminHosts = adminHosts
        }));
    }
}
