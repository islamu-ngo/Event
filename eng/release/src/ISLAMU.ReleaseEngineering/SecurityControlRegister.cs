using System.Text.Json;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public static class SecurityControlRegister
{
    private const int MaximumBytes = 1_048_576;
    private const string ApplicationSourcePrefix = "src/Explore.Application/";
    private const string PendingStatus = "pending";

    private static class JsonFields
    {
        public const string Id = "id";
        public const string Scenarios = "scenarios";
        public const string VerificationStatus = "verificationStatus";
        public const string ReferenceIds = "referenceIds";
        public const string OwningPhases = "owningPhases";
        public const string PrerequisiteProfiles = "prerequisiteProfiles";
        public const string CreateOwner = "createOwner";
        public const string UpdateOwner = "updateOwner";
        public const string ClassName = "className";
        public const string MethodName = "methodName";
        public const string TestName = "testName";
    }

    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 32 };
    private static readonly Regex ScenarioIdPattern = new("^S[0-9]{2,}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex FamilyIdPattern = new("^FAMILY-[0-9]{3,}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex ReferenceIdPattern = new("^REF-[0-9]{3,}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex SupplementaryIdPattern = new("^SUPPLEMENTARY-[0-9]{3,}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex ProviderFindingPattern = new("^PC:IVSD-F(?<num>[0-9]{3,})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex ApiFindingPattern = new("^IVSD-F(?<num>[0-9]{3,})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex ClassIdPattern = new("^(?:BUG|VULN)-[0-9]{2,}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static readonly Lazy<IReadOnlyDictionary<string, ExecutionRequirement>> EmbeddedRequirements = new(() =>
    {
        using Stream stream = typeof(SecurityControlRegister).Assembly.GetManifestResourceStream(
            "ISLAMU.ReleaseEngineering.SecurityControlRegister.json") ?? throw new InvalidDataException();
        using JsonDocument document = JsonDocument.Parse(stream, DocumentOptions);
        if (!UniqueProperties(document.RootElement) || document.RootElement.GetProperty("schemaVersion").GetInt32() != 2)
        {
            throw new InvalidDataException();
        }

        return ReadExecutionRequirements(document.RootElement);
    });

    internal static IReadOnlyDictionary<string, ExecutionRequirement> ExecutionRequirements => EmbeddedRequirements.Value;

    private static readonly HashSet<string> Dispositions = new(StringComparer.Ordinal)
    {
        "Protected", "Vulnerable", "Improvable", "Unverified", "Not applicable",
    };

    private static readonly HashSet<string> ResolvedStatuses = new(StringComparer.Ordinal)
    {
        "verified", "mitigated", "accepted",
    };

    private static readonly HashSet<string> SupportedProviders = new(StringComparer.Ordinal)
    {
        "PostgreSql", "Sqlite", "SqlServer", "MySql", "MariaDb",
    };

    private static readonly HashSet<string> SupportedHosts = new(StringComparer.Ordinal)
    {
        "split-api", "split-bff", "combined-standalone",
    };

    public static int Run(string[] args, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        if (args.Length != 2)
        {
            output.WriteLine("invalid_arguments: verify-security-register requires a register path");
            return Program.UsageError;
        }

        if (!TryValidate(args[1], out string diagnostic))
        {
            output.WriteLine("security_register_rejected: " + diagnostic);
            return Program.ToolchainRejected;
        }

        output.WriteLine("security_register_verified: structural register and lifecycle invariants passed");
        return Program.Success;
    }

    public static bool TryValidate(string path, out string diagnostic)
    {
        diagnostic = "invalid_register";
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is < 1 or > MaximumBytes)
            {
                return false;
            }

            byte[] bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            using JsonDocument document = JsonDocument.Parse(bytes, DocumentOptions);
            JsonElement root = document.RootElement;
            if (!UniqueProperties(root) ||
                root.GetProperty("schemaVersion").GetInt32() != 2 ||
                string.IsNullOrWhiteSpace(root.GetProperty("workstream").GetString()) ||
                string.IsNullOrWhiteSpace(root.GetProperty("assurance").GetString()))
            {
                return false;
            }

            HashSet<string> scenarios = UniqueMatchingIdentities(root.GetProperty(JsonFields.Scenarios), ScenarioIdPattern);
            _ = ReadExecutionRequirements(root);
            HashSet<string> families = UniqueMatchingIdentities(root.GetProperty("families"), FamilyIdPattern);

            if (!TryValidateExecutionDimensions(root, out string[] hosts))
            {
                return false;
            }

            if (!ValidateProviderPairs(root, scenarios) ||
                !ValidateTypedContractInventory(root) ||
                !ValidateExecutionOwnerInventory(root, scenarios))
            {
                return false;
            }

            if (!ValidateAsvsRequirements(root, hosts, ref diagnostic) ||
                !ValidateApiFindingMitigationPairs(root, scenarios, ref diagnostic))
            {
                return false;
            }

            JsonElement references = root.GetProperty("references");
            HashSet<string> referenceIds = UniqueMatchingIdentities(references, ReferenceIdPattern);

            if (!ValidateClassInventory(root, scenarios, ref diagnostic) ||
                !ValidateReferenceCoverage(root, families, referenceIds) ||
                !ValidateReferences(references, hosts, scenarios, ref diagnostic))
            {
                return false;
            }

            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
            UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            diagnostic = "invalid_register";
            return false;
        }
    }

    private static bool TryValidateExecutionDimensions(JsonElement root, out string[] hosts)
    {
        JsonElement dimensions = root.GetProperty("executionDimensions");
        hosts = dimensions.GetProperty("runtimeHosts").EnumerateArray()
            .Select(host => host.GetProperty(JsonFields.Id).GetString() ?? string.Empty).ToArray();
        if (hosts.Length != SupportedHosts.Count || !new HashSet<string>(hosts, StringComparer.Ordinal).SetEquals(SupportedHosts))
        {
            return false;
        }

        string[] providers = dimensions.GetProperty("databaseProviders").GetProperty("values").EnumerateArray()
            .Select(provider => provider.GetString() ?? string.Empty).ToArray();
        return providers.Length > 0 &&
            providers.Distinct(StringComparer.Ordinal).Count() == providers.Length &&
            providers.All(SupportedProviders.Contains);
    }

    private static bool ValidateProviderPairs(JsonElement root, HashSet<string> scenarios)
    {
        JsonElement providerPairs = root.GetProperty("qualifiedMappings").GetProperty("providerPairs");
        var qualifiedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement pair in providerPairs.EnumerateArray())
        {
            string finding = pair.GetProperty("findingId").GetString() ?? string.Empty;
            Match match = ProviderFindingPattern.Match(finding);
            if (!match.Success ||
                !qualifiedIds.Add(finding) ||
                pair.GetProperty("mitigationId").GetString() != "PC:IVSD-M" + match.Groups["num"].Value ||
                !IsValidGeneralStatus(pair.GetProperty(JsonFields.VerificationStatus).GetString()) ||
                pair.GetProperty(JsonFields.OwningPhases).GetArrayLength() == 0 ||
                pair.GetProperty(JsonFields.Scenarios).GetArrayLength() == 0 ||
                !ValidatePhasesAndScenarios(pair, scenarios))
            {
                return false;
            }
        }

        return qualifiedIds.Count > 0;
    }

    private static bool ValidateTypedContractInventory(JsonElement root)
    {
        JsonElement typedInventory = root.GetProperty("typedContractInventory");
        JsonElement dtoPairs = typedInventory.GetProperty("dtoPairs");
        if (dtoPairs.GetArrayLength() == 0 ||
            !IsValidGeneralStatus(typedInventory.GetProperty(JsonFields.VerificationStatus).GetString()))
        {
            return false;
        }

        var contractOwners = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement pair in dtoPairs.EnumerateArray())
        {
            string createOwner = pair.GetProperty(JsonFields.CreateOwner).GetString() ?? string.Empty;
            string updateOwner = pair.GetProperty(JsonFields.UpdateOwner).GetString() ?? string.Empty;
            if (!createOwner.StartsWith(ApplicationSourcePrefix, StringComparison.Ordinal) ||
                !updateOwner.StartsWith(ApplicationSourcePrefix, StringComparison.Ordinal) ||
                !contractOwners.Add(createOwner) ||
                !contractOwners.Add(updateOwner))
            {
                return false;
            }
        }

        JsonElement nativePairs = typedInventory.GetProperty("nativePairs");
        if (nativePairs.GetArrayLength() == 0)
        {
            return false;
        }

        var nativeTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement pair in nativePairs.EnumerateArray())
        {
            string createType = pair.GetProperty("createType").GetString() ?? string.Empty;
            string updateType = pair.GetProperty("updateType").GetString() ?? string.Empty;
            if ((!createType.StartsWith("Create", StringComparison.Ordinal) &&
                 !createType.StartsWith("Add", StringComparison.Ordinal)) ||
                !updateType.StartsWith("Update", StringComparison.Ordinal) ||
                !nativeTypes.Add(createType) ||
                !nativeTypes.Add(updateType) ||
                !(pair.GetProperty(JsonFields.CreateOwner).GetString() ?? string.Empty)
                    .StartsWith(ApplicationSourcePrefix, StringComparison.Ordinal) ||
                !(pair.GetProperty(JsonFields.UpdateOwner).GetString() ?? string.Empty)
                    .StartsWith(ApplicationSourcePrefix, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateExecutionOwnerInventory(JsonElement root, HashSet<string> scenarios)
    {
        JsonElement executionOwners = root.GetProperty("executionOwnerInventory");
        var entryIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement entry in executionOwners.EnumerateArray())
        {
            string id = entry.GetProperty(JsonFields.Id).GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id) ||
                !entryIds.Add(id) ||
                entry.GetProperty("owners").GetArrayLength() == 0 ||
                string.IsNullOrWhiteSpace(entry.GetProperty("requiredEvidence").GetString()) ||
                string.IsNullOrWhiteSpace(entry.GetProperty("refreshTrigger").GetString()) ||
                !IsValidGeneralStatus(entry.GetProperty(JsonFields.VerificationStatus).GetString()) ||
                entry.GetProperty(JsonFields.OwningPhases).GetArrayLength() == 0 ||
                entry.GetProperty(JsonFields.Scenarios).GetArrayLength() == 0 ||
                !ValidatePhasesAndScenarios(entry, scenarios))
            {
                return false;
            }
        }

        return entryIds.Count > 0;
    }

    private static bool ValidateAsvsRequirements(JsonElement root, string[] hosts, ref string diagnostic)
    {
        JsonElement asvs = root.GetProperty("asvs");
        if (asvs.GetProperty("version").GetString() != "5.0.0")
        {
            return false;
        }

        var requirementIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement requirement in asvs.GetProperty("requirements").EnumerateArray())
        {
            string id = requirement.GetProperty(JsonFields.Id).GetString() ?? string.Empty;
            string status = requirement.GetProperty(JsonFields.VerificationStatus).GetString() ?? string.Empty;
            if (!id.StartsWith("v5.0.0-", StringComparison.Ordinal) ||
                !requirementIds.Add(id) ||
                requirement.GetProperty("level").GetInt32() is < 1 or > 3 ||
                string.IsNullOrWhiteSpace(requirement.GetProperty("applicability").GetString()) ||
                requirement.GetProperty(JsonFields.PrerequisiteProfiles).GetArrayLength() == 0 ||
                (status != "unverified" && !ResolvedStatuses.Contains(status)))
            {
                diagnostic = "invalid_standard_requirement";
                return false;
            }

            if (!ValidateHostProfiles(requirement.GetProperty(JsonFields.PrerequisiteProfiles), hosts))
            {
                return false;
            }
        }

        return requirementIds.Count > 0;
    }

    private static bool ValidateApiFindingMitigationPairs(JsonElement root, HashSet<string> scenarios, ref string diagnostic)
    {
        JsonElement pairs = root.GetProperty("apiFindingMitigationPairs");
        var findingIds = new HashSet<string>(StringComparer.Ordinal);
        var mitigationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement pair in pairs.EnumerateArray())
        {
            string finding = pair.GetProperty("findingId").GetString() ?? string.Empty;
            string mitigation = pair.GetProperty("mitigationId").GetString() ?? string.Empty;
            Match match = ApiFindingPattern.Match(finding);
            if (!match.Success ||
                !findingIds.Add(finding) ||
                !mitigationIds.Add(mitigation) ||
                mitigation != "IVSD-M" + match.Groups["num"].Value)
            {
                diagnostic = "invalid_finding_mapping";
                return false;
            }

            JsonElement pairScenarios = pair.GetProperty(JsonFields.Scenarios);
            if (pairScenarios.GetArrayLength() == 0 ||
                !pairScenarios.EnumerateArray().All(scenario => scenarios.Contains(scenario.GetString() ?? string.Empty)))
            {
                return false;
            }

            JsonElement tasks = pair.GetProperty("owningTasks");
            bool hasDeliveredReceipt = pair.TryGetProperty("deliveredReceipt", out JsonElement receiptElement) &&
                !string.IsNullOrWhiteSpace(receiptElement.GetString());
            if (!IsValidGeneralStatus(pair.GetProperty(JsonFields.VerificationStatus).GetString()) ||
                (tasks.GetArrayLength() == 0 && !hasDeliveredReceipt) ||
                !ValidateOwningTasks(tasks))
            {
                return false;
            }
        }

        return findingIds.Count > 0;
    }

    private static bool ValidateOwningTasks(JsonElement tasks)
    {
        var taskIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement task in tasks.EnumerateArray())
        {
            string value = task.GetString() ?? string.Empty;
            string[] parts = value.Split('.');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out int phase) ||
                phase is < 1 or > 29 ||
                !int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out int item) ||
                item < 1 ||
                !taskIds.Add(value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateClassInventory(JsonElement root, HashSet<string> scenarios, ref string diagnostic)
    {
        JsonElement classes = root.GetProperty("classes");
        var classIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement entry in classes.EnumerateArray())
        {
            string id = entry.GetProperty(JsonFields.Id).GetString() ?? string.Empty;
            if (!classIds.Add(id) ||
                !ClassIdPattern.IsMatch(id) ||
                !IsValidGeneralStatus(entry.GetProperty(JsonFields.VerificationStatus).GetString()) ||
                entry.GetProperty(JsonFields.OwningPhases).GetArrayLength() == 0 ||
                string.IsNullOrWhiteSpace(entry.GetProperty("refreshTrigger").GetString()) ||
                !ValidatePhasesAndScenarios(entry, scenarios))
            {
                diagnostic = "invalid_class_inventory";
                return false;
            }
        }

        if (classIds.Count == 0)
        {
            diagnostic = "missing_class";
            return false;
        }

        return true;
    }

    private static bool ValidateReferenceCoverage(JsonElement root, HashSet<string> families, HashSet<string> referenceIds)
    {
        var assignedReferences = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement family in root.GetProperty("families").EnumerateArray())
        {
            if (!TryCollectAssignedReferences(family.GetProperty(JsonFields.ReferenceIds), referenceIds, assignedReferences))
            {
                return false;
            }
        }

        JsonElement supplements = root.GetProperty("supplementaryMechanisms");
        _ = UniqueMatchingIdentities(supplements, SupplementaryIdPattern);
        foreach (JsonElement supplement in supplements.EnumerateArray())
        {
            if (!IsValidGeneralStatus(supplement.GetProperty(JsonFields.VerificationStatus).GetString()) ||
                !TryCollectAssignedReferences(supplement.GetProperty(JsonFields.ReferenceIds), referenceIds, assignedReferences))
            {
                return false;
            }
        }

        return families.Count > 0 && assignedReferences.SetEquals(referenceIds);
    }

    private static bool TryCollectAssignedReferences(
        JsonElement referenceIdElements,
        HashSet<string> validReferenceIds,
        HashSet<string> assignedReferences)
    {
        if (referenceIdElements.GetArrayLength() == 0)
        {
            return false;
        }

        foreach (JsonElement reference in referenceIdElements.EnumerateArray())
        {
            string id = reference.GetString() ?? string.Empty;
            if (!validReferenceIds.Contains(id))
            {
                return false;
            }

            assignedReferences.Add(id);
        }

        return true;
    }

    private static bool ValidateReferences(
        JsonElement references,
        string[] hosts,
        HashSet<string> scenarios,
        ref string diagnostic)
    {
        foreach (JsonElement reference in references.EnumerateArray())
        {
            if (string.IsNullOrWhiteSpace(reference.GetProperty("functionalName").GetString()) ||
                !Dispositions.Contains(reference.GetProperty("initialDisposition").GetString() ?? string.Empty))
            {
                diagnostic = "invalid_reference";
                return false;
            }

            if (!ValidateReferenceScope(reference, hosts, scenarios))
            {
                return false;
            }

            if (!ValidateControlLifecycle(reference))
            {
                diagnostic = "invalid_control_lifecycle";
                return false;
            }
        }

        return true;
    }

    private static bool ValidateReferenceScope(JsonElement reference, string[] hosts, HashSet<string> scenarios)
    {
        JsonElement prerequisiteProfiles = reference.GetProperty(JsonFields.PrerequisiteProfiles);
        if (prerequisiteProfiles.GetArrayLength() == 0 ||
            string.IsNullOrWhiteSpace(reference.GetProperty("profileQualification").GetString()) ||
            !ValidateHostProfiles(prerequisiteProfiles, hosts))
        {
            return false;
        }

        JsonElement phases = reference.GetProperty(JsonFields.OwningPhases);
        bool delivered = reference.GetProperty("deliveredIssuance").GetBoolean();
        if ((phases.GetArrayLength() == 0 && !delivered) || !ValidateUniquePhases(phases))
        {
            return false;
        }

        JsonElement ownedScenarios = reference.GetProperty(JsonFields.Scenarios);
        return ownedScenarios.GetArrayLength() > 0 &&
            ownedScenarios.EnumerateArray().All(scenario => scenarios.Contains(scenario.GetString() ?? string.Empty));
    }

    private static bool ValidateHostProfiles(JsonElement prerequisiteProfiles, string[] hosts)
    {
        var profileIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement profile in prerequisiteProfiles.EnumerateArray())
        {
            string host = profile.GetString() ?? string.Empty;
            if (!hosts.Contains(host, StringComparer.Ordinal) || !profileIds.Add(host))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ValidateUniquePhases(JsonElement phases)
    {
        var owners = new HashSet<int>();
        foreach (JsonElement phase in phases.EnumerateArray())
        {
            int value = phase.GetInt32();
            if (value is < 1 or > 29 || !owners.Add(value))
            {
                return false;
            }
        }

        return true;
    }

    internal static IReadOnlyDictionary<string, ExecutionRequirement> ReadExecutionRequirements(string path)
    {
        if (!TryValidate(path, out _))
        {
            throw new InvalidDataException("invalid_register");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path), DocumentOptions);
        return ReadExecutionRequirements(document.RootElement);
    }

    private static bool ValidateControlLifecycle(JsonElement reference)
    {
        string status = reference.GetProperty(JsonFields.VerificationStatus).GetString() ?? string.Empty;
        JsonElement finalDisposition = reference.GetProperty("finalDisposition");
        JsonElement evidence = reference.GetProperty("executionEvidence");
        JsonElement artifactIdentity = reference.GetProperty("codeArtifactIdentity");

        if (evidence.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (string.Equals(status, PendingStatus, StringComparison.Ordinal))
        {
            return finalDisposition.ValueKind == JsonValueKind.Null &&
                evidence.GetArrayLength() == 0 &&
                artifactIdentity.ValueKind == JsonValueKind.Null;
        }

        if (!ResolvedStatuses.Contains(status))
        {
            return false;
        }

        if (finalDisposition.ValueKind != JsonValueKind.String ||
            !Dispositions.Contains(finalDisposition.GetString() ?? string.Empty))
        {
            return false;
        }

        if (artifactIdentity.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(artifactIdentity.GetString()) ||
            evidence.GetArrayLength() == 0)
        {
            return false;
        }

        return evidence.EnumerateArray().All(IsValidEvidenceEntry);
    }

    private static bool IsValidEvidenceEntry(JsonElement entry) =>
        entry.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(entry.GetString()),
            JsonValueKind.Object => entry.EnumerateObject().Any(),
            _ => false,
        };

    private static bool IsValidGeneralStatus(string? status) =>
        string.Equals(status, PendingStatus, StringComparison.Ordinal) ||
        (status is not null && ResolvedStatuses.Contains(status));

    private static bool ValidatePhasesAndScenarios(JsonElement element, HashSet<string> scenarios) =>
        !element.GetProperty(JsonFields.OwningPhases).EnumerateArray()
            .Any(phase => phase.GetInt32() is < 1 or > 29) &&
        element.GetProperty(JsonFields.Scenarios).EnumerateArray()
            .All(scenario => scenarios.Contains(scenario.GetString() ?? string.Empty));

    private static HashSet<string> UniqueMatchingIdentities(JsonElement entries, Regex pattern)
    {
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            string id = entry.GetProperty(JsonFields.Id).GetString() ?? string.Empty;
            if (!pattern.IsMatch(id) || !actual.Add(id))
            {
                throw new InvalidDataException();
            }
        }

        if (actual.Count == 0)
        {
            throw new InvalidDataException();
        }

        return actual;
    }

    private static IReadOnlyDictionary<string, ExecutionRequirement> ReadExecutionRequirements(JsonElement register)
    {
        var requirements = new Dictionary<string, ExecutionRequirement>(StringComparer.Ordinal);
        foreach (JsonElement requirement in register.GetProperty("executionRequirements").EnumerateArray())
        {
            string lane = requirement.GetProperty("laneId").GetString() ?? string.Empty;
            string project = requirement.GetProperty("project").GetString() ?? string.Empty;
            string profile = requirement.GetProperty("hostProfile").GetString() ?? string.Empty;
            const string apiProject = "tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj";
            const string ownedProject = "tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj";
            (string expectedProject, string expectedProfile, string selector) = lane switch
            {
                "security-integration" => (apiProject, "dedicated-api-test-host", "/*/*/*/*[Category=Security]"),
                "cerbos-policy-contract" => (apiProject, "dedicated-api-test-host", "/*/*/*/*[Category=PolicyContract]"),
                "owned-runtime" => (ownedProject, "owned-apphost-assurance", "/*/*/*/*[Category=Distributed]"),
                "owned-browser-journeys" => (ownedProject, "owned-browser-assurance", "/*/*/BrowserJourneyTests/*"),
                "owned-browser-security" => (ownedProject, "owned-browser-assurance", "/*/*/BrowserSecurityVectorTests/*"),
                "owned-load-control" => (ownedProject, "owned-apphost-load-assurance", "/*/*/*/*[Category=LoadControl]"),
                "owned-load-correctness" => (ownedProject, "owned-apphost-load-assurance", "/*/*/*/*[Category=Load]"),
                _ => throw new InvalidDataException(),
            };
            if (project != expectedProject || profile != expectedProfile)
            {
                throw new InvalidDataException();
            }

            var tests = new HashSet<RequiredTest>();
            var lifecycles = new Dictionary<RequiredTest, RuntimeLifecycle>();
            var loadPurposes = new Dictionary<RequiredTest, LoadPurpose>();
            foreach (JsonElement test in requirement.GetProperty("requiredTests").EnumerateArray())
            {
                string[] fields = lane switch
                {
                    "owned-runtime" => [JsonFields.ClassName, JsonFields.MethodName, JsonFields.TestName, "lifecycle"],
                    "owned-load-correctness" => [JsonFields.ClassName, JsonFields.MethodName, JsonFields.TestName, "loadPurpose"],
                    _ => [JsonFields.ClassName, JsonFields.MethodName, JsonFields.TestName],
                };
                if (!ClosedFields(test, fields))
                {
                    throw new InvalidDataException();
                }

                string className = test.GetProperty(JsonFields.ClassName).GetString() ?? string.Empty;
                string methodName = test.GetProperty(JsonFields.MethodName).GetString() ?? string.Empty;
                string testName = test.GetProperty(JsonFields.TestName).GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(className) || string.IsNullOrWhiteSpace(methodName) ||
                    string.IsNullOrWhiteSpace(testName) || !tests.Add(new RequiredTest(className, methodName, testName)))
                {
                    throw new InvalidDataException();
                }

                if (lane == "owned-runtime")
                {
                    lifecycles.Add(new RequiredTest(className, methodName, testName),
                        ReadRuntimeLifecycle(test.GetProperty("lifecycle")));
                }

                if (lane == "owned-load-correctness")
                {
                    loadPurposes.Add(new RequiredTest(className, methodName, testName), test.GetProperty("loadPurpose").GetString() switch
                    {
                        "foundation" => LoadPurpose.Foundation,
                        "programme" => LoadPurpose.Programme,
                        _ => throw new InvalidDataException(),
                    });
                }
            }

            if (tests.Count is < 1 or > 1024 || !requirements.TryAdd(lane, new ExecutionRequirement(project, profile, tests)
            {
                Environment = project == apiProject ? "Testing" : "Assurance",
                Selector = selector,
                ReportFileName = lane + "-tests.trx",
                Lifecycles = lifecycles,
                LoadPurposes = loadPurposes,
            }))
            {
                throw new InvalidDataException();
            }
        }

        if (!requirements.ContainsKey("security-integration") || !requirements.ContainsKey("cerbos-policy-contract") ||
            requirements.Count != 7)
        {
            throw new InvalidDataException();
        }

        return requirements;
    }

    internal sealed record ExecutionRequirement(string Project, string HostProfile, IReadOnlySet<RequiredTest> Tests)
    {
        internal string Environment { get; init; } = "Testing";
        internal string Selector { get; init; } = string.Empty;
        internal string ReportFileName { get; init; } = string.Empty;
        internal IReadOnlyDictionary<RequiredTest, RuntimeLifecycle> Lifecycles { get; init; } =
            new Dictionary<RequiredTest, RuntimeLifecycle>();
        internal IReadOnlyDictionary<RequiredTest, LoadPurpose> LoadPurposes { get; init; } =
            new Dictionary<RequiredTest, LoadPurpose>();
    }

    internal readonly record struct RequiredTest(string ClassName, string MethodName, string TestName);
    internal enum LoadPurpose { Foundation, Programme }

    internal enum RuntimeStage { None, Build, Startup, Hosted }
    internal enum RuntimeCompletion { Success, Cancelled, BuilderFault, ExecutionFault, ControlledFault, Refused, StopFault }
    internal enum RuntimeRootDisposition { NotApplicable, CaseCleanup, OwnedScopeCleanup }
    internal sealed record RuntimeLifecycle(RuntimeStage Stage, RuntimeCompletion Completion, bool DirectContainers,
        string? Resource, IReadOnlySet<string> Signals, RuntimeRootDisposition RootDisposition);

    private static RuntimeLifecycle ReadRuntimeLifecycle(JsonElement value)
    {
        if (!ClosedFields(value, ["stage", "completion", "directContainers", "resource", "signals", "rootDisposition"]))
        {
            throw new InvalidDataException();
        }

        RuntimeStage stage = value.GetProperty("stage").GetString() switch
        {
            "none" => RuntimeStage.None,
            "build" => RuntimeStage.Build,
            "startup" => RuntimeStage.Startup,
            "hosted" => RuntimeStage.Hosted,
            _ => throw new InvalidDataException(),
        };
        RuntimeCompletion completion = value.GetProperty("completion").GetString() switch
        {
            "success" => RuntimeCompletion.Success,
            "cancelled" => RuntimeCompletion.Cancelled,
            "builder-fault" => RuntimeCompletion.BuilderFault,
            "execution-fault" => RuntimeCompletion.ExecutionFault,
            "controlled-fault" => RuntimeCompletion.ControlledFault,
            "refused" => RuntimeCompletion.Refused,
            "stop-fault" => RuntimeCompletion.StopFault,
            _ => throw new InvalidDataException(),
        };
        RuntimeRootDisposition root = value.GetProperty("rootDisposition").GetString() switch
        {
            "not-applicable" => RuntimeRootDisposition.NotApplicable,
            "case-cleanup" => RuntimeRootDisposition.CaseCleanup,
            "owned-scope-cleanup" => RuntimeRootDisposition.OwnedScopeCleanup,
            _ => throw new InvalidDataException(),
        };
        bool direct = value.GetProperty("directContainers").GetBoolean();
        JsonElement resourceValue = value.GetProperty("resource");
        string? resource = resourceValue.ValueKind == JsonValueKind.Null ? null : resourceValue.GetString();
        JsonElement signalValues = value.GetProperty("signals");
        if (signalValues.ValueKind != JsonValueKind.Array || signalValues.GetArrayLength() > 4)
        {
            throw new InvalidDataException();
        }

        var signals = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement signal in signalValues.EnumerateArray())
        {
            if (signal.ValueKind != JsonValueKind.String || signal.GetString() is not { Length: > 0 and <= 128 } text
                || !signals.Add(text))
            {
                throw new InvalidDataException();
            }
        }

        string[][] alternatives = (stage, completion, direct, resource, root) switch
        {
            (RuntimeStage.None, RuntimeCompletion.Success, false, null, RuntimeRootDisposition.NotApplicable) => [[]],
            (RuntimeStage.None, RuntimeCompletion.Cancelled, true, null, RuntimeRootDisposition.NotApplicable) =>
                [["docker-wait-cancelled"]],
            (RuntimeStage.Build, RuntimeCompletion.Success, false, null, RuntimeRootDisposition.CaseCleanup) => [[]],
            (RuntimeStage.Build, RuntimeCompletion.BuilderFault, false, null, RuntimeRootDisposition.CaseCleanup) =>
                [["assurance_builder_fault"]],
            (RuntimeStage.Build, RuntimeCompletion.ControlledFault, true, null, RuntimeRootDisposition.CaseCleanup) =>
                [["test_resource_foreign_container", "root-retained"]],
            (RuntimeStage.Build, RuntimeCompletion.ExecutionFault, false, null, RuntimeRootDisposition.OwnedScopeCleanup) =>
                [["execution-unobserved", "root-retained"]],
            (RuntimeStage.Startup, RuntimeCompletion.Cancelled, false, "postgres", RuntimeRootDisposition.CaseCleanup) =>
                [["caller-cancelled", "postgres-healthy"], ["caller-cancelled", "postgres-starting"]],
            (RuntimeStage.Startup, RuntimeCompletion.ExecutionFault, false, "postgres", RuntimeRootDisposition.OwnedScopeCleanup) =>
                [["test_resource_start_failed:postgres", "test_application_execution_already_observed", "startup-held", "root-retained"]],
            (RuntimeStage.Startup, RuntimeCompletion.ControlledFault, false, "postgres", RuntimeRootDisposition.CaseCleanup) =>
                [["test_resource_start_failed:postgres", "assurance_disposal_fault", "root-retained"]],
            (RuntimeStage.Startup, RuntimeCompletion.Refused, false, "explore-blazor-peer", RuntimeRootDisposition.CaseCleanup) =>
                [["data_protection_admission_rejected", "non-cancellation-start-failure", "nonzero-resource-exit"]],
            (RuntimeStage.Hosted, RuntimeCompletion.Success, false, null, RuntimeRootDisposition.CaseCleanup) => [[]],
            (RuntimeStage.Hosted, RuntimeCompletion.StopFault, false, null, RuntimeRootDisposition.CaseCleanup) =>
                [["assurance_stop_fault", "root-retained"]],
            _ => throw new InvalidDataException(),
        };
        if (!alternatives.Any(signals.SetEquals))
        {
            throw new InvalidDataException();
        }

        return new(stage, completion, direct, resource, signals, root);
    }

    private static bool ClosedFields(JsonElement value, string[] names) => value.ValueKind == JsonValueKind.Object
        && value.EnumerateObject().Count() == names.Length
        && value.EnumerateObject().All(property => names.Contains(property.Name, StringComparer.Ordinal));

    private static bool UniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || !UniqueProperties(property.Value))
                {
                    return false;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (!UniqueProperties(item))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
