using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Mcp;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class EventCustomPropertyNativeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task McpToolsCall_OwnerReadsBoundedPublicOrPrivateContext_WithoutGrantingOutsiderOrForeignAccess(bool privateEvent)
    {
        await using var factory = await EventPropertyFactory.CreateAsync();
        using var admin = factory.CreateClient();
        var data = await SeedAsync(factory, admin);
        Guid eventId;
        Guid definitionId;
        Guid outsiderId;
        using (var scope = Scope(factory, PlatformDefaults.DefaultTenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var actor = await db.Actors.SingleAsync(row => row.UserId == data.MemberId);
            var outsider = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
            outsiderId = outsider.UserId;
            var parent = await EventScenarioSeed.SeedPublishedEventAsync(db, actor.Id, PlatformDefaults.DefaultTenantId);
            eventId = parent.EventId;
            (await db.Events.SingleAsync(row => row.Id == eventId)).VisibilityTypeId =
                (int)(privateEvent ? VisibilityTypeEnum.Private : VisibilityTypeEnum.Public);
            var definition = Definition(PlatformDefaults.DefaultTenantId, eventId, "managed-internal", 0);
            definitionId = definition.Id;
            definition.Description = "Description outside management descriptor";
            db.EventCustomPropertyDefinitions.Add(definition);
            db.EventCustomPropertyValues.Add(new EventCustomPropertyValue
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                EventId = eventId,
                EventCustomPropertyDefinitionId = definitionId,
                TextValue = new string('x', 500) + "TRUNCATED_TAIL",
                CreatedBy = data.MemberId
            });
            await db.SaveChangesAsync();
        }
        using var owner = factory.CreateClient();
        owner.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(data.MemberId));
        using var outsiderClient = factory.CreateClient();
        outsiderClient.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(outsiderId));
        using var anonymous = factory.CreateClient();
        using var rest = await owner.GetAsync($"/api/event/{eventId}/management-detail");
        await Assert.That(rest.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var hal = JsonDocument.Parse(await rest.Content.ReadAsStringAsync());
        await Assert.That(hal.RootElement.GetProperty("_links").TryGetProperty("edit", out _)).IsTrue();
        var rpc = await CallCustomPropertiesAsync(owner, eventId);
        await Assert.That(rpc.TryGetProperty("error", out _)).IsFalse();
        var text = rpc.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        var result = JsonSerializer.Deserialize<EventMcpCustomPropertiesResultDescriptor>(text, JsonOptions)!;
        await Assert.That(result.Found).IsTrue();
        await Assert.That(result.Available).IsTrue();
        using var json = JsonDocument.Parse(text);
        var context = json.RootElement.GetProperty("Context");
        await Assert.That(context.GetProperty("PageSize").GetInt32()).IsEqualTo(25);
        await Assert.That(context.GetProperty("PageSizeWasClamped").GetBoolean()).IsTrue();
        await Assert.That(context.GetProperty("TotalDefinitionCount").GetInt32()).IsEqualTo(1);
        await Assert.That(context.GetProperty("Definitions")[0].GetProperty("DefinitionId").GetGuid()).IsEqualTo(definitionId);
        await Assert.That(context.GetProperty("ValueCount").GetInt32()).IsEqualTo(1);
        await Assert.That(context.GetProperty("Values")[0].GetProperty("Value").GetString()).IsEqualTo(new string('x', 500));
        foreach (var hidden in new[] { "TenantId", "CreatedBy", "Description", "TRUNCATED_TAIL", data.MemberId.ToString() })
            await Assert.That(text).DoesNotContain(hidden);

        // A genuinely warm foreign cache must not change the management gate's denial.
        await Assert.That((await ListAsync(factory, data.ForeignTenantId, data.ForeignEventId, 1, 25)).Items.Single().Id)
            .IsEqualTo(data.ForeignDefinitionId);
        foreach (var denied in new[]
        {
            await CallCustomPropertiesAsync(anonymous, eventId),
            await CallCustomPropertiesAsync(outsiderClient, eventId),
            await CallCustomPropertiesAsync(owner, data.ForeignEventId),
            await CallCustomPropertiesAsync(owner, Guid.CreateVersion7())
        })
        {
            var isError = denied.TryGetProperty("error", out _) ||
                denied.GetProperty("result").TryGetProperty("isError", out var error) && error.GetBoolean();
            await Assert.That(isError).IsTrue();
            foreach (var hidden in new[] { "managed-internal", "foreign-internal", definitionId.ToString(), data.ForeignDefinitionId.ToString() })
                await Assert.That(denied.GetRawText()).DoesNotContain(hidden);
        }
        await Assert.That((await ValuesAsync(factory, PlatformDefaults.DefaultTenantId, eventId)).Single().TextValue)
            .IsEqualTo(new string('x', 500) + "TRUNCATED_TAIL");
    }

    private static async Task<JsonElement> CallCustomPropertiesAsync(HttpClient client, Guid eventId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = JsonContent.Create(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "tools/call",
                @params = new { name = "get_event_custom_properties_context", arguments = new { eventId, pageSize = 999 } }
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
