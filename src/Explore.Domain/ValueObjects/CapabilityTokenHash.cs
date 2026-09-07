namespace Explore.Domain.ValueObjects;

public sealed record CapabilityTokenHash
{
    public string Value { get; }

    private CapabilityTokenHash(string value)
    {
        Value = value;
    }

    public static CapabilityTokenHash Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Capability token hash must be a canonical SHA-256 hash.", nameof(value));
        }

        byte[] bytes;

        try
        {
            bytes = Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("Capability token hash must be a canonical SHA-256 hash.", nameof(value), exception);
        }

        if (bytes.Length != 32 || !string.Equals(Convert.ToBase64String(bytes), value, StringComparison.Ordinal))
        {
            throw new ArgumentException("Capability token hash must be a canonical SHA-256 hash.", nameof(value));
        }

        return new CapabilityTokenHash(value);
    }

    public override string ToString() => "CapabilityTokenHash(<redacted>)";
}
