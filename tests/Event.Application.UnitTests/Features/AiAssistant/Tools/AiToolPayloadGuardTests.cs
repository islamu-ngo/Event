using Explore.Application.Features.AiAssistant.Tools;

namespace Event.Application.UnitTests.Features.AiAssistant.Tools;

public sealed class AiToolPayloadGuardTests
{
    private static readonly HashSet<string> AllowedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "title",
        "description",
        "eventTypeId",
        "organizationId",
        "price",
        "currencyCode"
    };

    private const string SchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["title"],
          "properties": {
            "title": { "type": "string" },
            "description": { "type": "string" },
            "eventTypeId": { "type": "integer" },
            "organizationId": { "type": "string", "format": "uuid" },
            "price": { "type": "number", "minimum": 0 },
            "currencyCode": { "type": "string", "maxLength": 3 }
          }
        }
        """;

    private static readonly HashSet<string> ForbiddenFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "tenantId",
        "eventStatusId"
    };

    private static readonly HashSet<string> ShapeFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "startsOn",
        "startsAt",
        "timezone",
        "visibility",
        "optionalNote",
        "metadata",
        "runtimeTenantId"
    };

    private const string ShapeSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "required": ["startsOn", "startsAt", "visibility", "metadata"],
          "properties": {
            "startsOn": { "type": "string", "format": "date" },
            "startsAt": { "type": "string", "format": "date-time" },
            "timezone": { "type": "string", "enum": ["Europe/Brussels", "UTC"] },
            "visibility": { "type": "integer", "enum": [1, 2] },
            "optionalNote": { "type": ["string", "null"], "maxLength": 20 },
            "metadata": {
              "type": "object",
              "additionalProperties": false,
              "required": ["source"],
              "properties": {
                "source": { "type": "string", "enum": ["assistant"] }
              }
            },
            "runtimeTenantId": { "type": "string", "format": "uuid", "x-islamu-hiddenRuntimeContext": true }
          }
        }
        """;

    [Test]
    public async Task ValidateJsonObject_WhenPayloadIsAllowedObject_ReturnsSuccess()
    {
        var result = AiToolPayloadGuard.ValidateJsonObject(
            "{\"title\":\"Draft\",\"description\":\"Details\"}",
            AllowedFields,
            ForbiddenFields);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.FailureCode).IsNull();
    }

    [Test]
    public async Task ValidateJsonObject_WhenPayloadIsInvalidJson_ReturnsInvalidToolArgumentsFailure()
    {
        var result = AiToolPayloadGuard.ValidateJsonObject("not-json", AllowedFields, ForbiddenFields);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_tool_arguments");
        await Assert.That(result.FailureMessage).DoesNotContain("not-json");
        await Assert.That(result.CorrectionMessage).Contains("Regenerate the tool call arguments");
    }

    [Test]
    public async Task ValidateJsonObject_WhenPayloadIsArray_ReturnsInvalidToolArgumentsFailure()
    {
        var result = AiToolPayloadGuard.ValidateJsonObject("[]", AllowedFields, ForbiddenFields);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_tool_arguments");
    }

    [Test]
    public async Task ValidateJsonObject_WhenPayloadContainsUnknownField_ReturnsUnsupportedToolArgumentFailure()
    {
        var result = AiToolPayloadGuard.ValidateJsonObject(
            "{\"title\":\"Draft\",\"unexpected\":true}",
            AllowedFields,
            ForbiddenFields);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_tool_argument");
        await Assert.That(result.FailureMessage).DoesNotContain("unexpected");
    }

    [Test]
    public async Task ValidateJsonObject_WhenPayloadContainsForbiddenField_ReturnsForbiddenToolArgumentFailure()
    {
        var result = AiToolPayloadGuard.ValidateJsonObject(
            "{\"title\":\"Draft\",\"tenantId\":\"not allowed\"}",
            AllowedFields,
            ForbiddenFields);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("forbidden_tool_argument");
        await Assert.That(result.FailureMessage).DoesNotContain("tenantId");
    }

    [Test]
    public async Task ValidateJsonObject_WhenSchemaRequiredFieldIsMissing_ReturnsSafeCorrectionFailure()
    {
        var result = AiToolPayloadGuard.ValidateJsonObject(
            "{\"description\":\"Details\"}",
            AllowedFields,
            ForbiddenFields,
            SchemaJson);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("missing_tool_argument");
        await Assert.That(result.FailureMessage).DoesNotContain("title");
        await Assert.That(result.CorrectionMessage).Contains("matches the registered schema exactly");
        await Assert.That(result.EffectiveRecovery.RequiresClarification).IsTrue();
        await Assert.That(result.EffectiveRecovery.ClarificationQuestion).Contains("required event draft details");
        await Assert.That(result.EffectiveRecovery.StableFailureCode).IsEqualTo("missing_tool_argument");
    }

    [Test]
    public async Task ValidateJsonObject_WhenSchemaIntegerTypeIsWrong_ReturnsSafeTypeFailure()
    {
        var result = AiToolPayloadGuard.ValidateJsonObject(
            "{\"title\":\"Draft\",\"eventTypeId\":\"one\"}",
            AllowedFields,
            ForbiddenFields,
            SchemaJson);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_tool_argument_type");
        await Assert.That(result.FailureMessage).DoesNotContain("eventTypeId");
        await Assert.That(result.FailureMessage).DoesNotContain("one");
        await Assert.That(result.CorrectionMessage).Contains("documented JSON types and formats");
    }

    [Test]
    public async Task ValidateJsonObject_WhenSchemaUuidFormatIsInvalid_ReturnsSafeFormatFailure()
    {
        var result = AiToolPayloadGuard.ValidateJsonObject(
            "{\"title\":\"Draft\",\"organizationId\":\"not-a-guid\"}",
            AllowedFields,
            ForbiddenFields,
            SchemaJson);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_tool_argument_format");
        await Assert.That(result.FailureMessage).DoesNotContain("organizationId");
        await Assert.That(result.FailureMessage).DoesNotContain("not-a-guid");
    }

    [Test]
    public async Task ValidateJsonObject_WhenSchemaNumberIsBelowMinimum_ReturnsSafeValueFailure()
    {
        var result = AiToolPayloadGuard.ValidateJsonObject(
            "{\"title\":\"Draft\",\"price\":-1}",
            AllowedFields,
            ForbiddenFields,
            SchemaJson);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_tool_argument_value");
        await Assert.That(result.FailureMessage).DoesNotContain("price");
        await Assert.That(result.FailureMessage).DoesNotContain("-1");
    }

    [Test]
    public async Task ValidateJsonObject_WhenSchemaStringIsTooLong_ReturnsSafeValueFailure()
    {
        var result = AiToolPayloadGuard.ValidateJsonObject(
            "{\"title\":\"Draft\",\"currencyCode\":\"EURO\"}",
            AllowedFields,
            ForbiddenFields,
            SchemaJson);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_tool_argument_value");
        await Assert.That(result.FailureMessage).DoesNotContain("currencyCode");
        await Assert.That(result.FailureMessage).DoesNotContain("EURO");
    }

    [Test]
    public async Task ValidatePayload_WhenRegistryHasDefinition_UsesDefinitionFieldPolicy()
    {
        var registry = new AiToolContractRegistry(
        [
            new AiToolDefinition(
                Explore.Domain.Ai.AiProposedActionKind.CreateEventDraft,
                "CreateEventDraft",
                "Create event draft",
                "{}",
                AllowedFields,
                ForbiddenFields)
        ]);

        var result = registry.ValidatePayload(
            Explore.Domain.Ai.AiProposedActionKind.CreateEventDraft,
            "{\"title\":\"Draft\"}");

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ValidatePayload_WhenRegistryDoesNotKnowKind_ReturnsUnknownActionKindFailure()
    {
        var registry = new AiToolContractRegistry([]);

        var result = registry.ValidatePayload(
            Explore.Domain.Ai.AiProposedActionKind.CreateEventDraft,
            "{\"title\":\"Draft\"}");

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unknown_action_kind");
        await Assert.That(string.Join(" ", result.EffectiveRecovery.NextActions)).Contains("Regenerate the tool call arguments");
    }

    [Test]
    public async Task ValidateJsonObject_WhenSchemaUsesFormatsEnumsObjectsAndNullability_ReturnsSuccess()
    {
        var result = AiToolPayloadGuard.ValidateJsonObject(
            """
            {
              "startsOn": "2026-06-07",
              "startsAt": "2026-06-07T12:30:00Z",
              "timezone": "Europe/Brussels",
              "visibility": 1,
              "optionalNote": null,
              "metadata": { "source": "assistant" }
            }
            """,
            ShapeFields,
            schemaJson: ShapeSchemaJson);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ValidateJsonObject_WhenDateFormatIsInvalid_ReturnsSafeFormatFailure()
    {
        var result = AiToolPayloadGuard.ValidateJsonObject(
            """
            {
              "startsOn": "not-a-date",
              "startsAt": "2026-06-07T12:30:00Z",
              "visibility": 1,
              "metadata": { "source": "assistant" }
            }
            """,
            ShapeFields,
            schemaJson: ShapeSchemaJson);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_tool_argument_format");
        await Assert.That(result.FailureMessage).DoesNotContain("not-a-date");
    }

    [Test]
    public async Task ValidateJsonObject_WhenEnumValueIsUnsupported_ReturnsSafeValueFailure()
    {
        var result = AiToolPayloadGuard.ValidateJsonObject(
            """
            {
              "startsOn": "2026-06-07",
              "startsAt": "2026-06-07T12:30:00Z",
              "visibility": 9,
              "metadata": { "source": "assistant" }
            }
            """,
            ShapeFields,
            schemaJson: ShapeSchemaJson);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_tool_argument_value");
        await Assert.That(result.FailureMessage).DoesNotContain("9");
    }

    [Test]
    public async Task ValidateJsonObject_WhenNestedAdditionalPropertyExists_ReturnsUnsupportedField()
    {
        var result = AiToolPayloadGuard.ValidateJsonObject(
            """
            {
              "startsOn": "2026-06-07",
              "startsAt": "2026-06-07T12:30:00Z",
              "visibility": 1,
              "metadata": { "source": "assistant", "rawSql": "select * from events" }
            }
            """,
            ShapeFields,
            schemaJson: ShapeSchemaJson);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unsupported_tool_argument");
        await Assert.That(result.FailureMessage).DoesNotContain("rawSql");
    }

    [Test]
    public async Task ValidateJsonObject_WhenHiddenRuntimeContextParameterIsProvided_ReturnsForbiddenField()
    {
        var result = AiToolPayloadGuard.ValidateJsonObject(
            $$"""
            {
              "startsOn": "2026-06-07",
              "startsAt": "2026-06-07T12:30:00Z",
              "visibility": 1,
              "metadata": { "source": "assistant" },
              "runtimeTenantId": "{{Guid.CreateVersion7()}}"
            }
            """,
            ShapeFields,
            schemaJson: ShapeSchemaJson);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("forbidden_tool_argument");
        await Assert.That(result.FailureMessage).DoesNotContain("runtimeTenantId");
    }

    [Test]
    public async Task RecoveryResult_WhenMachineOutputIsTooLong_DropsMachineOutput()
    {
        var recovery = AiToolRecoveryResult.ForFailure(
            "invalid_tool_arguments",
            machineOutputJson: new string('x', AiToolRecoveryResult.MaxMachineOutputJsonLength + 1));

        await Assert.That(recovery.MachineOutputJson).IsNull();
    }

    [Test]
    public async Task RecoveryResult_WithWarnings_KeepsBoundedWarningsAndNextActions()
    {
        var recovery = AiToolRecoveryResult.WithWarnings(
            ["  Review timezone before confirmation.  ", new string('x', 241)],
            ["  Open the proposal card.  "]);

        await Assert.That(recovery.Warnings).IsEquivalentTo(["Review timezone before confirmation."]);
        await Assert.That(recovery.NextActions).IsEquivalentTo(["Open the proposal card."]);
    }
}
