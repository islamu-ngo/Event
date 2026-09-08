using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Tools;

public sealed class PublishEventAiToolDefinitionTests
{
    [Test]
    public async Task Create_ReturnsRequiredGovernanceMetadata()
    {
        var definition = PublishEventAiToolDefinition.Create();

        await Assert.That(definition.Kind).IsEqualTo(AiProposedActionKind.PublishEvent);
        await Assert.That(definition.Name).IsEqualTo("PublishEvent");
        await Assert.That(definition.ConfirmationMode).IsEqualTo(AiToolConfirmationMode.Required);
        await Assert.That(definition.PayloadMapperType).IsEqualTo(typeof(PublishEventAiActionMapper));
        await Assert.That(definition.RequiredAuthorization).IsNotNull();
        await Assert.That(definition.RequiredAuthorization!.ResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(definition.RequiredAuthorization.Action).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(definition.ExposeToProvider).IsFalse();
        await Assert.That(definition.ExposeToMcp).IsTrue();
        await Assert.That(definition.EffectiveAgentMetadata.RiskClass).IsEqualTo(AiToolRiskClass.High);
        await Assert.That(definition.EffectiveAgentMetadata.ApprovalMode).IsEqualTo(AiToolApprovalMode.HumanConfirmationRequired);
        await Assert.That(definition.EffectiveAgentMetadata.RequiredHalLinkRel).IsEqualTo("publish");
    }

    [Test]
    public async Task JsonSchemaProperties_MatchAllowedPayloadFields()
    {
        var schemaFields = GetSchemaPropertyNames();

        await Assert.That(schemaFields).IsEquivalentTo(PublishEventAiToolDefinition.AllowedPayloadFields);
    }

    [Test]
    public async Task JsonSchema_RequiresEventIdConcurrencyStampAndReadinessContext()
    {
        using var document = JsonDocument.Parse(PublishEventAiToolDefinition.JsonSchema);
        var root = document.RootElement;
        var requiredFields = root.GetProperty("required")
            .EnumerateArray()
            .Select(field => field.GetString())
            .ToArray();

        await Assert.That(root.GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(requiredFields).IsEquivalentTo([
            "eventId",
            "expectedConcurrencyStamp",
            "readinessIsReady",
            "readinessErrorCount"
        ]);
    }

    [Test]
    public async Task Mapper_AcceptsEveryAllowedPayloadField()
    {
        var mapper = new PublishEventAiActionMapper();

        foreach (var fieldName in PublishEventAiToolDefinition.AllowedPayloadFields)
        {
            var result = mapper.Map(CreatePayloadForAllowedField(fieldName));

            await Assert.That(result.Succeeded).IsTrue();
        }
    }

    [Test]
    public async Task Registry_RejectsEveryForbiddenPayloadFieldBeforeUnsupportedFieldHandling()
    {
        var registry = AiToolContractRegistry.CreateDefault();

        foreach (var fieldName in PublishEventAiToolDefinition.ForbiddenPayloadFields)
        {
            var result = registry.ValidatePayload(
                AiProposedActionKind.PublishEvent,
                $$"""
                  {
                    "eventId": "{{Guid.CreateVersion7()}}",
                    "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                    "readinessIsReady": true,
                    "readinessErrorCount": 0,
                    "{{fieldName}}": "not allowed"
                  }
                  """);

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo("forbidden_tool_argument");
        }
    }

    [Test]
    public async Task Registry_WhenReadinessContextIsMissing_RejectsPayload()
    {
        var registry = AiToolContractRegistry.CreateDefault();

        var result = registry.ValidatePayload(
            AiProposedActionKind.PublishEvent,
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}"
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_tool_argument");
    }

    [Test]
    public async Task Registry_WhenReadinessIsFalse_RejectsPayload()
    {
        var registry = AiToolContractRegistry.CreateDefault();

        var result = registry.ValidatePayload(
            AiProposedActionKind.PublishEvent,
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                "readinessIsReady": false,
                "readinessErrorCount": 0
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_tool_argument_value");
    }

    private static HashSet<string> GetSchemaPropertyNames()
    {
        using var document = JsonDocument.Parse(PublishEventAiToolDefinition.JsonSchema);
        return document.RootElement
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string CreatePayloadForAllowedField(string fieldName)
    {
        var basePayload = $$"""
            "eventId": "{{Guid.CreateVersion7()}}",
            "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
            "readinessIsReady": true,
            "readinessErrorCount": 0
            """;

        return fieldName switch
        {
            "eventId" => $"{{{basePayload}}}",
            "expectedConcurrencyStamp" => $"{{{basePayload}}}",
            "readinessIsReady" => $"{{{basePayload}}}",
            "readinessErrorCount" => $"{{{basePayload}}}",
            "readinessCheckedAtUtc" => $"{{{basePayload},\"readinessCheckedAtUtc\":\"2026-06-23T12:00:00Z\"}}",
            "readinessSummary" => $"{{{basePayload},\"readinessSummary\":\"Readiness check passed with no errors.\"}}",
            _ => throw new InvalidOperationException($"No valid mapper payload sample exists for allowed AI field '{fieldName}'.")
        };
    }
}
