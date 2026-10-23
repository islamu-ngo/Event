using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class SecurityExecutionEvidenceTests
{
    [Test]
    public async Task CompleteValidatedOwnedRegistrationAdmitsItsExactSyntheticWitnesses()
    {
        using var fixture = new EvidenceFixture(owned: true);
        bool accepted = SecurityExecutionEvidence.TryValidate(fixture.DirectoryPath, fixture.CandidateOid,
            fixture.RegisterPath!, out string? digest, out string diagnostic);
        await Assert.That(accepted).IsTrue();
        await Assert.That(digest).IsNotNull();
        await Assert.That(diagnostic).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task ProducerAndValidatorUseTheSameValidatedOwnedRegistration()
    {
        using var fixture = new EvidenceFixture(owned: true);
        string repository = fixture.ReportRepository();
        string destination = Path.Combine(repository, "artifacts", "security-tests");
        using var output = new StringWriter();
        int code = SecurityExecutionEvidence.Run(
            ["produce-security-execution", destination, fixture.CandidateOid, "42", "1", "push"],
            output, repository, fixture.RegisterPath!);
        await Assert.That(code).IsEqualTo(Program.Success);
        await Assert.That(SecurityExecutionEvidence.TryValidate(destination, fixture.CandidateOid,
            fixture.RegisterPath!, out _, out _)).IsTrue();
        var packet = JsonNode.Parse(File.ReadAllBytes(Path.Combine(destination, SecurityExecutionEvidence.PacketFileName)))!;
        await Assert.That(packet["lanes"]!.AsArray().Count).IsEqualTo(7);
    }

    [Test]
    public async Task OwnedProfilesCannotBeAdmittedByTheShippedTwoLaneRegistration()
    {
        using var fixture = new EvidenceFixture(owned: true);
        await Assert.That(SecurityExecutionEvidence.TryValidate(fixture.DirectoryPath, fixture.CandidateOid,
            out _, out _)).IsFalse();
    }

    [Test]
    [Arguments("wrong-profile")]
    [Arguments("unknown-profile")]
    [Arguments("wrong-environment")]
    [Arguments("claimed-provider")]
    [Arguments("claimed-role")]
    [Arguments("claimed-authentication")]
    [Arguments("missing-lane")]
    [Arguments("extra-lane")]
    [Arguments("zero-tests")]
    [Arguments("skipped-test")]
    [Arguments("wrong-candidate")]
    [Arguments("wrong-digest")]
    [Arguments("wrong-command")]
    [Arguments("wrong-selector")]
    [Arguments("missing-named-variant")]
    [Arguments("renamed-variant")]
    [Arguments("reused-named-variant")]
    [Arguments("unregistered-passing-variant")]
    [Arguments("reused-report")]
    [Arguments("contradictory-register")]
    [Arguments("invalid-register")]
    public async Task OwnedEvidenceRejectsProfileAndWitnessContradictions(string failure)
    {
        using var fixture = new EvidenceFixture(owned: true);
        JsonObject packet = fixture.Packet();
        JsonArray lanes = packet["lanes"]!.AsArray();
        JsonObject lane = lanes[2]!.AsObject();
        string candidate = fixture.CandidateOid;
        switch (failure)
        {
            case "wrong-profile": lane["executionContext"]!["hostProfile"] = "owned-browser-assurance"; break;
            case "unknown-profile": lane["executionContext"]!["hostProfile"] = "production"; break;
            case "wrong-environment": lane["executionContext"]!["environment"] = "Testing"; break;
            case "claimed-provider": lane["executionContext"]!["databaseProvider"] = "PostgreSql"; break;
            case "claimed-role": lane["executionContext"]!["databaseRole"] = "Runtime"; break;
            case "claimed-authentication": lane["executionContext"]!["authenticationScheme"] = "Local"; break;
            case "missing-lane": lanes.RemoveAt(lanes.Count - 1); break;
            case "extra-lane": var extra = lane.DeepClone(); extra["id"] = "unregistered-owned"; lanes.Add(extra); break;
            case "zero-tests": lane["counts"]!["selected"] = 0; break;
            case "skipped-test": lane["counts"]!["skipped"] = 1; break;
            case "wrong-candidate": candidate = new string('b', 40); break;
            case "wrong-digest": lane["artifacts"]![0]!["sha256"] = new string('b', 64); break;
            case "wrong-command": lane["command"] = "dotnet test unrelated.csproj"; break;
            case "wrong-selector": lane["selector"] = "/*/*/*/*[Category=Harness]"; break;
            case "reused-report": lanes[3]!["artifacts"] = lane["artifacts"]!.DeepClone(); break;
            case "contradictory-register":
            case "invalid-register":
                JsonObject register = JsonNode.Parse(File.ReadAllBytes(fixture.RegisterPath!))!.AsObject();
                if (failure == "contradictory-register")
                    register["executionRequirements"]![2]!["hostProfile"] = "owned-browser-assurance";
                else register["schemaVersion"] = 999;
                await File.WriteAllTextAsync(fixture.RegisterPath!, register.ToJsonString());
                break;
            default:
                XNamespace ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
                JsonNode artifact = lane["artifacts"]![0]!;
                string reportPath = Path.Combine(fixture.DirectoryPath, artifact["path"]!.GetValue<string>());
                XDocument report = XDocument.Load(reportPath);
                var results = report.Root!.Element(ns + "Results")!.Elements().ToArray();
                var definitions = report.Root!.Element(ns + "TestDefinitions")!.Elements().ToArray();
                if (failure == "unregistered-passing-variant")
                {
                    string testId = Guid.NewGuid().ToString("D");
                    string executionId = Guid.NewGuid().ToString("D");
                    var extraResult = new XElement(results[1]);
                    extraResult.SetAttributeValue("testId", testId);
                    extraResult.SetAttributeValue("executionId", executionId);
                    extraResult.SetAttributeValue("testName", "SyntheticWitness(unregistered)");
                    results[1].Parent!.Add(extraResult);
                    var extraDefinition = new XElement(definitions[1]);
                    extraDefinition.SetAttributeValue("id", testId);
                    extraDefinition.SetAttributeValue("name", "SyntheticWitness(unregistered)");
                    extraDefinition.Element(ns + "Execution")!.SetAttributeValue("id", executionId);
                    definitions[1].Parent!.Add(extraDefinition);
                    var counters = report.Root.Element(ns + "ResultSummary")!.Element(ns + "Counters")!;
                    foreach (string field in new[] { "total", "executed", "passed" }) counters.SetAttributeValue(field, 3);
                    foreach (string field in new[] { "selected", "executed", "passed" }) lane["counts"]![field] = 3;
                }
                else if (failure == "missing-named-variant")
                {
                    results[1].Remove(); definitions[1].Remove();
                    var counters = report.Root.Element(ns + "ResultSummary")!.Element(ns + "Counters")!;
                    foreach (string field in new[] { "total", "executed", "passed" }) counters.SetAttributeValue(field, 1);
                    foreach (string field in new[] { "selected", "executed", "passed" }) lane["counts"]![field] = 1;
                }
                else
                {
                    string name = failure == "reused-named-variant"
                        ? (string)results[0].Attribute("testName")! : "SyntheticWitness(renamed)";
                    results[1].SetAttributeValue("testName", name);
                    definitions[1].SetAttributeValue("name", name);
                }
                report.Save(reportPath);
                artifact["sha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(reportPath))).ToLowerInvariant();
                break;
        }

        await File.WriteAllTextAsync(fixture.PacketPath, packet.ToJsonString());
        await Assert.That(SecurityExecutionEvidence.TryValidate(fixture.DirectoryPath, candidate,
            fixture.RegisterPath!, out string? digest, out string diagnostic)).IsFalse();
        await Assert.That(digest).IsNull();
        await Assert.That(diagnostic).IsNotEqualTo(string.Empty);
    }

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
        internal EvidenceFixture(bool owned = false, [CallerFilePath] string sourcePath = "")
        {
            Root = Path.Combine(Path.GetTempPath(), "security-evidence-" + Guid.NewGuid().ToString("N"));
            DirectoryPath = Path.Combine(Root, "evidence");
            Directory.CreateDirectory(DirectoryPath);
            if (owned)
            {
                RegisterPath = Path.Combine(Root, "register.json");
                string shipped = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", "..", "..",
                    "security", "security-control-register.json"));
                File.WriteAllText(RegisterPath, SecurityExecutionEvidenceFixture.OwnedRegister(shipped).ToJsonString());
            }
            SecurityExecutionEvidenceFixture.Write(DirectoryPath, CandidateOid, registerPath: RegisterPath);
        }

        internal string Root { get; }
        internal string DirectoryPath { get; }
        internal string? RegisterPath { get; }
        internal string PacketPath => Path.Combine(DirectoryPath, SecurityExecutionEvidenceFixture.PacketFileName);
        internal string CandidateOid { get; } = new('a', 40);

        internal JsonObject Packet() =>
            JsonNode.Parse(File.ReadAllBytes(PacketPath))?.AsObject() ?? throw new InvalidDataException();

        internal string ReportRepository()
        {
            string repository = Path.Combine(Root, "repository");
            foreach (JsonNode? lane in Packet()["lanes"]!.AsArray())
            {
                string project = lane!["project"]!.GetValue<string>();
                string id = lane["id"]!.GetValue<string>();
                string reports = Path.Combine(repository, Path.GetDirectoryName(project)!, "TestResults");
                Directory.CreateDirectory(reports);
                File.Copy(Path.Combine(DirectoryPath, "security-execution-reports", id + ".trx"),
                    Path.Combine(reports, id + "-tests.trx"));
            }
            return repository;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
