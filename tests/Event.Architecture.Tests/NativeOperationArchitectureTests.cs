using Explore.Application.Contracts.Operations;
using Explore.Application;
using Explore.Application.Operations;
using Explore.API.Hosting;
using Explore.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
                || type.Namespace?.StartsWith("Explore.Application.Authentication", StringComparison.Ordinal) == true)
            .Concat(typeof(ApiHostServiceCollectionExtensions).Assembly.GetTypes()
                .Where(type => type.Namespace?.StartsWith("Explore.API.Controllers", StringComparison.Ordinal) == true));
        var failures = businessConsumers
            .Concat(typeof(Explore.Infrastructure.InfrastructureServicesRegistration).Assembly.GetTypes())
            .SelectMany(type => type.GetConstructors().SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => handlers.Contains(parameter.ParameterType)
                    || ((type.Assembly == application || type.Assembly == typeof(ApiHostServiceCollectionExtensions).Assembly)
                        && parameter.ParameterType == typeof(IServiceProvider)))
                .Select(parameter => $"{type.FullName}: {parameter.ParameterType.FullName}"))
            .ToArray();

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task ActualHostProviderConstructsTheEntireNativePartitionWithoutExecutingOperations()
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
            ["Database:Database"] = "event_composition_test"
        });
        builder.AddApiHostServices(static () => false);
        // Testing omits runtime DbContext registration. Construction-only assurance needs the real
        // context type but deliberately no database provider, connection, schema or business calls.
        builder.Services.AddDbContextFactory<ExploreDbContext>();
        await using var app = builder.Build();
        await app.Services.ValidateNativeOperationsDeepAsync();
        var nativeContracts = typeof(ApplicationServicesRegistration).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
            .SelectMany(type => type.GetInterfaces().Where(OperationContractDiscovery.IsNativeHandler)).Distinct().ToArray();
        await using var scope = app.Services.CreateAsyncScope();
        foreach (var contract in nativeContracts)
        {
            var handler = scope.ServiceProvider.GetRequiredService(contract);
            await Assert.That(handler.GetType().Namespace).IsEqualTo("Explore.Application.Operations.Decorators");
        }
    }

    private sealed record SyntheticWrite : ICommand;
    private sealed record SyntheticResultWrite : ICommand<string>;
    private sealed record SyntheticPreviewQuery : IQuery<bool>;
}
