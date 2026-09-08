using System.Xml.Linq;

namespace Event.Architecture.Tests;

public sealed class GeneratedClientBuildLifecycleArchitectureTests
{
    [Test]
    public async Task MissingGeneratedClientMustTraverseEntireGenerationPipeline()
    {
        XDocument project = XDocument.Load(ProjectPath());
        XElement source = Target(project, "GenerateApiClientSource");
        XElement fix = Target(project, "FixNSwagVoidReturns");
        XElement transform = Target(project, "TransformGeneratedApiClientRecords");
        XElement normalize = Target(project, "NormalizeGeneratedApiClient");
        XElement generate = Target(project, "GenerateApiClient");

        await Assert.That((string?)generate.Attribute("DependsOnTargets"))
            .IsEqualTo("NormalizeGeneratedApiClient");
        await Assert.That((string?)normalize.Attribute("DependsOnTargets"))
            .IsEqualTo("TransformGeneratedApiClientRecords");
        await Assert.That(normalize.Attribute("Condition")).IsNull();
        await Assert.That((string?)transform.Attribute("DependsOnTargets"))
            .IsEqualTo("FixNSwagVoidReturns");
        await Assert.That(transform.Attribute("Condition")).IsNull();
        await Assert.That((string?)fix.Attribute("DependsOnTargets"))
            .IsEqualTo("GenerateApiClientSource");

        XElement outputGuard = source.Elements("Error").Single();
        await Assert.That((string?)outputGuard.Attribute("Condition"))
            .IsEqualTo("!Exists('$(GeneratedApiClientFile)')");
    }

    private static XElement Target(XDocument project, string name) =>
        project.Descendants("Target").Single(element =>
            string.Equals(
                (string?)element.Attribute("Name"),
                name,
                StringComparison.Ordinal));

    private static string ProjectPath() => Path.Combine(
        RepositoryRoot(),
        "src",
        "Explore.Blazor.Client",
        "Explore.Blazor.Client.csproj");

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Repository root containing Explore.slnx was not found.");
    }
}
