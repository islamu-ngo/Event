using System.Net;
using System.Text;
using Explore.API.Filters;
using Explore.API.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Event.Api.IntegrationTests.Features.Middleware;

public sealed class BoundedETagTests
{
    private const int Limit = 256 * 1024;
    // Independent SHA-256 vector for the UTF-8 bytes of {}: 44136fa355b3678a...
    private const string EmptyObjectETag = "W/\"RBNvo1WzZ4o=\"";

    [Test]
    [Arguments("application/json")]
    [Arguments("application/hal+json")]
    [Arguments("Application/Json; charset=utf-8")]
    public async Task SmallJson_ReturnsExactBytesAndWeakHash(string contentType)
    {
        using var host = await StartHost(async context =>
        {
            context.Response.ContentType = contentType;
            await context.Response.Body.WriteAsync("{}"u8.ToArray());
        });
        using var client = host.GetTestClient();

        using var response = await client.GetAsync("/capture");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("{}");
        await Assert.That(response.Headers.ETag?.ToString()).IsEqualTo(EmptyObjectETag);
    }

    [Test]
    [Arguments("GET")]
    [Arguments("HEAD")]
    public async Task MatchingValidator_Returns304WithoutBodyOrContentLength(string method)
    {
        using var host = await StartHost(async context =>
        {
            context.Response.ContentType = "application/json";
            context.Response.ContentLength = 2;
            await context.Response.Body.WriteAsync("{}"u8.ToArray());
        });
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/capture");
        request.Headers.TryAddWithoutValidation("If-None-Match", EmptyObjectETag);

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
        await Assert.That(response.Headers.ETag?.ToString()).IsEqualTo(EmptyObjectETag);
        await Assert.That(response.Content.Headers.Contains("Content-Length")).IsFalse();
        await Assert.That((await response.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
    }

    [Test]
    public async Task DifferentValidator_ReturnsRepresentationInsteadOf304()
    {
        using var host = await StartHost(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync("{}"u8.ToArray());
        });
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/capture");
        request.Headers.TryAddWithoutValidation("If-None-Match", "W/\"different\"");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("{}");
        await Assert.That(response.Headers.ETag?.ToString()).IsEqualTo(EmptyObjectETag);
    }

    [Test]
    [Arguments(Limit - 1)]
    [Arguments(Limit)]
    public async Task JsonWithinRetentionBudget_PreservesBodyAndHasValidator(int size)
    {
        byte[] body = JsonBody(size);
        using var host = await StartHost(async context =>
        {
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync(body);
        });
        using var client = host.GetTestClient();

        using var response = await client.GetAsync("/capture");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await response.Content.ReadAsByteArrayAsync()).SequenceEqual(body)).IsTrue();
        await Assert.That(response.Headers.ETag).IsNotNull();
    }

    [Test]
    [Arguments(false, 0)]
    [Arguments(false, Limit - 7)]
    [Arguments(false, Limit)]
    [Arguments(true, Limit - 7)]
    public async Task OversizedJson_ForwardsPrefixCrossingWriteAndTailExactlyOnce(
        bool knownLength, int prefixLength)
    {
        byte[] body = JsonBody(Limit + 64);
        using var host = await StartHost(async context =>
        {
            context.Response.ContentType = "application/json";
            if (knownLength)
            {
                context.Response.ContentLength = body.Length;
            }
            await context.Response.Body.WriteAsync(body.AsMemory(0, prefixLength));
            await context.Response.Body.WriteAsync(body.AsMemory(prefixLength, Limit + 1 - prefixLength));
            await context.Response.Body.WriteAsync(body.AsMemory(Limit + 1));
        });
        using var client = host.GetTestClient();

        using var response = await client.GetAsync("/capture");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await response.Content.ReadAsByteArrayAsync()).SequenceEqual(body)).IsTrue();
        await Assert.That(response.Headers.ETag).IsNull();
        if (knownLength)
        {
            await Assert.That(response.Content.Headers.ContentLength).IsEqualTo((long?)body.Length);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CrossingBudget_ReleasesRetainedBytesAndForwardsBeforeEndpointReturns(bool synchronous)
    {
        byte[] body = JsonBody(Limit + 64);
        using var destination = new MemoryStream();
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Response.Body = destination;
        var middleware = new ETagMiddleware(async current =>
        {
            current.Response.ContentType = "application/json";
            Stream capture = current.Response.Body;
            if (synchronous)
            {
                capture.Write(body.AsSpan(0, Limit));
            }
            else
            {
                await capture.WriteAsync(body.AsMemory(0, Limit));
            }
            await Assert.That(capture.Length).IsEqualTo((long)Limit);
            await Assert.That(destination.Length).IsEqualTo(0L);

            if (synchronous)
            {
                capture.Write(body.AsSpan(Limit, 1));
            }
            else
            {
                await capture.WriteAsync(body.AsMemory(Limit, 1));
            }
            await Assert.That(capture.Length).IsEqualTo(0L);
            await Assert.That(destination.ToArray().SequenceEqual(body.Take(Limit + 1))).IsTrue();

            if (synchronous)
            {
                capture.Write(body.AsSpan(Limit + 1));
            }
            else
            {
                await capture.WriteAsync(body.AsMemory(Limit + 1));
            }
            await Assert.That(capture.Length).IsEqualTo(0L);
            await Assert.That(destination.ToArray().SequenceEqual(body)).IsTrue();
        });

        await middleware.InvokeAsync(context);

        await Assert.That(ReferenceEquals(context.Response.Body, destination)).IsTrue();
        await Assert.That(destination.ToArray().SequenceEqual(body)).IsTrue();
        await Assert.That(context.Response.Headers.ContainsKey("ETag")).IsFalse();
        await destination.WriteAsync("!"u8.ToArray());
        await Assert.That(destination.Length).IsEqualTo((long)body.Length + 1);
    }

    [Test]
    [Arguments("sync-flush")]
    [Arguments("async-flush")]
    [Arguments("start")]
    [Arguments("stream-metadata")]
    [Arguments("non-json-metadata")]
    [Arguments("stream-content-type")]
    [Arguments("non-json-content-type")]
    [Arguments("private-no-store")]
    public async Task IneligibleOrStreamingResponse_ForwardsBodyWithoutValidator(string bypass)
    {
        object[] metadata = bypass switch
        {
            "stream-metadata" => [new ProducesAttribute("text/event-stream")],
            "non-json-metadata" => [new ProducesAttribute("text/plain")],
            "private-no-store" => [new PrivateNoStoreAttribute()],
            _ => []
        };
        using var host = await StartHost(async context =>
        {
            context.Response.ContentType = bypass switch
            {
                "stream-content-type" => "text/event-stream",
                "non-json-content-type" => "text/plain",
                _ => "application/json"
            };
            if (bypass == "private-no-store")
            {
                context.Response.Headers.CacheControl = "private, no-store";
            }
            await context.Response.Body.WriteAsync("{"u8.ToArray());
            switch (bypass)
            {
                case "sync-flush":
                    context.Response.Body.Flush();
                    break;
                case "async-flush":
                    await context.Response.Body.FlushAsync();
                    break;
                case "start":
                    await context.Response.StartAsync();
                    break;
            }
            await Assert.That(context.Response.HasStarted).IsTrue();
            await context.Response.Body.WriteAsync("}"u8.ToArray());
        }, metadata);
        host.GetTestServer().AllowSynchronousIO = true;
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/capture");
        request.Headers.TryAddWithoutValidation("If-None-Match", EmptyObjectETag);

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("{}");
        await Assert.That(response.Headers.ETag).IsNull();
        if (bypass == "private-no-store")
        {
            await Assert.That(response.Headers.CacheControl?.Private).IsEqualTo(true);
            await Assert.That(response.Headers.CacheControl?.NoStore).IsEqualTo(true);
        }
    }

    [Test]
    [Arguments(2, false)]
    [Arguments(2, true)]
    [Arguments(Limit + 1, false)]
    [Arguments(Limit + 1, true)]
    public async Task Head_SuppressesBodyAndPreservesKnownRepresentationLength(int size, bool knownLength)
    {
        byte[] body = size == 2 ? "{}"u8.ToArray() : JsonBody(size);
        using var host = await StartHost(async context =>
        {
            context.Response.ContentType = "application/json";
            if (knownLength)
            {
                context.Response.ContentLength = body.Length;
            }
            await context.Response.Body.WriteAsync(body);
        });
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Head, "/capture");

        using var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await response.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
        await Assert.That(response.Headers.ETag?.ToString()).IsEqualTo(size <= Limit ? EmptyObjectETag : null);
        if (knownLength || size <= Limit)
        {
            await Assert.That(response.Content.Headers.ContentLength).IsEqualTo((long?)size);
        }
    }

    [Test]
    [Arguments(400)]
    [Arguments(404)]
    [Arguments(500)]
    public async Task UnsuccessfulJson_PreservesStatusAndBodyWithoutValidator(int status)
    {
        using var host = await StartHost(async context =>
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            await context.Response.Body.WriteAsync("{}"u8.ToArray());
        });
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/capture");
        request.Headers.TryAddWithoutValidation("If-None-Match", EmptyObjectETag);

        using var response = await client.SendAsync(request);

        await Assert.That((int)response.StatusCode).IsEqualTo(status);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("{}");
        await Assert.That(response.Headers.ETag).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NoContent_HasNeitherBodyNorValidator(bool attemptsBody)
    {
        using var host = await StartHost(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            context.Response.ContentType = "application/json";
            if (attemptsBody)
            {
                await context.Response.Body.WriteAsync("{}"u8.ToArray());
            }
        });
        using var client = host.GetTestClient();

        using var response = await client.GetAsync("/capture");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That((await response.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
        await Assert.That(response.Headers.ETag).IsNull();
    }

    [Test]
    public async Task EndpointException_RestoresOriginalStreamForUpstreamErrorResponse()
    {
        var failure = new InvalidOperationException("endpoint failed");
        bool restored = false;
        using var host = await new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.Configure(app =>
            {
                app.Use(async (context, next) =>
                {
                    Stream original = context.Response.Body;
                    try
                    {
                        await next(context);
                    }
                    catch (InvalidOperationException exception) when (ReferenceEquals(exception, failure))
                    {
                        restored = ReferenceEquals(context.Response.Body, original);
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        await context.Response.Body.WriteAsync("recovered"u8.ToArray());
                    }
                });
                app.UseETag();
                app.Run(async context =>
                {
                    context.Response.ContentType = "application/json";
                    await context.Response.Body.WriteAsync("{}"u8.ToArray());
                    throw failure;
                });
            });
        }).StartAsync();
        using var client = host.GetTestClient();

        using var response = await client.GetAsync("/capture");

        await Assert.That(restored).IsTrue();
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.InternalServerError);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("recovered");
        await Assert.That(response.Headers.ETag).IsNull();
    }

    private static byte[] JsonBody(int size) =>
        Encoding.UTF8.GetBytes("\"" + new string('x', size - 2) + "\"");

    private static Task<IHost> StartHost(RequestDelegate endpoint, params object[] metadata) =>
        new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(services => services.AddRouting());
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseETag();
                app.UseEndpoints(endpoints =>
                    endpoints.MapMethods("/capture", [HttpMethods.Get, HttpMethods.Head], endpoint)
                        .WithMetadata(metadata));
            });
        }).StartAsync();
}
