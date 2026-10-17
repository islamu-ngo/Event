using System.Runtime.CompilerServices;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Operations;
using Explore.Application.Operations.Decorators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

[assembly: InternalsVisibleTo("Event.Application.UnitTests")]
[assembly: InternalsVisibleTo("Event.Architecture.Tests")]
[assembly: InternalsVisibleTo("Event.API.IntegrationTests")]

namespace Explore.Application;

/// <summary>Registers the fixed native operation shapes, never a runtime request dispatcher.</summary>
public static class OperationServicesRegistration
{
    public static IServiceCollection AddNativeOperations(this IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(NativeOperationCatalog)))
            throw OperationCompositionValidation.Error("duplicate discovery", typeof(OperationServicesRegistration));
        return services.AddNativeOperations(typeof(OperationServicesRegistration).Assembly.GetTypes());
    }

    // The production entry scans Application once. Tests supply a small real graph without shipping
    // synthetic requests in Application or exposing a plugin registration API.
    internal static IServiceCollection AddNativeOperations(this IServiceCollection services, IEnumerable<Type> types)
    {
        var concreteTypes = types.Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false }).ToArray();
        var contractsByType = concreteTypes.ToDictionary(type => type, type => type.GetInterfaces());
        var requests = contractsByType.Where(pair => pair.Value.Any(IsRequestContract)).ToArray();
        var handlers = contractsByType.SelectMany(pair => pair.Value.Where(IsHandlerContract)
            .Select(contract => (Implementation: pair.Key, Contract: contract))).ToArray();
        var planned = new List<(Type Implementation, Type Contract)>();
        foreach (var request in requests)
        {
            var markers = request.Value.Where(IsRequestContract).ToArray();
            if (markers.Length != 1)
                throw OperationCompositionValidation.Error("mixed request shapes", request.Key);
            var marker = markers[0];
            var expected = marker == typeof(ICommand)
                ? typeof(ICommandHandler<>).MakeGenericType(request.Key)
                : (marker.GetGenericTypeDefinition() == typeof(ICommand<>)
                    ? typeof(ICommandHandler<,>) : typeof(IQueryHandler<,>))
                    .MakeGenericType(request.Key, marker.GetGenericArguments()[0]);
            var matches = handlers.Where(handler => handler.Contract == expected).ToArray();
            if (matches.Length != 1)
                throw OperationCompositionValidation.Error("missing or duplicate handler", expected);
            planned.Add(matches[0]);
        }
        if (planned.Count != handlers.Length)
            throw OperationCompositionValidation.Error("handler without discovered request", typeof(OperationServicesRegistration));

        var catalog = services.SingleOrDefault(descriptor => descriptor.ServiceType == typeof(NativeOperationCatalog))?.ImplementationInstance as NativeOperationCatalog;
        if (catalog is null)
        {
            catalog = new NativeOperationCatalog();
            services.AddSingleton(catalog);
        }
        services.TryAddScoped<OperationConstructionGuard>();
        services.TryAddScoped(typeof(RequestAuthorization<>));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IHumanCommitScopePreparation>(provider =>
            (IHumanCommitScopePreparation)provider.GetRequiredService<IPolicyMutationExecutionScopeFactory>());

        foreach (var group in planned.GroupBy(item => item.Implementation))
        {
            var implementation = group.Key;
            var parameters = implementation.GetConstructors().Select(constructor => constructor.GetParameters()).ToArray();
            var humanDependencies = parameters.SelectMany(constructor => constructor)
                .Select(parameter => parameter.ParameterType)
                .Where(type => type == typeof(IInstanceAdministratorCommitAuthority)
                    || type == typeof(ITenantAdministratorCommitAuthority)
                    || type == typeof(IHumanSubjectCommitAuthority)).Distinct().ToArray();
            bool requiresHumanPreparation = humanDependencies.Length != 0
                || parameters.SelectMany(constructor => constructor)
                    .Any(parameter => parameter.ParameterType == typeof(IHumanCommitScopePreparation));
            var existing = services.Where(descriptor => descriptor.ServiceType == implementation).ToArray();
            if (existing.Length > 1 || existing.Any(descriptor => descriptor.IsKeyedService
                || descriptor.Lifetime != ServiceLifetime.Scoped || descriptor.ImplementationType != implementation))
                throw OperationCompositionValidation.Error("competing concrete registration", implementation);
            ServiceDescriptor concrete;
            if (humanDependencies.Length != 0)
            {
                // Dependency inspection selects construction deferral only. Permissions remain
                // the actual handler's interface calls, never a reflected administrator policy.
                var construct = ActivatorUtilities.CreateFactory(implementation, humanDependencies);
                concrete = ServiceDescriptor.Scoped(implementation, provider =>
                {
                    if (provider.GetRequiredService<IHumanCommitScopePreparation>().Subject is not null)
                        return construct(provider, humanDependencies.Select(provider.GetRequiredService).ToArray());
                    var deferred = new DeferredHumanAdministratorCommitAuthority(provider);
                    return construct(provider, humanDependencies.Select(_ => (object)deferred).ToArray());
                });
                if (existing.Length != 0)
                    services.Remove(existing[0]);
                services.Add(concrete);
            }
            else
            {
                concrete = existing.SingleOrDefault() ?? ServiceDescriptor.Scoped(implementation, implementation);
                if (existing.Length == 0)
                    services.Add(concrete);
            }

            // Existing direct service ports use the same concrete scoped owner, just like explicit
            // factory aliases. Their authority disposition remains capability-owned.
            for (var index = 0; index < services.Count; index++)
            {
                var descriptor = services[index];
                if (descriptor.IsKeyedService || descriptor.ServiceType == implementation
                    || descriptor.ImplementationType != implementation || IsHandlerContract(descriptor.ServiceType))
                    continue;
                if (descriptor.Lifetime != ServiceLifetime.Scoped)
                    throw OperationCompositionValidation.Error("non-scoped service alias", descriptor.ServiceType);
                services[index] = ServiceDescriptor.Scoped(descriptor.ServiceType,
                    provider => provider.GetRequiredService(implementation));
            }
            foreach (var (_, contract) in group)
            {
                if (services.Any(descriptor => descriptor.ServiceType == contract))
                    throw OperationCompositionValidation.Error("competing handler registration", contract);
                var definition = contract.GetGenericTypeDefinition();
                var arguments = contract.GetGenericArguments();
                var authorization = definition == typeof(ICommandHandler<>) ? typeof(AuthorizationCommandHandlerDecorator<>)
                    : definition == typeof(ICommandHandler<,>) ? typeof(AuthorizationCommandHandlerDecorator<,>)
                    : typeof(AuthorizationQueryHandlerDecorator<,>);
                var performance = definition == typeof(ICommandHandler<>) ? typeof(PerformanceCommandHandlerDecorator<>)
                    : definition == typeof(ICommandHandler<,>) ? typeof(PerformanceCommandHandlerDecorator<,>)
                    : typeof(PerformanceQueryHandlerDecorator<,>);
                var authorizeFactory = ActivatorUtilities.CreateFactory(authorization.MakeGenericType(arguments), [contract]);
                var performanceFactory = ActivatorUtilities.CreateFactory(performance.MakeGenericType(arguments), [contract]);
                var mutationFactory = definition == typeof(ICommandHandler<,>) &&
                    (PolicyMutationCommandScope.Handles(arguments[0]) || ModerationMutationCommandScope.Handles(arguments[0])
                        || requiresHumanPreparation)
                    ? ActivatorUtilities.CreateFactory(typeof(MutationScopeCommandHandler<,>).MakeGenericType(arguments),
                        [typeof(IServiceProvider), typeof(Type)])
                    : null;
                var queryFactory = definition == typeof(IQueryHandler<,>) && requiresHumanPreparation
                    ? ActivatorUtilities.CreateFactory(typeof(HumanScopeQueryHandler<,>).MakeGenericType(arguments),
                        [typeof(IServiceProvider), typeof(Type)])
                    : null;
                var entry = ServiceDescriptor.Scoped(contract, provider =>
                {
                    var guard = provider.GetRequiredService<OperationConstructionGuard>();
                    guard.Enter(contract);
                    try
                    {
                        var inner = mutationFactory is not null
                            ? mutationFactory(provider, [provider, implementation])
                            : queryFactory is not null
                                ? queryFactory(provider, [provider, implementation])
                                : provider.GetRequiredService(implementation);
                        var timed = performanceFactory(provider, [inner]);
                        return authorizeFactory(provider, [timed]);
                    }
                    finally
                    {
                        guard.Exit(contract);
                    }
                });
                services.Add(entry);
                catalog.Registrations.Add(new NativeOperationRegistration(contract, implementation, entry, concrete, parameters));
            }
        }
        return services;
    }

    internal static bool IsHandlerContract(Type type) => type.IsGenericType
        && (type.GetGenericTypeDefinition() == typeof(ICommandHandler<>)
            || type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
            || type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>));

    private static bool IsRequestContract(Type type) => type == typeof(ICommand)
        || type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(ICommand<>)
            || type.GetGenericTypeDefinition() == typeof(IQuery<>));
}
