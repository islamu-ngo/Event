using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class WebhookEndpointHalLinkTests
{
    [Test]
    public async Task GeneratedWebhookEndpointResource_WhenActionLinksExist_ExposesUpdateRotateTestAndDeleteAffordances()
    {
        var endpoint = new HalResourceOfWebhookEndpointDto
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            ConsumerId = Guid.CreateVersion7(),
            DestinationHost = "integrator.example",
            StatusName = "Active"
        };

        GeneratedHalLinkTestHelper.SetLinks(
            endpoint,
            ("update", "/api/webhooks/endpoints/018f0000-0000-7000-8000-000000000001", "PUT"),
            ("rotate-secret", "/api/webhooks/endpoints/018f0000-0000-7000-8000-000000000001/rotate-secret", "POST"),
            ("test", "/api/webhooks/endpoints/018f0000-0000-7000-8000-000000000001/test", "POST"),
            ("delete", "/api/webhooks/endpoints/018f0000-0000-7000-8000-000000000001", "DELETE"));

        await Assert.That(endpoint._links).IsNotNull();
        await Assert.That(endpoint._links!).ContainsKey("update");
        await Assert.That(endpoint._links!).ContainsKey("rotate-secret");
        await Assert.That(endpoint._links!).ContainsKey("test");
        await Assert.That(endpoint._links!).ContainsKey("delete");
        await Assert.That(endpoint._links!["update"].Method).IsEqualTo("PUT");
        await Assert.That(endpoint._links!["rotate-secret"].Method).IsEqualTo("POST");
        await Assert.That(endpoint._links!["test"].Method).IsEqualTo("POST");
        await Assert.That(endpoint._links!["delete"].Method).IsEqualTo("DELETE");
    }
}
