
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using static Event.Api.IntegrationTests.Features.AnonymousRegistrationChallengeHttpTests;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class GuestRegistrationStatusHttpTests
{
    [Test]
    public async Task Cancellation_UsesNativePostAndNeverReplaysCachedAuthority()
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        (Guid orderId, string capability) = await ConfirmAsync(host);
        string statusPath = StatusPath(host.EventId, orderId);
        using HttpResponseMessage initial = await SendAsync(host.Client, HttpMethod.Get, statusPath, capability);
        using JsonDocument before = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
        JsonElement link = before.RootElement.GetProperty("_links").GetProperty("cancel-registration");
        await Assert.That(link.GetProperty("method").GetString()).IsEqualTo("POST");
        string href = link.GetProperty("href").GetString()!;
        await Assert.That(new Uri(href).AbsolutePath).IsEqualTo(CancellationPath(host.EventId, orderId));
        await Assert.That(new Uri(href).Query).IsEqualTo(string.Empty);
        await Assert.That(href).DoesNotContain(capability);

        // GET cannot cancel, release a consumed place or revoke admission.
        using HttpResponseMessage get = await SendAsync(host.Client, HttpMethod.Get, href, capability);
        await Assert.That(get.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
        using HttpResponseMessage unchanged = await SendAsync(host.Client, HttpMethod.Get, statusPath, capability);
        using JsonDocument stillConfirmed = JsonDocument.Parse(await unchanged.Content.ReadAsStringAsync());
        await Assert.That(stillConfirmed.RootElement.GetProperty("registrationOrderStatusId").GetInt32())
            .IsEqualTo((int)RegistrationOrderStatusEnum.Confirmed);
        await Assert.That((await host.HoldsAsync(orderId)).Single().RegistrationInventoryHoldStatusId)
            .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Consumed);

        string key = Guid.CreateVersion7().ToString("N");
        using HttpResponseMessage cancelled = await CancelAsync(host.Client, href, capability, key);
        await AssertNoContentAsync(cancelled);
        using HttpResponseMessage current = await SendAsync(host.Client, HttpMethod.Get, statusPath, capability);
        using JsonDocument after = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
        await Assert.That(after.RootElement.GetProperty("registrationOrderStatusId").GetInt32())
            .IsEqualTo((int)RegistrationOrderStatusEnum.Cancelled);
        DateTime cancelledAt = after.RootElement.GetProperty("cancelledAt").GetDateTime();
        await Assert.That(after.RootElement.GetProperty("_links").TryGetProperty("cancel-registration", out _)).IsFalse();
        await Assert.That(after.RootElement.GetProperty("_links").TryGetProperty("self", out _)).IsTrue();
        await Assert.That(after.RootElement.GetProperty("_links").TryGetProperty("calendar", out _)).IsTrue();

        using HttpResponseMessage duplicate = await CancelAsync(host.Client, href, capability, key);
        await AssertNoContentAsync(duplicate);
        using HttpResponseMessage duplicateWithoutKey = await CancelAsync(host.Client, href, capability);
        await AssertNoContentAsync(duplicateWithoutKey);
        using HttpResponseMessage deniedReplay = await CancelAsync(host.Client, href, null, key);
        await AssertPrivateNotFound(deniedReplay);
        using HttpResponseMessage reread = await SendAsync(host.Client, HttpMethod.Get, statusPath, capability);
        using JsonDocument repeated = JsonDocument.Parse(await reread.Content.ReadAsStringAsync());
        await Assert.That(repeated.RootElement.GetProperty("cancelledAt").GetDateTime()).IsEqualTo(cancelledAt);
        await using AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope();
        await Assert.That(await scope.ServiceProvider.GetRequiredService<IIdempotencyRepository>()
            .FindAsync(key, PlatformDefaults.DefaultTenantId)).IsNull();
    }

    [Test]
    public async Task Cancellation_InvalidRouteTenantAndHeaderAuthorityAreIndistinguishable()
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        (Guid orderId, string capability) = await ConfirmAsync(host);
        string path = CancellationPath(host.EventId, orderId);
        using HttpResponseMessage missing = await CancelAsync(host.Client, path, null);
        await AssertPrivateNotFound(missing);
        using JsonDocument baseline = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        string? type = baseline.RootElement.GetProperty("type").GetString();
        foreach ((string route, string? proof) in new (string, string?)[]
        {
            (path, "malformed"),
            (path, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_')),
            (CancellationPath(Guid.CreateVersion7(), orderId), capability),
            (CancellationPath(host.EventId, Guid.CreateVersion7()), capability),
            (path + "?capability=" + Uri.EscapeDataString(capability), null)
        })
        {
            using HttpResponseMessage denied = await CancelAsync(host.Client, route, proof);
            await AssertPrivateNotFound(denied);
            using JsonDocument problem = JsonDocument.Parse(await denied.Content.ReadAsStringAsync());
            await Assert.That(problem.RootElement.GetProperty("type").GetString()).IsEqualTo(type);
            await Assert.That(problem.RootElement.GetProperty("title").GetString())
                .IsEqualTo(baseline.RootElement.GetProperty("title").GetString());
            await Assert.That(problem.RootElement.GetProperty("detail").GetString())
                .IsEqualTo(baseline.RootElement.GetProperty("detail").GetString());
            await Assert.That(await denied.Content.ReadAsStringAsync()).DoesNotContain(capability);
        }
        using var cookieRequest = new HttpRequestMessage(HttpMethod.Post, path);
        cookieRequest.Headers.Add("Cookie", $"{CapabilityHeader}={capability}");
        using HttpResponseMessage cookie = await host.Client.SendAsync(cookieRequest);
        await AssertPrivateNotFound(cookie);
        using var otherHeader = new HttpRequestMessage(HttpMethod.Post, path);
        otherHeader.Headers.Add("X-Registration-Attempt-Capability", capability);
        using HttpResponseMessage wrongHeader = await host.Client.SendAsync(otherHeader);
        await AssertPrivateNotFound(wrongHeader);

        Guid foreignTenant = await host.SeedTenantAsync();
        await using var replica = host.CreateReplica(foreignTenant);
        using HttpClient client = replica.CreateClient();
        using HttpResponseMessage foreign = await CancelAsync(client, path, capability);
        await AssertPrivateNotFound(foreign);
        using HttpResponseMessage status = await SendAsync(host.Client, HttpMethod.Get,
            StatusPath(host.EventId, orderId), capability);
        using JsonDocument current = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        await Assert.That(current.RootElement.GetProperty("registrationOrderStatusId").GetInt32())
            .IsEqualTo((int)RegistrationOrderStatusEnum.Confirmed);
    }

    [Test]
    public async Task Cancellation_ExpiredPromiseCannotReplaySuccessfulResponse()
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        (Guid orderId, string capability) = await ConfirmAsync(host);
        DateTimeOffset deadline = await ReadDeadlineAsync(host, orderId, capability);
        string path = CancellationPath(host.EventId, orderId);
        string key = Guid.CreateVersion7().ToString("N");
        using HttpResponseMessage success = await CancelAsync(host.Client, path, capability, key);
        await AssertNoContentAsync(success);
        host.Clock.Advance(deadline - host.Clock.GetUtcNow());
        using HttpResponseMessage expired = await CancelAsync(host.Client, path, capability, key);
        await AssertPrivateNotFound(expired);
    }

    [Test]
    public async Task Cancellation_NativeLocalSessionDoesNotReplaceLimitedGuestAuthority()
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        (Guid orderId, string capability) = await ConfirmAsync(host);
        string token = await host.LoginAsync();
        host.Client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using HttpResponseMessage missing = await CancelAsync(host.Client,
            CancellationPath(host.EventId, orderId), null);
        await AssertPrivateNotFound(missing);
        using HttpResponseMessage authorized = await CancelAsync(host.Client,
            CancellationPath(host.EventId, orderId), capability);
        await AssertNoContentAsync(authorized);
    }

    private static string CancellationPath(Guid eventId, Guid orderId) =>
        $"/api/events/{eventId}/guest-registration-orders/{orderId}/cancellation";

    private static async Task<HttpResponseMessage> CancelAsync(HttpClient client, string path,
        string? capability, string? key = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (capability is not null) request.Headers.Add(CapabilityHeader, capability);
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task AssertNoContentAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent)
            .Because(await response.Content.ReadAsStringAsync());
        await AssertPrivate(response);
        await Assert.That(await response.Content.ReadAsByteArrayAsync()).IsEmpty();
        await Assert.That(response.Headers.Contains("X-Idempotency-Replay")).IsFalse();
        await Assert.That(response.Headers.Contains(CapabilityHeader)).IsFalse();
    }
}
