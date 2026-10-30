using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class TicketTransferHandlerNativeAuthenticationTests
{
    [Test]
    public async Task ExpiredCapabilityReadStopsBeforeDigestMatching()
    {
        await using var fixture = await TicketingNativeAuthenticationFixture.CreateAsync();
        var seed = await fixture.SeedOrderAsync(transfer: true);
        var offered = await SeedExpiredOffer(fixture, seed);
        string before = await Snapshot(fixture, seed);

        using (HttpResponseMessage anonymous = await Read(fixture, seed, offered.Id, offered.Capability, null))
            await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        var unrelated = await fixture.Http.Native.SeedLocalUserAsync(true);
        using (HttpResponseMessage stranger = await Read(fixture, seed, offered.Id, offered.Capability,
            await fixture.Http.SignInAsync(unrelated)))
            await Assert.That(stranger.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using (HttpResponseMessage related = await Read(fixture, seed, offered.Id, null, await fixture.Http.SignInAsync()))
            await Assert.That(related.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await Snapshot(fixture, seed)).IsEqualTo(before);
    }

    private static async Task<(Guid Id, string Capability)> SeedExpiredOffer(TicketingNativeAuthenticationFixture fixture,
        TicketingNativeAuthenticationFixture.OrderSeed seed)
    {
        await using var scope = fixture.Http.Host.Services.CreateAsyncScope();
        var capability = scope.ServiceProvider.GetRequiredService<IGuestCapabilityTokenService>().Issue();
        await using var database = fixture.Database();
        var ticket = await database.AdmissionTickets.Include(row => row.Credentials)
            .SingleAsync(row => row.Id == seed.TicketId);
        var policy = await database.TicketTransferPolicies.SingleAsync(row => row.EventTicketTypeId == ticket.EventTicketTypeId);
        DateTime now = fixture.Http.Clock.GetUtcNow().UtcDateTime;
        await Assert.That(ticket.CreatedAt <= now.AddMinutes(-61)).IsTrue();
        var transfer = AdmissionTicketTransfer.Offer(Guid.CreateVersion7(), ticket, policy, Guid.CreateVersion7(),
            capability.Hash.Value, now.AddDays(2), now.AddMinutes(-61));
        database.AdmissionTicketTransfers.Add(transfer);
        await database.SaveChangesAsync();
        await Assert.That(transfer.ExpiresAt).IsEqualTo(now.AddMinutes(-1));
        await Assert.That(transfer.CapabilityDigest)
            .IsEqualTo(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(capability.RawToken))));
        return (transfer.Id, capability.RawToken);
    }

    [Test]
    public async Task ExistingOfferDoesNotRevealNewUnmatchedClaim()
    {
        await using var fixture = await TicketingNativeAuthenticationFixture.CreateAsync();
        var seed = await fixture.SeedOrderAsync(transfer: true);
        string owner = await fixture.Http.SignInAsync();
        var offered = await Offer(fixture, seed, owner);
        string before = await Snapshot(fixture, seed);

        using var request = Authorized(HttpMethod.Post, Path(seed), owner);
        using HttpResponseMessage repeated = await fixture.Http.Client.SendAsync(request);
        await Assert.That(repeated.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using JsonDocument body = JsonDocument.Parse(await repeated.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.TryGetProperty("claimCapability", out _)).IsFalse();
        await Assert.That(await Snapshot(fixture, seed)).IsEqualTo(before);
        using HttpResponseMessage original = await Read(fixture, seed, offered.Id, offered.Capability, null);
        await Assert.That(original.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    private static async Task<(Guid Id, string Capability)> Offer(TicketingNativeAuthenticationFixture fixture,
        TicketingNativeAuthenticationFixture.OrderSeed seed, string owner)
    {
        using var request = Authorized(HttpMethod.Post, Path(seed), owner);
        using HttpResponseMessage response = await fixture.Http.Client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string capability = body.RootElement.GetProperty("claimCapability").GetString()!;
        await using var observer = fixture.Database();
        var transfer = await observer.AdmissionTicketTransfers.AsNoTracking().SingleAsync(row => row.AdmissionTicketId == seed.TicketId);
        await Assert.That(transfer.CapabilityDigest).IsEqualTo(Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(capability))));
        await Assert.That(transfer.StatusId).IsEqualTo((int)AdmissionTicketTransferStatus.Offered);
        return (transfer.Id, capability);
    }

    private static async Task<string> Snapshot(TicketingNativeAuthenticationFixture fixture,
        TicketingNativeAuthenticationFixture.OrderSeed seed)
    {
        await using var observer = fixture.Database();
        var transfers = await observer.AdmissionTicketTransfers.AsNoTracking()
            .Where(row => row.AdmissionTicketId == seed.TicketId).ToArrayAsync();
        await Assert.That(transfers.Length).IsEqualTo(1);
        var ticket = await observer.AdmissionTickets.AsNoTracking().Include(row => row.Credentials)
            .SingleAsync(row => row.Id == seed.TicketId);
        return JsonSerializer.Serialize(new { Transfers = transfers, Ticket = ticket });
    }

    private static async Task<HttpResponseMessage> Read(TicketingNativeAuthenticationFixture fixture,
        TicketingNativeAuthenticationFixture.OrderSeed seed, Guid id, string? capability, string? token)
    {
        using var request = Authorized(HttpMethod.Get, $"{Path(seed)}/{id}", token);
        if (capability is not null) request.Headers.Add("X-Ticket-Transfer-Capability", capability);
        return await fixture.Http.Client.SendAsync(request);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string? token)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string Path(TicketingNativeAuthenticationFixture.OrderSeed seed) =>
        $"/api/events/{seed.EventId}/admission-tickets/{seed.TicketId}/transfers";
}
