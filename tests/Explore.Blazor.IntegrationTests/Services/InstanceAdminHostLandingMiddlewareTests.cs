using Event.Web.BffHosting.Options;
using Event.Web.BffHosting.Security;
using Explore.Blazor.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Explore.Blazor.IntegrationTests.Services;

public sealed class InstanceAdminHostLandingMiddlewareTests
{
    [Test]
    [Arguments("", "/settings/instance")]
    [Arguments("/event", "/event/settings/instance")]
    public async Task AdminRootRedirectsToUnifiedSettings(string pathBase, string expectedLocation)
    {
        var context = Context("admin.example.org", "/");
        context.Request.PathBase = pathBase;
        bool nextCalled = false;
        var middleware = Middleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        await Assert.That(context.Response.StatusCode).IsEqualTo(StatusCodes.Status302Found);
        await Assert.That(context.Response.Headers.Location.ToString()).IsEqualTo(expectedLocation);
        await Assert.That(nextCalled).IsFalse();
    }

    [Test]
    [Arguments("events.example.org", "/")]
    [Arguments("admin.example.org", "/settings/instance")]
    [Arguments("admin.example.org", "/login")]
    [Arguments("admin.example.org", "/logout")]
    [Arguments("admin.example.org", "/auth/local/change-password")]
    [Arguments("admin.example.org", "/auth/local-account-recovery")]
    public async Task OtherRequestsReachSharedRoutes(string host, string path)
    {
        var context = Context(host, path);
        bool nextCalled = false;
        var middleware = Middleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        await Assert.That(nextCalled).IsTrue();
        await Assert.That(context.Response.Headers.Location.Count).IsEqualTo(0);
    }

    private static InstanceAdminHostLandingMiddleware Middleware(RequestDelegate next) =>
        new(next, new EventBffHostClassifier(Options.Create(new EventBffHostingOptions
        {
            AdminHosts = ["admin.example.org"]
        })));

    private static DefaultHttpContext Context(string host, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        return context;
    }
}
