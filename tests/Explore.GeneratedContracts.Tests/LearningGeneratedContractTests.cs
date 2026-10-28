using System.Text.Json;

namespace Explore.GeneratedContracts.Tests;

public sealed class LearningGeneratedContractTests
{
    [Test]
    public async Task PublishedLearningOperationsKeepTheirSevenNamedNativeContracts()
    {
        using var document = await ReadAsync();
        var paths = document.RootElement.GetProperty("paths");
        var expected = new[]
        {
            (Path: "/api/learning/context", Method: "get", Id: "GetLearningContext"),
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
            await Assert.That(schema.GetProperty("enum").EnumerateArray().Select(value => value.GetString()))
                .IsEquivalentTo(enumeration.Value);
        }
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
