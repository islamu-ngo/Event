using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Tools;

public sealed class UpdateEventDraftAiToolDefinitionTests
{
    [Test]
    public async Task Create_ReturnsRequiredGovernanceMetadata()
    {
        var definition = UpdateEventDraftAiToolDefinition.Create();

        await Assert.That(definition.Kind).IsEqualTo(AiProposedActionKind.UpdateEventDraft);
        await Assert.That(definition.Name).IsEqualTo("UpdateEventDraft");
        await Assert.That(definition.ConfirmationMode).IsEqualTo(AiToolConfirmationMode.Required);
        await Assert.That(definition.PayloadMapperType).IsEqualTo(typeof(UpdateEventDraftAiActionMapper));
        await Assert.That(definition.RequiredAuthorization).IsNotNull();
        await Assert.That(definition.RequiredAuthorization!.ResourceKind).IsEqualTo(ResourceKinds.Event);
        await Assert.That(definition.RequiredAuthorization.Action).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(definition.ExposeToProvider).IsFalse();
        await Assert.That(definition.ExposeToMcp).IsTrue();
        await Assert.That(definition.EffectiveAgentMetadata.RiskClass).IsEqualTo(AiToolRiskClass.Medium);
        await Assert.That(definition.EffectiveAgentMetadata.ApprovalMode).IsEqualTo(AiToolApprovalMode.HumanConfirmationRequired);
        await Assert.That(definition.EffectiveAgentMetadata.RequiredHalLinkRel).IsEqualTo("edit");
    }

    [Test]
    public async Task JsonSchemaProperties_MatchAllowedPayloadFields()
    {
        var schemaFields = GetSchemaPropertyNames();

        await Assert.That(schemaFields).IsEquivalentTo(UpdateEventDraftAiToolDefinition.AllowedPayloadFields);
    }

