using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class AuthorizedInventoryPolicyTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    [Test]
    public async Task EmptyInventoryFailsClosed()
    {
        using var fixture = new InventoryFixture();
        await Assert.That(fixture.Verify([]).Diagnostics.Single()).IsEqualTo("inventory_empty");
    }

    [Test]
    public async Task DuplicateVersionFailsClosed()
    {
        using var fixture = new InventoryFixture();
        AuthorizedInventoryEntry entry = fixture.AddRelease("1.1.0", "v1.1");
        await Assert.That(fixture.Verify([entry, entry]).Diagnostics.Single()).IsEqualTo("inventory_duplicate_version");
    }

    [Test]
    public async Task MissingEvidenceAndDisclosureFailClosed()
    {
        using var fixture = new InventoryFixture();
        AuthorizedInventoryEntry entry = fixture.AddRelease("1.1.0", "v1.1");
        await Assert.That(fixture.Verify([entry with { DisclosureAuthorized = false }]).Diagnostics.Single()).IsEqualTo("inventory_disclosure_missing");
        await Assert.That(fixture.Verify([entry with { AuthorizationEvidenceSha256 = "" }]).Diagnostics.Single()).IsEqualTo("inventory_disclosure_missing");
        File.Delete(Path.Combine(fixture.EvidenceRoot, entry.EvidencePath));
        await Assert.That(fixture.Verify([entry]).Diagnostics.Single()).IsEqualTo("inventory_evidence_missing");
    }

    [Test]
    public async Task SelfAssertedCompletenessWithoutAuthorityFailsClosed()
    {
        using var fixture = new InventoryFixture();
        AuthorizedInventoryEntry entry = fixture.AddRelease("1.1.0", "v1.1");
        await Assert.That(fixture.Verify([entry], supplyAuthority: false).Diagnostics.Single()).IsEqualTo("inventory_final_lane_authority_missing");
        await Assert.That(fixture.Verify([entry], complete: false).Diagnostics.Single()).IsEqualTo("inventory_incomplete");
        await Assert.That(fixture.Verify([entry], trusted: false).Diagnostics.Single()).IsEqualTo("inventory_final_lane_authority_invalid");
    }

    [Test]
    public async Task MixedLinesAndPrereleasesReturnAllCommittedPresentationInputs()
    {
        using var fixture = new InventoryFixture();
        AuthorizedInventoryEntry stable = fixture.AddRelease("2.0.0", "v2.0");
        AuthorizedInventoryEntry preview = fixture.AddRelease("3.0.0-rc.1", "v3.0");
        AuthorizedInventoryEntry maintenance = fixture.AddRelease("1.1.1", "v1.1");
        fixture.Git("switch", "--detach");
        fixture.Git("branch", "-D", "fixture");
        Directory.Delete(Path.Combine(fixture.Repository, "inputs"), recursive: true);
        AuthorizedInventoryResult result = fixture.Verify([stable, preview, maintenance]);
        await Assert.That(string.Join(",", result.Diagnostics)).IsEqualTo("");
        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Releases.Count).IsEqualTo(3);
        AuthorizedRelease release = result.Releases.Single(item => item.Version == stable.Version);
        await Assert.That(release.TargetOid).IsEqualTo(stable.TargetOid);
        await Assert.That(release.Context.Release.Version).IsEqualTo(stable.Version);
        await Assert.That(release.Descriptor.Line).IsEqualTo(stable.Line);
        await Assert.That(release.Summary).IsEqualTo("Improve release verification.\n");
        await Assert.That(release.EvidenceSha256).IsEqualTo(stable.EvidenceSha256);
    }

    [Test]
    public async Task MovedDeletedAndLightweightTagsFailClosed()
    {
        using var fixture = new InventoryFixture();
        AuthorizedInventoryEntry entry = fixture.AddRelease("1.1.0", "v1.1");
        fixture.Git("update-ref", "refs/tags/v1.1.0", entry.TargetOid);
        await Assert.That(fixture.Verify([entry]).Diagnostics.Single()).IsEqualTo("inventory_tag_ref_mismatch");
        await Assert.That(fixture.Verify([entry with { TagObjectId = entry.TargetOid }]).Diagnostics.Single()).IsEqualTo("inventory_tag_not_annotated");
        fixture.Git("update-ref", "-d", "refs/tags/v1.1.0");
        await Assert.That(fixture.Verify([entry]).IsValid).IsFalse();
    }

    [Test]
    public async Task MissingPinnedOidAndMixedVersionEvidenceFailClosed()
    {
        using var fixture = new InventoryFixture();
        AuthorizedInventoryEntry entry = fixture.AddRelease("1.1.0", "v1.1");
        await Assert.That(fixture.Verify([entry with { TagObjectId = "" }]).Diagnostics.Single()).IsEqualTo("inventory_object_id_invalid");
        await Assert.That(fixture.Verify([entry with { Version = "1.2.0", Line = "v1.2" }]).IsValid).IsFalse();
    }

    [Test]
    public async Task AcceptedHistoryCannotBeOmittedOrReplaced()
    {
        using var fixture = new InventoryFixture();
        AuthorizedInventoryEntry first = fixture.AddRelease("1.1.0", "v1.1");
        AuthorizedInventoryEntry second = fixture.AddRelease("2.0.0", "v2.0");
        var accepted = new AcceptedReleaseIdentity(first.Version, first.TagObjectId, first.EvidenceSha256);
        await Assert.That(fixture.Verify([second], [accepted]).Diagnostics.Single()).IsEqualTo("inventory_accepted_history_omitted");
        await Assert.That(fixture.Verify([first], [accepted with { EvidenceSha256 = second.EvidenceSha256 }]).Diagnostics.Single()).IsEqualTo("inventory_accepted_history_replaced");
    }

    [Test]
    public async Task TamperedEvidenceAndCommittedHashesFailClosed()
    {
        using var fixture = new InventoryFixture();
        AuthorizedInventoryEntry entry = fixture.AddRelease("1.1.0", "v1.1");
        string path = Path.Combine(fixture.EvidenceRoot, entry.EvidencePath);
        string evidence = File.ReadAllText(path);
        File.WriteAllText(path, evidence + "\n");
        await Assert.That(fixture.Verify([entry]).Diagnostics.Single()).IsEqualTo("inventory_evidence_hash_mismatch");
        using JsonDocument document = JsonDocument.Parse(evidence);
        var fields = document.RootElement.EnumerateObject().ToDictionary(item => item.Name, item => item.Value.Clone());
        fields["releaseSummarySha256"] = JsonSerializer.SerializeToElement(new string('0', 64));
        string altered = JsonSerializer.Serialize(fields);
        File.WriteAllText(path, altered);
        await Assert.That(fixture.Verify([entry with { EvidenceSha256 = Hash(Encoding.UTF8.GetBytes(altered)) }]).Diagnostics.Single()).IsEqualTo("inventory_committed_input_hash_mismatch");
    }

    [Test]
    public async Task TraversalAndOversizedDocumentsReturnBoundedDiagnostics()
    {
        using var fixture = new InventoryFixture();
        AuthorizedInventoryEntry entry = fixture.AddRelease("1.1.0", "v1.1");
        await Assert.That(fixture.Verify([entry with { EvidencePath = "../outside.json" }]).Diagnostics.Single()).IsEqualTo("inventory_path_invalid");
        AuthorizedInventoryResult oversized = AuthorizedInventoryPolicy.Verify(fixture.Repository, new string('x', 1_048_577), fixture.EvidenceRoot, null, [], TimeSpan.FromSeconds(5));
        await Assert.That(oversized.Diagnostics.Single()).IsEqualTo("inventory_too_large");
        await Assert.That(oversized.Releases.Count).IsEqualTo(0);
    }

    [Test]
    public async Task WholeInventoryAuthorityBindsDisclosureProofAndCompleteSet()
    {
        using var fixture = new InventoryFixture();
        AuthorizedInventoryEntry first = fixture.AddRelease("1.1.0", "v1.1");
        AuthorizedInventoryEntry second = fixture.AddRelease("2.0.0", "v2.0");
        string original = Serialize([first, second]);
        var authority = new PinnedTestAuthority(Hash(Encoding.UTF8.GetBytes(original)));
        AuthorizedInventoryResult Verify(string json) => AuthorizedInventoryPolicy.Verify(
            fixture.Repository, json, fixture.EvidenceRoot, authority, [], TimeSpan.FromSeconds(5));
        await Assert.That(Verify(original).IsValid).IsTrue();
        await Assert.That(Verify(Serialize([first])).Diagnostics.Single()).IsEqualTo("inventory_final_lane_authority_invalid");
        await Assert.That(Verify(Serialize([first with { AuthorizationEvidenceSha256 = second.AuthorizationEvidenceSha256 }, second])).Diagnostics.Single())
            .IsEqualTo("inventory_final_lane_authority_invalid");
        await Assert.That(Verify(Serialize([first with { DisclosureAuthorized = false }, second])).Diagnostics.Single())
            .IsEqualTo("inventory_disclosure_missing");
        await Assert.That(Verify(original + "\n").Diagnostics.Single()).IsEqualTo("inventory_final_lane_authority_invalid");

        static string Serialize(AuthorizedInventoryEntry[] entries) => JsonSerializer.Serialize(
            new { schemaVersion = "authorized-inventory.v1", producer = "final-lane", completeSet = true, entries }, JsonOptions);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    [NotInParallel("RuntimePromotionTrustRoot")]
    public async Task ExistingSignedFinalEvidenceValidatesCommittedFragments(bool subsequentRelease)
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        string version = subsequentRelease ? GovernedReleaseFixture.SecondReleaseVersion : GovernedReleaseFixture.FirstReleaseVersion;
        string target = subsequentRelease ? fixture.D : fixture.B;
        string tag = subsequentRelease ? fixture.SecondTagObject : fixture.FirstTagObject;
        string directory = subsequentRelease ? fixture.SecondReleaseDirectory : fixture.FirstReleaseDirectory;
        (int candidateCode, string candidateOutput) = fixture.VerifyCandidate(version, target);
        await Assert.That(candidateCode).IsEqualTo(Program.Success);
        (int tagCode, string tagOutput) = fixture.VerifyTag(version, target, tag);
        await Assert.That(tagCode).IsEqualTo(Program.Success);
        byte[] evidenceBytes = File.ReadAllBytes(Path.Combine(directory, "release-evidence.v1.json"));
        using JsonDocument evidence = JsonDocument.Parse(evidenceBytes);
        JsonElement fields = evidence.RootElement;
        ReleaseContext context = JsonSerializer.Deserialize<ReleaseContext>(
            File.ReadAllBytes(Path.Combine(directory, "release-context.v1.json")),
            JsonOptions)!;
        AuthorizedSourceDocument[] documents = context.Changes.Select(change => change.ChangeId).OfType<string>()
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(id => new AuthorizedSourceDocument($"docs/internal/releases/changes/{id}.yaml", "fragment")).ToArray();
        var entry = new AuthorizedInventoryEntry(
            fields.GetProperty("version").GetString()!, fields.GetProperty("line").GetString()!,
            fields.GetProperty("releaseDate").GetString()!, tag, target,
            $"docs/internal/releases/{version}",
            $"docs/internal/releases/{version}/release-evidence.v1.json",
            Hash(evidenceBytes), true, Hash(Encoding.UTF8.GetBytes("retained test disclosure approval")), documents);
        AuthorizedInventoryResult Verify(AuthorizedInventoryEntry item)
        {
            string json = JsonSerializer.Serialize(new { schemaVersion = "authorized-inventory.v1", producer = "final-lane", completeSet = true, entries = new[] { item } },
                JsonOptions);
            return AuthorizedInventoryPolicy.Verify(fixture.RepositoryPath, json, fixture.RepositoryPath,
                new PinnedTestAuthority(Hash(Encoding.UTF8.GetBytes(json))), [], TimeSpan.FromSeconds(5));
        }
        AuthorizedInventoryResult result = Verify(entry);
        await Assert.That(string.Join(",", result.Diagnostics)).IsEqualTo("");
        await Assert.That(result.Releases.Single().Fragments.Count).IsEqualTo(documents.Length);
        if (!subsequentRelease)
        {
            await Assert.That(documents.Length > 0).IsTrue();
            await Assert.That(Verify(entry with { SourceDocuments = [] }).Diagnostics.Single()).IsEqualTo("inventory_fragment_hash_mismatch");
        }
    }

    private sealed class PinnedTestAuthority(string digest) : IFinalLaneInventoryAuthority
    {
        public bool VerifyCompleteInventory(string inventorySha256, IReadOnlyList<AuthorizedInventoryEntry> entries) => inventorySha256 == digest;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class InventoryFixture : IDisposable, IFinalLaneInventoryAuthority
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"inventory-{Guid.NewGuid():N}");
        private string authorizedDigest = "";
        public string Repository => Path.Combine(root, "repository");
        public string EvidenceRoot => Path.Combine(root, "retained");

        public InventoryFixture()
        {
            Directory.CreateDirectory(Repository);
            Directory.CreateDirectory(EvidenceRoot);
            Git("init", "--initial-branch=fixture");
            Git("config", "user.name", "Release fixture");
            Git("config", "user.email", "fixture@example.invalid");
        }

        public AuthorizedInventoryEntry AddRelease(string version, string line)
        {
            string source = $"inputs/{version}";
            string directory = Path.Combine(Repository, source);
            Directory.CreateDirectory(directory);
            const string date = "2026-09-01";
            const string zero = "0000000000000000000000000000000000000000";
            string descriptor = $"Version: {version}\nLine: {line}\nRelease-Date: {date}\nBase-Stable-Tag: v1.0.0\nPrevious-Published-Tag: v1.0.0\nRelease-Range:\n  Base-Ref: v1.0.0\n  Base-Oid: {zero}\n  Previous-Ref: v1.0.0\n  Previous-Oid: {zero}\n";
            descriptor += "Compatibility:\n  - v1\nImpact-Dispositions:\n  breaking: not-applicable\n  security: not-applicable\n  migration: not-applicable\n  configuration: not-applicable\n  openapi: not-applicable\n  operator: not-applicable\n";
            var context = new ReleaseContext(1,
                new ReleaseContextRelease(version, line, date, "v1.0.0", "v1.0.0", "patch", version.Contains('-') ? "rc" : "stable", !version.Contains('-')),
                [], new ReleaseContextEvidence(zero, zero, []));
            byte[] contextBytes = ReleaseArtifactPolicy.NormalizeJson(JsonSerializer.Serialize(context, JsonOptions)).Bytes!;
            var inputs = new Dictionary<string, byte[]>
            {
                ["release.yaml"] = Encoding.UTF8.GetBytes(descriptor),
                ["summary.md"] = Encoding.UTF8.GetBytes("Improve release verification.\n"),
                ["release-context.v1.json"] = contextBytes,
                ["release-notes.md"] = Encoding.UTF8.GetBytes("Release notes.\n"),
            };
            foreach ((string name, byte[] bytes) in inputs) File.WriteAllBytes(Path.Combine(directory, name), bytes);
            Git("add", source);
            Git("commit", "-m", $"Record release {version}");
            string target = Git("rev-parse", "HEAD").Trim();
            Git("tag", "-a", $"v{version}", "-m", $"v{version}");
            string tag = Git("rev-parse", $"refs/tags/v{version}").Trim();
            byte[] evidence = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                schemaVersion = "release-evidence.v1", version, line, releaseDate = date,
                tagName = $"v{version}", tagObjectId = tag, targetOid = target, candidateOid = target,
                releaseDescriptorSha256 = Hash(inputs["release.yaml"]),
                releaseSummarySha256 = Hash(inputs["summary.md"]),
                releaseContextSha256 = Hash(inputs["release-context.v1.json"]),
                releaseNotesSha256 = Hash(inputs["release-notes.md"]),
                releaseFragmentsSha256 = Hash([]),
            }));
            string evidencePath = $"{version}.json";
            File.WriteAllBytes(Path.Combine(EvidenceRoot, evidencePath), evidence);
            return new AuthorizedInventoryEntry(version, line, date, tag, target, source, evidencePath, Hash(evidence), true, Hash(Encoding.UTF8.GetBytes($"disclose:{version}:{tag}")), []);
        }

        public AuthorizedInventoryResult Verify(IReadOnlyList<AuthorizedInventoryEntry> entries, IReadOnlyList<AcceptedReleaseIdentity>? accepted = null, bool supplyAuthority = true, bool complete = true, bool trusted = true)
        {
            string json = JsonSerializer.Serialize(new { schemaVersion = "authorized-inventory.v1", producer = "final-lane", completeSet = complete, entries }, JsonOptions);
            authorizedDigest = trusted ? Hash(Encoding.UTF8.GetBytes(json)) : new string('0', 64);
            return AuthorizedInventoryPolicy.Verify(Repository, json, EvidenceRoot, supplyAuthority ? this : null, accepted ?? [], TimeSpan.FromSeconds(5));
        }

        // A test-only retained authority pins the whole byte sequence, including every disclosure
        // record. Production must authenticate this digest outside the publisher.
        public bool VerifyCompleteInventory(string inventorySha256, IReadOnlyList<AuthorizedInventoryEntry> entries) => inventorySha256 == authorizedDigest;

        public string Git(params string[] args)
        {
            var info = new ProcessStartInfo("git") { WorkingDirectory = Repository, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string arg in args) info.ArgumentList.Add(arg);
            info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            info.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
            info.Environment["GIT_AUTHOR_DATE"] = "2026-09-01T00:00:00Z";
            info.Environment["GIT_COMMITTER_DATE"] = "2026-09-01T00:00:00Z";
            using Process process = Process.Start(info)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task.WhenAll(process.WaitForExitAsync(cancellation.Token), output, error).GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidOperationException(error.Result);
            return output.Result;
        }

        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
