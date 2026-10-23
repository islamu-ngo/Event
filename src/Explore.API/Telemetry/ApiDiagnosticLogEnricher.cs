using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace Explore.API.Telemetry;

/// <summary>Excludes inherited request scopes from the API's bounded diagnostic events.</summary>
internal sealed class ApiDiagnosticLogEnricher : ILogEventEnricher
{
    internal const string JwtCategory = "Explore.API.Authentication.JwtDiagnostics";

    internal static bool IsFrameworkCategory(string? category) =>
        category?.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) == true
        || category?.StartsWith("Microsoft.IdentityModel.", StringComparison.Ordinal) == true
        || category?.StartsWith("System.Net.Http.", StringComparison.Ordinal) == true
        || category?.StartsWith("Grpc.Net.Client.", StringComparison.Ordinal) == true
        || category?.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) == true
        || category?.StartsWith("Microsoft.Extensions.Hosting.Internal.Host", StringComparison.Ordinal) == true
        || category?.StartsWith("Microsoft.Hosting.Lifetime", StringComparison.Ordinal) == true;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Names alone are not authority: a scope can reuse another producer's
        // safe field name. Retain only arguments bound by this native event.
        var ownedFields = logEvent.MessageTemplate.Tokens.OfType<PropertyToken>()
            .Select(token => token.PropertyName).ToHashSet(StringComparer.Ordinal);
        foreach (string name in logEvent.Properties.Keys.ToArray())
        {
            if (name != "SourceContext" && !ownedFields.Contains(name))
                logEvent.RemovePropertyIfPresent(name);
        }
    }
}
