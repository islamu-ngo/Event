using Event.Web.BffHosting.Options;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class AdminHostControlPlaneShellSelectorTests
{
    [Test]
    public async Task ShouldUseControlPlaneShell_WithConfiguredAdminHost_ReturnsTrue()
    {
        var selector = CreateSelector("admin.example.org");
        var httpContext = CreateHttpContext("admin.example.org");

        var shouldUseShell = selector.ShouldUseControlPlaneShell(httpContext);

        await Assert.That(shouldUseShell).IsTrue();
    }

    [Test]
    public async Task ShouldUseControlPlaneShell_WithPublicHost_ReturnsFalse()
    {
        var selector = CreateSelector("admin.example.org");
        var httpContext = CreateHttpContext("events.example.org");

        var shouldUseShell = selector.ShouldUseControlPlaneShell(httpContext);

        await Assert.That(shouldUseShell).IsFalse();
    }

    [Test]
    public async Task ShouldUseControlPlaneShell_WithMissingContext_ReturnsFalse()
    {
        var selector = CreateSelector("admin.example.org");

        var shouldUseShell = selector.ShouldUseControlPlaneShell(null);

        await Assert.That(shouldUseShell).IsFalse();
    }

    private static DefaultHttpContext CreateHttpContext(string host)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Host = new HostString(host);
        return httpContext;
    }

    private static AdminHostControlPlaneShellSelector CreateSelector(params string[] adminHosts)
    {
        var classifier = new EventBffHostClassifier(Options.Create(new EventBffHostingOptions
        {
            AdminHosts = adminHosts
        }));

        return new AdminHostControlPlaneShellSelector(classifier);
    }
}
