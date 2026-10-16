using System.Text.RegularExpressions;

namespace Explore.Domain.CustomProperties;

/// <summary>
/// Bounds operator-defined patterns without a compiled per-definition cache.
/// Input length is checked before constructing or invoking a matcher.
/// </summary>
public static class CustomPropertyPatternPolicy
{
    public const int MaximumPatternLength = 2048;
    public static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    public static bool IsValid(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return true;
        }

        if (pattern.Length > MaximumPatternLength)
        {
            return false;
        }

        try
        {
            _ = new Regex(pattern, RegexOptions.NonBacktracking, MatchTimeout);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public static bool Matches(string value, string? pattern, int? minimumLength, int? maximumLength)
    {
        if ((minimumLength.HasValue && value.Length < minimumLength.Value) ||
            (maximumLength.HasValue && value.Length > maximumLength.Value))
        {
            return false;
        }

        if (string.IsNullOrEmpty(pattern))
        {
            return true;
        }

        if (pattern.Length > MaximumPatternLength)
        {
            return false;
        }

        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.NonBacktracking, MatchTimeout);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
