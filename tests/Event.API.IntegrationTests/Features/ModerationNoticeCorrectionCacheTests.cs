using System.Reflection;
using Explore.API.Controllers;
using Explore.API.Filters;
using Explore.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.IO;

namespace Event.API.IntegrationTests.Features;

public sealed class ModerationNoticeCorrectionCacheTests
{
    [Test]
    public async Task Correction_output_bypasses_ETag_capture_before_private_bytes_are_written()
    {
        var metadata = typeof(ModerationNoticeCorrectionController).GetCustomAttributes(true)
            .Concat(typeof(ModerationNoticeCorrectionController).GetMethod(nameof(ModerationNoticeCorrectionController.Read))!
                .GetCustomAttributes(true)).ToArray();
        await Assert.That(metadata.OfType<PrivateNoStoreAttribute>().Any()).IsTrue();
        var http = new DefaultHttpContext();
        http.Request.Method = HttpMethods.Get;
        http.Request.Headers.IfNoneMatch = "*";
        http.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "correction"));
        await using var stream = new MemoryStream();
        http.Response.Body = stream;
        bool originalStreamObserved = false;
        var middleware = new ETagMiddleware(async context =>
        {
            originalStreamObserved = ReferenceEquals(stream, context.Response.Body);
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync("{}"u8.ToArray());
        }, new RecyclableMemoryStreamManager());
        await middleware.InvokeAsync(http);
        await Assert.That(originalStreamObserved).IsTrue();
        await Assert.That(http.Response.Headers.ContainsKey("ETag")).IsFalse();
        await Assert.That(http.Response.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(stream.Length).IsEqualTo(2L);
    }
}
