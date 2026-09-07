using System.Net;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Event.Api.IntegrationTests.Features;

public sealed class WebhookPayloadNoStoreTests
{
    [Test]
    public async Task PayloadEndpoint_DeclaresAndEmitsNoStoreOnDeniedResponse()
    {
        var method = typeof(WebhookMessagesController).GetMethod(nameof(WebhookMessagesController.GetMessagePayload));
        var responseCache = method!
            .GetCustomAttributes(typeof(ResponseCacheAttribute), inherit: true)
            .Cast<ResponseCacheAttribute>()
            .Single();
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/webhooks/messages/{Guid.CreateVersion7():D}/payload");

        await Assert.That(responseCache.NoStore).IsTrue();
        await Assert.That(responseCache.Location).IsEqualTo(ResponseCacheLocation.None);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.CacheControl?.NoCache).IsTrue();
        await Assert.That(response.Headers.Pragma.Any(value => value.Name == "no-cache")).IsTrue();
    }
}
