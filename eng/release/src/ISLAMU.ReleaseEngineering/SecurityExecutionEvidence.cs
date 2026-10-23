using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace ISLAMU.ReleaseEngineering;

public static partial class SecurityExecutionEvidence
{
    public const string PacketFileName = "security-execution.v1.json";
    private const int MaximumPacketBytes = 262_144;
    private const int MaximumReportBytes = 16_777_216;
    private const string ProjectPath = "tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj";
    private static readonly XNamespace TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    private static readonly JsonDocumentOptions DocumentOptions = new() { MaxDepth = 16 };

    public static bool TryValidate(
        string directory,
        string candidateOid,
        out string? packetSha256,
        out string diagnostic)
    {
        packetSha256 = null;
        diagnostic = "invalid_packet";
        string packetPath = Path.Combine(directory, PacketFileName);
        if (!File.Exists(packetPath))
        {
            diagnostic = "missing";
            return false;
        }

        try
        {
            byte[] packetBytes = ReadBoundedFile(ResolveArtifact(directory, PacketFileName), MaximumPacketBytes);
            using JsonDocument document = JsonDocument.Parse(packetBytes, DocumentOptions);
            JsonElement packet = document.RootElement;
            if (!UniqueProperties(packet))
            {
                diagnostic = "duplicate_json_properties";
                return false;
            }

            if (Text(packet, "schemaVersion") != "security-execution.v1" ||
                Text(packet, "workflowPath") != ".github/workflows/security-tests.yml" ||
                Text(packet, "selection") != "executed")
            {
                diagnostic = "not_execution";
                return false;
            }

            if ((candidateOid.Length != 40 && candidateOid.Length != 64) ||
                !LowerHex(candidateOid) || Text(packet, "candidateOid") != candidateOid)
            {
                diagnostic = "wrong_candidate";
                return false;
            }

            string runId = Text(packet, "runId");
            int runAttempt = packet.GetProperty("runAttempt").GetInt32();
            string eventName = Text(packet, "eventName");
            if (!ulong.TryParse(runId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong runNumber) ||
                runNumber == 0 || runAttempt < 1 ||
                eventName is not ("push" or "pull_request" or "merge_group" or "workflow_dispatch" or "schedule"))
            {
                diagnostic = "invalid_invocation";
                return false;
            }

            JsonElement lanes = packet.GetProperty("lanes");
            var laneIds = new HashSet<string>(StringComparer.Ordinal);
            var artifactPaths = new HashSet<string>(StringComparer.Ordinal);
            var reportDigests = new HashSet<string>(StringComparer.Ordinal);
            var reportIds = new HashSet<Guid>();
            var executionIds = new HashSet<Guid>();
            foreach (JsonElement lane in lanes.EnumerateArray())
            {
                string id = Text(lane, "id");
                if (!laneIds.Add(id))
                {
                    diagnostic = "duplicate_lane";
                    return false;
                }

                string category = id switch
                {
                    "security-integration" => "Security",
                    "cerbos-policy-contract" => "PolicyContract",
                    _ => string.Empty
                };
                string selector = "/*/*/*/*[Category=" + category + "]";
                if (!SecurityControlRegister.ExecutionRequirements.TryGetValue(id, out var requirement) ||
                    category.Length == 0 || Text(lane, "project") != requirement.Project ||
                    Text(lane, "selector") != selector ||
                    Text(lane, "command") != Command(id, category) ||
                    Text(lane, "runId") != runId ||
                    lane.GetProperty("runAttempt").GetInt32() != runAttempt)
                {
                    diagnostic = "lane_identity_mismatch";
                    return false;
                }

                ExecutionCounts counts = JsonCounts(lane.GetProperty("counts"));
                if (Text(lane, "outcome") != "passed" || lane.GetProperty("exitCode").GetInt32() != 0 ||
                    counts.Selected < 1 || counts.Selected != counts.Executed ||
                    counts.Executed != counts.Passed || counts.Failed != 0 || counts.Skipped != 0)
                {
                    diagnostic = "nonpassing_execution";
                    return false;
                }

                JsonElement context = lane.GetProperty("executionContext");
                if (Text(context, "kind") != "test-host" || Text(context, "environment") != "Testing" ||
                    Text(context, "hostProfile") != requirement.HostProfile ||
                    Text(context, "databaseProvider") != "not-attested" ||
                    Text(context, "databaseRole") != "not-attested" ||
                    Text(context, "authenticationScheme") != "not-attested")
                {
                    diagnostic = "invalid_execution_context";
                    return false;
                }

                int reports = 0;
                JsonElement artifacts = lane.GetProperty("artifacts");
                if (artifacts.GetArrayLength() is < 1 or > 8)
                {
                    diagnostic = "artifact_inventory_invalid";
                    return false;
                }

                foreach (JsonElement artifact in artifacts.EnumerateArray())
                {
                    string kind = Text(artifact, "kind");
                    string relativePath = Text(artifact, "path");
                    string expectedDigest = Text(artifact, "sha256");
                    if (kind is not ("trx" or "log") || !artifactPaths.Add(relativePath) ||
                        (kind == "trx" && !reportDigests.Add(expectedDigest)) ||
                        expectedDigest.Length != 64 || !LowerHex(expectedDigest))
                    {
                        diagnostic = "artifact_inventory_invalid";
                        return false;
                    }

                    byte[] bytes = ReadBoundedFile(ResolveArtifact(directory, relativePath), MaximumReportBytes);
                    string digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    if (!string.Equals(digest, expectedDigest, StringComparison.Ordinal))
                    {
                        diagnostic = "artifact_digest_mismatch";
                        return false;
                    }

                    if (kind == "trx")
                    {
                        reports++;
                        if (!TryReadReport(bytes, out ExecutionReport measured) || measured.Counts != counts)
                        {
                            diagnostic = "report_counts_mismatch";
                            return false;
                        }
                        if (!reportIds.Add(measured.RunId) || executionIds.Overlaps(measured.Executions))
                        {
                            diagnostic = "replayed_report_execution";
                            return false;
                        }
                        executionIds.UnionWith(measured.Executions);
                        if (!requirement.Tests.IsSubsetOf(measured.Tests))
                        {
                            diagnostic = "required_test_missing";
                            return false;
                        }
                    }
                }

                if (reports != 1)
                {
                    diagnostic = "report_inventory_invalid";
                    return false;
                }
            }

            if (laneIds.Count != 2 || !laneIds.Contains("security-integration") ||
                !laneIds.Contains("cerbos-policy-contract"))
            {
                diagnostic = "required_lane_missing";
                return false;
            }

            packetSha256 = Convert.ToHexString(SHA256.HashData(packetBytes)).ToLowerInvariant();
            diagnostic = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or
            JsonException or XmlException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException)
        {
            diagnostic = "invalid_packet";
            return false;
        }
    }

