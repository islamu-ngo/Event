namespace Event.Architecture.Tests;

using System.Reflection;
using NetArchTest.Rules;

/// <summary>
/// Architecture tests that enforce Clean Architecture dependency rules.
/// These tests ensure the codebase maintains proper layering and dependency direction.
/// </summary>
public class CleanArchitectureTests
{
    // Assembly references for architecture testing
    private static readonly Assembly DomainAssembly = typeof(Explore.Domain.Event).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Explore.Application.ApplicationServicesRegistration).Assembly;
    private static readonly Assembly PersistenceAssembly = typeof(Explore.Persistence.ExploreDbContext).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(Explore.Infrastructure.InfrastructureServicesRegistration).Assembly;

    // Namespace constants
    private const string DomainNamespace = "Explore.Domain";
    private const string ApplicationNamespace = "Explore.Application";
    private const string PersistenceNamespace = "Explore.Persistence";
    private const string InfrastructureNamespace = "Explore.Infrastructure";
    private const string ApiNamespace = "Explore.API";

    #region Domain Layer Tests

    [Test]
    public async Task Domain_ShouldNotHaveDependencyOn_SharedModerationApplication()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("ISLAMU.Moderation.Application")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task SharedModerationDomain_ShouldNotHaveDependencyOn_EventApplication()
    {
        var result = Types.InAssembly(typeof(ISLAMU.Moderation.Domain.ModerationDecision).Assembly)
            .ShouldNot()
            .HaveDependencyOn(ApplicationNamespace)
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task Domain_ShouldNotHaveDependencyOn_Application()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn(ApplicationNamespace)
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task Domain_ShouldNotHaveDependencyOn_Infrastructure()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task Domain_ShouldNotHaveDependencyOn_Persistence()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn(PersistenceNamespace)
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task Domain_ShouldNotHaveDependencyOn_Api()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task Domain_ShouldNotHaveDependencyOn_EntityFramework()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task Domain_ShouldNotHaveDependencyOn_AspNetCore()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    #endregion

    #region Application Layer Tests

    [Test]
    public async Task Application_ShouldNotHaveDependencyOn_Infrastructure()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn(InfrastructureNamespace)
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task Application_ShouldNotHaveDependencyOn_Persistence()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn(PersistenceNamespace)
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task Application_ShouldNotHaveDependencyOn_Api()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task Application_ShouldNotHaveDependencyOn_EntityFramework()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task Application_ShouldNotHaveDependencyOn_AspNetCore()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    #endregion

    #region Infrastructure Layer Tests

    [Test]
    public async Task Infrastructure_ShouldNotHaveDependencyOn_Api()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task Persistence_ShouldNotHaveDependencyOn_Api()
    {
        var result = Types.InAssembly(PersistenceAssembly)
            .ShouldNot()
            .HaveDependencyOn(ApiNamespace)
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task Persistence_ShouldNotHaveDependencyOn_ApplicationDtos()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var violations = Directory
            .EnumerateFiles(Path.Combine(repositoryRoot, "src", "Explore.Persistence"), "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("Explore.Application.DTOs", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(repositoryRoot, path))
            .ToList();

        await Assert.That(violations).IsEmpty()
            .Because("repositories must return entities and accept persistence-neutral query contracts, not DTOs owned by Application presentation mapping.");
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root containing Explore.slnx.");
    }

    [Test]
    public async Task GenericRepository_ShouldNotExpose_IrreversibleDeleteMethod()
    {
        var violations = new[]
            {
                typeof(Explore.Application.Contracts.Persistence.IGenericRepository<,>),
                typeof(Explore.Persistence.Repositories.GenericRepository<,>)
            }
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.Name == "HardDelete")
                .Select(method => $"{type.FullName}.{method.Name}"))
            .ToList();

        await Assert.That(violations).IsEmpty()
            .Because("irreversible deletes must not be globally exposed from the shared repository contract.");
    }

    #endregion
}
