namespace ISLAMU.Event.Setup.Core;

using System.Security.Cryptography;

public readonly record struct ArtifactDigest
{
    private const int Sha256HexLength = 64;

    private ArtifactDigest(string value) => Value = value;

    public string Value { get; }

    public static ArtifactDigest Compute(ReadOnlySpan<byte> artifact) =>
        new(Convert.ToHexStringLower(SHA256.HashData(artifact)));

    public static ArtifactDigest Parse(string value)
    {
        if (!TryParse(value, out ArtifactDigest digest))
            throw new FormatException("The artifact digest is not valid SHA-256.");
        return digest;
    }

    public static bool TryParse(string? value, out ArtifactDigest digest)
    {
        digest = default;
        if (value is null || value.Length != Sha256HexLength)
            return false;

        if (value.Any(static character =>
                !(character is >= '0' and <= '9'
                    || character is >= 'a' and <= 'f'
                    || character is >= 'A' and <= 'F')))
            return false;

        digest = new ArtifactDigest(value.ToLowerInvariant());
        return true;
    }

    public override string ToString() => Value ?? string.Empty;
}
