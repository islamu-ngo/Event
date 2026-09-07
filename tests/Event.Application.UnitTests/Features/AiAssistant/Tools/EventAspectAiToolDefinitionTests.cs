using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Tools;

public sealed class EventAspectAiToolDefinitionTests
{
    [Test]
    public async Task Create_ForAspectDefinitions_ReturnsRequiredGovernanceMetadata()
    {
        var definitions = new[]
        {
            UpsertEventIslamicAspectAiToolDefinition.Create(),
            DeleteEventIslamicAspectAiToolDefinition.Create(),
            UpsertEventTechAspectAiToolDefinition.Create(),
            DeleteEventTechAspectAiToolDefinition.Create()
        };

        await Assert.That(definitions.Select(definition => definition.Kind)).IsEquivalentTo([
            AiProposedActionKind.UpsertEventIslamicAspect,
            AiProposedActionKind.DeleteEventIslamicAspect,
            AiProposedActionKind.UpsertEventTechAspect,
            AiProposedActionKind.DeleteEventTechAspect
        ]);

        foreach (var definition in definitions)
        {
            await Assert.That(definition.RequiredAuthorization).IsNotNull();
            await Assert.That(definition.RequiredAuthorization!.ResourceKind).IsEqualTo(ResourceKinds.Event);
            await Assert.That(definition.RequiredAuthorization.Action).IsEqualTo(AuthorizationActions.Update);
            await Assert.That(definition.ExposeToProvider).IsFalse();
            await Assert.That(definition.ExposeToMcp).IsTrue();
            await Assert.That(definition.EffectiveAgentMetadata.RequiredHalLinkRel).IsEqualTo("edit");
        }

        await Assert.That(UpsertEventIslamicAspectAiToolDefinition.Create().PayloadMapperType)
            .IsEqualTo(typeof(UpsertEventIslamicAspectAiActionMapper));
        await Assert.That(DeleteEventIslamicAspectAiToolDefinition.Create().PayloadMapperType)
            .IsEqualTo(typeof(DeleteEventIslamicAspectAiActionMapper));
        await Assert.That(UpsertEventTechAspectAiToolDefinition.Create().PayloadMapperType)
            .IsEqualTo(typeof(UpsertEventTechAspectAiActionMapper));
        await Assert.That(DeleteEventTechAspectAiToolDefinition.Create().PayloadMapperType)
            .IsEqualTo(typeof(DeleteEventTechAspectAiActionMapper));
        await Assert.That(UpsertEventIslamicAspectAiToolDefinition.Create().EffectiveAgentMetadata.DestructiveHint).IsFalse();
        await Assert.That(UpsertEventTechAspectAiToolDefinition.Create().EffectiveAgentMetadata.DestructiveHint).IsFalse();
        await Assert.That(DeleteEventIslamicAspectAiToolDefinition.Create().EffectiveAgentMetadata.DestructiveHint).IsTrue();
        await Assert.That(DeleteEventTechAspectAiToolDefinition.Create().EffectiveAgentMetadata.DestructiveHint).IsTrue();
    }

    [Test]
    public async Task JsonSchemas_MatchAllowedPayloadFields()
    {
        await Assert.That(GetSchemaPropertyNames(UpsertEventIslamicAspectAiToolDefinition.JsonSchema))
            .IsEquivalentTo(UpsertEventIslamicAspectAiToolDefinition.AllowedPayloadFields);
        await Assert.That(GetSchemaPropertyNames(DeleteEventIslamicAspectAiToolDefinition.JsonSchema))
            .IsEquivalentTo(DeleteEventIslamicAspectAiToolDefinition.AllowedPayloadFields);
        await Assert.That(GetSchemaPropertyNames(UpsertEventTechAspectAiToolDefinition.JsonSchema))
            .IsEquivalentTo(UpsertEventTechAspectAiToolDefinition.AllowedPayloadFields);
        await Assert.That(GetSchemaPropertyNames(DeleteEventTechAspectAiToolDefinition.JsonSchema))
            .IsEquivalentTo(DeleteEventTechAspectAiToolDefinition.AllowedPayloadFields);
    }

    [Test]
    public async Task Registry_RejectsAspectPayloadsWithoutModuleOrPermissionContext()
    {
        var registry = AiToolContractRegistry.CreateDefault();

        var missingModule = registry.ValidatePayload(
            AiProposedActionKind.UpsertEventIslamicAspect,
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "managementContextHasEdit": true,
                "genderMode": 0
              }
              """);

        var missingEditContext = registry.ValidatePayload(
            AiProposedActionKind.UpsertEventTechAspect,
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "aspectKind": "tech",
                "skillLevel": 0
              }
              """);

        await Assert.That(missingModule.Succeeded).IsFalse();
        await Assert.That(missingModule.FailureCode).IsEqualTo("missing_tool_argument");
        await Assert.That(missingEditContext.Succeeded).IsFalse();
        await Assert.That(missingEditContext.FailureCode).IsEqualTo("missing_tool_argument");
    }

    [Test]
    public async Task Registry_RejectsAspectForbiddenFields()
    {
        var registry = AiToolContractRegistry.CreateDefault();

        var result = registry.ValidatePayload(
            AiProposedActionKind.UpsertEventTechAspect,
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "aspectKind": "tech",
                "managementContextHasEdit": true,
                "skillLevel": 0,
                "tenantId": "{{Guid.CreateVersion7()}}"
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("forbidden_tool_argument");
    }

    private static HashSet<string> GetSchemaPropertyNames(string schema)
    {
        using var document = JsonDocument.Parse(schema);
        return document.RootElement
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
