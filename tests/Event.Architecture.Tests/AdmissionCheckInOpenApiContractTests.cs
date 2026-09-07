using System.Text.Json;
using Explore.Blazor.Client.Clients;

namespace Event.Architecture.Tests;

public sealed class AdmissionCheckInOpenApiContractTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedWireValues =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["AdmissionCheckInAction"] = ["CheckIn", "Undo"],
            ["AdmissionCheckInDependencyStatus"] = ["Available", "Unavailable"],
            ["AdmissionCheckInOperationalAction"] = ["Stop", "Restore", "Reconcile"],
            ["AdmissionCheckInOperationalReasonCode"] =
                ["DeviceLoss", "ConnectivityOutage", "OperatorCorrection", "PostIncidentReconciliation"],
            ["AdmissionCheckInOperationalStatus"] = ["Active", "Stopped", "Unavailable"],
            ["AdmissionCheckInUndoReasonCodeEnum"] =
                ["OperatorCorrection", "DuplicateScan", "WrongTarget", "ExceptionalReconciliation"],
        };

    [Test]
    public async Task AdmissionEnums_AreStringComponentsReferencedByDtosAndGeneratedAsEnums()
    {
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(
            ContextSystemHelpers.RepoPath("schemas", "openapi_islamu-event.json")));
        JsonElement schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        foreach ((string schemaName, string[] expectedValues) in ExpectedWireValues)
        {
            JsonElement schema = schemas.GetProperty(schemaName);
            await Assert.That(schema.GetProperty("type").GetString()).IsEqualTo("string");
            await Assert.That(schema.GetProperty("enum").EnumerateArray()
                .Select(value => value.GetString()!)
                .ToArray()).IsEquivalentTo(expectedValues);

            Type? generatedType = typeof(IAdmissionCheckInClient).Assembly.GetType(
                $"Explore.Blazor.Client.Clients.{schemaName}");
            await Assert.That(generatedType).IsNotNull()
                .Because($"NSwag must generate {schemaName} from its canonical OpenAPI component");
            await Assert.That(generatedType!.IsEnum).IsTrue();
        }

        await AssertPropertyReference(
            schemas, "AdmissionCheckInOperationalResultDto", "action", "AdmissionCheckInOperationalAction");
        await AssertPropertyReference(
            schemas, "AdmissionCheckInOperationalResultDto", "status", "AdmissionCheckInOperationalStatus");
        await AssertPropertyReference(
            schemas, "AdmissionCheckInHealthDto", "infrastructureStatus", "AdmissionCheckInDependencyStatus");
    }

    private static async Task AssertPropertyReference(
        JsonElement schemas,
        string dtoSchemaName,
        string propertyName,
        string enumSchemaName)
    {
        string? reference = schemas.GetProperty(dtoSchemaName)
            .GetProperty("properties")
            .GetProperty(propertyName)
            .GetProperty("$ref")
            .GetString();

        await Assert.That(reference).IsEqualTo($"#/components/schemas/{enumSchemaName}");
    }
}
