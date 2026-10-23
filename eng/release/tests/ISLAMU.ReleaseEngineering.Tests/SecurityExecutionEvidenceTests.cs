using System.Security.Cryptography;
using System.Text.Json.Nodes;

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
