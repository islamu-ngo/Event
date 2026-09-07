namespace Explore.Domain;

public sealed class RegistrationAttemptStatus
{
    public int Id { get; set; }
    public string MasterCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed class RegistrationSubmissionStatus
{
    public int Id { get; set; }
    public string MasterCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public sealed record RegistrationEvidenceHash
{
    public string Value { get; }

    private RegistrationEvidenceHash(string value)
    {
        Value = value;
    }

    public static RegistrationEvidenceHash Create(string value) => new(RegistrationSha256Hash.Normalize(value, nameof(value), "Registration evidence hash"));

    public override string ToString() => "RegistrationEvidenceHash(<redacted>)";
}

public sealed record RegistrationTransportIdempotencyHash
{
    public string Value { get; }

    private RegistrationTransportIdempotencyHash(string value)
    {
        Value = value;
    }

    public static RegistrationTransportIdempotencyHash Create(string value) => new(RegistrationSha256Hash.Normalize(value, nameof(value), "Registration transport idempotency hash"));

    public override string ToString() => "RegistrationTransportIdempotencyHash(<redacted>)";
}

internal static class RegistrationSha256Hash
{
    internal static string Normalize(string value, string parameterName, string displayName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{displayName} must be a canonical SHA-256 hash.", parameterName);
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"{displayName} must be a canonical SHA-256 hash.", parameterName, exception);
        }

        if (bytes.Length != 32 || !string.Equals(Convert.ToBase64String(bytes), value, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{displayName} must be a canonical SHA-256 hash.", parameterName);
        }

        return value;
    }
}
