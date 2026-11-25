using System.Text.Json.Nodes;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class SecurityControlRegisterTests
{
    private const string RegisterFileName = "register.json";
    private const string HostProfileField = "hostProfile";
    private const string ProjectField = "project";
    private const string OwnedRuntimeLane = "owned-runtime";
    private const string OwnedLoadCorrectnessLane = "owned-load-correctness";

    [Test]
    public async Task TwoApiLanesCannotReplaceTheCompleteSevenProfileRegistration()
    {
        await Assert.That(SecurityControlRegister.TryValidate(RegisterPath(), out _)).IsTrue();
        JsonObject register = JsonNode.Parse(await File.ReadAllBytesAsync(RegisterPath()))!.AsObject();
        JsonArray requirements = register["executionRequirements"]!.AsArray();
        await Assert.That(requirements.Count).IsEqualTo(7);
        for (int index = requirements.Count - 1; index >= 0; index--)
            if (requirements[index]!["laneId"]!.GetValue<string>().StartsWith("owned-", StringComparison.Ordinal))
                requirements.RemoveAt(index);
        await Assert.That(requirements.Count).IsEqualTo(2);
        string directory = Directory.CreateTempSubdirectory("native-seven-register-").FullName;
        try
        {
            string path = Path.Combine(directory, RegisterFileName);
            await File.WriteAllTextAsync(path, register.ToJsonString());
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
        JsonObject register = OwnedRegister(RegisterPath());
        JsonArray requirements = register["executionRequirements"]!.AsArray();
        JsonObject runtime = requirements[2]!.AsObject();
        switch (variant)
        {
            case "partial-group": requirements.RemoveAt(requirements.Count - 1); break;
            case "unknown-lane": runtime["laneId"] = "future-unreviewed-profile"; break;
            case "unknown-profile": runtime[HostProfileField] = "production"; break;
            case "contradictory-profile": runtime[HostProfileField] = "owned-browser-assurance"; break;
            case "wrong-project": runtime[ProjectField] = requirements[0]![ProjectField]!.GetValue<string>(); break;
            case "empty-tests": runtime["requiredTests"] = new JsonArray(); break;
            case "duplicate-variant": runtime["requiredTests"]![1] = runtime["requiredTests"]![0]!.DeepClone(); break;
        }

        string directory = Path.Combine(Path.GetTempPath(), $"owned-register-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, RegisterFileName);
            await File.WriteAllTextAsync(path, register.ToJsonString());
            bool accepted = SecurityControlRegister.TryValidate(path, out _);
            await Assert.That(accepted).IsEqualTo(expected);
            if (variant == "complete")
            {
                var parsed = SecurityControlRegister.ReadExecutionRequirements(path);
                var runtimeRequirement = parsed[OwnedRuntimeLane];
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
                await Assert.That(parsed.Where(lane => lane.Key != OwnedRuntimeLane)
                    .All(lane => lane.Value.Lifecycles.Count == 0)).IsTrue();
                var load = parsed[OwnedLoadCorrectnessLane];
                await Assert.That(load.LoadPurposes.Keys.ToHashSet().SetEquals(load.Tests)).IsTrue();
                await Assert.That(load.LoadPurposes.Values.ToHashSet().SetEquals(new[]
                    { SecurityControlRegister.LoadPurpose.Foundation, SecurityControlRegister.LoadPurpose.Programme })).IsTrue();
                await Assert.That(parsed.Where(lane => lane.Key != OwnedLoadCorrectnessLane).All(lane => lane.Value.LoadPurposes.Count == 0)).IsTrue();
                foreach (string invalidPurpose in new[] { "missing", "null", "unknown", "other-lane" })
                {
                    JsonObject malformed = register.DeepClone().AsObject();
                    JsonArray lanes = malformed["executionRequirements"]!.AsArray();
                    JsonObject loadRow = lanes.Single(lane => lane!["laneId"]!.GetValue<string>() == OwnedLoadCorrectnessLane)!["requiredTests"]![0]!.AsObject();
                    switch (invalidPurpose)
                    {
                        case "missing": loadRow.Remove("loadPurpose"); break;
                        case "null": loadRow["loadPurpose"] = null; break;
                        case "unknown": loadRow["loadPurpose"] = "future-purpose"; break;
                        case "other-lane": lanes[0]!["requiredTests"]![0]!["loadPurpose"] = "foundation"; break;
                    }
                    await File.WriteAllTextAsync(path, malformed.ToJsonString());
                    await Assert.That(SecurityControlRegister.TryValidate(path, out _)).IsFalse();
                }
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
    [Arguments("verified", "Protected", "ci-run-123", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", true)]
    [Arguments("mitigated", "Improvable", "pr-88-evidence", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", true)]
    [Arguments("accepted", "Not applicable", "steward-review-2026-11", "cccccccccccccccccccccccccccccccccccccccc", true)]
    [Arguments("verified", "Protected", "", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    [Arguments("verified", null, "ci-run-123", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    [Arguments("verified", "Protected", "ci-run-123", null, false)]
    [Arguments("mitigated", "UnknownDisposition", "ci-run-123", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    [Arguments("pending", "Protected", "ci-run-123", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    [Arguments("closed", "Protected", "ci-run-123", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", false)]
    public async Task ControlLifecycleProgressionRequiresStructuralEvidenceAndValidDisposition(
        string verificationStatus,
        string? finalDisposition,
        string evidenceEntry,
        string? codeArtifactIdentity,
        bool expected)
    {
        JsonObject register = JsonNode.Parse(await File.ReadAllBytesAsync(RegisterPath()))!.AsObject();
        JsonObject first = register["references"]!.AsArray()[0]!.AsObject();
        first["verificationStatus"] = verificationStatus;
        first["finalDisposition"] = finalDisposition is null ? null : JsonValue.Create(finalDisposition);
        first["executionEvidence"] = string.IsNullOrEmpty(evidenceEntry) ? new JsonArray() : new JsonArray(evidenceEntry);
        first["codeArtifactIdentity"] = codeArtifactIdentity is null ? null : JsonValue.Create(codeArtifactIdentity);

        string directory = Path.Combine(Path.GetTempPath(), "security-register-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, RegisterFileName);
        try
        {
            await File.WriteAllTextAsync(path, register.ToJsonString());
            bool accepted = SecurityControlRegister.TryValidate(path, out string diagnostic);

            await Assert.That(accepted).IsEqualTo(expected).Because(diagnostic);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    [Arguments("empty-references")]
    [Arguments("duplicate-reference")]
    [Arguments("unknown-owner")]
    [Arguments("unknown-scenario-reference")]
    [Arguments("unknown-family-reference")]
    [Arguments("unassigned-reference")]
    [Arguments("duplicate-supplementary-mechanism")]
    [Arguments("unowned-finding")]
    [Arguments("missing-runtime-host")]
    [Arguments("test-provider-as-supported-engine")]
    [Arguments("unqualified-provider-finding")]
    [Arguments("empty-typed-pairs")]
    [Arguments("missing-execution-owner-evidence")]
    [Arguments("unknown-standard-profile")]
    [Arguments("empty-native-pairs")]
    [Arguments("empty-classes")]
    [Arguments("empty-mitigations")]
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
        JsonObject register = JsonNode.Parse(await File.ReadAllBytesAsync(RegisterPath()))?.AsObject() ??
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
                register["executionRequirements"]![0]![HostProfileField] = "combined-standalone";
                break;
            case "wrong-required-project":
                register["executionRequirements"]![0]![ProjectField] = "unrelated.csproj";
                break;
            case "empty-references":
                references.Clear();
                break;
            case "duplicate-reference":
                first["id"] = "REF-209";
                break;
            case "unknown-owner":
                first["owningPhases"] = new JsonArray(30);
                break;
            case "unknown-scenario-reference":
                first["scenarios"] = new JsonArray("S99");
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
            case "empty-typed-pairs":
                register["typedContractInventory"]!["dtoPairs"]!.AsArray().Clear();
                break;
            case "missing-execution-owner-evidence":
                register["executionOwnerInventory"]![0]!["requiredEvidence"] = "";
                break;
            case "unknown-standard-profile":
                register["asvs"]!["requirements"]![0]!["prerequisiteProfiles"] = new JsonArray("test-only");
                break;
            case "empty-native-pairs":
                register["typedContractInventory"]!["nativePairs"]!.AsArray().Clear();
                break;
            case "empty-classes":
                (register["classes"]?.AsArray() ?? throw new InvalidDataException()).Clear();
                break;
            case "empty-mitigations":
                (register["apiFindingMitigationPairs"]?.AsArray() ?? throw new InvalidDataException()).Clear();
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
        string path = Path.Combine(directory, RegisterFileName);
        try
        {
            await File.WriteAllTextAsync(path, register.ToJsonString());
            bool accepted = SecurityControlRegister.TryValidate(path, out string diagnostic);

            await Assert.That(accepted).IsFalse();
            await Assert.That(diagnostic).IsNotEqualTo(string.Empty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static JsonObject OwnedRegister(string shippedRegister)
    {
        var register = JsonNode.Parse(File.ReadAllBytes(shippedRegister))!.AsObject();
        JsonArray requirements = register["executionRequirements"]!.AsArray();
        foreach ((string id, string profile) in new[]
        {
            (OwnedRuntimeLane, "owned-apphost-assurance"),
            ("owned-browser-journeys", "owned-browser-assurance"),
            ("owned-browser-security", "owned-browser-assurance"),
            ("owned-load-control", "owned-apphost-load-assurance"),
            (OwnedLoadCorrectnessLane, "owned-apphost-load-assurance"),
        })
        {
            foreach (JsonNode? previous in requirements.Where(requirement =>
                requirement?["laneId"]?.GetValue<string>() == id).ToArray())
            {
                requirements.Remove(previous);
            }

            requirements.Add(new JsonObject
            {
                ["laneId"] = id,
                [ProjectField] = "tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj",
                [HostProfileField] = profile,
                ["requiredTests"] = new JsonArray(new[] { "alpha", "beta" }.Select(variant =>
                {
                    var test = new JsonObject
                    {
                        ["className"] = "Synthetic.OwnedProfile",
                        ["methodName"] = "SyntheticWitness",
                        ["testName"] = $"SyntheticWitness({id}, {variant})",
                    };
                    if (id == OwnedRuntimeLane)
                    {
                        test["lifecycle"] = new JsonObject
                        {
                            ["stage"] = "none",
                            ["completion"] = "success",
                            ["directContainers"] = false,
                            ["resource"] = null,
                            ["signals"] = new JsonArray(),
                            ["rootDisposition"] = "not-applicable",
                        };
                    }

                    if (id == OwnedLoadCorrectnessLane)
                    {
                        test["loadPurpose"] = variant == "alpha" ? "foundation" : "programme";
                    }

                    return (JsonNode)test;
                }).ToArray()),
            });
        }

        return register;
    }

    /// <summary>
    /// Resolves the shipped register from the test output location rather than <c>[CallerFilePath]</c>,
    /// because deterministic CI builds map source paths to <c>/_/</c>.
    /// </summary>
    private static string RegisterPath() =>
        Path.Combine(RepositoryRoot.Find(), "eng", "security", "security-control-register.json");
}
