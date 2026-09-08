namespace Explore.Diagnostic.Doctor;

public sealed record DoctorCheckResult(
    string Code,
    DoctorCheckCategory Category,
    DoctorCheckStatus Status,
    string Summary,
    string Remediation,
    string DocsLink,
    string? RedactedEvidence = null)
{
    public static DoctorCheckResult Pass(
        string code,
        DoctorCheckCategory category,
        string summary,
        string remediation,
        string docsLink,
        string? redactedEvidence = null) =>
        new(code, category, DoctorCheckStatus.Pass, summary, remediation, docsLink, redactedEvidence);

    public static DoctorCheckResult Warn(
        string code,
        DoctorCheckCategory category,
        string summary,
        string remediation,
        string docsLink,
        string? redactedEvidence = null) =>
        new(code, category, DoctorCheckStatus.Warn, summary, remediation, docsLink, redactedEvidence);

    public static DoctorCheckResult Fail(
        string code,
        DoctorCheckCategory category,
        string summary,
        string remediation,
        string docsLink,
        string? redactedEvidence = null) =>
        new(code, category, DoctorCheckStatus.Fail, summary, remediation, docsLink, redactedEvidence);
}
