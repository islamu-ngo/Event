using System.Diagnostics;

namespace ISLAMU.Wire.Contracts.Admissions;

[DebuggerDisplay("AdmissionCredentialBearer(<redacted>)")]
public sealed class AdmissionCredentialBearer
{
    public const int ByteLength = 32;
    public const int EncodedLength = 43;

    private AdmissionCredentialBearer(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static AdmissionCredentialBearer FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength)
        {
            throw new ArgumentException("Admission credential material must have the required length.", nameof(bytes));
        }

        return new AdmissionCredentialBearer(EncodeBase64Url(bytes));
    }

    public static bool TryCreate(string? candidate, out AdmissionCredentialBearer? bearer)
    {
        bearer = null;
        if (candidate is null || candidate.Length != EncodedLength)
        {
            return false;
        }

        foreach (char value in candidate)
        {
            if (value is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z') and not (>= '0' and <= '9')
                && value is not '-' and not '_')
            {
                return false;
            }
        }

        Span<char> padded = stackalloc char[44];
        candidate.AsSpan().CopyTo(padded);
        padded[43] = '=';
        for (int index = 0; index < candidate.Length; index++)
        {
            padded[index] = padded[index] switch
            {
                '-' => '+',
                '_' => '/',
                _ => padded[index]
            };
        }

        Span<byte> decoded = stackalloc byte[ByteLength + 1];
        if (!Convert.TryFromBase64Chars(padded, decoded, out int bytesWritten) || bytesWritten != ByteLength)
        {
            return false;
        }

        if (!string.Equals(EncodeBase64Url(decoded[..bytesWritten]), candidate, StringComparison.Ordinal))
        {
            return false;
        }

        bearer = new AdmissionCredentialBearer(candidate);
        return true;
    }

    public override string ToString() => "AdmissionCredentialBearer(<redacted>)";

    private static string EncodeBase64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

[DebuggerDisplay("AdmissionQrPayload(version=1, <redacted>)")]
public sealed class AdmissionQrPayload
{
    internal AdmissionQrPayload(AdmissionCredentialBearer bearer)
    {
        Bearer = bearer;
    }

    public AdmissionCredentialBearer Bearer { get; }

    public override string ToString() => "AdmissionQrPayload(version=1, <redacted>)";
}

public static class AdmissionQrPayloadCodec
{
    public const string Prefix = "islamu-admission:v1:";
    public const int PayloadLength = 63;

    public static string Encode(AdmissionCredentialBearer bearer)
    {
        ArgumentNullException.ThrowIfNull(bearer);
        return Prefix + bearer.Value;
    }

    public static bool TryDecode(string? candidate, out AdmissionQrPayload? payload)
    {
        payload = null;
        if (candidate is null || candidate.Length != PayloadLength ||
            !candidate.StartsWith(Prefix, StringComparison.Ordinal) ||
            !AdmissionCredentialBearer.TryCreate(candidate[Prefix.Length..], out AdmissionCredentialBearer? bearer))
        {
            return false;
        }

        payload = new AdmissionQrPayload(bearer!);
        return true;
    }
}
