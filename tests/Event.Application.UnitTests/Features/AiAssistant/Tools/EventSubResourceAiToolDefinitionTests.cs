using System.Text.Json;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Tools;

public sealed class EventSubResourceAiToolDefinitionTests
{
    [Test]
    public async Task CreateAll_ReturnsPhaseFiveDefinitionsWithSharedMapper()
    {
        var definitions = EventSubResourceAiToolDefinitions.CreateAll();

        await Assert.That(definitions.Select(definition => definition.Kind)).IsEquivalentTo(ExpectedPhaseFiveKinds);
        await Assert.That(definitions.All(definition => definition.ExposeToMcp)).IsTrue();
        await Assert.That(definitions.All(definition => !definition.ExposeToProvider)).IsTrue();
        await Assert.That(definitions.All(definition => definition.PayloadMapperType == typeof(EventSubResourceAiActionMapper))).IsTrue();
        await Assert.That(definitions.All(definition => definition.RequiredAuthorization is not null)).IsTrue();
    }

    [Test]
    public async Task CreateAll_UsesDestructiveHintsOnlyForDeletesRevokesAndSyncApply()
    {
        var definitions = EventSubResourceAiToolDefinitions.CreateAll();

        var destructiveKinds = definitions
            .Where(definition => definition.EffectiveAgentMetadata.DestructiveHint)
            .Select(definition => definition.Kind)
            .ToArray();

        await Assert.That(destructiveKinds).IsEquivalentTo([
            AiProposedActionKind.DeleteEventSession,
            AiProposedActionKind.DeleteEventSessionGroup,
            AiProposedActionKind.UnassignSessionFromEventSessionGroup,
            AiProposedActionKind.DeleteEventDay,
            AiProposedActionKind.DeleteEventAgendaItem,
            AiProposedActionKind.DeleteEventCustomPropertyDefinition,
            AiProposedActionKind.PurgeEventCustomPropertyDefinition,
            AiProposedActionKind.RevokeEventTeamRole,
            AiProposedActionKind.DeleteEventTemplate,
            AiProposedActionKind.DeleteEventSessionTemplate,
            AiProposedActionKind.ApplyEventTemplateSync,
            AiProposedActionKind.ApplyEventSessionTemplateSync]);
    }

    [Test]
    public async Task CreateAll_SchemasExposeOnlyAllowedPayloadFields()
    {
        foreach (var definition in EventSubResourceAiToolDefinitions.CreateAll())
        {
            using var document = JsonDocument.Parse(definition.JsonSchema);
            var schemaFields = document.RootElement
                .GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            await Assert.That(schemaFields).IsEquivalentTo(definition.AllowedPayloadFields);
            await Assert.That(schemaFields.Contains("tenantId")).IsFalse();
            await Assert.That(schemaFields.Contains("actorUserId")).IsFalse();
            await Assert.That(schemaFields.Contains("createdBy")).IsFalse();
            await Assert.That(schemaFields.Contains("updatedBy")).IsFalse();
        }
    }

    [Test]
    public async Task CreateAll_RequiredFieldsAreAllowedAndNeverForbidden()
    {
        foreach (var definition in EventSubResourceAiToolDefinitions.CreateAll())
        {
            var allowedForbiddenOverlap = definition.AllowedPayloadFields
                .Intersect(definition.ForbiddenPayloadFields, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            await Assert.That(allowedForbiddenOverlap).IsEmpty();

            using var document = JsonDocument.Parse(definition.JsonSchema);
            var requiredFields = document.RootElement
                .GetProperty("required")
                .EnumerateArray()
                .Select(field => field.GetString())
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .Select(field => field!)
                .ToArray();

            await Assert.That(requiredFields.All(definition.AllowedPayloadFields.Contains)).IsTrue();
            await Assert.That(requiredFields.Any(definition.ForbiddenPayloadFields.Contains)).IsFalse();
        }
    }

    [Test]
    public async Task CreateAll_RejectsForbiddenServerOwnedFields()
    {
        var registry = new AiToolContractRegistry(EventSubResourceAiToolDefinitions.CreateAll());

        foreach (var definition in EventSubResourceAiToolDefinitions.CreateAll())
        {
            foreach (var fieldName in definition.ForbiddenPayloadFields.Take(8))
            {
                var validation = registry.ValidatePayload(
                    definition.Kind,
                    $$"""{ "{{fieldName}}": "not allowed" }""");

                await Assert.That(validation.Succeeded).IsFalse();
                await Assert.That(validation.FailureCode).IsEqualTo("forbidden_tool_argument");
            }
        }
    }

    private static readonly AiProposedActionKind[] ExpectedPhaseFiveKinds =
    [
        AiProposedActionKind.CreateEventSession,
        AiProposedActionKind.UpdateEventSession,
        AiProposedActionKind.DeleteEventSession,
        AiProposedActionKind.CreateEventSessionGroup,
        AiProposedActionKind.UpdateEventSessionGroup,
        AiProposedActionKind.DeleteEventSessionGroup,
        AiProposedActionKind.AssignSessionToEventSessionGroup,
        AiProposedActionKind.UnassignSessionFromEventSessionGroup,
        AiProposedActionKind.CreateEventDay,
        AiProposedActionKind.UpdateEventDay,
        AiProposedActionKind.DeleteEventDay,
        AiProposedActionKind.CreateEventAgendaItem,
        AiProposedActionKind.UpdateEventAgendaItem,
        AiProposedActionKind.DeleteEventAgendaItem,
        AiProposedActionKind.CreateEventCustomPropertyDefinition,
        AiProposedActionKind.UpdateEventCustomPropertyDefinition,
        AiProposedActionKind.DeleteEventCustomPropertyDefinition,
        AiProposedActionKind.PurgeEventCustomPropertyDefinition,
        AiProposedActionKind.SetEventCustomPropertyValue,
        AiProposedActionKind.SetEventCustomPropertyMultiValues,
        AiProposedActionKind.AssignEventTeamRole,
        AiProposedActionKind.RevokeEventTeamRole,
        AiProposedActionKind.CreateEventTemplate,
        AiProposedActionKind.UpdateEventTemplate,
        AiProposedActionKind.DeleteEventTemplate,
        AiProposedActionKind.CreateEventSessionTemplate,
        AiProposedActionKind.UpdateEventSessionTemplate,
        AiProposedActionKind.DeleteEventSessionTemplate,
        AiProposedActionKind.ApplyEventTemplateSync,
        AiProposedActionKind.ApplyEventSessionTemplateSync
    ];
}
