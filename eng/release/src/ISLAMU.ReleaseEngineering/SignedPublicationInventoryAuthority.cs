using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Verifies an operator-approved complete inventory against a signer list inside the already
/// promoted release bundle. The publication adapter supplies retained inventory and approval
/// receipts; the publisher cannot choose or modify the trusted signer list.
/// </summary>
internal sealed class SignedPublicationInventoryAuthority(
    string inventoryPath, string retainedEvidenceRoot, string trustedAllowedSignersPath,
    string repositoryRoot, string trustedBundleRoot,
    Func<string, string, string, string, string, string, CancellationToken, bool>? releaseVerifier = null)
    : IFinalLaneInventoryAuthority
{
    private const string Principal = "publication-approver";
    private const string Namespace = "islamu-publication";

    public bool VerifyCompleteInventory(string inventorySha256, IReadOnlyList<AuthorizedInventoryEntry> entries, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(PublicationInventoryVerificationBudget.OverallTimeout);
        cancellationToken = deadline.Token;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string signaturePath = inventoryPath + ".sig";
            if (!IsRegularFile(inventoryPath) || !IsRegularFile(signaturePath) ||
                !IsRegularFile(trustedAllowedSignersPath) ||
                new FileInfo(inventoryPath).Length is <= 0 or > 1_048_576 ||
                new FileInfo(signaturePath).Length is <= 0 or > 16_384)
            {
                return false;
            }

            byte[] original = File.ReadAllBytesAsync(inventoryPath, cancellationToken).GetAwaiter().GetResult();
            if (Convert.ToHexStringLower(SHA256.HashData(original)) != inventorySha256)
            {
                return false;
            }

            foreach (AuthorizedInventoryEntry entry in entries)
            {
                using var entryDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                entryDeadline.CancelAfter(PublicationInventoryVerificationBudget.PerEntryTimeout);
                CancellationToken entryCancellationToken = entryDeadline.Token;
                entryCancellationToken.ThrowIfCancellationRequested();
                if (entry is null || !entry.DisclosureAuthorized ||
                    !IsSafeVersion(entry.Version) || string.IsNullOrEmpty(entry.EvidencePath) ||
                    Path.IsPathRooted(entry.EvidencePath) || entry.EvidencePath.Contains('\\', StringComparison.Ordinal) ||
                    entry.EvidencePath.Contains(':', StringComparison.Ordinal) ||
                    entry.EvidencePath.Split('/').Any(part => part is "" or "." or "..") ||
                    !IsRegularFile(Path.Combine(retainedEvidenceRoot, entry.EvidencePath)))
                {
                    return false;
                }

                string approvalPath = Path.Combine(retainedEvidenceRoot, "publication-approvals", entry.Version + ".json");
                if (!IsRegularFile(approvalPath) || new FileInfo(approvalPath).Length is <= 0 or > 16_384)
                {
                    return false;
                }

                byte[] approval = File.ReadAllBytesAsync(approvalPath, entryCancellationToken).GetAwaiter().GetResult();
                if (Convert.ToHexStringLower(SHA256.HashData(approval)) != entry.AuthorizationEvidenceSha256)
                {
                    return false;
                }

                using JsonDocument document = JsonDocument.Parse(approval);
                JsonElement root = document.RootElement;
                if (root.GetProperty("schemaVersion").GetString() != "publication-approval.v1" ||
                    root.GetProperty("version").GetString() != entry.Version ||
                    root.GetProperty("tagObjectId").GetString() != entry.TagObjectId ||
                    root.GetProperty("evidenceSha256").GetString() != entry.EvidenceSha256 ||
                    !root.GetProperty("disclosureAuthorized").GetBoolean())
                {
                    return false;
                }

                if (!(releaseVerifier ?? TagCommand.IsSignedFinalRelease)(
                    repositoryRoot, trustedBundleRoot, Path.Combine(retainedEvidenceRoot, entry.EvidencePath),
                    entry.TagObjectId, entry.Version, entry.TargetOid, entryCancellationToken))
                {
                    return false;
                }
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(ReleaseToolPaths.SshKeygen)
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            foreach (string argument in new[] {
                "-Y", "verify", "-f", trustedAllowedSignersPath, "-I", Principal,
                "-n", Namespace, "-s", signaturePath })
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            return VerifySignature(process, original, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return false;
        }
    }

    internal static bool VerifySignature(Process process, byte[] original, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cancellationToken = deadline.Token;
        cancellationToken.ThrowIfCancellationRequested();
        process.Start();
        Task stdout = Drain(process.StandardOutput);
        Task stderr = Drain(process.StandardError);
        Task stdin = WriteInput();
        try
        {
            Task.WhenAll(process.WaitForExitAsync(cancellationToken), stdin, stdout, stderr).GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            return process.ExitCode == 0;
        }
        finally
        {
            deadline.Cancel();
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            try { Task.WhenAll(stdin, stdout, stderr).GetAwaiter().GetResult(); }
            catch (Exception exception) when (exception is IOException or OperationCanceledException) { }
        }

        async Task WriteInput()
        {
            try
            {
                await process.StandardInput.BaseStream.WriteAsync(original, cancellationToken);
                process.StandardInput.Close();
            }
            catch { deadline.Cancel(); throw; }
        }

        async Task Drain(StreamReader reader)
        {
            try
            {
                var buffer = new char[4_096];
                int count = 0;
                int read;
                while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
                {
                    count += read;
                    if (count >= buffer.Length) throw new IOException("publication_signature_output_too_large");
                }
            }
            catch { deadline.Cancel(); throw; }
        }
    }

    private static bool IsSafeVersion(string version) =>
        version is { Length: > 0 and <= 64 } &&
        version.All(character => character is >= '0' and <= '9' or '.' or '-' or >= 'a' and <= 'z');

    private static bool IsRegularFile(string path)
    {
        if (!File.Exists(path)) return false;
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
        }
        return true;
    }
}
