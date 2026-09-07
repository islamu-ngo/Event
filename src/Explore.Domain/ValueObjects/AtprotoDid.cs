using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Explore.Domain.ValueObjects;

public readonly partial struct AtprotoDid : IEquatable<AtprotoDid>, IComparable<AtprotoDid>
{
    private const int MaxLength = 2048;

    [GeneratedRegex(@"^did:[a-z]+:[a-zA-Z0-9._:%-]*[a-zA-Z0-9._%-]$", RegexOptions.CultureInvariant)]
    private static partial Regex DidPattern();

    public string Value { get; }

    public string Method { get; }

    private AtprotoDid(string value, string method)
    {
        Value = value;
        Method = method;
    }

    public static AtprotoDid Parse(string value)
    {
        if (!TryParse(value, out var did))
        {
            throw new ArgumentException("The AT Protocol DID is invalid.", nameof(value));
        }

        return did;
    }

    public static bool TryParse([NotNullWhen(true)] string? value, out AtprotoDid did)
    {
        did = default;

        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            return false;
        }

        if (value.Contains('?') || value.Contains('#') || value.Contains('%') || value.Contains(' ') || value.EndsWith(':'))
        {
            return false;
        }

        var parts = value.Split(':');
        if (parts.Length < 3 || parts[0] != "did")
        {
            return false;
        }

        var method = parts[1];
        if (string.IsNullOrEmpty(method) || method == "deleted" || !method.All(c => c >= 'a' && c <= 'z'))
        {
            return false;
        }

        if (!DidPattern().IsMatch(value))
        {
            return false;
        }

        did = new AtprotoDid(value, method);
        return true;
    }


    public static explicit operator AtprotoDid(string value) => Parse(value);

    public bool Equals(AtprotoDid other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is AtprotoDid other && Equals(other);

    public override int GetHashCode() => Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;

    public override string ToString() => "[AT Protocol DID]";

    public int CompareTo(AtprotoDid other) => string.Compare(Value, other.Value, StringComparison.Ordinal);

    public static bool operator ==(AtprotoDid left, AtprotoDid right) => left.Equals(right);

    public static bool operator !=(AtprotoDid left, AtprotoDid right) => !left.Equals(right);
}
