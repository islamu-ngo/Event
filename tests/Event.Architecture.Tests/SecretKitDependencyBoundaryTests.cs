namespace Event.Architecture.Tests;

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
}
