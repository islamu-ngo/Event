using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Application.Hateoas;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Tools;

public sealed class EventModerationAiToolDefinitionTests
{
    [Test]
    public async Task CreateAll_ReturnsModerationDefinitionsWithGovernanceMetadata()
    {
        var definitions = EventModerationAiToolDefinitions.CreateAll();

        await Assert.That(definitions.Select(definition => definition.Kind)).IsEquivalentTo(
        [
            AiProposedActionKind.LightModerateEvent,
            AiProposedActionKind.HeavyModerateEvent,
            AiProposedActionKind.UnmoderateEvent
        ]);
        await Assert.That(definitions.All(definition => definition.PayloadMapperType == typeof(EventModerationAiActionMapper))).IsTrue();
        await Assert.That(definitions.All(definition => definition.ExposeToProvider is false && definition.ExposeToMcp)).IsTrue();
        await Assert.That(definitions.Single(definition => definition.Kind == AiProposedActionKind.LightModerateEvent).RequiredAuthorization!.Action)
            .IsEqualTo(AuthorizationActions.Events.ModerateLight);
        await Assert.That(definitions.Single(definition => definition.Kind == AiProposedActionKind.HeavyModerateEvent).RequiredAuthorization!.Action)
            .IsEqualTo(AuthorizationActions.Events.ModerateHeavy);
        await Assert.That(definitions.Single(definition => definition.Kind == AiProposedActionKind.UnmoderateEvent).RequiredAuthorization!.Action)
            .IsEqualTo(AuthorizationActions.Events.Unmoderate);
        await Assert.That(definitions.All(definition => definition.RequiredAuthorization!.ResourceKind == ResourceKinds.Event)).IsTrue();
    }

    [Test]
    public async Task HeavyModerationDefinition_RequiresHalContextAndIrreversibleConfirmation()
    {
        var definition = EventModerationAiToolDefinitions.CreateAll()
            .Single(definition => definition.Kind == AiProposedActionKind.HeavyModerateEvent);
        using var document = JsonDocument.Parse(definition.JsonSchema);
        var root = document.RootElement;
        var requiredFields = root.GetProperty("required")
            .EnumerateArray()
            .Select(field => field.GetString())
            .ToArray();

        await Assert.That(definition.Name).IsEqualTo("HeavyModerateEvent");
        await Assert.That(definition.EffectiveAgentMetadata.RiskClass).IsEqualTo(AiToolRiskClass.Critical);
        await Assert.That(definition.EffectiveAgentMetadata.RequiredHalLinkRel).IsEqualTo(LinkRelations.ModerateHeavy);
        await Assert.That(definition.EffectiveAgentMetadata.DestructiveHint).IsTrue();
        await Assert.That(root.GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(requiredFields).IsEquivalentTo([
            "eventId",
            "expectedConcurrencyStamp",
            "managementContextHasModerateHeavy",
            "reasonCode",
            "destructiveSummary",
            "confirmationPhrase",
            "acknowledgedConsequences"
        ]);
        await Assert.That(GetSchemaPropertyNames(definition)).IsEquivalentTo(definition.AllowedPayloadFields);
    }

    [Test]
    public async Task DefaultRegistry_ContainsModerationDefinitionsAndRejectsForbiddenFields()
    {
        var registry = AiToolContractRegistry.CreateDefault();
        var definition = registry.FindDefinition(AiProposedActionKind.LightModerateEvent);

        await Assert.That(definition).IsNotNull();
        foreach (var fieldName in definition!.ForbiddenPayloadFields)
        {
            var result = registry.ValidatePayload(
                AiProposedActionKind.LightModerateEvent,
                $$"""
                  {
                    "eventId": "{{Guid.CreateVersion7()}}",
                    "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                    "managementContextHasModerateLight": true,
                    "reasonCode": "policy_violation",
                    "{{fieldName}}": "not allowed"
                  }
                  """);

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo("forbidden_tool_argument");
        }
    }

    [Test]
    public async Task Registry_WhenHeavyConfirmationPhraseIsWrong_RejectsPayload()
    {
        var result = AiToolContractRegistry.CreateDefault().ValidatePayload(
            AiProposedActionKind.HeavyModerateEvent,
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "managementContextHasModerateHeavy": true,
                "reasonCode": "severe_policy_violation",
                "destructiveSummary": "Irreversibly redact the event from public surfaces.",
                "confirmationPhrase": "HEAVY_REDACT_EVENT",
                "acknowledgedConsequences": true
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_tool_argument_value");
    }

    private static HashSet<string> GetSchemaPropertyNames(AiToolDefinition definition)
    {
        using var document = JsonDocument.Parse(definition.JsonSchema);
        return document.RootElement
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
