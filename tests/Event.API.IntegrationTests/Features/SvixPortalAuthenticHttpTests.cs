using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Webhooks;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Svix;

namespace Event.API.IntegrationTests.Features;

/// <summary>Real Local login and HTTP pipeline; only external Svix/Cerbos transports are substituted.</summary>
[NotInParallel]
public sealed class SvixPortalAuthenticHttpTests
{
    [Test]
    [Arguments("valid", HttpStatusCode.OK)]
    [Arguments("provider_failure", HttpStatusCode.ServiceUnavailable)]
    [Arguments("audit_failure", HttpStatusCode.InternalServerError)]
    [Arguments("revoke", HttpStatusCode.Unauthorized)]
    [Arguments("options", HttpStatusCode.Conflict)]
    public async Task PortalHttpPreservesStatusNoStoreAndAuditWithGenuineSelectedSources(string condition, HttpStatusCode expected)
    {
        await using var portal = await HumanWebhookPreparationTests.PortalFixture.CreateAsync("local");
        await using var host = portal.Identity.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISvixWebhookClient>();
                services.AddSingleton(portal.Svix);
                services.RemoveAll<IOptionsMonitor<WebhookOptions>>();
                services.AddSingleton<IOptionsMonitor<WebhookOptions>>(portal.Options);
                services.RemoveAll<IHumanWebhookMutationPreparation>();
                services.AddScoped(portal.CreateHttpPreparation);
            }));
        using var client = host.CreateClient();
        if (condition == "provider_failure")
            portal.ProviderFailure = new ApiException(503, portal.Token);
        else if (condition == "audit_failure")
            portal.Boundaries.FailAudit = true;
        else if (condition == "revoke")
            portal.BeforeRemoteReturn = step => step == "create" ? portal.DriftAsync("revoke") : Task.CompletedTask;
        else if (condition == "options")
            portal.AtBoundary("audit", () => portal.DriftAsync("options"));

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/svix/app-portal")
        {
            Content = JsonContent.Create(new { consumerId = portal.Consumer.Id, expiresInSeconds = 300 })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await portal.Identity.SignInAsync());
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.Pragma.Any(value => value.Name == "no-cache")).IsTrue();
        if (condition == "valid")
        {
            var access = await response.Content.ReadFromJsonAsync<WebhookProviderPortalAccessDto>();
            await Assert.That(access!.Token).IsEqualTo(portal.Token);
            await Assert.That(access.Url).IsEqualTo(portal.Url);
        }
        else
        {
            var body = await response.Content.ReadAsStringAsync();
            await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
            await Assert.That(body).DoesNotContain(portal.Token, StringComparison.Ordinal);
            await Assert.That(body).DoesNotContain(portal.Url, StringComparison.Ordinal);
        }
        var audits = await portal.ReadAuditsAsync();
        await Assert.That(audits.Count).IsEqualTo(condition switch
        {
            "valid" or "provider_failure" => 1,
            "options" => 2,
            _ => 0
        });
        if (condition == "valid")
            await Assert.That(audits.Single().Action).IsEqualTo(WebhookAuditAction.PortalAccessIssued);
        if (condition == "provider_failure")
            await Assert.That(audits.Single().Action).IsEqualTo(WebhookAuditAction.PortalAccessRejected);
        foreach (var audit in audits)
        {
            await Assert.That(audit.SafeAfterJson).DoesNotContain(portal.Token, StringComparison.Ordinal);
            await Assert.That(audit.SafeAfterJson).DoesNotContain(portal.Url, StringComparison.Ordinal);
        }
    }
}
