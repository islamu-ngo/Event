using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.PrivacyErasure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Privacy;

public sealed class PrivacyErasureReceiptApiTests
{
    [Test]
    public async Task ValidReceipt_ReturnsBoundedNoStoreStatus()
    {
        await using var factory = new ReceiptFactory();
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/privacy-erasure/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("ErasureReceipt", ReceiptService.ValidReceipt);

        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();
        PrivacyErasureStatusDto? status = await response.Content.ReadFromJsonAsync<PrivacyErasureStatusDto>();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(status?.Status).IsEqualTo("completed");
        await Assert.That(body).DoesNotContain(ReceiptService.SubjectCanary);
    }

    [Test]
    [Arguments(null)]
    [Arguments("invalid")]
    public async Task MissingOrInvalidReceipt_ReturnsUnauthorized(string? receipt)
    {
        await using var factory = new ReceiptFactory();
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/privacy-erasure/status");
        if (receipt is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("ErasureReceipt", receipt);
        }

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    private sealed class ReceiptFactory : AuthenticatedWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPrivacyErasureService>();
                services.AddScoped<IPrivacyErasureService, ReceiptService>();
            });
        }
    }

    private sealed class ReceiptService : IPrivacyErasureService
    {
        public const string ValidReceipt = "valid-receipt";
        public const string SubjectCanary = "subject-identifier-canary";
        private static readonly Guid IntentId = Guid.CreateVersion7();

        public Task<PrivacyErasureStartDto> EraseUserAsync(
            Guid userId,
            Guid intentId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Guid?> AuthenticateReceiptAsync(string receipt, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(receipt == ValidReceipt ? IntentId : null);

        public Task<PrivacyErasureStatusDto?> GetStatusAsync(Guid intentId, CancellationToken cancellationToken) =>
            Task.FromResult<PrivacyErasureStatusDto?>(intentId == IntentId
                ? new PrivacyErasureStatusDto(
                    "completed",
                    0,
                    0,
                    DateTime.UtcNow.AddDays(1),
                    DateTime.UtcNow,
                    DateTime.UtcNow)
                : null);

        public Task ReplayPendingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
