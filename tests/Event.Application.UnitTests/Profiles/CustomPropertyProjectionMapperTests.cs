using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.Mappings;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Profiles;

public sealed class CustomPropertyProjectionMapperTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Guid RowId = Guid.Parse("01920000-0000-7000-8000-000000000001");
    private static readonly Guid DefinitionId = Guid.Parse("01920000-0000-7000-8000-000000000002");
    private static readonly Guid ValueId = Guid.Parse("01920000-0000-7000-8000-000000000003");
    private static readonly Guid ResourceId = Guid.Parse("01920000-0000-7000-8000-000000000004");
    private static readonly Guid TenantId = Guid.Parse("01920000-0000-7000-8000-000000000005");
    private static readonly Guid OptionId = Guid.Parse("01920000-0000-7000-8000-000000000006");
    private static readonly DateTimeOffset RebuildStarted = new(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(2));

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("checkpoint-42")]
    public async Task Status_PreservesCountersOffsetsAndSignalDefaults(string? checkpoint)
    {
        var source = new CustomPropertyProjectionStatus
        {
            ProjectionName = "event",
            ProjectionVersion = 2,
            TenantId = TenantId,
            State = CustomPropertyProjectionState.Failed,
            LastRebuildStartedAt = RebuildStarted,
            LastRebuildCompletedAt = null,
            RowsProcessed = 6_000_000_000L,
            RowsFailed = 3,
            LastCheckpoint = checkpoint,
            LastErrorMessage = null,
            ConcurrencyStamp = RowId
        };
        var expected = JsonNode.Parse("""
            {
              "projectionName": "event", "projectionVersion": 2,
              "tenantId": "01920000-0000-7000-8000-000000000005", "state": 2,
              "lastRebuildStartedAt": "2026-09-01T10:00:00+02:00",
              "lastRebuildCompletedAt": null, "rowsProcessed": 6000000000, "rowsFailed": 3,
              "lastCheckpoint": null, "lastErrorMessage": null,
              "pendingDirtyScopeCount": 0, "requiresOperatorAction": false,
              "operationalState": "unknown", "recommendedAction": null
            }
            """)!.AsObject();
        expected["lastCheckpoint"] = checkpoint;
        expected["recommendedAction"] = new ProjectionStatusDto().RecommendedAction;
        await AssertJson(CustomPropertyProjectionMapper.ToStatus(source), expected);
    }

    [Test]
    public async Task DirtyScope_PreservesLongCursorScopeAndUndrainedState()
    {
        var source = new CustomPropertyProjectionDirtyScope
        {
            Id = 5_000_000_001L,
            ProjectionName = "session",
            ProjectionVersion = 2,
            TenantId = TenantId,
            ScopeType = CustomPropertyProjectionScopeType.EventSession,
            ScopeId = ResourceId,
            DefinitionId = null,
            Reason = "value_changed",
            CreatedAt = RebuildStarted,
            DrainedAt = null
        };
        var expected = JsonNode.Parse("""
            {
              "id": 5000000001, "projectionName": "session", "projectionVersion": 2,
              "tenantId": "01920000-0000-7000-8000-000000000005", "scopeType": 1,
              "scopeId": "01920000-0000-7000-8000-000000000004", "definitionId": null,
              "reason": "value_changed", "createdAt": "2026-09-01T10:00:00+02:00", "drainedAt": null
            }
            """)!.AsObject();
        await AssertJson(CustomPropertyProjectionMapper.ToDirtyScope(source), expected);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EventRow_PreservesOnlyScalarReadModelValues(bool nullableValues)
    {
        var source = new EventCustomPropertyProjection
        {
            Id = RowId,
            EventCustomPropertyDefinitionId = DefinitionId,
            EventCustomPropertyValueId = ValueId,
            EventId = ResourceId,
            TenantId = TenantId,
            Namespace = "test",
            Key = "capacity",
            PropertyType = PropertyType.Number,
            ExposureLevel = ExposureLevel.OrganizerOnly,
            IsSearchable = true,
            IsFilterable = false,
            IsExportable = true,
            IsModerationRelevant = false,
            IsAnalyticsRelevant = true,
            Ordinal = 7,
            OptionId = nullableValues ? null : OptionId,
            TextValue = nullableValues ? null : "",
            NumberValue = nullableValues ? null : 42.75m,
            BooleanValue = nullableValues ? null : false,
            DateTimeValue = nullableValues ? null : RebuildStarted,
            NormalizedValue = nullableValues ? null : "normalized",
            UpdatedAt = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc)
        };
        await AssertJson(CustomPropertyProjectionMapper.ToEventRow(source), ExpectedRow(false, nullableValues));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SessionRow_PreservesOnlyScalarReadModelValues(bool nullableValues)
    {
        var source = new EventSessionCustomPropertyProjection
        {
            Id = RowId,
            EventSessionCustomPropertyDefinitionId = DefinitionId,
            EventSessionCustomPropertyValueId = ValueId,
            EventSessionId = ResourceId,
            TenantId = TenantId,
            Namespace = "test",
            Key = "capacity",
            PropertyType = PropertyType.Number,
            ExposureLevel = ExposureLevel.OrganizerOnly,
            IsSearchable = true,
            IsFilterable = false,
            IsExportable = true,
            IsModerationRelevant = false,
            IsAnalyticsRelevant = true,
            Ordinal = 7,
            OptionId = nullableValues ? null : OptionId,
            TextValue = nullableValues ? null : "",
            NumberValue = nullableValues ? null : 42.75m,
            BooleanValue = nullableValues ? null : false,
            DateTimeValue = nullableValues ? null : RebuildStarted,
            NormalizedValue = nullableValues ? null : "normalized",
            UpdatedAt = new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc)
        };
        await AssertJson(CustomPropertyProjectionMapper.ToSessionRow(source), ExpectedRow(true, nullableValues));
    }

    private static JsonObject ExpectedRow(bool session, bool nullableValues)
    {
        var expected = JsonNode.Parse("""
            {
              "id": "01920000-0000-7000-8000-000000000001",
              "tenantId": "01920000-0000-7000-8000-000000000005",
              "namespace": "test", "key": "capacity", "propertyType": 2, "exposureLevel": 2,
              "isSearchable": true, "isFilterable": false, "isExportable": true,
              "isModerationRelevant": false, "isAnalyticsRelevant": true, "ordinal": 7,
              "optionId": "01920000-0000-7000-8000-000000000006", "textValue": "",
              "numberValue": 42.75, "booleanValue": false,
              "dateTimeValue": "2026-09-01T10:00:00+02:00", "normalizedValue": "normalized",
              "updatedAt": "2026-09-02T10:00:00Z"
            }
            """)!.AsObject();
        expected[session ? "eventSessionCustomPropertyDefinitionId" : "eventCustomPropertyDefinitionId"] = "01920000-0000-7000-8000-000000000002";
        expected[session ? "eventSessionCustomPropertyValueId" : "eventCustomPropertyValueId"] = "01920000-0000-7000-8000-000000000003";
        expected[session ? "eventSessionId" : "eventId"] = "01920000-0000-7000-8000-000000000004";
        if (nullableValues)
        {
            foreach (var field in new[] { "optionId", "textValue", "numberValue", "booleanValue", "dateTimeValue", "normalizedValue" })
            {
                expected[field] = null;
            }
        }

        return expected;
    }

    private static async Task AssertJson<T>(T actual, JsonObject expected)
    {
        var serialized = JsonSerializer.SerializeToNode(actual, JsonOptions);
        await Assert.That(JsonNode.DeepEquals(serialized, expected)).IsTrue()
            .Because($"Expected {expected}; actual {serialized}");
    }

}
