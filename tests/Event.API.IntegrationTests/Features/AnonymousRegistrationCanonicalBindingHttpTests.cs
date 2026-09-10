
using System.Net;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;
using static Event.Api.IntegrationTests.Features.AnonymousRegistrationChallengeHttpTests;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class AnonymousRegistrationCanonicalBindingHttpTests
{
    [Test]
    [Arguments("quantity", "Quantity", false)]
    [Arguments("quantity", "Quantity", true)]
    [Arguments("quantity", "\\u0051uantity", false)]
    [Arguments("quantity", "\\u0051uantity", true)]
    [Arguments("quantity", "quantity", false)]
    [Arguments("quantity", "quantity", true)]
    [Arguments("bookingPartyType", "BookingPartyType", false)]
    [Arguments("bookingPartyType", "BookingPartyType", true)]
    public async Task ReorderedEffectiveMembersCannotAllocateOrDiscloseCachedAuthority(
        string property, string alias, bool committedFirst)
    {
        await using var host = await NativeHost.CreateAsync();
        string original = host.Body.Replace($"\"{property}\":1",
            $"\"{property}\":1,\"{alias}\":2", StringComparison.Ordinal);
        string changed = host.Body.Replace($"\"{property}\":1",
            $"\"{alias}\":2,\"{property}\":1", StringComparison.Ordinal);
        SolvedProof proof = await host.IssueAsync(host.Key, original);
        await Assert.That(await host.OrdersAsync()).Count().IsEqualTo(0);

        if (committedFirst)
        {
            using HttpResponseMessage originalStart = await host.StartAsync(host.Key, proof, body: original);
            await Assert.That(originalStart.StatusCode).IsEqualTo(HttpStatusCode.Created);
        }

        using HttpResponseMessage attack = await host.StartAsync(host.Key, proof, body: changed);
        await Assert.That(attack.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using JsonDocument problem = JsonDocument.Parse(await attack.Content.ReadAsStringAsync());
        await Assert.That(problem.RootElement.GetProperty("code").GetString())
            .IsEqualTo("anonymous_registration_challenge_invalid");
        await Assert.That(attack.Headers.Contains("X-Registration-Order-Capability")).IsFalse();
        await Assert.That(attack.Headers.Contains("X-Idempotency-Replay")).IsFalse();
        await Assert.That(attack.Headers.Location).IsNull();
        await Assert.That(await host.OrdersAsync()).Count().IsEqualTo(committedFirst ? 1 : 0);

        // Whitespace changes preserve the original ordering of duplicate effective members.
        using JsonDocument document = JsonDocument.Parse(original);
        string formatted = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
        using HttpResponseMessage accepted = await host.StartAsync(host.Key, proof, body: formatted);
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var order = (await host.OrdersAsync()).Single();
        await using AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope();
        var persisted = await scope.ServiceProvider.GetRequiredService<IRegistrationInventoryRepository>()
            .GetOrderWithLinesAsync(order.Id, PlatformDefaults.DefaultTenantId, CancellationToken.None);
        await Assert.That(persisted!.Lines.Single().Quantity).IsEqualTo(property == "quantity" ? 2 : 1);
        await Assert.That(persisted.BookingPartyTypeId).IsEqualTo(property == "bookingPartyType" ? 2 : 1);
    }

    [Test]
    public async Task UniqueMembersStillAllowHarmlessRootAndNestedOrderingChanges()
    {
        await using var host = await NativeHost.CreateAsync();
        SolvedProof proof = await host.IssueAsync(host.Key);
        using JsonDocument document = JsonDocument.Parse(host.Body);
        Guid catalog = document.RootElement.GetProperty("ticketCatalogVersionId").GetGuid();
        Guid ticket = document.RootElement.GetProperty("lines")[0].GetProperty("ticketTypeId").GetGuid();
        string changedFormatting = $$"""
            {
              "lines": [{ "quantity": 1, "chosenUnitPriceMinor": null, "ticketTypeId": "{{ticket:D}}" }],
              "platformContributionBasisPoints": null,
              "bookingPartyType": 1,
              "ticketCatalogVersionId": "{{catalog:D}}"
            }
            """;
        using HttpResponseMessage created = await host.StartAsync(host.Key, proof, body: changedFormatting);
        using HttpResponseMessage replay = await host.StartAsync(host.Key, proof);
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await Assert.That(replay.Headers.GetValues("X-Idempotency-Replay").Single()).IsEqualTo("true");
        await Assert.That(string.Equals(created.Headers.GetValues("X-Registration-Order-Capability").Single(),
            replay.Headers.GetValues("X-Registration-Order-Capability").Single(), StringComparison.Ordinal)).IsTrue();
        await Assert.That(await host.OrdersAsync()).Count().IsEqualTo(1);
    }
}
