using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;

namespace Explore.Infrastructure.Services.Moderation;

/// <summary>
/// Verifies only the authenticity of a closed, canonical version-1 ES256 measurement receipt.
/// The caller supplies the native observation, expected binding and enrollment snapshot.
/// No discovery, signing, receipt acceptance, honest-execution inference or current authority lookup occurs here.
/// </summary>
public sealed class ModerationMeasurementReceiptVerifier : IModerationMeasurementReceiptVerifier
{
    public const int MaximumTokenLength = 64 * 1024;
    public const string RequiredType = "islamu-moderation-conformance+jwt";
    private static readonly JsonSerializerOptions PayloadOptions = new() { MaxDepth = 8 };

    public ModerationMeasurementVerificationResult Verify(
        string? token, ModerationMeasurementTrustKey? trust, ModerationActivationBinding? expectedBinding,
        DateTimeOffset observedAt)
    {
        if (trust is null) return Reject(ModerationMeasurementVerificationFailure.UnknownKey);
        if (!FiniteUtc(observedAt) || !FiniteUtc(trust.ValidUntil) || trust.ValidUntil <= observedAt)
            return Reject(ModerationMeasurementVerificationFailure.InvalidWindow);
        if (!ValidTrust(trust)) return Reject(ModerationMeasurementVerificationFailure.InvalidTrustKey);
        if (expectedBinding is null) return Reject(ModerationMeasurementVerificationFailure.BindingMismatch);
        if (string.IsNullOrEmpty(token) || token.Length > MaximumTokenLength)
            return Reject(ModerationMeasurementVerificationFailure.InvalidToken);

        try
        {
            string[] parts = token.Split('.');
            if (parts.Length != 3 || parts[0].Length > 1024)
                return Reject(ModerationMeasurementVerificationFailure.InvalidToken);
            byte[] headerBytes = Decode(parts[0]);
            byte[] payloadBytes = Decode(parts[1]);
            byte[] signature = Decode(parts[2]);
            if (signature.Length != 64) return Reject(ModerationMeasurementVerificationFailure.InvalidSignature);

            using var header = JsonDocument.Parse(headerBytes, new JsonDocumentOptions { MaxDepth = 2 });
            if (!ValidHeader(header.RootElement, trust.KeyId)
                || !headerBytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(
                    new { alg = "ES256", typ = RequiredType, kid = trust.KeyId.ToString("D") })))
                return Reject(ModerationMeasurementVerificationFailure.InvalidToken);

            using var key = ECDsa.Create();
            try
            {
                key.ImportSubjectPublicKeyInfo(trust.SubjectPublicKeyInfo.AsSpan(), out int consumed);
                if (consumed != trust.SubjectPublicKeyInfo.Length || key.KeySize != 256
                    || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7"
                    || !key.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(trust.SubjectPublicKeyInfo.AsSpan()))
                    return Reject(ModerationMeasurementVerificationFailure.InvalidTrustKey);
            }
            catch (CryptographicException)
            {
                return Reject(ModerationMeasurementVerificationFailure.InvalidTrustKey);
            }

            try
            {
                byte[] signedBytes = Encoding.ASCII.GetBytes(token[..(parts[0].Length + 1 + parts[1].Length)]);
                if (!key.VerifyData(signedBytes, signature, HashAlgorithmName.SHA256,
                        DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                    return Reject(ModerationMeasurementVerificationFailure.InvalidSignature);
            }
            catch (CryptographicException)
            {
                return Reject(ModerationMeasurementVerificationFailure.InvalidSignature);
            }

            var receipt = JsonSerializer.Deserialize<ModerationMeasurementReceipt>(payloadBytes, PayloadOptions);
            // A single spelling, field order and scalar grammar prevent alternate wire identities.
            // Receipt replay/acceptance still belongs to the native owner, not token byte equality.
            if (receipt is null || !payloadBytes.AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(receipt)))
                return Reject(ModerationMeasurementVerificationFailure.InvalidToken);
            if (receipt.Issuer != trust.Issuer || receipt.HostBindingId != trust.HostBindingId
                || receipt.TrustDomainId != trust.TrustDomainId || receipt.TenantId != trust.TenantId)
                return Reject(ModerationMeasurementVerificationFailure.ScopeMismatch);
            if (receipt.Binding != expectedBinding
                || receipt.Binding.Jurisdiction.TrustDomainId != receipt.TrustDomainId
                || (receipt.Binding.Jurisdiction.Coordinates.TenantId is { } tenant && tenant != receipt.TenantId))
                return Reject(ModerationMeasurementVerificationFailure.BindingMismatch);
            if (receipt.RunnerDigest != trust.ApprovedRunnerDigest
                || receipt.CatalogueDigest != trust.ApprovedCatalogueDigest
                || receipt.TestBundleDigest != trust.ApprovedTestBundleDigest)
                return Reject(ModerationMeasurementVerificationFailure.ArtifactMismatch);
            if (receipt.StartedAt > receipt.CompletedAt || receipt.CompletedAt > receipt.IssuedAt
                || receipt.IssuedAt > observedAt || observedAt >= receipt.ValidUntil)
                return Reject(ModerationMeasurementVerificationFailure.InvalidWindow);
            return ModerationMeasurementVerificationResult.Known(receipt, trust.ValidUntil);
        }
        catch (JsonException)
        {
            return Reject(ModerationMeasurementVerificationFailure.InvalidToken);
        }
        catch (FormatException)
        {
            return Reject(ModerationMeasurementVerificationFailure.InvalidToken);
        }
    }

    private static ModerationMeasurementVerificationResult Reject(ModerationMeasurementVerificationFailure reason) =>
        ModerationMeasurementVerificationResult.Reject(reason);

    private static bool ValidHeader(JsonElement header, Guid keyId)
    {
        if (header.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in header.EnumerateObject())
        {
            if (!seen.Add(property.Name) || property.Value.ValueKind != JsonValueKind.String) return false;
            string? expected = property.Name switch
            {
                "alg" => "ES256",
                "typ" => RequiredType,
                "kid" => keyId.ToString("D"),
                _ => null
            };
            if (expected is null || property.Value.GetString() != expected) return false;
        }
        return seen.Count == 3;
    }

    private static byte[] Decode(string segment)
    {
        if (segment.Length == 0 || segment.Any(character =>
                character is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
            throw new FormatException();
        string padded = segment.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
        byte[] bytes = Convert.FromBase64String(padded);
        if (Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != segment)
            throw new FormatException();
        return bytes;
    }

    private static bool ValidTrust(ModerationMeasurementTrustKey trust) =>
        Uuid7(trust.KeyId) && Uuid7(trust.HostBindingId) && Uuid7(trust.TrustDomainId) && Uuid7(trust.TenantId)
        && trust.Issuer is { Length: > 0 and <= 256 }
        && trust.Issuer.All(character => character is >= '!' and <= '~')
        && !trust.SubjectPublicKeyInfo.IsDefaultOrEmpty && trust.SubjectPublicKeyInfo.Length <= 256
        && Digest(trust.ApprovedRunnerDigest) && Digest(trust.ApprovedCatalogueDigest) && Digest(trust.ApprovedTestBundleDigest);

    private static bool Uuid7(Guid value) => value.Version == 7 && (value.ToByteArray()[8] & 0xc0) == 0x80;

    private static bool Digest(string? value) => value is { Length: 64 }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool FiniteUtc(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
}
