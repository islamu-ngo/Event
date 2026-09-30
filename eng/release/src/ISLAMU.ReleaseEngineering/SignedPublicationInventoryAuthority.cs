using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Verifies an operator-approved complete inventory against a signer list inside the already
/// promoted release bundle. The publication adapter supplies retained inventory and approval
/// receipts; the publisher cannot choose or modify the trusted signer list.
/// </summary>
internal sealed class SignedPublicationInventoryAuthority(
    string inventoryPath, string retainedEvidenceRoot, string trustedAllowedSignersPath,
    string repositoryRoot, string trustedBundleRoot)
    : IFinalLaneInventoryAuthority
{
    private const string Principal = "publication-approver";
    private const string Namespace = "islamu-publication";

    public bool VerifyCompleteInventory(string inventorySha256, IReadOnlyList<AuthorizedInventoryEntry> entries)
    {
        try
        {
            string signaturePath = inventoryPath + ".sig";
            if (!IsRegularFile(inventoryPath) || !IsRegularFile(signaturePath) ||
                !IsRegularFile(trustedAllowedSignersPath) ||
                new FileInfo(inventoryPath).Length is <= 0 or > 1_048_576 ||
                new FileInfo(signaturePath).Length is <= 0 or > 16_384)
            {
                return false;
            }

            byte[] original = File.ReadAllBytes(inventoryPath);
            if (Convert.ToHexStringLower(SHA256.HashData(original)) != inventorySha256)
            {
                return false;
            }

            foreach (AuthorizedInventoryEntry entry in entries)
            {
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

                byte[] approval = File.ReadAllBytes(approvalPath);
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
                    root.GetProperty("disclosureAuthorized").GetBoolean() != true)
                {
                    return false;
                }

                if (!TagCommand.IsSignedFinalRelease(
                    repositoryRoot, trustedBundleRoot, Path.Combine(retainedEvidenceRoot, entry.EvidencePath),
                    entry.TagObjectId, entry.Version, entry.TargetOid))
                {
                    return false;
                }
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("ssh-keygen")
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

            process.Start();
            process.StandardInput.BaseStream.Write(original);
            process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr).GetAwaiter().GetResult();
                return process.ExitCode == 0 &&
                    stdout.Result.Length < 4_096 && stderr.Result.Length < 4_096;
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException)
        {
            return false;
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
