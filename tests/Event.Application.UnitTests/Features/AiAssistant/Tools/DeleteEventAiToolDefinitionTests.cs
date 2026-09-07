using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Tools;

public sealed class DeleteEventAiToolDefinitionTests
{
    [Test]
    public async Task Create_ReturnsRequiredGovernanceMetadata()
    {
        var definition = DeleteEventAiToolDefinition.Create();

        await Assert.That(definition.Kind).IsEqualTo(AiProposedActionKind.DeleteEvent);
        await Assert.That(definition.Name).IsEqualTo("DeleteEvent");
        await Assert.That(definition.ConfirmationMode).IsEqualTo(AiToolConfirmationMode.Required);
        await Assert.That(definition.PayloadMapperType).IsEqualTo(typeof(DeleteEventAiActionMapper));
        await Assert.That(definition.RequiredAuthorization).IsNotNull();
        await Assert.That(definition.RequiredAuthorization!.ResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(definition.RequiredAuthorization.Action).IsEqualTo(AuthorizationActions.Delete);
        await Assert.That(definition.ExposeToProvider).IsFalse();
        await Assert.That(definition.ExposeToMcp).IsTrue();
        await Assert.That(definition.EffectiveAgentMetadata.RiskClass).IsEqualTo(AiToolRiskClass.High);
        await Assert.That(definition.EffectiveAgentMetadata.RequiredHalLinkRel).IsEqualTo("delete");
        await Assert.That(definition.EffectiveAgentMetadata.DestructiveHint).IsTrue();
    }

    [Test]
    public async Task JsonSchema_RequiresConcurrencyHalContextAndDestructiveConfirmation()
    {
        using var document = JsonDocument.Parse(DeleteEventAiToolDefinition.JsonSchema);
        var root = document.RootElement;
        var requiredFields = root.GetProperty("required")
            .EnumerateArray()
            .Select(field => field.GetString())
            .ToArray();

        await Assert.That(root.GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(requiredFields).IsEquivalentTo([
            "eventId",
            "expectedConcurrencyStamp",
            "managementContextHasDelete",
            "destructiveSummary",
            "confirmationPhrase",
            "acknowledgedConsequences"
        ]);
        await Assert.That(GetSchemaPropertyNames()).IsEquivalentTo(DeleteEventAiToolDefinition.AllowedPayloadFields);
    }

    [Test]
    public async Task Registry_RejectsEveryForbiddenPayloadFieldBeforeUnsupportedFieldHandling()
    {
        var registry = AiToolContractRegistry.CreateDefault();

        foreach (var fieldName in DeleteEventAiToolDefinition.ForbiddenPayloadFields)
        {
            var result = registry.ValidatePayload(
                AiProposedActionKind.DeleteEvent,
                $$"""
                  {
                    "eventId": "{{Guid.CreateVersion7()}}",
                    "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                    "managementContextHasDelete": true,
                    "destructiveSummary": "Delete this duplicate event.",
                    "confirmationPhrase": "DELETE_EVENT",
                    "acknowledgedConsequences": true,
                    "{{fieldName}}": "not allowed"
                  }
                  """);

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo("forbidden_tool_argument");
        }
    }

    [Test]
    public async Task Registry_WhenConfirmationPhraseIsWrong_RejectsPayload()
    {
        var registry = AiToolContractRegistry.CreateDefault();

        var result = registry.ValidatePayload(
            AiProposedActionKind.DeleteEvent,
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "managementContextHasDelete": true,
                "destructiveSummary": "Delete this duplicate event.",
                "confirmationPhrase": "DELETE",
                "acknowledgedConsequences": true
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_tool_argument_value");
    }

    private static HashSet<string> GetSchemaPropertyNames()
    {
        using var document = JsonDocument.Parse(DeleteEventAiToolDefinition.JsonSchema);
        return document.RootElement
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
