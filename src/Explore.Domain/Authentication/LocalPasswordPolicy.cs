using System.Buffers;
using System.Text;

namespace Explore.Domain.Authentication;

/// <summary>
/// Local credentials use NFC, preserving whitespace and case. Bounds count normalized
/// Unicode scalar values; malformed UTF-16 is rejected rather than repaired.
/// </summary>
public static class LocalPasswordPolicy
{
    public const int MinimumScalars = 15;
    public const int MaximumScalars = 128;
    public const int MaximumInputCodeUnits = 1024;

    public static bool TryNormalizeNewCredential(string? password, out string normalized) =>
        TryNormalize(password, MinimumScalars, out normalized);

    public static bool TryNormalizeForVerification(string? password, out string normalized) =>
        TryNormalize(password, 1, out normalized);

    private static bool TryNormalize(string? password, int minimum, out string normalized)
    {
        normalized = string.Empty;
        if (password is null || password.Length is 0 or > MaximumInputCodeUnits)
            return false;
        ReadOnlySpan<char> remaining = password.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out int consumed) != OperationStatus.Done)
                return false;
            remaining = remaining[consumed..];
        }
        string candidate = password.Normalize(NormalizationForm.FormC);
        int count = 0;
        foreach (Rune _ in candidate.EnumerateRunes())
        {
            if (++count > MaximumScalars)
                return false;
        }
        if (count < minimum)
            return false;
        normalized = candidate;
        return true;
    }
}
