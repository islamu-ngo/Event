using System.Text.Json;

namespace ISLAMU.ReleaseEngineering;

public static class SecurityControlRegister
{
    private const int MaximumBytes = 1_048_576;
    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 32 };
    private static readonly Lazy<IReadOnlyDictionary<string, ExecutionRequirement>> EmbeddedRequirements = new(() =>
    {
        using Stream stream = typeof(SecurityControlRegister).Assembly.GetManifestResourceStream(
            "ISLAMU.ReleaseEngineering.SecurityControlRegister.json") ?? throw new InvalidDataException();
        using JsonDocument document = JsonDocument.Parse(stream, DocumentOptions);
        if (!UniqueProperties(document.RootElement) || document.RootElement.GetProperty("schemaVersion").GetInt32() != 2)
            throw new InvalidDataException();
        return ReadExecutionRequirements(document.RootElement);
    });
    internal static IReadOnlyDictionary<string, ExecutionRequirement> ExecutionRequirements => EmbeddedRequirements.Value;
    private static readonly HashSet<string> PinnedRequirements = new(StringComparer.Ordinal)
    {
        "v5.0.0-6.1.1",
        "v5.0.0-6.3.1",
        "v5.0.0-6.1.3",
        "v5.0.0-6.3.4",
        "v5.0.0-6.8.1",
        "v5.0.0-6.8.2",
        "v5.0.0-6.8.4",
        "v5.0.0-6.3.8",
        "v5.0.0-9.1.1",
        "v5.0.0-9.1.2",
        "v5.0.0-9.1.3",
        "v5.0.0-9.2.1",
        "v5.0.0-9.2.2",
        "v5.0.0-9.2.3",
        "v5.0.0-9.2.4",
        "v5.0.0-8.1.1",
        "v5.0.0-8.2.1",
        "v5.0.0-8.2.2",
        "v5.0.0-8.3.1",
        "v5.0.0-8.1.2",
        "v5.0.0-8.2.3",
        "v5.0.0-8.4.1",
        "v5.0.0-8.3.2",
        "v5.0.0-8.3.3",
        "v5.0.0-1.3.6",
        "v5.0.0-1.5.3",
        "v5.0.0-13.1.1",
        "v5.0.0-13.2.4",
        "v5.0.0-13.2.5",
        "v5.0.0-13.3.1",
        "v5.0.0-13.3.2",
        "v5.0.0-13.1.4",
        "v5.0.0-13.3.4",
        "v5.0.0-13.3.3",
        "v5.0.0-13.1.2",
        "v5.0.0-13.1.3",
        "v5.0.0-13.2.6",
        "v5.0.0-14.2.1",
        "v5.0.0-14.1.1",
        "v5.0.0-14.1.2",
        "v5.0.0-14.2.2",
        "v5.0.0-14.2.3",
        "v5.0.0-14.2.4",
        "v5.0.0-16.1.1",
        "v5.0.0-16.2.1",
        "v5.0.0-16.2.2",
        "v5.0.0-16.2.4",
        "v5.0.0-16.2.5",
        "v5.0.0-16.3.1",
        "v5.0.0-16.3.2",
        "v5.0.0-16.3.3",
        "v5.0.0-16.3.4",
        "v5.0.0-16.4.1",
        "v5.0.0-16.4.2",
        "v5.0.0-16.4.3",
        "v5.0.0-16.5.1",
        "v5.0.0-16.5.2",
        "v5.0.0-16.5.3",
        "v5.0.0-2.2.1",
        "v5.0.0-2.2.2",
        "v5.0.0-2.1.3",
        "v5.0.0-2.3.2",
        "v5.0.0-2.4.1",
        "v5.0.0-5.2.1",
        "v5.0.0-5.2.3",
        "v5.0.0-5.2.4",
        "v5.0.0-5.2.6"
    };
    private static readonly HashSet<string> Dispositions = new(StringComparer.Ordinal)
    {
        "Protected", "Vulnerable", "Improvable", "Unverified", "Not applicable"
    };

    public static int Run(string[] args, TextWriter output)
    {
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

        output.WriteLine("security_register_verified: inventory-only; runtime admission remains separate");
        return Program.Success;
    }

    public static bool TryValidate(string path, out string diagnostic)
    {
        diagnostic = "invalid_register";
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is < 1 or > MaximumBytes) return false;
            byte[] bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            using JsonDocument document = JsonDocument.Parse(bytes, DocumentOptions);
            JsonElement root = document.RootElement;
            if (!UniqueProperties(root) || root.GetProperty("schemaVersion").GetInt32() != 2 ||
                root.GetProperty("workstream").GetString() != "event-api-security-remediation" ||
                root.GetProperty("assurance").GetString() != "source-inventory-not-runtime-certification")
            {
                return false;
            }

            HashSet<string> scenarios = Identities(root.GetProperty("scenarios"), "S", 50, 2);
            _ = ReadExecutionRequirements(root);
            HashSet<string> families = Identities(root.GetProperty("families"), "FAMILY-", 53, 3);
            JsonElement dimensions = root.GetProperty("executionDimensions");
            string[] hosts = dimensions.GetProperty("runtimeHosts").EnumerateArray()
                .Select(host => host.GetProperty("id").GetString() ?? string.Empty).ToArray();
            if (hosts.Length != 3 || !new HashSet<string>(hosts, StringComparer.Ordinal).SetEquals(
                    ["split-api", "split-bff", "combined-standalone"])) return false;
            JsonElement engines = dimensions.GetProperty("databaseProviders").GetProperty("values");
            string[] providers = engines.EnumerateArray()
                .Select(provider => provider.GetString() ?? string.Empty).ToArray();
            if (providers.Length != 5 || !new HashSet<string>(providers, StringComparer.Ordinal).SetEquals(
                    ["PostgreSql", "Sqlite", "SqlServer", "MySql", "MariaDb"])) return false;
            JsonElement providerPairs = root.GetProperty("qualifiedMappings").GetProperty("providerPairs");
            var qualifiedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement pair in providerPairs.EnumerateArray())
            {
                string finding = pair.GetProperty("findingId").GetString() ?? string.Empty;
                if (!qualifiedIds.Add(finding) || !Enumerable.Range(1, 6).Any(number =>
                        finding == "PC:IVSD-F" + number.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) &&
                        pair.GetProperty("mitigationId").GetString() == "PC:IVSD-M" +
                        number.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)) ||
                    pair.GetProperty("verificationStatus").GetString() != "pending" ||
                    pair.GetProperty("owningPhases").GetArrayLength() == 0 ||
                    pair.GetProperty("scenarios").GetArrayLength() == 0) return false;
                foreach (JsonElement phase in pair.GetProperty("owningPhases").EnumerateArray())
                {
                    if (phase.GetInt32() is < 1 or > 29) return false;
                }
                foreach (JsonElement scenario in pair.GetProperty("scenarios").EnumerateArray())
                {
                    if (!scenarios.Contains(scenario.GetString() ?? string.Empty)) return false;
                }
            }
            if (qualifiedIds.Count != 6) return false;
            JsonElement typedInventory = root.GetProperty("typedContractInventory");
            JsonElement dtoPairs = typedInventory.GetProperty("dtoPairs");
            if (dtoPairs.GetArrayLength() != 35 ||
                typedInventory.GetProperty("verificationStatus").GetString() != "pending") return false;
            var contractOwners = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement pair in dtoPairs.EnumerateArray())
            {
                string createOwner = pair.GetProperty("createOwner").GetString() ?? string.Empty;
                string updateOwner = pair.GetProperty("updateOwner").GetString() ?? string.Empty;
                if (!createOwner.StartsWith("src/Explore.Application/", StringComparison.Ordinal) ||
                    !updateOwner.StartsWith("src/Explore.Application/", StringComparison.Ordinal) ||
                    !contractOwners.Add(createOwner) || !contractOwners.Add(updateOwner)) return false;
            }
            JsonElement nativePairs = typedInventory.GetProperty("nativePairs");
            if (nativePairs.GetArrayLength() != 16) return false;
            var nativeTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement pair in nativePairs.EnumerateArray())
            {
                string createType = pair.GetProperty("createType").GetString() ?? string.Empty;
                string updateType = pair.GetProperty("updateType").GetString() ?? string.Empty;
                if ((!createType.StartsWith("Create", StringComparison.Ordinal) &&
                     !createType.StartsWith("Add", StringComparison.Ordinal)) ||
                    !updateType.StartsWith("Update", StringComparison.Ordinal) ||
                    !nativeTypes.Add(createType) || !nativeTypes.Add(updateType) ||
                    !(pair.GetProperty("createOwner").GetString() ?? string.Empty)
                        .StartsWith("src/Explore.Application/", StringComparison.Ordinal) ||
                    !(pair.GetProperty("updateOwner").GetString() ?? string.Empty)
                        .StartsWith("src/Explore.Application/", StringComparison.Ordinal)) return false;
            }
            JsonElement executionOwners = root.GetProperty("executionOwnerInventory");
            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in executionOwners.EnumerateArray())
            {
                string id = entry.GetProperty("id").GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id) || !entryIds.Add(id) ||
                    entry.GetProperty("owners").GetArrayLength() == 0 ||
                    string.IsNullOrWhiteSpace(entry.GetProperty("requiredEvidence").GetString()) ||
                    string.IsNullOrWhiteSpace(entry.GetProperty("refreshTrigger").GetString()) ||
                    entry.GetProperty("verificationStatus").GetString() != "pending" ||
                    entry.GetProperty("owningPhases").GetArrayLength() == 0 ||
                    entry.GetProperty("scenarios").GetArrayLength() == 0) return false;
                foreach (JsonElement phase in entry.GetProperty("owningPhases").EnumerateArray())
                {
                    if (phase.GetInt32() is < 1 or > 29) return false;
                }
                foreach (JsonElement scenario in entry.GetProperty("scenarios").EnumerateArray())
                {
                    if (!scenarios.Contains(scenario.GetString() ?? string.Empty)) return false;
                }
            }
            if (!entryIds.SetEquals(["nullable-definition-options", "nullable-detail-consumers",
                    "provider-query-entrypoints", "process-entrypoints", "message-and-tool-entrypoints",
                    "non-testing-worker-and-middleware", "provider-uniqueness-assumptions"])) return false;
            JsonElement asvs = root.GetProperty("asvs");
            if (asvs.GetProperty("version").GetString() != "5.0.0") return false;
            var requirementIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement requirement in asvs.GetProperty("requirements").EnumerateArray())
            {
                string id = requirement.GetProperty("id").GetString() ?? string.Empty;
                if (!PinnedRequirements.Contains(id) || !requirementIds.Add(id) ||
                    requirement.GetProperty("level").GetInt32() is < 1 or > 3 ||
                    requirement.GetProperty("applicability").GetString() != "applicable-source-scope" ||
                    requirement.GetProperty("prerequisiteProfiles").GetArrayLength() == 0 ||
                    requirement.GetProperty("verificationStatus").GetString() != "unverified")
                {
                    diagnostic = "invalid_standard_requirement";
                    return false;
                }
                var prerequisiteHosts = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement profile in requirement.GetProperty("prerequisiteProfiles").EnumerateArray())
                {
                    string host = profile.GetString() ?? string.Empty;
                    if (!hosts.Contains(host, StringComparer.Ordinal) || !prerequisiteHosts.Add(host)) return false;
                }
            }

            if (!PinnedRequirements.SetEquals(requirementIds)) return false;
            JsonElement pairs = root.GetProperty("apiFindingMitigationPairs");
            var findingIds = new HashSet<string>(StringComparer.Ordinal);
            var mitigationIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement pair in pairs.EnumerateArray())
            {
                string finding = pair.GetProperty("findingId").GetString() ?? string.Empty;
                string mitigation = pair.GetProperty("mitigationId").GetString() ?? string.Empty;
                if (!findingIds.Add(finding) || !mitigationIds.Add(mitigation) ||
                    !Enumerable.Range(1, 31).Any(number =>
                        finding == "IVSD-F" + number.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) &&
                        mitigation == "IVSD-M" + number.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)))
                {
                    diagnostic = "invalid_finding_mapping";
                    return false;
                }

                foreach (JsonElement scenario in pair.GetProperty("scenarios").EnumerateArray())
                {
                    if (!scenarios.Contains(scenario.GetString() ?? string.Empty)) return false;
                }

                JsonElement tasks = pair.GetProperty("owningTasks");
                if (pair.GetProperty("verificationStatus").GetString() != "pending" ||
                    pair.GetProperty("scenarios").GetArrayLength() == 0 ||
                    (tasks.GetArrayLength() == 0 &&
                     (finding != "IVSD-F016" || pair.GetProperty("deliveredReceipt").GetString() !=
                      "https://github.com/islamu-ngo/Event/pull/71"))) return false;
                var taskIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement task in tasks.EnumerateArray())
                {
                    string value = task.GetString() ?? string.Empty;
                    string[] parts = value.Split('.');
                    if (parts.Length != 2 || !int.TryParse(parts[0], out int phase) ||
                        phase is < 1 or > 29 || !int.TryParse(parts[1], out int item) ||
                        item < 1 || !taskIds.Add(value)) return false;
                }
            }

            if (findingIds.Count != 31) return false;
            JsonElement references = root.GetProperty("references");
            HashSet<string> referenceIds = Identities(references, "REF-", 209, 3);
            JsonElement classes = root.GetProperty("classes");
            var classIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in classes.EnumerateArray())
            {
                string id = entry.GetProperty("id").GetString() ?? string.Empty;
                if (!classIds.Add(id) || !ValidClassId(id) ||
                    entry.GetProperty("verificationStatus").GetString() != "pending" ||
                    entry.GetProperty("owningPhases").GetArrayLength() == 0 ||
                    string.IsNullOrWhiteSpace(entry.GetProperty("refreshTrigger").GetString()))
                {
                    diagnostic = "invalid_class_inventory";
                    return false;
                }
                foreach (JsonElement phase in entry.GetProperty("owningPhases").EnumerateArray())
                {
                    if (phase.GetInt32() is < 1 or > 29) return false;
                }
                foreach (JsonElement scenario in entry.GetProperty("scenarios").EnumerateArray())
                {
                    if (!scenarios.Contains(scenario.GetString() ?? string.Empty)) return false;
                }
            }

            if (classIds.Count != 22)
            {
                diagnostic = "missing_class";
                return false;
            }

            var assignedReferences = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement family in root.GetProperty("families").EnumerateArray())
            {
                if (family.GetProperty("referenceIds").GetArrayLength() == 0) return false;
                foreach (JsonElement reference in family.GetProperty("referenceIds").EnumerateArray())
                {
                    string id = reference.GetString() ?? string.Empty;
                    if (!referenceIds.Contains(id)) return false;
                    assignedReferences.Add(id);
                }
            }

            JsonElement supplements = root.GetProperty("supplementaryMechanisms");
            Identities(supplements, "SUPPLEMENTARY-", 4, 3);
            foreach (JsonElement supplement in supplements.EnumerateArray())
            {
                if (supplement.GetProperty("referenceIds").GetArrayLength() == 0 ||
                    supplement.GetProperty("verificationStatus").GetString() != "pending") return false;
                foreach (JsonElement reference in supplement.GetProperty("referenceIds").EnumerateArray())
                {
                    string id = reference.GetString() ?? string.Empty;
                    if (!referenceIds.Contains(id)) return false;
                    assignedReferences.Add(id);
                }
            }

            if (families.Count != 53 || !assignedReferences.SetEquals(referenceIds)) return false;
            foreach (JsonElement reference in references.EnumerateArray())
            {
                if (string.IsNullOrWhiteSpace(reference.GetProperty("functionalName").GetString()) ||
                    !Dispositions.Contains(reference.GetProperty("initialDisposition").GetString() ?? string.Empty))
                {
                    diagnostic = "invalid_reference";
                    return false;
                }
                JsonElement prerequisiteProfiles = reference.GetProperty("prerequisiteProfiles");
                if (prerequisiteProfiles.GetArrayLength() == 0 ||
                    string.IsNullOrWhiteSpace(reference.GetProperty("profileQualification").GetString())) return false;
                var profileIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement profile in prerequisiteProfiles.EnumerateArray())
                {
                    string host = profile.GetString() ?? string.Empty;
                    if (!hosts.Contains(host, StringComparer.Ordinal) || !profileIds.Add(host)) return false;
                }

                JsonElement phases = reference.GetProperty("owningPhases");
                bool delivered = reference.GetProperty("deliveredIssuance").GetBoolean();
                if (phases.GetArrayLength() == 0 && !delivered) return false;
                var owners = new HashSet<int>();
                foreach (JsonElement phase in phases.EnumerateArray())
                {
                    int value = phase.GetInt32();
                    if (value is < 1 or > 29 || !owners.Add(value)) return false;
                }

                JsonElement ownedScenarios = reference.GetProperty("scenarios");
                if (ownedScenarios.GetArrayLength() == 0) return false;
                foreach (JsonElement scenario in ownedScenarios.EnumerateArray())
                {
                    if (!scenarios.Contains(scenario.GetString() ?? string.Empty)) return false;
                }

                if (reference.GetProperty("finalDisposition").ValueKind != JsonValueKind.Null ||
                    reference.GetProperty("verificationStatus").GetString() != "pending" ||
                    reference.GetProperty("executionEvidence").GetArrayLength() != 0 ||
                    reference.GetProperty("codeArtifactIdentity").ValueKind != JsonValueKind.Null)
                {
                    diagnostic = "source_inventory_cannot_certify_runtime";
                    return false;
                }
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

    private static IReadOnlyDictionary<string, ExecutionRequirement> ReadExecutionRequirements(JsonElement register)
    {
        var requirements = new Dictionary<string, ExecutionRequirement>(StringComparer.Ordinal);
        foreach (JsonElement requirement in register.GetProperty("executionRequirements").EnumerateArray())
        {
            string lane = requirement.GetProperty("laneId").GetString() ?? string.Empty;
            string project = requirement.GetProperty("project").GetString() ?? string.Empty;
            string profile = requirement.GetProperty("hostProfile").GetString() ?? string.Empty;
            if (lane is not ("security-integration" or "cerbos-policy-contract") ||
                project != "tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj" ||
                profile != "dedicated-api-test-host") throw new InvalidDataException();

            var tests = new HashSet<RequiredTest>();
            foreach (JsonElement test in requirement.GetProperty("requiredTests").EnumerateArray())
            {
                string className = test.GetProperty("className").GetString() ?? string.Empty;
                string methodName = test.GetProperty("methodName").GetString() ?? string.Empty;
                string testName = test.GetProperty("testName").GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(className) || string.IsNullOrWhiteSpace(methodName) ||
                    string.IsNullOrWhiteSpace(testName) || !tests.Add(new RequiredTest(className, methodName, testName)))
                    throw new InvalidDataException();
            }

            if (tests.Count is < 1 or > 1024 || !requirements.TryAdd(lane, new ExecutionRequirement(project, profile, tests)))
                throw new InvalidDataException();
        }

        if (requirements.Count != 2) throw new InvalidDataException();
        return requirements;
    }

    internal sealed record ExecutionRequirement(string Project, string HostProfile, IReadOnlySet<RequiredTest> Tests);
    internal readonly record struct RequiredTest(string ClassName, string MethodName, string TestName);

    private static HashSet<string> Identities(JsonElement entries, string prefix, int count, int digits)
    {
        var expected = Enumerable.Range(1, count).Select(index => prefix +
            index.ToString("D" + digits, System.Globalization.CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement entry in entries.EnumerateArray())
        {
            string id = entry.GetProperty("id").GetString() ?? string.Empty;
            if (!expected.Contains(id) || !actual.Add(id)) throw new InvalidDataException();
        }

        if (!expected.SetEquals(actual)) throw new InvalidDataException();
        return actual;
    }

    private static bool ValidClassId(string id) =>
        Enumerable.Range(1, 10).Any(number => id == "BUG-" +
            number.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)) ||
        Enumerable.Range(1, 12).Any(number => id == "VULN-" +
            number.ToString("D2", System.Globalization.CultureInfo.InvariantCulture));

    private static bool UniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name) || !UniqueProperties(property.Value)) return false;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (!UniqueProperties(item)) return false;
            }
        }

        return true;
    }
}