    private static string Command(string id, string category)
    {
        string filename = id == "security-integration"
            ? "security-integration-tests.trx"
            : "cerbos-policy-contract-tests.trx";
        return "dotnet test --project " + ProjectPath +
            " --configuration Release --no-build --verbosity minimal -- --treenode-filter \"/*/*/*/*[Category=" +
            category + "]\" --minimum-expected-tests 1" +
            (id == "security-integration" ? " --maximum-parallel-tests 1" : string.Empty) +
            " --no-progress --report-trx --report-trx-filename " + filename;
    }

    private static string Text(JsonElement element, string name) =>
        element.GetProperty(name).GetString() ?? throw new InvalidDataException();

    private static ExecutionCounts JsonCounts(JsonElement counts) => new(
        counts.GetProperty("selected").GetInt32(),
        counts.GetProperty("executed").GetInt32(),
        counts.GetProperty("passed").GetInt32(),
        counts.GetProperty("failed").GetInt32(),
        counts.GetProperty("skipped").GetInt32());

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

    private static bool LowerHex(string text) =>
        text.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string ResolveArtifact(string directory, string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.Contains('\\', StringComparison.Ordinal) ||
            relativePath.Contains(':', StringComparison.Ordinal))
        {
            throw new InvalidDataException();
        }

