using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Explore.Infrastructure.Tests.Services;

public sealed class PublicUrlBuilderTests
{
    [Test]
    public async Task GetBaseUrl_IgnoresUntrustedForwardedProtoHeader()
    {
        var context = CreateContext("http");
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        var builder = CreateBuilder(context);

        await Assert.That(builder.GetBaseUrl()).IsEqualTo("http://event.example");
    }

    [Test]
    public async Task GetBaseUrl_UsesSchemeNormalizedByTrustedForwardedHeadersMiddleware()
    {
        var context = CreateContext("https");
        context.Request.Headers["X-Forwarded-Proto"] = "https";

        var builder = CreateBuilder(context);

        await Assert.That(builder.GetBaseUrl()).IsEqualTo("https://event.example");
    }

    private static PublicUrlBuilder CreateBuilder(HttpContext context)
    {
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);
        return new PublicUrlBuilder(accessor, NullLogger<PublicUrlBuilder>.Instance);
    }

    private static DefaultHttpContext CreateContext(string scheme)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString("event.example");
        return context;
    }
}
