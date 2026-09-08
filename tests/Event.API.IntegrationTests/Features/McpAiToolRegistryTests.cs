using System.Text.Json;
using Explore.API.Mcp;
using Explore.Application.Authorization;
using Explore.Application.Features.AiAssistant.Tools;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class McpAiToolRegistryTests
{
    [Test]
    public async Task ListAiToolContracts_ReturnsSafeRegistryBackedContracts()
    {
        var tool = new AiToolRegistryMcpTools(AiToolContractRegistry.CreateDefault());

        var json = tool.ListAiToolContracts();

        using var document = JsonDocument.Parse(json);
        var tools = document.RootElement.GetProperty("Tools");
        var expectedMcpToolNames = AiToolContractRegistry.CreateDefault()
            .Definitions
            .Where(definition => definition.ExposeToMcp)
            .Select(AiMcpProjectedToolFactory.BuildToolName)
            .ToArray();
        await Assert.That(tools.GetArrayLength()).IsEqualTo(expectedMcpToolNames.Length);

        await Assert.That(tools.EnumerateArray()
                .Select(tool => tool.GetProperty("McpToolName").GetString()))
            .IsEquivalentTo(expectedMcpToolNames, TUnit.Assertions.Enums.CollectionOrdering.Any);

        var createEventDraft = tools.EnumerateArray().Single(tool =>
            tool.GetProperty("Name").GetString() == "CreateEventDraft");
        await Assert.That(createEventDraft.GetProperty("Name").GetString()).IsEqualTo("CreateEventDraft");
        await Assert.That(createEventDraft.GetProperty("McpToolName").GetString()).IsEqualTo("propose_create_event_draft");
        await Assert.That(createEventDraft.GetProperty("ConfirmationMode").GetString()).IsEqualTo("Required");
        await Assert.That(createEventDraft.GetProperty("AllowedPayloadFields")
            .EnumerateArray()
            .Select(value => value.GetString())).Contains("title");
        await Assert.That(createEventDraft.GetProperty("ForbiddenPayloadFields")
            .EnumerateArray()
            .Select(value => value.GetString())).Contains("tenantId");
        await Assert.That(createEventDraft.GetProperty("RequiredAuthorization").GetProperty("Action").GetString()).IsEqualTo("create");

        var updateEventDraft = tools.EnumerateArray().Single(tool =>
            tool.GetProperty("Name").GetString() == "UpdateEventDraft");
        await Assert.That(updateEventDraft.GetProperty("McpToolName").GetString()).IsEqualTo("propose_update_event_draft");
        await Assert.That(new[] { "eventId", "expectedConcurrencyStamp", "title" }.All(updateEventDraft.GetProperty("AllowedPayloadFields")
            .EnumerateArray()
            .Select(value => value.GetString()).Contains)).IsTrue();
        await Assert.That(new[] { "tenantId", "actorId", "eventStatusId", "sessions" }.All(updateEventDraft.GetProperty("ForbiddenPayloadFields")
            .EnumerateArray()
            .Select(value => value.GetString()).Contains)).IsTrue();
        await Assert.That(updateEventDraft.GetProperty("RequiredAuthorization").GetProperty("Action").GetString()).IsEqualTo("update");

        var publishEvent = tools.EnumerateArray().Single(tool =>
            tool.GetProperty("Name").GetString() == "PublishEvent");
        await Assert.That(publishEvent.GetProperty("McpToolName").GetString()).IsEqualTo("propose_publish_event");
        await Assert.That(new[] { "eventId", "expectedConcurrencyStamp", "readinessIsReady", "readinessErrorCount" }.All(publishEvent.GetProperty("AllowedPayloadFields")
            .EnumerateArray()
            .Select(value => value.GetString()).Contains)).IsTrue();
        await Assert.That(new[] { "tenantId", "actorId", "eventStatusId", "publishedAt", "outboxMessages" }.All(publishEvent.GetProperty("ForbiddenPayloadFields")
            .EnumerateArray()
            .Select(value => value.GetString()).Contains)).IsTrue();
        await Assert.That(publishEvent.GetProperty("RequiredAuthorization").GetProperty("Action").GetString()).IsEqualTo("update");

        var deleteEvent = tools.EnumerateArray().Single(tool =>
            tool.GetProperty("Name").GetString() == "DeleteEvent");
        await Assert.That(deleteEvent.GetProperty("McpToolName").GetString()).IsEqualTo("propose_delete_event");
        await Assert.That(new[] { "eventId", "expectedConcurrencyStamp", "managementContextHasDelete", "confirmationPhrase" }.All(deleteEvent.GetProperty("AllowedPayloadFields")
            .EnumerateArray()
            .Select(value => value.GetString()).Contains)).IsTrue();
        await Assert.That(new[] { "tenantId", "actorId", "eventStatusId", "sessions", "concurrencyStamp" }.All(deleteEvent.GetProperty("ForbiddenPayloadFields")
            .EnumerateArray()
            .Select(value => value.GetString()).Contains)).IsTrue();
        await Assert.That(deleteEvent.GetProperty("RequiredAuthorization").GetProperty("Action").GetString()).IsEqualTo("delete");

        var upsertIslamicAspect = tools.EnumerateArray().Single(tool =>
            tool.GetProperty("Name").GetString() == "UpsertEventIslamicAspect");
        await Assert.That(upsertIslamicAspect.GetProperty("McpToolName").GetString()).IsEqualTo("propose_upsert_event_islamic_aspect");
        await Assert.That(new[] { "aspectKind", "managementContextHasEdit", "genderMode" }.All(upsertIslamicAspect.GetProperty("AllowedPayloadFields")
            .EnumerateArray()
            .Select(value => value.GetString()).Contains)).IsTrue();

        var deleteTechAspect = tools.EnumerateArray().Single(tool =>
            tool.GetProperty("Name").GetString() == "DeleteEventTechAspect");
        await Assert.That(deleteTechAspect.GetProperty("McpToolName").GetString()).IsEqualTo("propose_delete_event_tech_aspect");
        await Assert.That(new[] { "aspectKind", "managementContextHasEdit", "confirmationPhrase", "acknowledgedConsequences" }.All(deleteTechAspect.GetProperty("AllowedPayloadFields")
            .EnumerateArray()
            .Select(value => value.GetString()).Contains)).IsTrue();

        var createSession = tools.EnumerateArray().Single(tool =>
            tool.GetProperty("Name").GetString() == "CreateEventSession");
        await Assert.That(createSession.GetProperty("McpToolName").GetString()).IsEqualTo("propose_create_event_session");
        await Assert.That(new[] { "eventId", "expectedConcurrencyStamp", "managementContextHasAddSession", "title", "startTime", "endTime" }.All(createSession.GetProperty("AllowedPayloadFields")
            .EnumerateArray()
            .Select(value => value.GetString()).Contains)).IsTrue();
        await Assert.That(new[] { "tenantId", "actorId", "userId", "createdAt", "updatedAt" }.All(createSession.GetProperty("ForbiddenPayloadFields")
            .EnumerateArray()
            .Select(value => value.GetString()).Contains)).IsTrue();
        await Assert.That(createSession.GetProperty("RequiredAuthorization").GetProperty("ResourceKind").GetString()).IsEqualTo(ResourceKinds.EventSession);
        await Assert.That(createSession.GetProperty("RequiredAuthorization").GetProperty("Action").GetString()).IsEqualTo(AuthorizationActions.Create);

        var assignGroup = tools.EnumerateArray().Single(tool =>
            tool.GetProperty("Name").GetString() == "AssignSessionToEventSessionGroup");
        await Assert.That(assignGroup.GetProperty("McpToolName").GetString()).IsEqualTo("propose_assign_session_to_event_session_group");
        await Assert.That(new[] { "eventId", "expectedConcurrencyStamp", "groupId", "sessionId", "isPrimary", "sortOrder" }.All(assignGroup.GetProperty("AllowedPayloadFields")
            .EnumerateArray()
            .Select(value => value.GetString()).Contains)).IsTrue();
        await Assert.That(assignGroup.GetProperty("ForbiddenPayloadFields")
            .EnumerateArray()
            .Select(value => value.GetString())).DoesNotContain("groupId");

        var templateSync = tools.EnumerateArray().Single(tool =>
            tool.GetProperty("Name").GetString() == "ApplyEventTemplateSync");
        await Assert.That(templateSync.GetProperty("McpToolName").GetString()).IsEqualTo("propose_apply_event_template_sync");
        await Assert.That(new[] { "eventId", "expectedConcurrencyStamp", "baseProvenanceVersion", "plan", "confirmationPhrase" }.All(templateSync.GetProperty("AllowedPayloadFields")
            .EnumerateArray()
            .Select(value => value.GetString()).Contains)).IsTrue();
        await Assert.That(templateSync.GetProperty("RequiredAuthorization").GetProperty("Action").GetString()).IsEqualTo(AuthorizationActions.CustomPropertyTemplates.SyncApply);

        var normalized = json.ToLowerInvariant();
        await Assert.That(normalized).DoesNotContain("prompt");
        await Assert.That(normalized).DoesNotContain("providerendpoint");
        await Assert.That(normalized).DoesNotContain("apikey");
    }
}
