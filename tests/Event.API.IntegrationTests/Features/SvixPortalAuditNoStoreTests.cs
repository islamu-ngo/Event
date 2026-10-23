using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Authentication;
using Explore.API.Controllers;
using Explore.Domain;
using Microsoft.AspNetCore.Mvc;
using Svix;

namespace Event.API.IntegrationTests.Features;

[NotInParallel]
public sealed class SvixPortalAuditNoStoreTests
{
    [Test]
    public async Task PortalEndpoint_DeclaresNoStoreAndWriteMiddlewareEnforcesIt()
    {
        var method = typeof(WebhooksController).GetMethod(nameof(WebhooksController.OpenSvixAppPortal));
        var responseCache = method!
            .GetCustomAttributes(typeof(ResponseCacheAttribute), inherit: true)
            .Cast<ResponseCacheAttribute>()
            .Single();
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/svix/app-portal")
        {
            Content = JsonContent.Create(new
            {
                consumerId = Guid.CreateVersion7(),
                expiresInSeconds = 300
            })
        };
        using var response = await client.SendAsync(request);

        await Assert.That(responseCache.NoStore).IsTrue();
        await Assert.That(responseCache.Location).IsEqualTo(ResponseCacheLocation.None);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.Pragma.Any(value => value.Name == "no-cache")).IsTrue();
    }

    [Test]
    public async Task AuditFailure_ProducesNoPortalResponse()
    {
        await using var portal = await HumanWebhookPreparationTests.PortalFixture.CreateAsync();
        portal.Boundaries.FailAudit = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => portal.ExecuteAsync());
        await Assert.That(portal.CreateCalls).IsEqualTo(1);
        await Assert.That(await portal.ReadAuditsAsync()).IsEmpty();
    }

    [Test]
    public async Task SuccessfulAudit_ContainsSafeMetadataOnly()
    {
        await using var portal = await HumanWebhookPreparationTests.PortalFixture.CreateAsync();
        var result = await portal.ExecuteAsync();
        var audit = (await portal.ReadAuditsAsync()).Single();

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id!.Token).IsEqualTo(portal.Token);
        await Assert.That(audit.Action).IsEqualTo(WebhookAuditAction.PortalAccessIssued);
        await Assert.That(audit.SafeAfterJson).Contains(portal.Binding.Id.ToString("D"), StringComparison.OrdinalIgnoreCase);
        await Assert.That(audit.ConfigurationVersion).IsEqualTo("svix-1.96.1-v1");
        await Assert.That(audit.SafeAfterJson).DoesNotContain(portal.Url, StringComparison.Ordinal);
        await Assert.That(audit.SafeAfterJson).DoesNotContain(portal.Token, StringComparison.Ordinal);
    }

    [Test]
    public async Task ProviderFailure_IsAuditedWithSafeCategoryBeforeResponse()
    {
        await using var portal = await HumanWebhookPreparationTests.PortalFixture.CreateAsync();
        portal.ProviderFailure = new ApiException(503, portal.Token);
        var result = await portal.ExecuteAsync();
        var audit = (await portal.ReadAuditsAsync()).Single();

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.IsRetryable).IsTrue();
        await Assert.That(result.Id).IsNull();
        await Assert.That(audit.SafeAfterJson).Contains("provider_failure", StringComparison.Ordinal);
        await Assert.That(audit.SafeAfterJson).Contains("svix_provider_unavailable", StringComparison.Ordinal);
        await Assert.That(audit.SafeAfterJson).DoesNotContain("SvixApi:503", StringComparison.Ordinal);
        await Assert.That(audit.SafeAfterJson).DoesNotContain(portal.Token, StringComparison.Ordinal);
    }
}
