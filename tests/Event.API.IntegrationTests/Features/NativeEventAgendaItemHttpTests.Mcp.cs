using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Explore.API.Mcp;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventAgendaItemHttpTests
{
    [Test]
    public async Task McpTransport_UsesManagedAgendaPortAndPreservesAuthorityAndDisclosureCeilings()
    {
        await using var factory = await AgendaFactory.CreateAsync();
        using var owner = factory.Client(factory.OwnerId);
        using var outsider = factory.Client(factory.OutsiderId);
        using var anonymous = factory.CreateClient();
        var before = await GetAsync(owner, ManagedDetail(factory.PublicId, factory.ItemId));
        foreach (var eventId in new[] { factory.PublicId, factory.PrivateId, factory.DraftId })
        {
            var rpc = await CallProgramToolAsync(owner, eventId);
            await Assert.That(rpc.TryGetProperty("error", out _)).IsFalse();
            var text = rpc.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
            var result = JsonSerializer.Deserialize<EventMcpProgramManagementResultDescriptor>(text, JsonOptions)!;
            await Assert.That(result.Found).IsTrue();
            await Assert.That(result.Available).IsTrue();
            await Assert.That(result.Context!.AgendaItemCount).IsEqualTo(1);
            await Assert.That(result.Context.AgendaItems.Single().EventId).IsEqualTo(eventId);
            await Assert.That(result.Context.AgendaItems.Single().Title).IsEqualTo("Seed agenda");
            await Assert.That(result.Context.AgendaItems.Single().StartsAtUtc).IsEqualTo(Start);
            foreach (var hidden in new[] { "Approved agenda venue", "Hidden street", "Hidden postcode", "LocationId", "RoomId", factory.LocationId.ToString() })
                await Assert.That(text).DoesNotContain(hidden);
        }
        foreach (var denied in new[]
        {
            await CallProgramToolAsync(anonymous, factory.PublicId),
            await CallProgramToolAsync(outsider, factory.PublicId),
            await CallProgramToolAsync(owner, factory.ForeignId),
            await CallProgramToolAsync(owner, factory.DeletedId),
            await CallProgramToolAsync(owner, Guid.CreateVersion7())
        })
        {
            bool isError = denied.TryGetProperty("error", out _) ||
                denied.GetProperty("result").TryGetProperty("isError", out var error) && error.GetBoolean();
            await Assert.That(isError).IsTrue();
            await Assert.That(denied.GetRawText()).DoesNotContain("Seed agenda");
            await Assert.That(denied.GetRawText()).DoesNotContain("Approved agenda venue");
        }
        await Assert.That(JsonElement.DeepEquals(await GetAsync(owner, ManagedDetail(factory.PublicId, factory.ItemId)), before)).IsTrue();
    }

    private static async Task<JsonElement> CallProgramToolAsync(HttpClient client, Guid eventId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new { name = "get_event_program_management_context", arguments = new { eventId } }
            })
        };
        request.Headers.Add("MCP-Protocol-Version", "2025-06-18");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        var json = body.TrimStart().StartsWith('{') ? body : body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Single(line => line.StartsWith("data:", StringComparison.Ordinal))[5..].Trim();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
