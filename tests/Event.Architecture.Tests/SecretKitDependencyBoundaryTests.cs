namespace Event.Architecture.Tests;

using System.Text.Json;

public sealed class SecretKitDependencyBoundaryTests
{
    [Test]
    [Arguments("Explore.Domain")]
    [Arguments("Explore.Application")]
    [Arguments("Event.Wire.Contracts")]
    [Arguments("Explore.Blazor.Client")]
    public async Task InnerAndBrowserAssembliesDoNotReferenceProviderSdk(string assemblyName)
    {
        var references = System.Reflection.Assembly.Load(assemblyName).GetReferencedAssemblies()
            .Where(reference => reference.Name?.StartsWith("SecretKit.", StringComparison.Ordinal) == true)
            .Select(reference => reference.Name);
        await Assert.That(references).IsEmpty();
    }

    [Test]
    [Arguments("Explore.Domain")]
    [Arguments("Explore.Application")]
    [Arguments("Event.Wire.Contracts")]
    [Arguments("Explore.Blazor.Client")]
    public async Task InnerAndBrowserPackageGraphsDoNotReferenceProviderSdk(string projectName)
    {
        string lockFile = ContextSystemHelpers.RepoPath("src", projectName, "packages.lock.json");
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllBytesAsync(lockFile));
        var providerPackages = document.RootElement.GetProperty("dependencies").EnumerateObject()
            .SelectMany(target => target.Value.EnumerateObject()
                .Where(package => package.Name.StartsWith("SecretKit.", StringComparison.OrdinalIgnoreCase))
                .Select(package => $"{target.Name}: {package.Name}"))
            .ToArray();

        await Assert.That(providerPackages).IsEmpty();
    }
}
