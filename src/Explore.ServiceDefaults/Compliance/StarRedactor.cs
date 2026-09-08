using Microsoft.Extensions.Compliance.Redaction;

namespace Explore.ServiceDefaults.Compliance;

/// <summary>
/// A zero-allocation redactor that replaces sensitive input with a constant mask string (default: "****").
/// </summary>
public sealed class StarRedactor : Redactor
{
    private const string DefaultMask = "****";
    private readonly string _mask;

    public StarRedactor(string mask = DefaultMask)
    {
        _mask = string.IsNullOrEmpty(mask) ? DefaultMask : mask;
    }

    public static StarRedactor Instance { get; } = new(DefaultMask);

    public override int Redact(ReadOnlySpan<char> source, Span<char> destination)
    {
        if (source.IsEmpty)
        {
            return 0;
        }

        if (destination.Length < _mask.Length)
        {
            throw new ArgumentException("Destination span is too small to receive the redaction mask.", nameof(destination));
        }

        _mask.AsSpan().CopyTo(destination);
        return _mask.Length;
    }

    public override int GetRedactedLength(ReadOnlySpan<char> input)
    {
        return input.IsEmpty ? 0 : _mask.Length;
    }
}
