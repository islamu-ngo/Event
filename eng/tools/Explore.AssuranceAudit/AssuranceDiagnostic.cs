namespace Explore.AssuranceAudit;

public sealed record AssuranceDiagnostic(
    string Category,
    string Path,
    int Line,
    int Column,
    string Message)
{
    public override string ToString() => $"{Path}({Line},{Column}): {Category}: {Message}";
}
