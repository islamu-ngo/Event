using System.Numerics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public static class ChangeIdPolicy
{
    public const string ValuePattern = "CHG-(?:[0-9]{4}-[0-9]{4}|[0-9A-HJKMNP-TV-Z]{26})";

    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static readonly Regex ValidPattern = new(
        $"^{ValuePattern}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex GeneratedPattern = new(
        "^CHG-[0-9A-HJKMNP-TV-Z]{26}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static bool IsValid(string? value) =>
        value is not null && ValidPattern.IsMatch(value);

    public static bool IsGenerated(string? value) =>
        value is not null && GeneratedPattern.IsMatch(value);

    public static string Create()
    {
        Span<byte> entropy = stackalloc byte[10];
        RandomNumberGenerator.Fill(entropy);
        return Create(DateTimeOffset.UtcNow, entropy);
    }

    public static string Create(DateTimeOffset timestamp, ReadOnlySpan<byte> entropy)
    {
        if (entropy.Length != 10)
        {
            throw new ArgumentException("Change-Id entropy must contain exactly 10 bytes.", nameof(entropy));
        }

        long milliseconds = timestamp.ToUnixTimeMilliseconds();
        if (milliseconds is < 0 or > 0x0000FFFFFFFFFFFF)
        {
            throw new ArgumentOutOfRangeException(nameof(timestamp));
        }

        Span<byte> bytes = stackalloc byte[16];
        bytes[0] = (byte)(milliseconds >> 40);
        bytes[1] = (byte)(milliseconds >> 32);
        bytes[2] = (byte)(milliseconds >> 24);
        bytes[3] = (byte)(milliseconds >> 16);
        bytes[4] = (byte)(milliseconds >> 8);
        bytes[5] = (byte)milliseconds;
        entropy.CopyTo(bytes[6..]);

        BigInteger value = new(bytes, isUnsigned: true, isBigEndian: true);
        Span<char> encoded = stackalloc char[26];
        for (int index = encoded.Length - 1; index >= 0; index--)
        {
            encoded[index] = Alphabet[(int)(value & 31)];
            value >>= 5;
        }

        return "CHG-" + new string(encoded);
    }
}
