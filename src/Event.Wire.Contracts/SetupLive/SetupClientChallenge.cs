namespace ISLAMU.Wire.Contracts.SetupLive;

using System.Diagnostics;
using System.Text.Json.Serialization;

[JsonConverter(typeof(SetupClientChallengeJsonConverter))]
[DebuggerDisplay("SetupClientChallenge(<redacted>)")]
public sealed class SetupClientChallenge
{
    public const int ByteLength = 32;
    public const int EncodedLength = 43;

    private SetupClientChallenge(string encodedValue)
    {
        EncodedValue = encodedValue;
    }

    internal string EncodedValue { get; }

    public static SetupClientChallenge FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength)
        {
            throw new ArgumentException(
                "Setup client challenge material must have the required length.",
                nameof(bytes));
        }

        return new SetupClientChallenge(SetupLiveBase64Url32.Encode(bytes));
    }

    public static bool TryCreate(
        string? candidate,
        out SetupClientChallenge? challenge)
    {
        challenge = null;
        if (!SetupLiveBase64Url32.IsCanonical(candidate))
            return false;

        challenge = new SetupClientChallenge(candidate!);
        return true;
    }

    public string ToWireValue() => EncodedValue;

    public override string ToString() => "SetupClientChallenge(<redacted>)";
}

internal static class SetupLiveBase64Url32
{
    internal static bool IsCanonical(string? candidate)
    {
        if (candidate is null || candidate.Length != 43)
            return false;

        foreach (char value in candidate)
        {
            if (value is not (>= 'A' and <= 'Z')
                and not (>= 'a' and <= 'z')
                and not (>= '0' and <= '9')
                && value is not '-'
                && value is not '_')
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

        Span<byte> decoded = stackalloc byte[33];
        return Convert.TryFromBase64Chars(
                padded,
                decoded,
                out int bytesWritten)
            && bytesWritten == 32
            && string.Equals(
                Encode(decoded[..bytesWritten]),
                candidate,
                StringComparison.Ordinal);
    }

    internal static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
