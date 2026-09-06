// ABOUTME: Validates complete location source text and derives invariant-uppercase NFC search text.
// ABOUTME: Rejects malformed or nonportable scalars before mutation without trimming or truncating accepted values.

using System.Buffers;
using System.Text;

namespace Explore.Domain.ValueObjects;

internal static class LocationTextNormalization
{
    internal const short CurrentRevision = 2;
    internal const int MaximumSourceLength = 500;
    internal const int MaximumKeyLength = 2_000;

    internal static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumSourceLength)
        {
            throw new ArgumentException("Location text must contain between 1 and 500 UTF-16 code units.", nameof(value));
        }

        ReadOnlySpan<char> remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out Rune rune, out int consumed) != OperationStatus.Done
                || rune.Value == 0
                || rune.Value is >= 0xFDD0 and <= 0xFDEF
                || (rune.Value & 0xFFFF) is 0xFFFE or 0xFFFF)
            {
                throw new ArgumentException("Location text contains an unsupported Unicode scalar.", nameof(value));
            }
            remaining = remaining[consumed..];
        }

        string normalized = value.Normalize(NormalizationForm.FormC)
            .ToUpperInvariant()
            .Normalize(NormalizationForm.FormC);
        if (normalized.Length > MaximumKeyLength)
        {
            throw new ArgumentException("Normalized location text exceeds 2000 UTF-16 code units.", nameof(value));
        }
        return normalized;
    }
}
