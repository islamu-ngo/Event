using System.Text.Json;

namespace Explore.GeneratedContracts.Tests;

public sealed class LearningGeneratedContractTests
{
    [Test]
    public async Task PublishedLearningOperationsKeepTheirNamedNativeContracts()
    {
        using var document = await ReadAsync();
        var paths = document.RootElement.GetProperty("paths");
        var expected = new[]
        {
            (Path: "/api/learning/context", Method: "get", Id: "GetLearningContext"),
            (Path: "/api/learning/tours/{family}/plan", Method: "get", Id: "GetLearningTourPlan"),
            (Path: "/api/learning/practice/{id}", Method: "get", Id: "GetLearningPractice"),
            (Path: "/api/learning/tours/claim", Method: "post", Id: "ClaimLearningTour"),
            (Path: "/api/learning/tours/outcome", Method: "post", Id: "RecordLearningTourOutcome"),
            (Path: "/api/learning/preferences", Method: "patch", Id: "UpdateLearningPreferences"),
            (Path: "/api/learning/practice/actions", Method: "post", Id: "ApplyLearningPracticeAction"),
            (Path: "/api/learning/space/actions", Method: "post", Id: "ManageTenantLearningSpace")
        };
        foreach (var operation in expected)
        {
            var contract = paths.GetProperty(operation.Path).GetProperty(operation.Method);
            await Assert.That(contract.GetProperty("operationId").GetString()).IsEqualTo(operation.Id);
            await Assert.That(contract.GetProperty("responses").TryGetProperty("200", out _)).IsTrue();
            foreach (string status in new[] { "400", "401", "403", "404", "409" })
                await Assert.That(contract.GetProperty("responses").TryGetProperty(status, out _)).IsTrue();
        }
    }

    [Test]
    public async Task TenantLearningGuidanceOperationsKeepTheirNamedResponses()
    {
        using var document = await ReadAsync();
        var paths = document.RootElement.GetProperty("paths");
        foreach (var operation in new[]
        {
            (Path: "/api/tenant/settings/documents/learning-guidance", Method: "get", Id: "GetLearningGuidancePolicy"),
            (Path: "/api/tenant/settings/documents/learning-guidance", Method: "patch", Id: "UpdateLearningGuidancePolicy"),
            (Path: "/api/learning/requirements/{requirementKey}/acknowledge", Method: "post", Id: "LearningAcknowledgeRequirement")
        })
        {
            var contract = paths.GetProperty(operation.Path).GetProperty(operation.Method);
            await Assert.That(contract.GetProperty("operationId").GetString()).IsEqualTo(operation.Id);
            foreach (string status in new[] { "200", "400", "401", "403", "409" })
                await Assert.That(contract.GetProperty("responses").TryGetProperty(status, out _)).IsTrue();
        }
    }

    [Test]
    public async Task PublishedLearningRequestsAreClosedAndTheirEnumsAreExactStrings()
    {
        using var document = await ReadAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (string request in new[]
        {
            "LearningClaimTourRequest", "LearningRecordTourOutcomeRequest", "LearningUpdatePreferencesRequest",
            "LearningApplyPracticeActionRequest", "LearningManageSpaceRequest"
        })
        {
            var schema = schemas.GetProperty(request);
            await Assert.That(schema.GetProperty("additionalProperties").ValueKind).IsEqualTo(JsonValueKind.False);
            await Assert.That(schema.GetProperty("properties").TryGetProperty("expectedScopeStamp", out _)).IsTrue();
            await Assert.That(schema.GetProperty("properties").TryGetProperty("expectedRevision", out _)).IsTrue();
        }
        var expected = new Dictionary<string, string[]>
        {
            ["LearningTourFamily"] = ["Shell", "Studio", "EventManagement", "NormalAi"],
            ["LearningTourAction"] = ["Progress", "Complete", "Skip", "Interrupt", "Snooze"],
            ["LearningSnoozeDuration"] = ["OneHour", "OneDay", "OneWeek"],
            ["LearningTourOutcome"] = ["InProgress", "Completed", "Skipped", "Interrupted"],
            ["LearningPracticeAction"] = ["Start", "Choose", "Confirm", "Reset", "Clear"],
            ["LearningPracticeChoiceSlot"] = ["None", "Primary", "Secondary", "Tertiary"],
            ["LearningSpaceAction"] = ["Remove", "Recreate"],
            ["LearningSpaceStatus"] = ["Absent", "Active", "Removed"],
            ["LearningSimulationStatus"] = ["Choosing", "AwaitingConfirmation", "Simulated", "Cleared"]
        };
        foreach (var enumeration in expected)
        {
            var schema = schemas.GetProperty(enumeration.Key);
            await Assert.That(schema.GetProperty("type").GetString()).IsEqualTo("string");
            await Assert.That(schema.GetProperty("enum").EnumerateArray().Select(value =>
                    value.GetString() ?? throw new JsonException("An enum member must be a string.")))
                .IsEquivalentTo(enumeration.Value);
        }
    }

