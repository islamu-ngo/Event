using Explore.Application.Contracts.Operations;
using Explore.Application;
using Explore.Application.Operations;
using Explore.API.Hosting;
using Explore.Persistence;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Event.Architecture.Tests.Fixtures;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace Event.Architecture.Tests;

public sealed class NativeOperationArchitectureTests
{
    [Test]
    public async Task NewlyAddedNativeMutationsCannotDisappearFromAuthorizationCoverage()
    {
        var discovery = AuthorizationSurfaceInventory.DiscoverMutatingRequests(
            [typeof(SyntheticWrite), typeof(SyntheticResultWrite), typeof(SyntheticPreviewQuery)]);

        await Assert.That(discovery.UnprotectedMutatingRequests.Select(request => request.Id).Order().ToArray())
            .IsEquivalentTo(new[]
            {
                typeof(SyntheticWrite).FullName!, typeof(SyntheticResultWrite).FullName!,
                typeof(SyntheticPreviewQuery).FullName!
            });
    }

    [Test]
    public async Task NativeConsumersCannotInjectConcreteHandlersOrServiceProviders()
    {
        var application = typeof(Explore.Application.ApplicationServicesRegistration).Assembly;
        var handlers = application.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
            .Where(type => type.GetInterfaces().Any(OperationContractDiscovery.IsNativeHandler))
            .ToHashSet();
        var businessConsumers = application.GetTypes()
            .Where(type => type.Namespace?.StartsWith("Explore.Application.Features", StringComparison.Ordinal) == true
                || type.Namespace?.StartsWith("Explore.Application.Services", StringComparison.Ordinal) == true
                || type.Namespace?.StartsWith("Explore.Application.Settings", StringComparison.Ordinal) == true
                || type.Namespace?.StartsWith("Explore.Application.Authentication", StringComparison.Ordinal) == true
                || type.Namespace?.StartsWith("Explore.Application.Notifications", StringComparison.Ordinal) == true)
            .Concat(typeof(ApiHostServiceCollectionExtensions).Assembly.GetTypes()
                .Where(IsApiBusinessConsumer));
        var failures = FindForbiddenDependencies(businessConsumers
            .Concat(typeof(Explore.Infrastructure.InfrastructureServicesRegistration).Assembly.GetTypes()), handlers);

        await Assert.That(failures).IsEmpty();
    }

    private static bool IsApiBusinessConsumer(Type type) =>
        type.Namespace?.StartsWith("Explore.API.", StringComparison.Ordinal) == true
        && !type.Namespace.StartsWith("Explore.API.Hosting", StringComparison.Ordinal);

    private static string[] FindForbiddenDependencies(IEnumerable<Type> consumers, IReadOnlySet<Type> handlers)
    {
        var application = typeof(ApplicationServicesRegistration).Assembly;
        var failures = new HashSet<string>();
        bool IsConcreteDependency(Type dependency) =>
            handlers.Contains(dependency)
            || (dependency.IsGenericType
                && dependency.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                && IsConcreteDependency(dependency.GetGenericArguments()[0]));
        foreach (var type in consumers.Distinct())
        {
            // Worker scopes and framework authentication activation retain their existing providers.
            // Concrete handler injection/resolution is forbidden in every inspected consumer.
            var forbidProvider = type.Assembly == application
                || type.Namespace?.StartsWith("Explore.API.Controllers", StringComparison.Ordinal) == true
                || type.Namespace?.StartsWith("Explore.API.Services", StringComparison.Ordinal) == true;
            var parameters = type.GetConstructors().SelectMany(constructor => constructor.GetParameters())
                .Concat(type.GetMethods().SelectMany(method => method.GetParameters())
                    .Where(parameter => parameter.IsDefined(typeof(FromServicesAttribute))
                        || parameter.IsDefined(typeof(FromKeyedServicesAttribute))));
            foreach (var parameter in parameters)
            {
                if (IsConcreteDependency(parameter.ParameterType)
                    || (forbidProvider && parameter.ParameterType == typeof(IServiceProvider)))
                    failures.Add($"{type.FullName}.{parameter.Member.Name}: {parameter.ParameterType.FullName}");
            }
            foreach (var property in type.GetProperties().Where(property => property.IsDefined(typeof(FromServicesAttribute))))
            {
                if (IsConcreteDependency(property.PropertyType)
                    || (forbidProvider && property.PropertyType == typeof(IServiceProvider)))
                    failures.Add($"{type.FullName}.{property.Name}: {property.PropertyType.FullName}");
            }
            foreach (var body in ApiCompiledBoundaryTests.EnumerateImplementationBodies(type))
            {
                foreach (var called in ApiCompiledBoundaryTests.ResolveCalls(body))
                {
                    if (called is MethodInfo { IsGenericMethod: true }
                        && called.DeclaringType?.Namespace == "Microsoft.Extensions.DependencyInjection"
                        && called.Name is "GetService" or "GetRequiredService" or "GetServices"
                            or "GetKeyedService" or "GetRequiredKeyedService" or "GetKeyedServices"
                        && called.GetGenericArguments().Any(IsConcreteDependency))
                        failures.Add($"{type.FullName}.{body.Name}: concrete service resolution");
                }
            }
        }
        return failures.ToArray();
    }

