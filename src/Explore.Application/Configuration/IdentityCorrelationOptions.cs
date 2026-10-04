using ISLAMU.Wire.Contracts.Identity;

namespace Explore.Application.Configuration;

public sealed class IdentityCorrelationOptions
{
    public const string SectionName = "IdentityCorrelation";

    public string[] TrustedIssuers { get; set; } = [];

    public static bool IsValid(IdentityCorrelationOptions options)
    {
        if (options.TrustedIssuers is null)
            return false;
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            return options.TrustedIssuers.All(issuer => !string.IsNullOrWhiteSpace(issuer)
                && !issuer.Contains('*', StringComparison.Ordinal)
                && normalized.Add(OidcIssuerAuthority.Normalize(issuer)));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
