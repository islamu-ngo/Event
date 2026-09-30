using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel("RuntimePromotionTrustRoot")]
public sealed class SyncPublicChangelogTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [Test]
    public async Task RebuildsFromPinnedReleasesAndCheckModeNeverWrites()
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        string changelog = Path.Combine(fixture.RepositoryPath, "docs", "public", "changelog");
        Directory.CreateDirectory(changelog);
        File.WriteAllText(Path.Combine(changelog, "README.md"),
            $"{PublicChangelogPolicy.StartMarker}\n{PublicChangelogPolicy.EndMarker}\n");
        AuthorizedInventoryEntry first = VerifyAndRetain(fixture, GovernedReleaseFixture.FirstReleaseVersion,
            fixture.B, fixture.FirstTagObject, fixture.FirstReleaseDirectory);
        string inventoryPath = Path.Combine(fixture.RepositoryPath, "authorized-inventory.v1.json");
        WriteInventory(inventoryPath, [first]);

        (int staleCode, string staleOutput) = Run(fixture, inventoryPath, check: true);
        (int writeCode, string writeOutput) = Run(fixture, inventoryPath);
        string pagePath = Path.Combine(changelog, "README.md");
        string manifestPath = Path.Combine(changelog, "publication-manifest.v1.json");
        byte[] firstPage = File.ReadAllBytes(pagePath);
        byte[] firstManifest = File.ReadAllBytes(manifestPath);
        (int currentCode, string currentOutput) = Run(fixture, inventoryPath, check: true);
        (int repeatedCode, string repeatedOutput) = Run(fixture, inventoryPath);

        await Assert.That(staleCode).IsNotEqualTo(Program.Success);
        await Assert.That(staleOutput).Contains("changelog_stale");
        await Assert.That(writeCode).IsEqualTo(Program.Success).Because(writeOutput);
        await Assert.That(Encoding.UTF8.GetString(firstPage)).Contains("## v1.1.0");
        await Assert.That(currentCode).IsEqualTo(Program.Success).Because(currentOutput);
        await Assert.That(repeatedCode).IsEqualTo(Program.Success).Because(repeatedOutput);
        await Assert.That(repeatedOutput).Contains("changelog_unchanged");
        await Assert.That(File.ReadAllBytes(pagePath)).IsEquivalentTo(firstPage);
        await Assert.That(File.ReadAllBytes(manifestPath)).IsEquivalentTo(firstManifest);

        AuthorizedInventoryEntry second = VerifyAndRetain(fixture, GovernedReleaseFixture.SecondReleaseVersion,
            fixture.D, fixture.SecondTagObject, fixture.SecondReleaseDirectory);
        WriteInventory(inventoryPath, [first, second]);
        (int newStale, _) = Run(fixture, inventoryPath, check: true);
        (int unionCode, string unionOutput) = Run(fixture, inventoryPath);
        string union = File.ReadAllText(pagePath);

        await Assert.That(newStale).IsNotEqualTo(Program.Success);
        await Assert.That(unionCode).IsEqualTo(Program.Success).Because(unionOutput);
        await Assert.That(union).Contains("## v1.1.0");
        await Assert.That(union).Contains("## v1.1.1");
        await Assert.That(union.IndexOf("## v1.1.1", StringComparison.Ordinal)).IsLessThan(union.IndexOf("## v1.1.0", StringComparison.Ordinal));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        await Assert.That(manifest.RootElement.GetProperty("releases").GetArrayLength()).IsEqualTo(2);
        await Assert.That(manifest.RootElement.GetProperty("projectionSha256").GetString()).IsEqualTo(Digest(File.ReadAllBytes(pagePath)));

        WriteInventory(inventoryPath, [second]);
        byte[] acceptedPage = File.ReadAllBytes(pagePath);
        (int omittedCode, string omittedDiagnostic) = Run(fixture, inventoryPath);
        await Assert.That(omittedCode).IsNotEqualTo(Program.Success);
        await Assert.That(omittedDiagnostic).Contains("inventory_accepted_history_omitted");
        await Assert.That(File.ReadAllBytes(pagePath)).IsEquivalentTo(acceptedPage);
    }

    [Test]
    public async Task RejectsMissingAuthorityAndAcceptedPageMutationWithoutOverwriting()
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        string changelog = Path.Combine(fixture.RepositoryPath, "docs", "public", "changelog");
        Directory.CreateDirectory(changelog);
        AuthorizedInventoryEntry first = VerifyAndRetain(fixture, GovernedReleaseFixture.FirstReleaseVersion,
            fixture.B, fixture.FirstTagObject, fixture.FirstReleaseDirectory);
        string inventoryPath = Path.Combine(fixture.RepositoryPath, "authorized-inventory.v1.json");
        WriteInventory(inventoryPath, [first]);
        using var missingOutput = new StringWriter();
        int missingCode = SyncPublicChangelogCommand.Run(Arguments(inventoryPath, fixture.RepositoryPath), missingOutput,
            fixture.RepositoryPath, authority: null);
        (int generated, string generatedOutput) = Run(fixture, inventoryPath);
        string pagePath = Path.Combine(changelog, "README.md");
        File.AppendAllText(pagePath, "unauthorized mutation\n");
        byte[] mutated = File.ReadAllBytes(pagePath);
        (int rejected, string diagnostic) = Run(fixture, inventoryPath);

        await Assert.That(missingCode).IsNotEqualTo(Program.Success);
        await Assert.That(missingOutput.ToString()).Contains("inventory_final_lane_authority_missing");
        await Assert.That(generated).IsEqualTo(Program.Success).Because(generatedOutput);
        await Assert.That(rejected).IsNotEqualTo(Program.Success);
        await Assert.That(diagnostic).Contains("changelog_accepted_projection_drift");
        await Assert.That(File.ReadAllBytes(pagePath)).IsEquivalentTo(mutated);
    }

    [Test]
    [Arguments("manifest", false)]
    [Arguments("page", false)]
    [Arguments("manifest", true)]
    [Arguments("page", true)]
    public async Task RecoversExactInterruptedGenerationAndRollsBackIoFailures(string replacement, bool ioFailure)
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        string changelog = Path.Combine(fixture.RepositoryPath, "docs", "public", "changelog");
        Directory.CreateDirectory(changelog);
        string pagePath = Path.Combine(changelog, "README.md");
        string manifestPath = Path.Combine(changelog, "publication-manifest.v1.json");
        string transactionPath = Path.Combine(changelog, ".publication-transaction.v1.json");
        string inventoryPath = Path.Combine(fixture.RepositoryPath, "authorized-inventory.v1.json");
        AuthorizedInventoryEntry first = VerifyAndRetain(fixture, GovernedReleaseFixture.FirstReleaseVersion,
            fixture.B, fixture.FirstTagObject, fixture.FirstReleaseDirectory);
        WriteInventory(inventoryPath, [first]);
        (int initial, string initialOutput) = Run(fixture, inventoryPath);
        await Assert.That(initial).IsEqualTo(Program.Success).Because(initialOutput);
        byte[] oldPage = File.ReadAllBytes(pagePath);
        byte[] oldManifest = File.ReadAllBytes(manifestPath);
        AuthorizedInventoryEntry second = VerifyAndRetain(fixture, GovernedReleaseFixture.SecondReleaseVersion,
            fixture.D, fixture.SecondTagObject, fixture.SecondReleaseDirectory);
        WriteInventory(inventoryPath, [first, second]);

        bool injected = false;
        using var failedOutput = new StringWriter();
        int? failedCode = null;
        try
        {
            failedCode = SyncPublicChangelogCommand.Run(Arguments(inventoryPath, fixture.RepositoryPath),
                failedOutput, fixture.RepositoryPath, new PinnedInventoryAuthority(Digest(File.ReadAllBytes(inventoryPath))),
                member =>
                {
                    if (member != replacement) return;
                    injected = true;
                    if (ioFailure) throw new IOException("injected replacement failure");
                    throw new PublicationInterruptedException();
                });
        }
        catch (PublicationInterruptedException)
        {
            // Simulates process interruption without invoking the ordinary I/O rollback.
        }

        await Assert.That(injected).IsTrue();
        if (ioFailure)
        {
            await Assert.That(failedCode).IsEqualTo(Program.ToolchainRejected);
            await Assert.That(File.ReadAllBytes(pagePath)).IsEquivalentTo(oldPage);
            await Assert.That(File.ReadAllBytes(manifestPath)).IsEquivalentTo(oldManifest);
            await Assert.That(File.Exists(transactionPath)).IsFalse();
        }
        else
        {
            await Assert.That(File.Exists(transactionPath)).IsTrue();
            using JsonDocument journal = JsonDocument.Parse(File.ReadAllBytes(transactionPath));
            await Assert.That(journal.RootElement.GetProperty("oldPage").GetBytesFromBase64()).IsEquivalentTo(oldPage);
            await Assert.That(journal.RootElement.GetProperty("oldManifest").GetBytesFromBase64()).IsEquivalentTo(oldManifest);
            await Assert.That(File.ReadAllBytes(manifestPath)).IsEquivalentTo(
                journal.RootElement.GetProperty("newManifest").GetBytesFromBase64());
            await Assert.That(File.ReadAllBytes(pagePath)).IsEquivalentTo(replacement == "manifest"
                ? oldPage : journal.RootElement.GetProperty("newPage").GetBytesFromBase64());
        }

        string[] files = Directory.GetFiles(changelog).Order(StringComparer.Ordinal).ToArray();
        byte[][] beforeCheck = files.Select(File.ReadAllBytes).ToArray();
        DateTime[] timestamps = files.Select(File.GetLastWriteTimeUtc).ToArray();
        (int checkCode, string checkOutput) = Run(fixture, inventoryPath, check: true);
        await Assert.That(checkCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(checkOutput).Contains(ioFailure ? "changelog_stale" : "changelog_transaction_incomplete");
        await Assert.That(Directory.GetFiles(changelog).Order(StringComparer.Ordinal).ToArray()).IsEquivalentTo(files);
        for (int index = 0; index < files.Length; index++)
        {
            await Assert.That(File.ReadAllBytes(files[index])).IsEquivalentTo(beforeCheck[index]);
            await Assert.That(File.GetLastWriteTimeUtc(files[index])).IsEqualTo(timestamps[index]);
        }

        if (!ioFailure)
        {
            using var unauthorizedOutput = new StringWriter();
            int unauthorized = SyncPublicChangelogCommand.Run(Arguments(inventoryPath, fixture.RepositoryPath),
                unauthorizedOutput, fixture.RepositoryPath, authority: null);
            await Assert.That(unauthorized).IsEqualTo(Program.ToolchainRejected);
            await Assert.That(unauthorizedOutput.ToString()).Contains("inventory_final_lane_authority_missing");
            WriteInventory(inventoryPath, [second]);
            (int omitted, string omittedOutput) = Run(fixture, inventoryPath);
            await Assert.That(omitted).IsEqualTo(Program.ToolchainRejected);
            await Assert.That(omittedOutput).Contains("inventory_accepted_history_omitted");
            WriteInventory(inventoryPath, [first]);
            (int mismatch, string mismatchOutput) = Run(fixture, inventoryPath);
            await Assert.That(mismatch).IsEqualTo(Program.ToolchainRejected);
            await Assert.That(mismatchOutput).Contains("changelog_transaction_generation_mismatch");
            for (int index = 0; index < files.Length; index++)
                await Assert.That(File.ReadAllBytes(files[index])).IsEquivalentTo(beforeCheck[index]);
            WriteInventory(inventoryPath, [first, second]);
        }

        (int recovered, string recoveredOutput) = Run(fixture, inventoryPath);
        await Assert.That(recovered).IsEqualTo(Program.Success).Because(recoveredOutput);
        string page = File.ReadAllText(pagePath);
        await Assert.That(page).Contains("## v1.1.0");
        await Assert.That(page).Contains("## v1.1.1");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        await Assert.That(manifest.RootElement.GetProperty("releases").GetArrayLength()).IsEqualTo(2);
        await Assert.That(manifest.RootElement.GetProperty("projectionSha256").GetString()).IsEqualTo(Digest(File.ReadAllBytes(pagePath)));
        await Assert.That(Directory.GetFiles(changelog).Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo(new[] { "README.md", "publication-manifest.v1.json" });
        byte[] recoveredPage = File.ReadAllBytes(pagePath);
        byte[] recoveredManifest = File.ReadAllBytes(manifestPath);
        (int repeated, string repeatedOutput) = Run(fixture, inventoryPath);
        await Assert.That(repeated).IsEqualTo(Program.Success).Because(repeatedOutput);
        await Assert.That(repeatedOutput).Contains("changelog_unchanged");
        await Assert.That(File.ReadAllBytes(pagePath)).IsEquivalentTo(recoveredPage);
        await Assert.That(File.ReadAllBytes(manifestPath)).IsEquivalentTo(recoveredManifest);
    }

    [Test]
    public async Task InterruptedTransactionRejectsUnownedPageMutationAndSymlinks()
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        string changelog = Path.Combine(fixture.RepositoryPath, "docs", "public", "changelog");
        Directory.CreateDirectory(changelog);
        string inventoryPath = Path.Combine(fixture.RepositoryPath, "authorized-inventory.v1.json");
        AuthorizedInventoryEntry first = VerifyAndRetain(fixture, GovernedReleaseFixture.FirstReleaseVersion,
            fixture.B, fixture.FirstTagObject, fixture.FirstReleaseDirectory);
        WriteInventory(inventoryPath, [first]);
        using var interruptedOutput = new StringWriter();
        try
        {
            SyncPublicChangelogCommand.Run(Arguments(inventoryPath, fixture.RepositoryPath), interruptedOutput,
                fixture.RepositoryPath, new PinnedInventoryAuthority(Digest(File.ReadAllBytes(inventoryPath))),
                _ => throw new PublicationInterruptedException());
        }
        catch (PublicationInterruptedException) { }
        string transactionPath = Path.Combine(changelog, ".publication-transaction.v1.json");
        byte[] transaction = File.ReadAllBytes(transactionPath);
        string pagePath = Path.Combine(changelog, "README.md");
        File.WriteAllText(pagePath, "unauthorized mutation\n");
        byte[] mutation = File.ReadAllBytes(pagePath);
        foreach (bool check in new[] { true, false })
        {
            (int rejected, string diagnostic) = Run(fixture, inventoryPath, check);
            await Assert.That(rejected).IsEqualTo(Program.ToolchainRejected);
            await Assert.That(diagnostic).Contains("changelog_transaction_invalid");
            await Assert.That(File.ReadAllBytes(pagePath)).IsEquivalentTo(mutation);
            await Assert.That(File.ReadAllBytes(transactionPath)).IsEquivalentTo(transaction);
        }

        string external = Path.Combine(fixture.RepositoryPath, "external-journal.json");
        File.WriteAllBytes(external, transaction);
        File.Delete(transactionPath);
        File.CreateSymbolicLink(transactionPath, external);
        (int unsafeCode, string unsafeOutput) = Run(fixture, inventoryPath);
        await Assert.That(unsafeCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(unsafeOutput).Contains("changelog_path_invalid");
        await Assert.That(File.ReadAllBytes(external)).IsEquivalentTo(transaction);
        await Assert.That(File.ReadAllBytes(pagePath)).IsEquivalentTo(mutation);
    }

    [Test]
    public async Task RejectsUnknownAndMissingArguments()
    {
        using var output = new StringWriter();
        int unknown = SyncPublicChangelogCommand.Run(["sync-public-changelog", "--unrecognized"], output, ".", null);
        int missing = SyncPublicChangelogCommand.Run(["sync-public-changelog", "--check"], output, ".", null);
        await Assert.That(unknown).IsEqualTo(Program.UsageError);
        await Assert.That(missing).IsEqualTo(Program.UsageError);
    }

    private static (int Code, string Output) Run(GovernedReleaseFixture fixture, string inventoryPath, bool check = false)
    {
        using var output = new StringWriter();
        var authority = new PinnedInventoryAuthority(Digest(File.ReadAllBytes(inventoryPath)));
        int code = SyncPublicChangelogCommand.Run(Arguments(inventoryPath, fixture.RepositoryPath, check), output,
            fixture.RepositoryPath, authority);
        return (code, output.ToString());
    }

    private static string[] Arguments(string path, string retainedRoot, bool check = false)
    {
        string[] arguments =
        [
            "sync-public-changelog", "--inventory", path,
            "--retained-evidence", retainedRoot,
            "--publication-base", "https://github.com/ISLAMU/Event/blob/",
        ];
        return check ? [.. arguments, "--check"] : arguments;
    }

    private static void WriteInventory(string path, AuthorizedInventoryEntry[] entries) =>
        File.WriteAllText(path, JsonSerializer.Serialize(
            new { schemaVersion = "authorized-inventory.v1", producer = "final-lane", completeSet = true, entries }, JsonOptions));

    private static AuthorizedInventoryEntry VerifyAndRetain(
        GovernedReleaseFixture fixture, string version, string target, string tag, string directory)
    {
        (int candidateCode, string candidateOutput) = fixture.VerifyCandidate(version, target);
        if (candidateCode != Program.Success) throw new InvalidOperationException(candidateOutput);
        (int tagCode, string tagOutput) = fixture.VerifyTag(version, target, tag);
        if (tagCode != Program.Success) throw new InvalidOperationException(tagOutput);

        byte[] evidence = File.ReadAllBytes(Path.Combine(directory, "release-evidence.v1.json"));
        using JsonDocument document = JsonDocument.Parse(evidence);
        JsonElement fields = document.RootElement;
        ReleaseContext context = JsonSerializer.Deserialize<ReleaseContext>(
            File.ReadAllBytes(Path.Combine(directory, "release-context.v1.json")), JsonOptions)!;
        AuthorizedSourceDocument[] sourceDocuments = context.Changes.Select(change => change.ChangeId)
            .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(id => new AuthorizedSourceDocument($"docs/internal/releases/changes/{id}.yaml", "fragment")).ToArray();
        return new AuthorizedInventoryEntry(version, fields.GetProperty("line").GetString()!,
            fields.GetProperty("releaseDate").GetString()!, tag, target,
            $"docs/internal/releases/{version}", $"docs/internal/releases/{version}/release-evidence.v1.json",
            Digest(evidence), true, Digest("operator disclosure approval"u8.ToArray()), sourceDocuments);
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class PublicationInterruptedException : Exception;

    private sealed class PinnedInventoryAuthority(string acceptedDigest) : IFinalLaneInventoryAuthority
    {
        public bool VerifyCompleteInventory(string inventorySha256, IReadOnlyList<AuthorizedInventoryEntry> entries, CancellationToken cancellationToken = default) =>
            inventorySha256 == acceptedDigest;
    }
}