    [Test]
    [Arguments(typeof(Explore.API.Controllers.NativeConsumerController))]
    [Arguments(typeof(Explore.API.Controllers.NativeConsumerPropertyController))]
    [Arguments(typeof(Explore.API.Services.NativeConsumerService))]
    [Arguments(typeof(Explore.API.Services.NativeScopedConsumerService))]
    [Arguments(typeof(Explore.API.Services.NativeCollectionConsumerService))]
    [Arguments(typeof(Explore.API.Services.NativePluralResolutionService))]
    [Arguments(typeof(Explore.API.Services.NativeEnumerableResolutionService))]
    public async Task CompiledGuardRejectsEveryConcreteConsumerEntry(Type consumer)
    {
        var failures = FindForbiddenDependencies(new[] { consumer }.Where(IsApiBusinessConsumer),
            new HashSet<Type> { typeof(NativeConsumerHandler) });

        await Assert.That(failures.Length).IsEqualTo(1);
    }

    [Test]
    public async Task CompiledGuardAllowsClosedProtectedServiceDependencies()
    {
        var failures = FindForbiddenDependencies(
            new[] { typeof(Explore.API.Services.ProtectedNativeConsumerService) }.Where(IsApiBusinessConsumer),
            new HashSet<Type> { typeof(NativeConsumerHandler) });

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task ActualHostProviderConstructsEveryPublicNativePipelineWithoutExecutingOperations()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = "Environment",
            ["Keycloak:Authority"] = "https://authority.example.test",
            ["Keycloak:Realm"] = "ISLAMU",
            ["Keycloak:Audience"] = "islamu-event-api",
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "localhost",
            ["Database:Database"] = "event_composition_test",
            ["Instance:OperatorIdentity:OperatorId"] = Guid.CreateVersion7().ToString(),
            ["Instance:OperatorIdentity:PublicName"] = "Composition Test Operator",
            ["Instance:OperatorIdentity:LegalName"] = "Composition Test Operator ASBL",
            ["Instance:OperatorIdentity:OperatorKindCode"] = "registered_organization",
            ["Instance:OperatorIdentity:JurisdictionCountryCode"] = "BE",
            ["Instance:OperatorIdentity:PublicContactEmail"] = "contact@instance.example.test",
            ["Instance:OperatorIdentity:OfficialOrigin"] = "https://instance.example.test",
            ["Instance:OperatorIdentity:WebsiteUrl"] = "https://instance.example.test",
            ["Instance:OperatorIdentity:LegalNoticeUrl"] = "https://instance.example.test/legal",
            ["Instance:OperatorIdentity:TermsUrl"] = "https://instance.example.test/terms",
            ["Instance:OperatorIdentity:PrivacyUrl"] = "https://instance.example.test/privacy"
        });
        builder.AddApiHostServices(static () => false);
        // This construction-only host exercises real cryptography without an external key authority.
        builder.Services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(
            new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        // Testing omits database composition. Retain the real context and Global factory aliases;
        // the credential-free SQLite Global pool is constructed but no connection or operation runs.
        builder.Services.AddDbContextFactory<ExploreDbContext>(options =>
            Microsoft.EntityFrameworkCore.SqliteDbContextOptionsBuilderExtensions.UseSqlite(options,
                $"Data Source={Path.Combine(Path.GetTempPath(), $"native-runtime-construction-{Guid.CreateVersion7():N}.db")}"));
        builder.Services.AddGlobalExploreDbContextFactory(new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Global,
            Provider = PrimaryDatabaseProvider.Sqlite,
            Database = Path.Combine(Path.GetTempPath(), $"native-partition-construction-{Guid.CreateVersion7():N}.db")
        });
        builder.Services.AddSingleton<Explore.Persistence.Database.ITenantExploreDbContextFactory>(
            new Explore.Persistence.Database.TenantExploreDbContextFactory(new PrimaryDatabaseConnectionOptions
            {
                Provider = PrimaryDatabaseProvider.Sqlite,
                Role = PrimaryDatabaseRole.Runtime,
                Database = Path.Combine(Path.GetTempPath(), $"native-tenant-construction-{Guid.CreateVersion7():N}.db")
            }));
        builder.Services.AddPolicyMutationExecutionScopes();
        builder.Services.AddCheckoutDispatchPersistence();
        await using var app = builder.Build();
        app.Services.ValidateNativeOperations();
        // Protected implementations require real selected requests; their exhaustive activation
        // is covered by NativePartitionConstructsEveryImplementationUnderItsOriginalHumanOwner.
        var nativeContracts = typeof(ApplicationServicesRegistration).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
            .SelectMany(type => type.GetInterfaces().Where(OperationContractDiscovery.IsNativeHandler)).Distinct().ToArray();
        await using var scope = app.Services.CreateAsyncScope();
        foreach (var contract in nativeContracts)
        {
            var handler = scope.ServiceProvider.GetRequiredService(contract);
            string[] expected = contract.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)
                ? ["Explore.Application.Operations.Decorators.AuthorizationQueryHandlerDecorator`2"]
                : contract.GetGenericTypeDefinition() == typeof(ICommandHandler<>)
                    ? ["Explore.Application.Operations.Decorators.AuthorizationCommandHandlerDecorator`1"]
                    : [
                        "Explore.Application.Operations.Decorators.AuthorizationCommandHandlerDecorator`2",
                        "Explore.Application.Operations.MutationScopeCommandHandler`2"
                    ];
            await Assert.That(expected.Contains(handler.GetType().GetGenericTypeDefinition().FullName!)).IsTrue();
            await Assert.That(handler.GetType().GetGenericArguments()
                .SequenceEqual(contract.GetGenericArguments())).IsTrue();
        }
    }

    private sealed record SyntheticWrite : ICommand;
    private sealed record SyntheticResultWrite : ICommand<string>;
    private sealed record SyntheticPreviewQuery : IQuery<bool>;
}
