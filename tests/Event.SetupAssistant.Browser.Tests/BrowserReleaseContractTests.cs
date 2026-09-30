namespace Event.SetupAssistant.Browser.Tests;

using System.Reflection;
using ISLAMU.Event.SetupAssistant.Browser;

public sealed class BrowserReleaseContractTests
{
    [Test]
    public async Task EnabledBrowserRuntimeExcludesConnectedAndTelemetryProducts()
    {
        Assembly assembly = typeof(BrowserPublicManifest).Assembly;
        string[] forbidden =
        [
            "Event.SetupAssistant.SetupLive",
            "ApplicationInsights",
            "OpenTelemetry",
            "Yarp",
            "Authentication"
        ];
        string[] references = assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        await Assert.That(references.Any(reference => forbidden.Any(term =>
            reference.Contains(term, StringComparison.OrdinalIgnoreCase)))).IsFalse();
    }

    [Test]
    public async Task BrowserPublicSurfaceExposesNoConnectionOrStorageContract()
    {
        string[] forbiddenTerms =
        [
            "HttpClient", "Token", "Login", "Identity", "Storage",
            "Telemetry", "ServiceWorker", "InstanceUrl", "ApiKey"
        ];
        Type[] publicTypes = typeof(BrowserPublicManifest).Assembly.GetExportedTypes();
        string[] publicMembers = publicTypes
            .SelectMany(type => type.GetMembers(BindingFlags.Public
                | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(member => $"{member.DeclaringType?.FullName}.{member.Name}")
            .ToArray();

        await Assert.That(publicMembers.Any(member => forbiddenTerms.Any(term =>
            member.Contains(term, StringComparison.OrdinalIgnoreCase)))).IsFalse();
    }
}