        string path = Path.GetFullPath(directory);
        if (new DirectoryInfo(path).LinkTarget is not null) throw new InvalidDataException();
        foreach (string segment in relativePath.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or "..") throw new InvalidDataException();
            path = Path.Combine(path, segment);
            FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            if (entry.LinkTarget is not null) throw new InvalidDataException();
        }

        return path;
    }

    private static byte[] ReadBoundedFile(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 1 || stream.Length > maximumBytes) throw new InvalidDataException();
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static bool TryReadReport(byte[] bytes, out ExecutionReport report)
    {
        report = default;
        using var stream = new MemoryStream(bytes, writable: false);
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumReportBytes
        });
        XDocument document = XDocument.Load(reader);
        XElement? root = document.Root;
        XElement? summary = root?.Element(TrxNamespace + "ResultSummary");
        XElement? counters = summary?.Element(TrxNamespace + "Counters");
        XElement? results = root?.Element(TrxNamespace + "Results");
        XElement? definitions = root?.Element(TrxNamespace + "TestDefinitions");
        if (root?.Name != TrxNamespace + "TestRun" || counters is null || results is null || definitions is null ||
            !Guid.TryParse((string?)root.Attribute("id"), out Guid runId) || runId == Guid.Empty ||
            root.Elements(TrxNamespace + "ResultSummary").Count() != 1 ||
            summary!.Elements(TrxNamespace + "Counters").Count() != 1 ||
            root.Elements(TrxNamespace + "Results").Count() != 1 ||
            root.Elements(TrxNamespace + "TestDefinitions").Count() != 1 ||
            (string?)summary?.Attribute("outcome") is not ("Completed" or "Passed"))
        {
            return false;
        }

        int total = Counter(counters, "total");
        int executed = Counter(counters, "executed");
        int passed = Counter(counters, "passed");
        int failed = Counter(counters, "failed");
        int skipped = OptionalCounter(counters, "notExecuted");
        if (total < 1 || total != executed || executed != passed || failed != 0 || skipped != 0)
        {
            return false;
        }

        foreach (string failure in new[]
        {
            "error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
            "notRunnable", "disconnected", "pending"
        })
        {
            if (OptionalCounter(counters, failure) != 0) return false;
        }

        var definitionsById = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (XElement definition in definitions.Elements())
        {
            string? id = (string?)definition.Attribute("id");
            if (definition.Name != TrxNamespace + "UnitTest" || string.IsNullOrWhiteSpace(id) ||
                !definitionsById.TryAdd(id, definition)) return false;
        }

        var executions = new HashSet<Guid>();
        var tests = new HashSet<string>(StringComparer.Ordinal);
        var namedTests = new HashSet<SecurityControlRegister.RequiredTest>();
        foreach (XElement result in results.Elements())
        {
            string? identity = (string?)result.Attribute("executionId");
            string? testId = (string?)result.Attribute("testId");
            if (result.Name != TrxNamespace + "UnitTestResult" ||
                (string?)result.Attribute("outcome") != "Passed" ||
                !Guid.TryParse(identity, out Guid executionId) || executionId == Guid.Empty || !executions.Add(executionId) ||
                string.IsNullOrWhiteSpace(testId) || !tests.Add(testId) ||
                string.IsNullOrWhiteSpace((string?)result.Attribute("testName")))
            {
                return false;
            }

            if (!definitionsById.TryGetValue(testId, out XElement? definition) ||
                (string?)definition.Attribute("name") != (string?)result.Attribute("testName") ||
                (string?)definition.Elements(TrxNamespace + "Execution").SingleOrDefault()?.Attribute("id") != identity)
                return false;
            XElement? method = definition.Elements(TrxNamespace + "TestMethod").SingleOrDefault();
            string? className = (string?)method?.Attribute("className");
            string? methodName = (string?)method?.Attribute("name");
            if (string.IsNullOrWhiteSpace(className) || string.IsNullOrWhiteSpace(methodName) ||
                !namedTests.Add(new SecurityControlRegister.RequiredTest(className, methodName,
                    (string)result.Attribute("testName")!))) return false;
        }

        if (executions.Count != total || definitionsById.Count != total) return false;
        report = new ExecutionReport(new ExecutionCounts(total, executed, passed, failed, skipped), namedTests, runId, executions);
        return true;
    }

    private static int Counter(XElement counters, string name)
    {
        if (!int.TryParse((string?)counters.Attribute(name), NumberStyles.None, CultureInfo.InvariantCulture,
            out int value) || value < 0)
        {
            throw new InvalidDataException();
        }

        return value;
    }

    private static int OptionalCounter(XElement counters, string name) =>
        counters.Attribute(name) is null ? 0 : Counter(counters, name);

    private readonly record struct ExecutionCounts(int Selected, int Executed, int Passed, int Failed, int Skipped);
    private readonly record struct ExecutionReport(ExecutionCounts Counts,
        HashSet<SecurityControlRegister.RequiredTest> Tests, Guid RunId, HashSet<Guid> Executions);
}
