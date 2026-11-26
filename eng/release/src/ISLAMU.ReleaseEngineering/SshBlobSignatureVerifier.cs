using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public sealed record SshBlobSignatureVerificationResult(
    bool IsValid,
    string? SignerPrincipal,
    string? Diagnostic);

/// <summary>
/// Verifies OpenSSH detached signatures (<c>ssh-keygen -Y verify</c>) over exact in-memory bytes
/// against an explicit <c>allowed-signers</c> trust root using a required signature namespace.
/// </summary>
public static class SshBlobSignatureVerifier
{
    public const string ReleaseChecksumsNamespace = "islamu-release-checksums";

    private const int MaximumOutputCharacters = 64 * 1024;
    private const long MaximumPayloadBytes = 16L * 1024 * 1024;
    private const long MaximumSignatureBytes = 64L * 1024;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromSeconds(30);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Regex NamespacePattern = new(
        "^[a-z0-9][a-z0-9._-]{0,63}\\z",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static class Diagnostics
    {
        public const string PayloadInvalid = "artifact_manifest_checksums_payload_invalid";
        public const string NamespaceInvalid = "artifact_manifest_checksums_namespace_invalid";
        public const string AllowedSignersMissing = "artifact_manifest_allowed_signers_missing";
        public const string AllowedSignersEmpty = "artifact_manifest_allowed_signers_empty";
        public const string SignatureMissing = "artifact_manifest_missing_file:" + ReleaseArtifactManifest.ChecksumsSignatureFileName;
        public const string SignatureInvalid = "artifact_manifest_checksums_signature_invalid";
        public const string SymlinkRejected = "artifact_manifest_symlink_rejected";
    }

    public static SshBlobSignatureVerificationResult Verify(
        byte[] payloadBytes,
        string signatureFilePath,
        string allowedSignersPath,
        string signatureNamespace,
        TimeSpan? timeout = null)
    {
        TimeSpan effectiveTimeout = timeout ?? DefaultTimeout;
        string? inputError = ValidateInputs(
            payloadBytes,
            signatureFilePath,
            allowedSignersPath,
            signatureNamespace,
            effectiveTimeout,
            out string resolvedSignaturePath,
            out string resolvedSignersPath);
        if (inputError is not null)
        {
            return Invalid(inputError);
        }

        if (!TryFindMatchingPrincipals(resolvedSignersPath, resolvedSignaturePath, effectiveTimeout, out string[] principals))
        {
            return Invalid(Diagnostics.SignatureInvalid);
        }

        string? verifiedPrincipal = Array.Find(
            principals,
            principal => VerifyForPrincipal(
                payloadBytes,
                resolvedSignersPath,
                resolvedSignaturePath,
                principal,
                signatureNamespace,
                effectiveTimeout));
        return verifiedPrincipal is not null
            ? new SshBlobSignatureVerificationResult(true, verifiedPrincipal, null)
            : Invalid(Diagnostics.SignatureInvalid);
    }

    private static string? ValidateInputs(
        byte[]? payloadBytes,
        string signatureFilePath,
        string allowedSignersPath,
        string signatureNamespace,
        TimeSpan timeout,
        out string resolvedSignaturePath,
        out string resolvedSignersPath)
    {
        resolvedSignaturePath = string.Empty;
        resolvedSignersPath = string.Empty;

        if (payloadBytes is null || payloadBytes.Length == 0 || payloadBytes.LongLength > MaximumPayloadBytes)
        {
            return Diagnostics.PayloadInvalid;
        }

        if (string.IsNullOrWhiteSpace(signatureNamespace) ||
            !NamespacePattern.IsMatch(signatureNamespace) ||
            timeout <= TimeSpan.Zero ||
            timeout > MaximumTimeout)
        {
            return Diagnostics.NamespaceInvalid;
        }

        string? signersError = ValidateAllowedSigners(allowedSignersPath, out resolvedSignersPath);
        if (signersError is not null)
        {
            return signersError;
        }

        return ValidateSignatureFile(signatureFilePath, out resolvedSignaturePath);
    }

    private static string? ValidateAllowedSigners(string allowedSignersPath, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(allowedSignersPath) || allowedSignersPath.Any(char.IsControl))
        {
            return Diagnostics.AllowedSignersMissing;
        }

        try
        {
            resolvedPath = Path.GetFullPath(allowedSignersPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Diagnostics.AllowedSignersMissing;
        }

        if (!File.Exists(resolvedPath))
        {
            return Diagnostics.AllowedSignersMissing;
        }

        if (HasReparsePointInExistingPath(resolvedPath))
        {
            return Diagnostics.SymlinkRejected;
        }

        try
        {
            string[] lines = File.ReadAllLines(resolvedPath, StrictUtf8);
            bool hasActiveEntry = lines.Any(line =>
            {
                string trimmed = line.Trim();
                return trimmed.Length > 0 && !trimmed.StartsWith('#');
            });
            return hasActiveEntry ? null : Diagnostics.AllowedSignersEmpty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return Diagnostics.AllowedSignersMissing;
        }
    }

    private static string? ValidateSignatureFile(string signatureFilePath, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(signatureFilePath) || signatureFilePath.Any(char.IsControl))
        {
            return Diagnostics.SignatureMissing;
        }

        try
        {
            resolvedPath = Path.GetFullPath(signatureFilePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Diagnostics.SignatureMissing;
        }

        if (!File.Exists(resolvedPath))
        {
            return Diagnostics.SignatureMissing;
        }

        if (HasReparsePointInExistingPath(resolvedPath))
        {
            return Diagnostics.SymlinkRejected;
        }

        try
        {
            var info = new FileInfo(resolvedPath);
            if (info.Length <= 0 || info.Length > MaximumSignatureBytes)
            {
                return Diagnostics.SignatureInvalid;
            }

            string text = StrictUtf8.GetString(File.ReadAllBytes(resolvedPath));
            return text.Contains("-----BEGIN SSH SIGNATURE-----", StringComparison.Ordinal) &&
                text.Contains("-----END SSH SIGNATURE-----", StringComparison.Ordinal)
                ? null
                : Diagnostics.SignatureInvalid;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return Diagnostics.SignatureInvalid;
        }
    }

    private static bool TryFindMatchingPrincipals(
        string allowedSignersPath,
        string signatureFilePath,
        TimeSpan timeout,
        out string[] principals)
    {
        principals = [];
        if (!TryRunSshKeygen(
            stdinPayload: null,
            timeout,
            out string stdout,
            out _,
            "-Y", "find-principals",
            "-f", allowedSignersPath,
            "-s", signatureFilePath))
        {
            return false;
        }

        principals = stdout
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return principals.Length > 0;
    }

    private static bool VerifyForPrincipal(
        byte[] payloadBytes,
        string allowedSignersPath,
        string signatureFilePath,
        string principal,
        string signatureNamespace,
        TimeSpan timeout)
    {
        if (!TryRunSshKeygen(
            payloadBytes,
            timeout,
            out string stdout,
            out string stderr,
            "-Y", "verify",
            "-f", allowedSignersPath,
            "-I", principal,
            "-n", signatureNamespace,
            "-s", signatureFilePath))
        {
            return false;
        }

        string combined = stdout + "\n" + stderr;
        string expectedPrefix = $"Good \"{signatureNamespace}\" signature for {principal}";
        return combined.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Any(line =>
                line.Equals(expectedPrefix, StringComparison.Ordinal) ||
                line.StartsWith(expectedPrefix + " ", StringComparison.Ordinal));
    }

    private static bool TryRunSshKeygen(
        byte[]? stdinPayload,
        TimeSpan timeout,
        out string stdout,
        out string stderr,
        params string[] arguments)
    {
        stdout = string.Empty;
        stderr = string.Empty;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(ReleaseToolPaths.SshKeygen)
            {
                UseShellExecute = false,
                RedirectStandardInput = stdinPayload is not null,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = StrictUtf8,
                StandardErrorEncoding = StrictUtf8,
            },
        };

        process.StartInfo.Environment["LC_ALL"] = "C";
        process.StartInfo.Environment["TZ"] = "UTC";
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
            using var timeoutSource = new CancellationTokenSource(timeout);
            Task<string> outTask = ReadBoundedAsync(process.StandardOutput, timeoutSource.Token);
            Task<string> errTask = ReadBoundedAsync(process.StandardError, timeoutSource.Token);

            if (stdinPayload is not null)
            {
                process.StandardInput.BaseStream.WriteAsync(stdinPayload, timeoutSource.Token).AsTask().GetAwaiter().GetResult();
                process.StandardInput.BaseStream.FlushAsync(timeoutSource.Token).GetAwaiter().GetResult();
                process.StandardInput.Close();
            }

            process.WaitForExitAsync(timeoutSource.Token).GetAwaiter().GetResult();
            stdout = outTask.GetAwaiter().GetResult();
            stderr = errTask.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[MaximumOutputCharacters];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(count, buffer.Length - count), cancellationToken);
            if (read == 0)
            {
                return new string(buffer, 0, count);
            }

            count += read;
        }

        if (await reader.ReadAsync(new char[1], cancellationToken) != 0)
        {
            throw new OperationCanceledException("ssh-keygen output exceeded maximum allowed length.");
        }

        return new string(buffer);
    }

    private static bool HasReparsePointInExistingPath(string fullPath)
    {
        string? current = fullPath;
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent;
        }

        return false;
    }

    private static SshBlobSignatureVerificationResult Invalid(string diagnostic) =>
        new(false, null, diagnostic);
}