    [Test]
    public async Task LearningGuidancePolicyAndAcknowledgementRequestsAreClosedAndSourceBound()
    {
        using var document = await ReadAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");

        var update = schemas.GetProperty("UpdateLearningGuidancePolicyRequest");
        await Assert.That(update.GetProperty("additionalProperties").ValueKind).IsEqualTo(JsonValueKind.False);
        await Assert.That(update.GetProperty("required").EnumerateArray().Select(value => value.GetString())
                .Select(value => value ?? throw new JsonException("A schema member must be a string.")))
            .IsEquivalentTo(new[] { "expectedConcurrencyStamp", "policy" });
        var updateStamp = update.GetProperty("properties").GetProperty("expectedConcurrencyStamp");
        await Assert.That(updateStamp.GetProperty("type").EnumerateArray().Select(value => value.GetString())
                .Select(value => value ?? throw new JsonException("A schema member must be a string.")))
            .IsEquivalentTo(new[] { "null", "string" });
        await Assert.That(updateStamp.GetProperty("format").GetString()).IsEqualTo("uuid");

        var policy = schemas.GetProperty("LearningGuidancePolicyDto");
        var policyProperties = policy.GetProperty("properties");
        await Assert.That(policy.GetProperty("required").EnumerateArray().Select(value => value.GetString())
                .Select(value => value ?? throw new JsonException("A schema member must be a string.")))
            .IsEquivalentTo(new[] { "schemaVersion", "defaultsVersion", "concurrencyStamp", "policy" });
        foreach (string name in new[] { "schemaVersion", "defaultsVersion", "concurrencyStamp", "policy" })
            await Assert.That(policyProperties.TryGetProperty(name, out _)).IsTrue();
        await Assert.That(policyProperties.TryGetProperty("canEdit", out _)).IsFalse();
        await Assert.That(policyProperties.TryGetProperty("CanEdit", out _)).IsFalse();
        var policyStamp = policyProperties.GetProperty("concurrencyStamp");
        await Assert.That(policyStamp.GetProperty("type").EnumerateArray().Select(value => value.GetString())
                .Select(value => value ?? throw new JsonException("A schema member must be a string.")))
            .IsEquivalentTo(new[] { "null", "string" });
        await Assert.That(policyStamp.GetProperty("format").GetString()).IsEqualTo("uuid");

        var payload = schemas.GetProperty("LearningGuidancePolicyPayload");
        await Assert.That(payload.GetProperty("additionalProperties").ValueKind).IsEqualTo(JsonValueKind.False);
        await Assert.That(payload.GetProperty("required").EnumerateArray().Select(value => value.GetString())
                .Select(value => value ?? throw new JsonException("A schema member must be a string.")))
            .IsEquivalentTo(new[]
            {
                "enabled", "automaticInvitations", "invitationAudience", "manualAccessAudience",
                "requirementPolicy", "familyOverrides"
            });
        var payloadProperties = payload.GetProperty("properties");
        await Assert.That(payloadProperties.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(new[]
            {
                "enabled", "automaticInvitations", "invitationAudience", "manualAccessAudience",
                "requirementPolicy", "familyOverrides"
            });

        var familyOverride = schemas.GetProperty("LearningGuidanceFamilyOverrideDto");
        await Assert.That(familyOverride.GetProperty("additionalProperties").ValueKind).IsEqualTo(JsonValueKind.False);
        await Assert.That(familyOverride.GetProperty("required").EnumerateArray().Select(value => value.GetString())
                .Select(value => value ?? throw new JsonException("A schema member must be a string.")))
            .IsEquivalentTo(new[] { "family", "enabled", "automaticInvitations" });
        await Assert.That(familyOverride.GetProperty("properties").EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(new[]
            {
                "family", "enabled", "automaticInvitations", "invitationAudience", "manualAccessAudience"
            });

        var wrapper = schemas.GetProperty("HalResourceOfLearningGuidancePolicyDto");
        if (!wrapper.TryGetProperty("properties", out var wrapperProperties))
            throw new JsonException(
                "HalResourceOfLearningGuidancePolicyDto must expose nonempty properties: schemaVersion, defaultsVersion, concurrencyStamp, policy, _links.");
        await Assert.That(wrapperProperties.EnumerateObject().Any()).IsTrue();
        foreach (string name in new[] { "schemaVersion", "defaultsVersion", "concurrencyStamp", "policy", "_links" })
            await Assert.That(wrapperProperties.TryGetProperty(name, out _)).IsTrue();
        await Assert.That(wrapperProperties.TryGetProperty("canEdit", out _)).IsFalse();
    }

    [Test]
    public async Task LearningAcknowledgementRequirementContractsKeepExactWireShapes()
    {
        using var document = await ReadAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var request = schemas.GetProperty("LearningAcknowledgeRequirementRequest");
        await Assert.That(request.GetProperty("additionalProperties").ValueKind).IsEqualTo(JsonValueKind.False);
        await Assert.That(request.GetProperty("required").EnumerateArray().Select(value => value.GetString())
                .Select(value => value ?? throw new JsonException("A schema member must be a string.")))
            .IsEquivalentTo(new[]
            {
                "intentId", "expectedScopeStamp", "expectedRevision", "expectedPolicySourceStamp", "materialVersion"
            });
        var requestProperties = request.GetProperty("properties");
        await Assert.That(requestProperties.EnumerateObject().Select(property => property.Name))
            .IsEquivalentTo(new[]
            {
                "intentId", "expectedScopeStamp", "expectedRevision", "expectedPolicySourceStamp", "materialVersion"
            });
        foreach (string name in new[] { "userId", "tenantId", "contextKey", "eventContextId", "ownerContext" })
            await Assert.That(requestProperties.TryGetProperty(name, out _)).IsFalse();
        await Assert.That(requestProperties.GetProperty("intentId").GetProperty("format").GetString()).IsEqualTo("uuid");

        var contextRequirement = schemas.GetProperty("LearningContextDto").GetProperty("properties")
            .GetProperty("requirement");
        await Assert.That(contextRequirement.GetProperty("$ref").GetString())
            .IsEqualTo("#/components/schemas/LearningRequirementDto");
        var requirement = schemas.GetProperty("LearningRequirementDto");
        var requirementProperties = requirement.GetProperty("properties");
        await Assert.That(requirement.GetProperty("required").EnumerateArray().Select(value => value.GetString())
                .Select(value => value ?? throw new JsonException("A schema member must be a string.")))
            .IsEquivalentTo(new[] { "requirementKey", "materialVersion", "status", "policySourceStamp" });
        foreach (string name in new[]
        {
            "requirementKey", "materialVersion", "status", "policySourceStamp", "acknowledgedAtUtc"
        })
            await Assert.That(requirementProperties.TryGetProperty(name, out _)).IsTrue();
        await Assert.That(requirementProperties.GetProperty("acknowledgedAtUtc").GetProperty("type")
                .EnumerateArray().Select(value => value.GetString()
                    ?? throw new JsonException("A schema member must be a string.")))
            .IsEquivalentTo(new[] { "null", "string" });
        await Assert.That(requirementProperties.GetProperty("acknowledgedAtUtc").GetProperty("format").GetString())
            .IsEqualTo("date-time");

        var expectedEnums = new Dictionary<string, string[]>
        {
            ["LearningGuidanceAudience"] = ["ActiveMembers", "TenantAdministrators"],
            ["LearningRequirementPolicy"] = ["Optional", "TenantAdminOrientationAcknowledgement"],
            ["LearningRequirementStatus"] = ["NotApplicable", "Pending", "Satisfied"]
        };
        foreach (var enumeration in expectedEnums)
        {
            var schema = schemas.GetProperty(enumeration.Key);
            await Assert.That(schema.GetProperty("type").GetString()).IsEqualTo("string");
            await Assert.That(schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString())
                    .Select(value => value ?? throw new JsonException("A schema member must be a string.")))
                .IsEquivalentTo(enumeration.Value);
        }

        foreach (string name in new[]
        {
            "LearningTourPlanDto", "HalResourceOfLearningTourPlanDto", "LearningTourProgressDto"
        })
            await Assert.That(schemas.GetProperty(name).GetProperty("properties").TryGetProperty("canInvite", out _))
                .IsTrue();
        await Assert.That(schemas.GetProperty("LearningCapabilitiesDto").GetProperty("properties")
            .TryGetProperty("canInviteTours", out _)).IsTrue();
        var practiceSummary = schemas.GetProperty("LearningPracticeSummaryDto").GetProperty("properties");
        await Assert.That(practiceSummary.TryGetProperty("unavailableReason", out _)).IsTrue();
    }

    [Test]
    public async Task SemanticPlanHalAndActionsExposeUsableSourceBoundContracts()
    {
        using var document = await ReadAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        var plan = schemas.GetProperty("HalResourceOfLearningTourPlanDto").GetProperty("properties");
        foreach (string name in new[]
        {
            "family", "contextKey", "eventContextId", "planFingerprint", "policySourceStamp",
            "learnerRevision", "steps", "_links"
        })
            await Assert.That(plan.TryGetProperty(name, out _)).IsTrue();

        var action = schemas.GetProperty("LearningRecordTourOutcomeRequest").GetProperty("properties");
        foreach (string name in new[]
        {
            "action", "stepKey", "stepVersion", "snoozeDuration",
            "expectedPlanFingerprint", "expectedPolicySourceStamp", "contextKey", "eventContextId"
        })
            await Assert.That(action.TryGetProperty(name, out _)).IsTrue();
        await Assert.That(action.TryGetProperty("outcome", out _)).IsFalse();
        await Assert.That(action.TryGetProperty("stepIndex", out _)).IsFalse();
        var progress = schemas.GetProperty("LearningTourProgressDto").GetProperty("properties");
        await Assert.That(progress.TryGetProperty("completedSteps", out _)).IsTrue();
        await Assert.That(progress.TryGetProperty("currentCoverage", out _)).IsTrue();
        await Assert.That(progress.TryGetProperty("stepIndex", out _)).IsFalse();
    }

    [Test]
    public async Task PublishedLearningResponsesAndNestedSimulationStateHaveNamedNonemptyShapes()
    {
        using var document = await ReadAsync();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        foreach (string name in new[]
        {
            "LearningContextDto", "LearningPracticeDto", "LearningMutationReceiptDto", "LearningPracticeSummaryDto",
            "LearningLessonDto", "LearningChoiceDto", "LearningSimulationStateDto", "LearningSimulationReceiptDto"
        })
        {
            var definition = schemas.GetProperty(name);
            await Assert.That(definition.GetProperty("properties").EnumerateObject().Any()).IsTrue();
        }
        await Assert.That(schemas.GetProperty("LearningMutationReceiptDto").GetProperty("properties")
            .TryGetProperty("id", out _)).IsTrue();
        await Assert.That(schemas.GetProperty("LearningPracticeDto").GetProperty("properties")
            .TryGetProperty("learnerRevision", out _)).IsTrue();
    }

    private static async Task<JsonDocument> ReadAsync()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
            directory = directory.Parent;
        string root = directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
        await using var stream = File.OpenRead(Path.Combine(root, "schemas", "openapi_islamu-event.json"));
        return await JsonDocument.ParseAsync(stream);
    }
}
