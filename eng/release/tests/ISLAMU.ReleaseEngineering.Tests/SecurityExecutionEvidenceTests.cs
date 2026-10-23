using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class SecurityExecutionEvidenceTests
{
    [Test]
    public async Task AcceptedPacketDigestMatchesExactRetainedBytes()
    {
        using var fixture = new EvidenceFixture();

        bool accepted = SecurityExecutionEvidence.TryValidate(fixture.DirectoryPath, fixture.CandidateOid,
            out string? digest, out string diagnostic);
        string expected = Convert.ToHexString(
            SHA256.HashData(File.ReadAllBytes(fixture.PacketPath))).ToLowerInvariant();

        await Assert.That(accepted).IsTrue();
        await Assert.That(digest).IsEqualTo(expected);
        await Assert.That(diagnostic).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ProducerRetainsMeasuredReportsThatAdmissionCanRead()
    {
        using var fixture = new EvidenceFixture();
        string repository = fixture.ReportRepository();
        string outputDirectory = Path.Combine(repository, "artifacts", "security-tests");
        using var output = new StringWriter();

        int exitCode = SecurityExecutionEvidence.Run(
            ["produce-security-execution", outputDirectory, fixture.CandidateOid, "42", "1", "push"],
            output,
            repository);
        bool accepted = SecurityExecutionEvidence.TryValidate(outputDirectory, fixture.CandidateOid,
            out string? digest, out _);

        await Assert.That(exitCode).IsEqualTo(Program.Success);
        await Assert.That(accepted).IsTrue();
        await Assert.That(digest).IsNotNull();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("ambiguous")]
    [Arguments("nonpassing")]
    public async Task ProducerFailureCannotLeaveAnAdmissiblePacket(string failure)
    {
        using var fixture = new EvidenceFixture();
        if (failure == "nonpassing")
        {
            SecurityExecutionEvidenceFixture.Write(fixture.DirectoryPath, fixture.CandidateOid, "zero-tests");
        }

        string repository = fixture.ReportRepository();
        string project = Path.Combine(repository, "tests", "Event.API.IntegrationTests");
        string report = Path.Combine(project, "TestResults", "security-integration-tests.trx");
        if (failure == "missing")
        {
            File.Delete(report);
        }
        else if (failure == "ambiguous")
        {
            string duplicateDirectory = Path.Combine(project, "additional-results");
            Directory.CreateDirectory(duplicateDirectory);
            File.Copy(report, Path.Combine(duplicateDirectory, Path.GetFileName(report)));
        }

        string outputDirectory = Path.Combine(repository, "artifacts", "security-tests");
        Directory.CreateDirectory(outputDirectory);
        string packet = Path.Combine(outputDirectory, SecurityExecutionEvidence.PacketFileName);
        File.Copy(fixture.PacketPath, packet);
        using var output = new StringWriter();

        int exitCode = SecurityExecutionEvidence.Run(
            ["produce-security-execution", outputDirectory, fixture.CandidateOid, "42", "1", "push"],
            output,
            repository);

        await Assert.That(exitCode).IsNotEqualTo(Program.Success);
        await Assert.That(File.Exists(packet)).IsFalse();
    }

    [Test]
    [Arguments("unknown-schema")]
    [Arguments("no-op")]
    [Arguments("missing-lane")]
    [Arguments("mixed-invocation")]
    [Arguments("duplicate-property")]
    [Arguments("zero-tests")]
    [Arguments("skipped-tests")]
    [Arguments("forged-counts")]
    [Arguments("wrong-digest")]
    [Arguments("missing-report")]
    [Arguments("traversal")]
    [Arguments("external-entity")]
    [Arguments("production-context")]
    [Arguments("claimed-provider")]
    [Arguments("claimed-role")]
    [Arguments("claimed-authentication")]
    [Arguments("overflow-attempt")]
    [Arguments("missing-test-id")]
    [Arguments("missing-test-name")]
    [Arguments("duplicate-test-id")]
    [Arguments("replayed-report")]
    [Arguments("missing-definition")]
    [Arguments("renamed-required-method")]
    [Arguments("wrong-required-class")]
    [Arguments("unrelated-passing-test")]
    [Arguments("definition-execution-mismatch")]
    [Arguments("duplicate-named-result")]
    [Arguments("duplicate-summary")]
    [Arguments("duplicate-counters")]
    [Arguments("replayed-report-identities")]
    [Arguments("replayed-execution-identities")]
    [Arguments("oversized-packet")]
    public async Task InvalidExecutionCannotBecomeAdmissionEvidence(string failure)
    {
        using var fixture = new EvidenceFixture();
        JsonObject packet = fixture.Packet();
        JsonArray lanes = packet["lanes"]?.AsArray() ?? throw new InvalidDataException();
        JsonObject lane = lanes[0]?.AsObject() ?? throw new InvalidDataException();
        JsonObject counts = lane["counts"]?.AsObject() ?? throw new InvalidDataException();
        JsonArray artifacts = lane["artifacts"]?.AsArray() ?? throw new InvalidDataException();
        JsonObject artifact = artifacts[0]?.AsObject() ?? throw new InvalidDataException();
        string reportPath = Path.Combine(fixture.DirectoryPath,
            artifact["path"]?.GetValue<string>() ?? throw new InvalidDataException());

        switch (failure)
        {
            case "unknown-schema":
                packet["schemaVersion"] = "security-execution.unknown";
                break;
            case "no-op":
                packet["selection"] = "no-op";
                break;
            case "missing-lane":
                lanes.RemoveAt(1);
                break;
            case "mixed-invocation":
                lane["runId"] = "43";
                break;
            case "duplicate-property":
                File.WriteAllText(fixture.PacketPath, File.ReadAllText(fixture.PacketPath).Replace(
                    "\"selection\": \"executed\"",
                    "\"selection\": \"executed\", \"selection\": \"executed\"",
                    StringComparison.Ordinal));
                break;
            case "zero-tests":
                SecurityExecutionEvidenceFixture.Write(fixture.DirectoryPath, fixture.CandidateOid, "zero-tests");
                break;
            case "skipped-tests":
                counts["executed"] = 0;
                counts["passed"] = 0;
                counts["skipped"] = 1;
                break;
            case "forged-counts":
                counts["selected"] = 2;
                counts["executed"] = 2;
                counts["passed"] = 2;
                break;
            case "wrong-digest":
                artifact["sha256"] = new string('0', 64);
                break;
            case "missing-report":
                File.Delete(reportPath);
                break;
            case "traversal":
                File.Copy(reportPath, Path.Combine(fixture.Root, "outside.trx"));
                artifact["path"] = "../outside.trx";
                break;
            case "external-entity":
                File.WriteAllText(reportPath,
                    "<?xml version=\"1.0\"?><!DOCTYPE TestRun [<!ENTITY external SYSTEM \"file:///untrusted\">]>" +
                    "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">&external;</TestRun>");
                artifact["sha256"] = Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(reportPath))).ToLowerInvariant();
                break;
            case "production-context":
                JsonObject context = lane["executionContext"]?.AsObject() ?? throw new InvalidDataException();
                context["environment"] = "Production";
                break;
            case "claimed-provider":
                lane["executionContext"]!["databaseProvider"] = "PostgreSql";
                break;
            case "claimed-role":
                lane["executionContext"]!["databaseRole"] = "Runtime";
                break;
            case "claimed-authentication":
                lane["executionContext"]!["authenticationScheme"] = "Bearer";
                break;
            case "overflow-attempt":
                packet["runAttempt"] = long.MaxValue;
                break;
            case "replayed-report":
                lanes[1]!["artifacts"] = artifacts.DeepClone();
                break;
            case "replayed-report-identities":
            case "replayed-execution-identities":
                XNamespace replayNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
                XDocument union = XDocument.Load(reportPath);
                JsonObject otherArtifact = lanes[1]!["artifacts"]![0]!.AsObject();
                string otherReport = Path.Combine(fixture.DirectoryPath, otherArtifact["path"]!.GetValue<string>());
                XDocument other = XDocument.Load(otherReport);
                foreach (string container in new[] { "Results", "TestDefinitions" })
                    union.Root!.Element(replayNamespace + container)!.Add(other.Root!.Element(replayNamespace + container)!.Elements());
                XElement unionCounters = union.Descendants(replayNamespace + "Counters").Single();
                foreach (string count in new[] { "total", "executed", "passed" }) unionCounters.SetAttributeValue(count, 3);
                foreach (JsonNode? currentLane in lanes)
                {
                    currentLane!["counts"]!["selected"] = 3;
                    currentLane["counts"]!["executed"] = 3;
                    currentLane["counts"]!["passed"] = 3;
                }
                union.Save(reportPath);
                union.Root!.Add(new XComment("Different bytes do not create a new execution."));
                if (failure == "replayed-execution-identities") union.Root.SetAttributeValue("id", Guid.NewGuid());
                union.Save(otherReport);
                artifact["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(reportPath))).ToLowerInvariant();
                otherArtifact["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(otherReport))).ToLowerInvariant();
                break;
            case "missing-test-id":
            case "missing-test-name":
            case "duplicate-test-id":
            case "missing-definition":
            case "renamed-required-method":
            case "wrong-required-class":
            case "unrelated-passing-test":
            case "definition-execution-mismatch":
            case "duplicate-named-result":
            case "duplicate-summary":
            case "duplicate-counters":
                XDocument report = XDocument.Load(reportPath);
                XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
                XElement result = report.Descendants(ns + "UnitTestResult").Single();
                XElement definition = report.Descendants(ns + "UnitTest").Single();
                if (failure is "duplicate-test-id" or "duplicate-named-result")
                {
                    var duplicate = new XElement(result);
                    string duplicateExecution = Guid.NewGuid().ToString("D");
                    duplicate.SetAttributeValue("executionId", duplicateExecution);
                    if (failure == "duplicate-named-result")
                    {
                        string duplicateTest = Guid.NewGuid().ToString("D");
                        duplicate.SetAttributeValue("testId", duplicateTest);
                        var duplicateDefinition = new XElement(definition);
                        duplicateDefinition.SetAttributeValue("id", duplicateTest);
                        duplicateDefinition.Element(ns + "Execution")!.SetAttributeValue("id", duplicateExecution);
                        definition.Parent!.Add(duplicateDefinition);
                    }
                    result.Parent!.Add(duplicate);
                    XElement reportCounts = report.Descendants(ns + "Counters").Single();
                    foreach (string count in new[] { "total", "executed", "passed" }) reportCounts.SetAttributeValue(count, 2);
                    counts["selected"] = 2;
                    counts["executed"] = 2;
                    counts["passed"] = 2;
                }
                else if (failure is "missing-test-id" or "missing-test-name")
                {
                    result.Attribute(failure == "missing-test-id" ? "testId" : "testName")!.Remove();
                }
                else if (failure is "duplicate-summary" or "duplicate-counters")
                {
                    XElement summary = report.Root!.Element(ns + "ResultSummary")!;
                    XElement contradictory = new(failure == "duplicate-summary" ? summary : summary.Element(ns + "Counters")!);
                    XElement contradictoryCounts = failure == "duplicate-summary" ? contradictory.Element(ns + "Counters")! : contradictory;
                    contradictoryCounts.SetAttributeValue("failed", 1);
                    (failure == "duplicate-summary" ? report.Root : summary)!.Add(contradictory);
                }
                else if (failure == "missing-definition") definition.Remove();
                else if (failure == "definition-execution-mismatch")
                    definition.Element(ns + "Execution")!.SetAttributeValue("id", Guid.NewGuid());
                else if (failure == "wrong-required-class")
                    definition.Element(ns + "TestMethod")!.SetAttributeValue("className", "Unrelated.Tests");
                else
                {
                    definition.Element(ns + "TestMethod")!.SetAttributeValue("name", "UnrelatedPassingWitness");
                    if (failure == "unrelated-passing-test")
                    {
                        definition.SetAttributeValue("name", "UnrelatedPassingWitness");
                        result.SetAttributeValue("testName", "UnrelatedPassingWitness");
                    }
                }
                report.Save(reportPath);
                artifact["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(reportPath))).ToLowerInvariant();
                break;
            case "oversized-packet":
                packet["padding"] = new string('x', 262_145);
                break;
            default:
                throw new InvalidDataException();
        }

        if (failure is not ("duplicate-property" or "zero-tests"))
        {
            File.WriteAllText(fixture.PacketPath, packet.ToJsonString());
        }

        bool accepted = SecurityExecutionEvidence.TryValidate(fixture.DirectoryPath, fixture.CandidateOid,
            out string? digest, out string diagnostic);

        await Assert.That(accepted).IsFalse();
        await Assert.That(digest).IsNull();
        await Assert.That(diagnostic).IsNotEqualTo(string.Empty);
    }

    private sealed class EvidenceFixture : IDisposable
    {
        internal EvidenceFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "security-evidence-" + Guid.NewGuid().ToString("N"));
            DirectoryPath = Path.Combine(Root, "evidence");
            Directory.CreateDirectory(DirectoryPath);
            SecurityExecutionEvidenceFixture.Write(DirectoryPath, CandidateOid);
        }

        internal string Root { get; }
        internal string DirectoryPath { get; }
        internal string PacketPath => Path.Combine(DirectoryPath, SecurityExecutionEvidenceFixture.PacketFileName);
        internal string CandidateOid { get; } = new('a', 40);

        internal JsonObject Packet() =>
            JsonNode.Parse(File.ReadAllBytes(PacketPath))?.AsObject() ?? throw new InvalidDataException();

        internal string ReportRepository()
        {
            string repository = Path.Combine(Root, "repository");
            string reports = Path.Combine(repository, "tests", "Event.API.IntegrationTests", "TestResults");
            Directory.CreateDirectory(reports);
            File.Copy(Path.Combine(DirectoryPath, "security-execution-reports", "security-integration.trx"),
                Path.Combine(reports, "security-integration-tests.trx"));
            File.Copy(Path.Combine(DirectoryPath, "security-execution-reports", "cerbos-policy-contract.trx"),
                Path.Combine(reports, "cerbos-policy-contract-tests.trx"));
            return repository;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