    [Test]
    public async Task JsonSchema_RequiresEventAndParticipationConfigurationConcurrencyStampsTitleAndConfiguration()
    {
        using var document = JsonDocument.Parse(UpdateEventDraftAiToolDefinition.JsonSchema);
        var root = document.RootElement;
        var requiredFields = root.GetProperty("required")
            .EnumerateArray()
            .Select(field => field.GetString())
            .ToArray();

        await Assert.That(root.GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(requiredFields).IsEquivalentTo([
            "eventId",
            "expectedConcurrencyStamp",
            "expectedParticipationConfigurationConcurrencyStamp",
            "title",
            "participationConfiguration"
        ]);
    }

    [Test]
    public async Task Mapper_AcceptsEveryAllowedPayloadField()
    {
        var mapper = new UpdateEventDraftAiActionMapper();

        foreach (var fieldName in UpdateEventDraftAiToolDefinition.AllowedPayloadFields)
        {
            var result = mapper.Map(CreatePayloadForAllowedField(fieldName));

            await Assert.That(result.Succeeded).IsTrue();
        }
    }

    [Test]
    public async Task Registry_RejectsEveryForbiddenPayloadFieldBeforeUnsupportedFieldHandling()
    {
        var registry = AiToolContractRegistry.CreateDefault();

        foreach (var fieldName in UpdateEventDraftAiToolDefinition.ForbiddenPayloadFields)
        {
            var result = registry.ValidatePayload(
                AiProposedActionKind.UpdateEventDraft,
                $$"""
                  {
                    "eventId": "{{Guid.CreateVersion7()}}",
                    "expectedConcurrencyStamp": "{{Guid.CreateVersion7()}}",
                    "title": "Draft update",
                    "{{fieldName}}": "not allowed"
                  }
                  """);

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo("forbidden_tool_argument");
        }
    }

    [Test]
    public async Task Registry_WhenExpectedConcurrencyStampIsMissing_RejectsPayload()
    {
        var registry = AiToolContractRegistry.CreateDefault();

        var result = registry.ValidatePayload(
            AiProposedActionKind.UpdateEventDraft,
            $$"""
              {
                "eventId": "{{Guid.CreateVersion7()}}",
                "title": "Draft update"
              }
              """);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_tool_argument");
    }

    private static HashSet<string> GetSchemaPropertyNames()
    {
        using var document = JsonDocument.Parse(UpdateEventDraftAiToolDefinition.JsonSchema);
        return document.RootElement
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string CreatePayloadForAllowedField(string fieldName)
    {
        var eventId = Guid.CreateVersion7();
        var concurrencyStamp = Guid.CreateVersion7();
        var participationConfigurationConcurrencyStamp = Guid.CreateVersion7();
        var imageId = Guid.CreateVersion7();
        var templateId = Guid.CreateVersion7();
        var seriesId = Guid.CreateVersion7();
        var basePayload = $$"""
            "eventId": "{{eventId}}",
            "expectedConcurrencyStamp": "{{concurrencyStamp}}",
            "expectedParticipationConfigurationConcurrencyStamp": "{{participationConfigurationConcurrencyStamp}}",
            "participationConfiguration": {"participationHandlingModeId": 1, "advanceRegistrationObligationId": 1},
            "title": "Draft update"
            """;

        return fieldName switch
        {
            "eventId" => $"{{{basePayload}}}",
            "expectedConcurrencyStamp" => $"{{{basePayload}}}",
            "expectedParticipationConfigurationConcurrencyStamp" => $"{{{basePayload}}}",
            "title" => $"{{{basePayload}}}",
            "subtitle" => $"{{{basePayload},\"subtitle\":\"Subtitle\"}}",
            "description" => $"{{{basePayload},\"description\":\"Short description\"}}",
            "content" => $"{{{basePayload},\"content\":\"Longer content\"}}",
            "slug" => $"{{{basePayload},\"slug\":\"draft-event\"}}",
            "eventTypeId" => $"{{{basePayload},\"eventTypeId\":1}}",
            "audienceGenderId" => $"{{{basePayload},\"audienceGenderId\":1}}",
            "audienceAgeId" => $"{{{basePayload},\"audienceAgeId\":1}}",
            "price" => $"{{{basePayload},\"price\":12.5}}",
            "currencyCode" => $"{{{basePayload},\"currencyCode\":\"EUR\"}}",
            "featuredImageId" => $"{{{basePayload},\"featuredImageId\":\"{imageId}\"}}",
            "participationConfiguration" => $"{{{basePayload}}}",
            "visibilityTypeId" => $"{{{basePayload},\"visibilityTypeId\":1}}",
            "eventFormatId" => $"{{{basePayload},\"eventFormatId\":1}}",
            "madhabId" => $"{{{basePayload},\"madhabId\":1}}",
            "timezone" => $"{{{basePayload},\"timezone\":\"Europe/Brussels\"}}",
            "eventTimeZoneId" => $"{{{basePayload},\"eventTimeZoneId\":\"Europe/Brussels\"}}",
            "eventUrl" => $"{{{basePayload},\"eventUrl\":\"https://example.test/event\"}}",
            "backgroundColor" => $"{{{basePayload},\"backgroundColor\":\"#123456\"}}",
            "backgroundEffect" => $"{{{basePayload},\"backgroundEffect\":\"none\"}}",
            "backgroundImageId" => $"{{{basePayload},\"backgroundImageId\":\"{imageId}\"}}",
            "templateId" => $"{{{basePayload},\"templateId\":\"{templateId}\"}}",
            "eventSeriesId" => $"{{{basePayload},\"eventSeriesId\":\"{seriesId}\"}}",
            "seriesOrder" => $"{{{basePayload},\"seriesOrder\":3}}",
            "registrationPolicyId" => $"{{{basePayload},\"registrationPolicyId\":1}}",
            _ => throw new InvalidOperationException($"No valid mapper payload sample exists for allowed AI field '{fieldName}'.")
        };
    }
}
