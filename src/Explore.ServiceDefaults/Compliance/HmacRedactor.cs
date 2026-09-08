using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Compliance.Redaction;

namespace Explore.ServiceDefaults.Compliance;

/// <summary>
/// A deterministic redactor that computes an HMAC-SHA256 hash using a server pepper key,
/// producing a fixed 64-character hex string for safe telemetry correlation.
/// </summary>
public sealed class HmacRedactor : Redactor
{
    private readonly byte[] _key;
    private const int HashByteLength = 32;
    private const int HexStringLength = 64;

    public HmacRedactor(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 16)
        {
            throw new ArgumentException("HMAC key must be at least 16 bytes for cryptographic security.", nameof(key));
        }

        _key = (byte[])key.Clone();
    }

    public HmacRedactor(string keyString)
        : this(Encoding.UTF8.GetBytes(keyString ?? throw new ArgumentNullException(nameof(keyString))))
    {
    }

    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        if (source.IsEmpty)
        {
            return 0;
        }

        if (destination.Length < HexStringLength)
        {
            throw new ArgumentException($"Destination span must be at least {HexStringLength} characters.", nameof(destination));
        }

        int maxByteCount = Encoding.UTF8.GetMaxByteCount(source.Length);
        byte[]? rentedBytes = null;
        Span<byte> utf8Span = maxByteCount <= 512
            ? stackalloc byte[maxByteCount]
            : (rentedBytes = ArrayPool<byte>.Shared.Rent(maxByteCount));

        try
        {
            int bytesWritten = Encoding.UTF8.GetBytes(source, utf8Span);
            ReadOnlySpan<byte> sourceBytes = utf8Span[..bytesWritten];

            Span<byte> hashBytes = stackalloc byte[HashByteLength];
            HMACSHA256.HashData(_key, sourceBytes, hashBytes);

            Convert.ToHexString(hashBytes).AsSpan().CopyTo(destination);
            return HexStringLength;
        }
        finally
        {
            if (rentedBytes != null)
            {
                ArrayPool<byte>.Shared.Return(rentedBytes);
            }
        }
    }

    public override int GetRedactedLength(ReadOnlySpan<char> input)
    {
        return input.IsEmpty ? 0 : HexStringLength;
    }
}
