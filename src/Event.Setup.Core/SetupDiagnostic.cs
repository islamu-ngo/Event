namespace ISLAMU.Event.Setup.Core;

public enum SetupDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record SetupDiagnosticCode
{
    public SetupDiagnosticCode(string value) => Value = SetupText.Identifier(value, nameof(value));

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record SetupDiagnosticPath
{
    public SetupDiagnosticPath(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string normalized = value.Trim();
        if (normalized.Length is < 1 or > 256
            || normalized[0] != '$'
            || normalized.Any(static character =>
                !(character is >= 'a' and <= 'z'
                    || character is >= '0' and <= '9'
                    || character is '$' or '.' or '[' or ']' or '-' or '_')))
        {
            throw new ArgumentException("Diagnostic path format is invalid.", nameof(value));
        }

        Value = normalized;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record SetupDiagnostic(
    SetupDiagnosticCode Code,
    SetupDiagnosticPath Path,
    SetupDiagnosticSeverity Severity);
