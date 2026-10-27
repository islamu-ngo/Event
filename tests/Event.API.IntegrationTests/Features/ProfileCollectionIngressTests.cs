using System.Security.Claims;
using Explore.API.Attributes;
using Explore.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Event.API.IntegrationTests.Features;

public sealed class ProfileCollectionIngressTests
{
    [Test]
    public async Task DownstreamStartCompleteAndPipeWritesCannotPublishBeforeTheOwnerCommits()
    {
        var context = new DefaultHttpContext();
        using var output = new MemoryStream();
        var original = new StreamResponseBodyFeature(output);
        context.Features.Set<IHttpResponseBodyFeature>(original);
        using (var buffer = new ProfileCollectionResponseBuffer(context))
        {
            await context.Response.StartAsync();
            await context.Response.BodyWriter.WriteAsync(new byte[] { 1, 2, 3 });
            await context.Response.CompleteAsync();
            await Assert.That(output.Length).IsEqualTo(0L);
            await buffer.SealAsync(CancellationToken.None);
            await buffer.DeliverAsync(CancellationToken.None);
        }
        await Assert.That(context.Features.Get<IHttpResponseBodyFeature>()).IsEqualTo(original);
        await Assert.That(output.ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3 });
    }

    [Test]
    public async Task OutstandingPipeOutputMustPassTheLimitBeforeTheNativeConsumerReturns()
    {
        var context = new DefaultHttpContext();
        using var output = new MemoryStream();
        context.Response.Body = output;
        using var buffer = new ProfileCollectionResponseBuffer(context);
        context.Response.BodyWriter.GetMemory(65537).Span[..65537].Fill(1);
        context.Response.BodyWriter.Advance(65537);
        await Assert.That(async () => await buffer.SealAsync(CancellationToken.None))
            .Throws<InvalidOperationException>();
        await Assert.That(output.Length).IsEqualTo(0L);
        await Assert.That(async () => await buffer.DeliverAsync(CancellationToken.None))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task FailedOrOversizedBufferedOutputCannotEscapeToTheClient()
    {
        var context = new DefaultHttpContext();
        using var output = new MemoryStream();
        context.Response.Body = output;
        using (var buffer = new ProfileCollectionResponseBuffer(context))
        {
            await Assert.That(async () => await context.Response.Body.WriteAsync(new byte[65537]))
                .Throws<InvalidOperationException>();
        }
        await Assert.That(output.Length).IsEqualTo(0L);
    }

    [Test]
    [Arguments(false, null, 401)]
    [Arguments(true, null, 400)]
    [Arguments(true, "{", 400)]
    [Arguments(true, "{\"assessment\":null,\"callerIsQualified\":true}", 400)]
    public async Task RejectedIngressCannotReadOrBindTheProfileBody(bool authenticated, string? nomination, int status)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        using var body = new UnreadableProfileBody();
        context.Request.Body = body;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new ProfileCollectionAdmissionAttribute()), "profile collection"));
        if (authenticated)
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())], "test"));
        if (nomination is not null) context.Request.Headers[ProfileCollectionAdmissionAttribute.HeaderName] = nomination;
        var middleware = new ProfileCollectionAdmissionMiddleware(_ =>
            throw new InvalidOperationException("The body consumer must not be invoked."));
        await middleware.InvokeAsync(context, null!, Options.Create(new JsonOptions()));
        await Assert.That(context.Response.StatusCode).IsEqualTo(status);
        await Assert.That(body.ReadAttempts).IsEqualTo(0);
    }

    private sealed class UnreadableProfileBody : MemoryStream
    {
        public int ReadAttempts { get; private set; }
        public override int Read(byte[] buffer, int offset, int count)
        { ReadAttempts++; throw new InvalidOperationException("Profile body was read before admission."); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { ReadAttempts++; throw new InvalidOperationException("Profile body was read before admission."); }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        { ReadAttempts++; throw new InvalidOperationException("Profile body was read before admission."); }
    }
}
