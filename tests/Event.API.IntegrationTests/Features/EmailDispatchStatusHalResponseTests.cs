using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class EmailDispatchStatusHalResponseTests
{
    [Test]
    public async Task StatusSerializesCompletedHalAfterAsynchronousRealLinkAuthorization()
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        Guid id = await factory.SeedDispatchAsync(EmailDispatchStatus.Parked);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var delayed = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuthorizationProvider>();
            services.AddScoped<IAuthorizationProvider>(provider => new DelayedLinkAuthorization(
                provider.GetRequiredService<RuntimeAuthorizationProvider>(), entered, release));
        }));
        using var client = delayed.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(factory.TenantAdminId));
        client.DefaultRequestHeaders.Accept.ParseAdd("application/hal+json");
        string url = $"/api/admin/email-dispatch/status?tenantId={PlatformDefaults.DefaultTenantId}&limit=1";
        var pending = client.GetAsync(url);
        try
        {
            // The production assembler is now suspended inside its real permission batch.
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { release.TrySetResult(); }
        using var response = await pending.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/hal+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var self = root.GetProperty("_links").GetProperty("self").GetProperty("href").GetString()!;
        var selfUri = new Uri(new Uri("https://integration.test"), self);
        await Assert.That(selfUri.AbsolutePath).IsEqualTo("/api/admin/email-dispatch/status");
        var query = QueryHelpers.ParseQuery(selfUri.Query);
        await Assert.That(query["tenantId"].ToString()).IsEqualTo(PlatformDefaults.DefaultTenantId.ToString());
        await Assert.That(query["limit"].ToString()).IsEqualTo("1");
        var rows = root.GetProperty("_embedded").GetProperty("items");
        await Assert.That(rows.GetArrayLength()).IsEqualTo(1);
        var row = rows[0];
        await Assert.That(row.GetProperty("outboxId").GetGuid()).IsEqualTo(id);
        await Assert.That(row.GetProperty("tenantId").GetGuid()).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(row.GetProperty("deliveryStatus").GetString()).IsEqualTo("Parked");
        var replay = row.GetProperty("_links").GetProperty("replay");
        await Assert.That(replay.GetProperty("href").GetString())
            .IsEqualTo($"/api/admin/email-dispatch/tenants/{PlatformDefaults.DefaultTenantId}/outbox/{id}/replay");
        await Assert.That(replay.GetProperty("method").GetString()).IsEqualTo("POST");
        foreach (string taskField in new[] { "result", "id", "status", "isCanceled", "isCompleted", "isCompletedSuccessfully", "creationOptions", "isFaulted", "exception" })
            await Assert.That(root.TryGetProperty(taskField, out _)).IsFalse();
        foreach (string privateField in new[] { "recipientEmail", "subject", "plainTextBody", "htmlBody", "providerMessageId", "lastError" })
            await Assert.That(row.TryGetProperty(privateField, out _)).IsFalse();
    }

    private sealed class DelayedLinkAuthorization(
        IAuthorizationProvider inner,
        TaskCompletionSource entered,
        TaskCompletionSource release) : IAuthorizationProvider
    {
        public Task<AuthorizationDecision> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken = default) =>
            inner.AuthorizeAsync(request, cancellationToken);

        public async Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
            IReadOnlyList<AuthorizationRequest> requests, CancellationToken cancellationToken = default)
        {
            if (requests.Any(request => request.ResourceKind == ResourceKinds.EmailDispatch))
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }
            return await inner.AuthorizeBatchAsync(requests, cancellationToken);
        }
    }
}
