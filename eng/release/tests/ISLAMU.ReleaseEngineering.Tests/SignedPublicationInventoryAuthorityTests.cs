using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class SignedPublicationInventoryAuthorityTests
{
    [Test]
    public async Task CancelledAuthorityAndFinalTagFailClosedBeforeReadingInputs()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var authority = new SignedPublicationInventoryAuthority("", "", "", "", "");
        await Assert.That(authority.VerifyCompleteInventory("", [], cancellation.Token)).IsFalse();
        await Assert.That(TagCommand.IsSignedFinalRelease("", "", "", "", "", "", cancellation.Token)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProcessCancellationOrOutputFaultReapsTheStartedProcess(bool outputFault)
        => await VerifyProcessCleanup(outputFault, signatureInput: false);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SignatureBlockedInputCancellationOrOutputFaultReapsProcess(bool outputFault)
        => await VerifyProcessCleanup(outputFault, signatureInput: true);

    [Test]
    public async Task MaximumRetainedInventoryProcessesEveryApprovalWithinSharedBudget()
    {
        string root = Path.Combine(Path.GetTempPath(), $"publication-large-{Guid.NewGuid():N}");
        string retained = Path.Combine(root, "retained");
        string approvals = Path.Combine(retained, "publication-approvals");
        Directory.CreateDirectory(approvals);
        try
        {
            string key = Path.Combine(root, "signer");
            Run(ReleaseToolPaths.SshKeygen, "-q", "-t", "ed25519", "-N", string.Empty, "-f", key);
            string publicKey = string.Join(' ', File.ReadAllText(key + ".pub")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
            string signers = Path.Combine(root, "allowed-signers");
            File.WriteAllText(signers, $"publication-approver namespaces=\"islamu-publication\" {publicKey}\n");
            string evidencePath = Path.Combine(retained, "release-evidence.v1.json");
            byte[] evidence = "{}"u8.ToArray();
            File.WriteAllBytes(evidencePath, evidence);
            string evidenceDigest = Digest(evidence);
            string tagObjectId = new('a', 40);
            string targetOid = new('b', 40);
            var entries = new AuthorizedInventoryEntry[PublicationInventoryVerificationBudget.MaximumEntries];
            for (int index = 0; index < entries.Length; index++)
            {
                string version = $"1.0.{index}";
                byte[] approval = Encoding.UTF8.GetBytes(
                    $$"""{"schemaVersion":"publication-approval.v1","version":"{{version}}","tagObjectId":"{{tagObjectId}}","evidenceSha256":"{{evidenceDigest}}","disclosureAuthorized":true}""");
                File.WriteAllBytes(Path.Combine(approvals, version + ".json"), approval);
                entries[index] = new AuthorizedInventoryEntry(
                    version, "v1.0", "2026-09-01", tagObjectId, targetOid, "release",
                    "release-evidence.v1.json", evidenceDigest, true, Digest(approval), []);
            }

            string inventoryPath = Path.Combine(root, "inventory.json");
            byte[] inventory = "{\"completeSet\":true}"u8.ToArray();
            File.WriteAllBytes(inventoryPath, inventory);
            Run(ReleaseToolPaths.SshKeygen, "-Y", "sign", "-f", key, "-n", "islamu-publication", inventoryPath);
            int verifiedEntries = 0;
            var authority = new SignedPublicationInventoryAuthority(
                inventoryPath, retained, signers, root, root,
                (_, _, _, _, _, _, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    verifiedEntries++;
                    return true;
                });

            bool result = authority.VerifyCompleteInventory(Digest(inventory), entries);

            await Assert.That(result).IsTrue();
            await Assert.That(verifiedEntries).IsEqualTo(PublicationInventoryVerificationBudget.MaximumEntries);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task VerifyProcessCleanup(bool outputFault, bool signatureInput)
    {
        using var cancellation = new CancellationTokenSource();
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        Task<System.Net.Sockets.TcpClient> started = listener.AcceptTcpClientAsync();
        Task<Exception?> run = Task.Run(() =>
        {
            try
            {
                string command = $"printf '%s' \"$$\" > /dev/tcp/127.0.0.1/{port}; exec " +
                    (outputFault ? "yes" : "tail -f /dev/null");
                if (signatureInput)
                {
                    using var process = new Process
                    {
                        StartInfo = new ProcessStartInfo("/bin/bash")
                        {
                            UseShellExecute = false,
                            RedirectStandardInput = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                        },
                    };
                    process.StartInfo.ArgumentList.Add("-c");
                    process.StartInfo.ArgumentList.Add(command);
                    SignedPublicationInventoryAuthority.VerifySignature(process, new byte[1_048_576], cancellation.Token);
                }
                else
                {
                    TagCommand.RunProcess("/bin/bash", null, cancellation.Token, "-c", command);
                }
                return null;
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException)
            {
                return exception;
            }
        });
        try
        {
            using System.Net.Sockets.TcpClient client = await started.WaitAsync(TimeSpan.FromSeconds(10));
            using var reader = new StreamReader(client.GetStream());
            int pid = int.Parse(await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10)), System.Globalization.CultureInfo.InvariantCulture);
            if (!outputFault) cancellation.Cancel();
            Exception? failure = await run.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(outputFault ? failure is IOException : failure is OperationCanceledException).IsTrue();
            bool exited;
            try { using Process process = Process.GetProcessById(pid); exited = process.HasExited; }
            catch (ArgumentException) { exited = true; }
            await Assert.That(exited).IsTrue();
        }
        finally
        {
            cancellation.Cancel();
            await run.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Test]
    [NotInParallel("RuntimePromotionTrustRoot")]
    public async Task OperatorSignatureBindsCompleteInventoryAndDisclosureReceipt()
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        string version = GovernedReleaseFixture.FirstReleaseVersion;
        (int candidateCode, string candidateOutput) = fixture.VerifyCandidate(version, fixture.B);
        if (candidateCode != Program.Success) throw new InvalidOperationException(candidateOutput);
        (int tagCode, string tagOutput) = fixture.VerifyTag(version, fixture.B, fixture.FirstTagObject);
        if (tagCode != Program.Success) throw new InvalidOperationException(tagOutput);
        string evidencePath = $"docs/internal/releases/{version}/release-evidence.v1.json";
        byte[] finalEvidence = File.ReadAllBytes(Path.Combine(fixture.RepositoryPath, evidencePath));
        string root = Path.Combine(fixture.Root, "publication-authority");
        string approvals = Path.Combine(fixture.RepositoryPath, "publication-approvals");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(approvals);
        string key = Path.Combine(root, "signer");
        Run("ssh-keygen", "-q", "-t", "ed25519", "-N", string.Empty, "-f", key);
        string publicKey = string.Join(' ', File.ReadAllText(key + ".pub")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
        string signers = Path.Combine(root, "allowed-signers");
        File.WriteAllText(signers, $"publication-approver namespaces=\"islamu-publication\" {publicKey}\n");

        string tag = fixture.FirstTagObject;
        string evidence = Digest(finalEvidence);
        string approval = $$"""{"schemaVersion":"publication-approval.v1","version":"{{version}}","tagObjectId":"{{tag}}","evidenceSha256":"{{evidence}}","disclosureAuthorized":true}""";
        string approvalPath = Path.Combine(approvals, version + ".json");
        File.WriteAllText(approvalPath, approval);
        var entry = new AuthorizedInventoryEntry(
            version, "v1.1", "2026-08-14", tag, fixture.B,
            $"docs/internal/releases/{version}", evidencePath, evidence,
            true, Digest(Encoding.UTF8.GetBytes(approval)), []);
        string inventoryPath = Path.Combine(root, "inventory.json");
        byte[] original = "{\"completeSet\":true,\"entries\":[\"1.2.0\"]}"u8.ToArray();
        File.WriteAllBytes(inventoryPath, original);
        Run("ssh-keygen", "-Y", "sign", "-f", key, "-n", "islamu-publication", inventoryPath);
        var authority = new SignedPublicationInventoryAuthority(
            inventoryPath, fixture.RepositoryPath, signers, fixture.RepositoryPath, Path.Combine(fixture.Root, "bundle"));

        await Assert.That(authority.VerifyCompleteInventory(Digest(original), [entry])).IsTrue();
        string finalPath = Path.Combine(fixture.RepositoryPath, evidencePath);
        File.WriteAllText(finalPath, Encoding.UTF8.GetString(finalEvidence)
            .Replace("\"signerRole\": \"release\"", "\"signerRole\": \"tooling-promotion\"", StringComparison.Ordinal));
        await Assert.That(authority.VerifyCompleteInventory(Digest(original), [entry])).IsFalse();
        File.WriteAllBytes(finalPath, finalEvidence);
        await Assert.That(authority.VerifyCompleteInventory(Digest(original), [entry with { DisclosureAuthorized = false }])).IsFalse();
        File.WriteAllText(approvalPath, approval.Replace("\"disclosureAuthorized\":true", "\"disclosureAuthorized\":false", StringComparison.Ordinal));
        await Assert.That(authority.VerifyCompleteInventory(Digest(original), [entry])).IsFalse();
        File.WriteAllText(approvalPath, approval);
        File.AppendAllText(inventoryPath, "mutation");
        await Assert.That(authority.VerifyCompleteInventory(Digest(File.ReadAllBytes(inventoryPath)), [entry])).IsFalse();
    }

    private static string Digest(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));

    private static void Run(string executable, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("publication_signature_fixture_timeout");
        }
        if (process.ExitCode != 0) throw new InvalidOperationException("publication_signature_fixture_failed");
    }
}
