using System.Net;
using Event.Web.BffHosting.Options;
using Event.Web.BffHosting.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class EventBffAdminHostAccessPolicyTests
{
    [Test]
    public async Task IsAllowed_AdminHostWithoutAllowlist_ReturnsTrue()
    {
        var policy = CreatePolicy(["admin.example.org"], []);
        var context = CreateContext("admin.example.org", IPAddress.Parse("203.0.113.10"));

        var isAllowed = policy.IsAllowed(context);

        await Assert.That(isAllowed).IsTrue();
    }

    [Test]
    public async Task IsAllowed_PublicHostWithAllowlist_ReturnsTrue()
    {
        var policy = CreatePolicy(["admin.example.org"], ["203.0.113.0/24"]);
        var context = CreateContext("events.example.org", IPAddress.Parse("198.51.100.10"));

        var isAllowed = policy.IsAllowed(context);

        await Assert.That(isAllowed).IsTrue();
    }

    [Test]
    public async Task IsAllowed_AdminHostInsideCidr_ReturnsTrue()
    {
        var policy = CreatePolicy(["admin.example.org"], ["203.0.113.0/24"]);
        var context = CreateContext("admin.example.org", IPAddress.Parse("203.0.113.42"));

        var isAllowed = policy.IsAllowed(context);

        await Assert.That(isAllowed).IsTrue();
    }

    [Test]
    public async Task IsAllowed_AdminHostOutsideCidr_ReturnsFalse()
    {
        var policy = CreatePolicy(["admin.example.org"], ["203.0.113.0/24"]);
        var context = CreateContext("admin.example.org", IPAddress.Parse("198.51.100.42"));

        var isAllowed = policy.IsAllowed(context);

        await Assert.That(isAllowed).IsFalse();
    }

    [Test]
    public async Task IsAllowed_AdminHostWithMissingRemoteAddress_ReturnsFalse()
    {
        var policy = CreatePolicy(["admin.example.org"], ["203.0.113.0/24"]);
        var context = CreateContext("admin.example.org", null);

        var isAllowed = policy.IsAllowed(context);

        await Assert.That(isAllowed).IsFalse();
    }

    private static EventBffAdminHostAccessPolicy CreatePolicy(string[] adminHosts, string[] allowedIpRanges)
    {
        var options = Options.Create(new EventBffHostingOptions
        {
            AdminHosts = adminHosts,
            AdminHostAllowedIpRanges = allowedIpRanges
        });
        var classifier = new EventBffHostClassifier(options);
        return new EventBffAdminHostAccessPolicy(options, classifier);
    }

    private static DefaultHttpContext CreateContext(string host, IPAddress? remoteAddress)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Connection.RemoteIpAddress = remoteAddress;
        return context;
    }
}
