namespace Event.Architecture.Tests;

using System.Reflection;
using System.Runtime.CompilerServices;
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
    public async Task Persistence_DeclaredStorageContracts_ShouldNotExpose_ApplicationDtos()
    {
        const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var violations = PersistenceAssembly.GetTypes()
            .Where(type => !IsCompilerGenerated(type))
            .SelectMany(type => type.GetMembers(declared)
                .SelectMany(member => DeclaredContractTypes(member)
                    .Where(ContainsApplicationDto)
                    .Select(contract => $"{type.FullName}.{member.Name}: {contract}")))
            .Concat(ApplicationAssembly.GetTypes()
                .Where(type => type.IsInterface && type.Namespace == "Explore.Application.Contracts.Persistence")
                .SelectMany(type => type.GetMethods()
                    .Where(method => ContainsApplicationDto(method.ReturnType))
                    .Select(method => $"{type.FullName}.{method.Name}: {method.ReturnType}")))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(violations).IsEmpty()
            .Because("declared storage contracts use entities or persistence-neutral values; Application maps presentation DTO outputs. Immutable original command inputs retain their separate custody contracts.");
    }

    private static IEnumerable<Type> DeclaredContractTypes(MemberInfo member) => member switch
    {
        FieldInfo field => [field.FieldType],
        PropertyInfo property => [property.PropertyType, .. property.GetIndexParameters().Select(parameter => parameter.ParameterType)],
        MethodInfo method => [method.ReturnType, .. method.GetParameters().Select(parameter => parameter.ParameterType)],
        ConstructorInfo constructor => constructor.GetParameters().Select(parameter => parameter.ParameterType),
        _ => []
    };

    private static bool ContainsApplicationDto(Type type) =>
        type.Namespace?.StartsWith("Explore.Application.DTOs.", StringComparison.Ordinal) == true
        || type.Namespace == "Explore.Application.DTOs"
        || type.HasElementType && ContainsApplicationDto(type.GetElementType()!)
        || type.IsGenericType && type.GetGenericArguments().Any(ContainsApplicationDto);

    private static bool IsCompilerGenerated(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
        || type.DeclaringType is { } declaringType && IsCompilerGenerated(declaringType);

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
