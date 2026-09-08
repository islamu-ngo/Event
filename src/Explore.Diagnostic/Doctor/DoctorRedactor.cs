using System.Text.RegularExpressions;

namespace Explore.Diagnostic.Doctor;

public static partial class DoctorRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var redacted = SensitiveKeyPattern().Replace(value, "$1=<redacted>");
        redacted = UriCredentialPattern().Replace(redacted, "$1<redacted>@");
        return redacted;
    }

    [GeneratedRegex(@"(?i)\b(password|pwd|secret|token|cookie|authorization|clientsecret|accesskey|secretaccesskey)\s*=\s*([^;\s]+)")]
    private static partial Regex SensitiveKeyPattern();

    [GeneratedRegex(@"(://)[^\s/@:]+:[^\s/@]+@")]
    private static partial Regex UriCredentialPattern();
}
