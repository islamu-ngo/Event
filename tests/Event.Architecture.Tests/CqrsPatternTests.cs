namespace Event.Architecture.Tests;

using System.Reflection;
using NetArchTest.Rules;

/// <summary>
/// Tests that enforce CQRS (Command Query Responsibility Segregation) patterns.
/// Ensures proper separation between command and query operations.
/// </summary>
public class CqrsPatternTests
{
    private static readonly Assembly ApplicationAssembly = typeof(Explore.Application.ApplicationServicesRegistration).Assembly;

    #region Command Pattern Tests

    [Test]
    public async Task Commands_ShouldResideIn_CommandsNamespace()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameEndingWith("Command")
            .And()
            .AreClasses()
            .Should()
            .ResideInNamespaceContaining("Commands")
            .GetResult();

        await Assert.That(result.FailingTypeNames ?? []).IsEmpty();
    }

    [Test]
    public async Task CommandHandlers_ShouldResideIn_CommandsNamespace()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameEndingWith("CommandHandler")
            .And()
            .AreClasses()
            .Should()
            .ResideInNamespaceContaining("Commands")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    #endregion

    #region Query Pattern Tests

    [Test]
    public async Task Queries_ShouldResideIn_QueriesNamespace()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameEndingWith("Request")
            .And()
            .AreClasses()
            .And()
            .ResideInNamespaceContaining("Features")
            .And()
            .DoNotHaveNameEndingWith("CommandRequest")
            .And()
            .DoNotResideInNamespaceContaining("Requests.Commands")
            .And()
            .DoNotResideInNamespaceContaining("DTOs")
            .And()
            .DoNotResideInNamespaceContaining("Contracts")
            .And()
            .DoNotHaveName("AddressGovernancePolicyRequest")
            .And()
            .DoNotHaveName("ConfigurationManagedFieldRequest")
            .And()
            .DoNotHaveName("ConfigurationImportPreviewRequest")
            .And()
            .DoNotHaveName("ConfigurationImportApplyRequest")
            .Should()
            .ResideInNamespaceContaining("Queries")
            .GetResult();

        await Assert.That(result.FailingTypeNames ?? []).IsEmpty();
    }

    [Test]
    public async Task QueryHandlers_ShouldResideIn_HandlersQueriesNamespace()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameEndingWith("RequestHandler")
            .And()
            .AreClasses()
            .Should()
            .ResideInNamespaceContaining("Handlers.Queries")
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    [Test]
    public async Task ConfigurationManifestQueries_ShouldUseCanonicalQuerySuffixAndNamespace()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespaceContaining("Features.ConfigurationManifest")
            .And()
            .HaveNameEndingWith("Query")
            .And()
            .AreClasses()
            .Should()
            .ResideInNamespaceContaining("Requests.Queries")
            .GetResult();

        await Assert.That(result.FailingTypeNames ?? []).IsEmpty();
    }

    [Test]
    public async Task ConfigurationManifestQueryHandlers_ShouldUseCanonicalSuffixAndNamespace()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespaceContaining("Features.ConfigurationManifest")
            .And()
            .HaveNameEndingWith("QueryHandler")
            .And()
            .AreClasses()
            .Should()
            .ResideInNamespaceContaining("Handlers.Queries")
            .GetResult();

        await Assert.That(result.FailingTypeNames ?? []).IsEmpty();
    }

    #endregion

    #region Handler Implementation Tests

    [Test]
    public async Task Handlers_ShouldBePublic()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespaceContaining("Handlers")
            .And()
            .HaveNameEndingWith("Handler")
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .Should()
            .BePublic()
            .GetResult();

        await Assert.That(result.IsSuccessful).IsTrue();
    }

    #endregion
}
