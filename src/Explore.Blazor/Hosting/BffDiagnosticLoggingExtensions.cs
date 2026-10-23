using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace Explore.Blazor.Hosting;

/// <summary>Configures the split BFF's native diagnostic boundary before export.</summary>
public static class BffDiagnosticLoggingExtensions
{
    public static LoggerConfiguration WithBffDiagnosticConfidentiality(this LoggerConfiguration configuration) =>
        configuration.Enrich.With(new BffDiagnosticLogEnricher())
            .Filter.ByExcluding(logEvent =>
                logEvent.Properties.TryGetValue("SourceContext", out var source)
                && source is ScalarValue { Value: string category }
                && IsFrameworkCategory(category));

    internal static bool IsFrameworkCategory(string? category) =>
        category?.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal) == true
        || category?.StartsWith("Microsoft.IdentityModel.", StringComparison.Ordinal) == true
        || category?.StartsWith("System.Net.Http.", StringComparison.Ordinal) == true
        || category?.StartsWith("Yarp.ReverseProxy.", StringComparison.Ordinal) == true
        || category?.StartsWith("Microsoft.Extensions.Hosting.Internal.Host", StringComparison.Ordinal) == true
        || category?.StartsWith("Microsoft.Hosting.Lifetime", StringComparison.Ordinal) == true;

    private sealed class BffDiagnosticLogEnricher : ILogEventEnricher
    {
        public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
        {
            var ownedFields = logEvent.MessageTemplate.Tokens.OfType<PropertyToken>()
                .Select(token => token.PropertyName).ToHashSet(StringComparer.Ordinal);
            foreach (string name in logEvent.Properties.Keys.ToArray())
            {
                if (name != "SourceContext" && !ownedFields.Contains(name))
                    logEvent.RemovePropertyIfPresent(name);
            }
        }
    }
}
