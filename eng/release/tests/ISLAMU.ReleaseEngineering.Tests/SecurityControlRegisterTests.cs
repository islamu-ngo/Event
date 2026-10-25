using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class SecurityControlRegisterTests
{
    [Test]
    public async Task TwoApiLanesCannotReplaceTheCompleteSevenProfileRegistration()
    {
        await Assert.That(SecurityControlRegister.TryValidate(RegisterPath(), out _)).IsTrue();
        JsonObject register = JsonNode.Parse(File.ReadAllBytes(RegisterPath()))!.AsObject();
        JsonArray requirements = register["executionRequirements"]!.AsArray();
        await Assert.That(requirements.Count).IsEqualTo(7);
        for (int index = requirements.Count - 1; index >= 0; index--)
            if (requirements[index]!["laneId"]!.GetValue<string>().StartsWith("owned-", StringComparison.Ordinal))
                requirements.RemoveAt(index);
        await Assert.That(requirements.Count).IsEqualTo(2);
        string directory = Directory.CreateTempSubdirectory("native-seven-register-").FullName;
        try
        {
            string path = Path.Combine(directory, "register.json");
            File.WriteAllText(path, register.ToJsonString());
            await Assert.That(SecurityControlRegister.TryValidate(path, out _)).IsFalse();
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    [Arguments("complete", true)]
    [Arguments("partial-group", false)]
    [Arguments("unknown-lane", false)]
    [Arguments("unknown-profile", false)]
    [Arguments("contradictory-profile", false)]
    [Arguments("wrong-project", false)]
    [Arguments("empty-tests", false)]
    [Arguments("duplicate-variant", false)]
    public async Task OwnedProfilesRequireACompleteClosedRegistration(string variant, bool expected)
    {
        JsonObject register = SecurityExecutionEvidenceFixture.OwnedRegister(RegisterPath());
        JsonArray requirements = register["executionRequirements"]!.AsArray();
        JsonObject runtime = requirements[2]!.AsObject();
        switch (variant)
        {
            case "partial-group": requirements.RemoveAt(requirements.Count - 1); break;
            case "unknown-lane": runtime["laneId"] = "future-unreviewed-profile"; break;
            case "unknown-profile": runtime["hostProfile"] = "production"; break;
            case "contradictory-profile": runtime["hostProfile"] = "owned-browser-assurance"; break;
            case "wrong-project": runtime["project"] = requirements[0]!["project"]!.GetValue<string>(); break;
            case "empty-tests": runtime["requiredTests"] = new JsonArray(); break;
            case "duplicate-variant": runtime["requiredTests"]![1] = runtime["requiredTests"]![0]!.DeepClone(); break;
        }

        string directory = Path.Combine(Path.GetTempPath(), $"owned-register-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "register.json");
            await File.WriteAllTextAsync(path, register.ToJsonString());
            bool accepted = SecurityControlRegister.TryValidate(path, out _);
            await Assert.That(accepted).IsEqualTo(expected);
            if (variant == "complete")
            {
                var parsed = SecurityControlRegister.ReadExecutionRequirements(path);
                var runtimeRequirement = parsed["owned-runtime"];
                await Assert.That(runtimeRequirement.Lifecycles.Keys.ToHashSet().SetEquals(runtimeRequirement.Tests)).IsTrue();
                foreach (var lifecycle in runtimeRequirement.Lifecycles.Values)
                {
                    await Assert.That(lifecycle.Stage).IsEqualTo(SecurityControlRegister.RuntimeStage.None);
                    await Assert.That(lifecycle.Completion).IsEqualTo(SecurityControlRegister.RuntimeCompletion.Success);
                    await Assert.That(lifecycle.DirectContainers).IsFalse();
                    await Assert.That(lifecycle.Resource is null).IsTrue();
                    await Assert.That(lifecycle.Signals.Count).IsEqualTo(0);
                    await Assert.That(lifecycle.RootDisposition).IsEqualTo(SecurityControlRegister.RuntimeRootDisposition.NotApplicable);
                }
                await Assert.That(parsed.Where(lane => lane.Key != "owned-runtime")
                    .All(lane => lane.Value.Lifecycles.Count == 0)).IsTrue();
                foreach (string invalid in new[] { "missing", "null", "unknown-field", "unknown-stage",
                    "unknown-completion", "contradictory-stage", "direct-without-resource-operation",
                    "unexpected-resource", "duplicate-signal", "unknown-root-disposition", "other-lane" })
                {
                    JsonObject malformed = register.DeepClone().AsObject();
                    JsonArray lanes = malformed["executionRequirements"]!.AsArray();
                    JsonObject test = lanes[2]!["requiredTests"]![0]!.AsObject();
                    JsonObject lifecycle = test["lifecycle"]!.AsObject();
                    switch (invalid)
                    {
                        case "missing": test.Remove("lifecycle"); break;
                        case "null": test["lifecycle"] = null; break;
                        case "unknown-field": lifecycle["allowMissingHost"] = true; break;
                        case "unknown-stage": lifecycle["stage"] = "future-stage"; break;
                        case "unknown-completion": lifecycle["completion"] = "future-outcome"; break;
                        case "contradictory-stage": lifecycle["completion"] = "refused"; break;
                        case "direct-without-resource-operation": lifecycle["directContainers"] = true; break;
                        case "unexpected-resource": lifecycle["resource"] = "unreviewed-resource"; break;
                        case "duplicate-signal": lifecycle["signals"] = new JsonArray("caller-cancelled", "caller-cancelled"); break;
                        case "unknown-root-disposition": lifecycle["rootDisposition"] = "ignore-root"; break;
                        case "other-lane": lanes[0]!["requiredTests"]![0]!["lifecycle"] = lifecycle.DeepClone(); break;
                    }
                    await File.WriteAllTextAsync(path, malformed.ToJsonString());
                    await Assert.That(SecurityControlRegister.TryValidate(path, out _)).IsFalse();
                }
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Test]
    public async Task CompleteShippedSourceInventoryIsNotRuntimeCertification()
    {
        bool accepted = SecurityControlRegister.TryValidate(RegisterPath(), out string diagnostic);

        await Assert.That(accepted).IsTrue();
        await Assert.That(diagnostic).IsEqualTo(string.Empty);
    }

    [Test]
    [Arguments("missing-reference")]
    [Arguments("duplicate-reference")]
    [Arguments("unknown-owner")]
    [Arguments("missing-scenario")]
    [Arguments("unknown-family-reference")]
    [Arguments("unassigned-reference")]
    [Arguments("duplicate-supplementary-mechanism")]
    [Arguments("unowned-finding")]
    [Arguments("missing-runtime-host")]
    [Arguments("test-provider-as-supported-engine")]
    [Arguments("unqualified-provider-finding")]
    [Arguments("missing-typed-pair")]
    [Arguments("missing-execution-owner-evidence")]
    [Arguments("unknown-standard-profile")]
    [Arguments("missing-native-pair")]
    [Arguments("missing-class")]
    [Arguments("missing-mitigation")]
    [Arguments("mixed-standard-version")]
    [Arguments("unearned-closure")]
    [Arguments("missing-execution-requirements")]
    [Arguments("duplicate-required-lane")]
    [Arguments("empty-required-tests")]
    [Arguments("duplicate-required-test")]
    [Arguments("wrong-required-profile")]
    [Arguments("wrong-required-project")]
    public async Task IncompleteOrMisleadingCatalogueIsRejected(string failure)
    {
        JsonObject register = JsonNode.Parse(File.ReadAllBytes(RegisterPath()))?.AsObject() ??
            throw new InvalidDataException();
        JsonArray references = register["references"]?.AsArray() ?? throw new InvalidDataException();
        JsonObject first = references[0]?.AsObject() ?? throw new InvalidDataException();
        switch (failure)
        {
            case "missing-execution-requirements":
                register.Remove("executionRequirements");
                break;
            case "duplicate-required-lane":
                register["executionRequirements"]![1]!["laneId"] = "security-integration";
                break;
            case "empty-required-tests":
                register["executionRequirements"]![0]!["requiredTests"] = new JsonArray();
                break;
            case "duplicate-required-test":
                JsonArray requiredTests = register["executionRequirements"]![0]!["requiredTests"]!.AsArray();
                requiredTests.Add(requiredTests[0]!.DeepClone());
                break;
            case "wrong-required-profile":
                register["executionRequirements"]![0]!["hostProfile"] = "combined-standalone";
                break;
            case "wrong-required-project":
                register["executionRequirements"]![0]!["project"] = "unrelated.csproj";
                break;
            case "missing-reference":
                references.RemoveAt(0);
                break;
            case "duplicate-reference":
                first["id"] = "REF-209";
                break;
            case "unknown-owner":
                first["owningPhases"] = new JsonArray(30);
                break;
            case "missing-scenario":
                (register["scenarios"]?.AsArray() ?? throw new InvalidDataException()).RemoveAt(49);
                break;
            case "unknown-family-reference":
                JsonObject family = register["families"]?[0]?.AsObject() ?? throw new InvalidDataException();
                family["referenceIds"] = new JsonArray("REF-999");
                break;
            case "unassigned-reference":
                foreach (JsonNode? group in (register["families"]?.AsArray() ?? throw new InvalidDataException())
                    .Concat(register["supplementaryMechanisms"]?.AsArray() ?? throw new InvalidDataException()))
                {
                    JsonArray members = group?["referenceIds"]?.AsArray() ?? throw new InvalidDataException();
                    for (int index = members.Count - 1; index >= 0; index--)
                    {
                        if (members[index]?.GetValue<string>() == "REF-209") members.RemoveAt(index);
                    }
                }
                break;
            case "duplicate-supplementary-mechanism":
                JsonArray mechanisms = register["supplementaryMechanisms"]?.AsArray() ?? throw new InvalidDataException();
                mechanisms.Add(mechanisms[0]?.DeepClone());
                break;
            case "unowned-finding":
                register["apiFindingMitigationPairs"]![0]!["owningTasks"] = new JsonArray();
                break;
            case "missing-runtime-host":
                register["executionDimensions"]!["runtimeHosts"]!.AsArray().RemoveAt(2);
                break;
            case "test-provider-as-supported-engine":
                register["executionDimensions"]!["databaseProviders"]!["values"]!.AsArray().Add("InMemory");
                break;
            case "unqualified-provider-finding":
                register["qualifiedMappings"]!["providerPairs"]![0]!["findingId"] = "IVSD-F001";
                break;
            case "missing-typed-pair":
                register["typedContractInventory"]!["dtoPairs"]!.AsArray().RemoveAt(0);
                break;
            case "missing-execution-owner-evidence":
                register["executionOwnerInventory"]![0]!["requiredEvidence"] = "";
                break;
            case "unknown-standard-profile":
                register["asvs"]!["requirements"]![0]!["prerequisiteProfiles"] = new JsonArray("test-only");
                break;
            case "missing-native-pair":
                register["typedContractInventory"]!["nativePairs"]!.AsArray().RemoveAt(0);
                break;
            case "missing-class":
                (register["classes"]?.AsArray() ?? throw new InvalidDataException()).RemoveAt(0);
                break;
            case "missing-mitigation":
                (register["apiFindingMitigationPairs"]?.AsArray() ?? throw new InvalidDataException()).RemoveAt(0);
                break;
            case "mixed-standard-version":
                JsonObject asvs = register["asvs"]?.AsObject() ?? throw new InvalidDataException();
                asvs["version"] = "4.0.3";
                break;
            case "unearned-closure":
                first["finalDisposition"] = "Protected";
                first["verificationStatus"] = "closed";
                break;
            default:
                throw new InvalidDataException();
        }

        string directory = Path.Combine(Path.GetTempPath(), "security-register-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "register.json");
        try
        {
            File.WriteAllText(path, register.ToJsonString());
            bool accepted = SecurityControlRegister.TryValidate(path, out string diagnostic);

            await Assert.That(accepted).IsFalse();
            await Assert.That(diagnostic).IsNotEqualTo(string.Empty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string RegisterPath([CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath) ??
            throw new InvalidDataException(), "..", "..", "..", "security", "security-control-register.json"));
}
