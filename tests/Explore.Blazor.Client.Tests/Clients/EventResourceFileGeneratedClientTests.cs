using System.Net;
using System.Net.Http.Headers;

namespace Explore.Blazor.Client.Tests.Clients;

public sealed class EventResourceFileGeneratedClientTests
{
    [Test]
    public async Task GeneratedDownloadReadsBinaryInsteadOfDeserializingAnMvcResult()
    {
        Guid id = Guid.CreateVersion7();
        byte[] bytes = "%PDF-1.7\nbinary resource response\n%%EOF"u8.ToArray();
        using var handler = new BinaryResponseHandler(bytes);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.example.test") };
        var client = new EventResourcesClient(http);
        using FileResponse response = await client.GetEventResourceContentAsync(id);
        using var body = new MemoryStream();
        await response.Stream.CopyToAsync(body);
        await Assert.That(response.StatusCode).IsEqualTo(200);
        await Assert.That(body.ToArray()).IsEquivalentTo(bytes);
        await Assert.That(handler.RequestPath).IsEqualTo($"/api/eventresource/{id:D}/content");
    }

    private sealed class BinaryResponseHandler(byte[] bytes) : HttpMessageHandler
    {
        public string? RequestPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestPath = request.RequestUri!.AbsolutePath;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            return Task.FromResult(response);
        }
    }
}
