using System.Buffers.Binary;

namespace Explore.Application.Contracts.Admissions;

public sealed record AdmissionCheckInAuditCursor(DateTime OccurredAtUtc, Guid CheckInId)
{
    private const byte FormatVersion = 1;
    private const int PayloadLength = 25;
    private const int EncodedLength = 34;

    public string Encode()
    {
        if (OccurredAtUtc.Kind != DateTimeKind.Utc ||
            CheckInId == Guid.Empty ||
            CheckInId.Version != 7)
        {
            throw new InvalidOperationException("Admission audit cursor boundary is invalid.");
        }

        Span<byte> payload = stackalloc byte[PayloadLength];
        payload[0] = FormatVersion;
        BinaryPrimitives.WriteInt64BigEndian(payload[1..9], OccurredAtUtc.Ticks);
        CheckInId.TryWriteBytes(payload[9..]);
        return Convert.ToBase64String(payload)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(string? value, out AdmissionCheckInAuditCursor? cursor)
    {
        cursor = null;
        if (value is null)
        {
            return true;
        }
        if (value.Length != EncodedLength ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return false;
        }

        string base64 = value.Replace('-', '+').Replace('_', '/') + "==";
        Span<byte> payload = stackalloc byte[PayloadLength];
        if (!Convert.TryFromBase64String(base64, payload, out int bytesWritten) ||
            bytesWritten != PayloadLength ||
            payload[0] != FormatVersion)
        {
            return false;
        }

        long ticks = BinaryPrimitives.ReadInt64BigEndian(payload[1..9]);
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        var checkInId = new Guid(payload[9..]);
        if (checkInId == Guid.Empty || checkInId.Version != 7)
        {
            return false;
        }

        cursor = new AdmissionCheckInAuditCursor(new DateTime(ticks, DateTimeKind.Utc), checkInId);
        return string.Equals(cursor.Encode(), value, StringComparison.Ordinal);
    }
}
