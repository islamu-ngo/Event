using System.Reflection;
using Explore.Application.Contracts.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Operations;

/// <summary>
/// Separates final descriptor validation and bounded runtime parameter preflight from CI-only
/// actual-provider construction. None of these checks executes an operation.
/// </summary>
public static class OperationCompositionValidation
{
    public static void ValidateNativeOperationRegistrations(this IServiceCollection services)
    {
        var catalog = services.SingleOrDefault(descriptor => descriptor.ServiceType == typeof(NativeOperationCatalog))?.ImplementationInstance as NativeOperationCatalog
            ?? throw Error("missing discovery", typeof(OperationServicesRegistration));
        foreach (var descriptor in services)
        {
            if (!OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType))
                continue;
            if (!catalog.Registrations.Any(entry => ReferenceEquals(entry.PublicDescriptor, descriptor)))
                throw Error("unprotected handler registration", descriptor.ServiceType);
        }
        foreach (var entry in catalog.Registrations)
        {
            if (services.Count(descriptor => descriptor.ServiceType == entry.Contract) != 1
                || !services.Contains(entry.PublicDescriptor)
                || services.Count(descriptor => descriptor.ServiceType == entry.Implementation) != 1
                || !services.Contains(entry.ConcreteDescriptor))
                throw Error("missing or replaced registration", entry.Contract);

            if (!typeof(IDisposable).IsAssignableFrom(entry.Implementation)
                && !typeof(IAsyncDisposable).IsAssignableFrom(entry.Implementation))
                continue;

            foreach (var descriptor in services)
            {
                if (ReferenceEquals(descriptor, entry.ConcreteDescriptor)
                    || catalog.Registrations.Any(candidate => ReferenceEquals(candidate.PublicDescriptor, descriptor)))
                    continue;
                var implementation = descriptor.IsKeyedService
                    ? descriptor.KeyedImplementationType : descriptor.ImplementationType;
                var hasFactory = descriptor.IsKeyedService
                    ? descriptor.KeyedImplementationFactory is not null : descriptor.ImplementationFactory is not null;
                // DI captures disposable factory results independently for each descriptor.
                // Opaque assignable factories cannot prove that the scoped owner is not returned.
                if (implementation == entry.Implementation
                    || (hasFactory && descriptor.ServiceType.IsAssignableFrom(entry.Implementation)))
                    throw Error("disposable handler service alias", descriptor.ServiceType);
            }
        }
    }

    public static void ValidateNativeOperations(this IServiceProvider provider)
    {
        var catalog = provider.GetRequiredService<NativeOperationCatalog>();
        var available = provider.GetRequiredService<IServiceProviderIsService>();
        if (catalog.Registrations.Count != 0 && !available.IsService(typeof(IAuthorizationProvider)))
            throw Error("missing authorization provider", typeof(IAuthorizationProvider));
        foreach (var entry in catalog.Registrations)
        {
            var constructible = false;
            foreach (var constructor in entry.Constructors)
            {
                var satisfied = true;
                foreach (var parameter in constructor)
                {
                    if (!parameter.HasDefaultValue && !available.IsService(parameter.ParameterType))
                    {
                        satisfied = false;
                        break;
                    }
                }
                if (satisfied)
                {
                    constructible = true;
                    break;
                }
            }
            if (!constructible)
                throw Error("missing constructor dependency", entry.Contract);
        }
    }

    /// <summary>CI assurance against the final provider, including dependencies hidden by factories.</summary>
    public static async Task ValidateNativeOperationsDeepAsync(this IServiceProvider provider)
    {
        provider.ValidateNativeOperations();
        await using var scope = provider.CreateAsyncScope();
        foreach (var entry in provider.GetRequiredService<NativeOperationCatalog>().Registrations)
            _ = scope.ServiceProvider.GetRequiredService(entry.Contract);
    }

    internal static InvalidOperationException Error(string reason, Type type)
    {
        var name = type.Name;
        var request = type.IsGenericType ? type.GetGenericArguments()[0].Name : string.Empty;
        return new InvalidOperationException($"Native operation composition: {reason}: {name[..Math.Min(name.Length, 128)]} {request[..Math.Min(request.Length, 128)]}.");
    }
}

internal sealed class NativeOperationCatalog
{
    internal List<NativeOperationRegistration> Registrations { get; } = [];
}

internal sealed record NativeOperationRegistration(
    Type Contract,
    Type Implementation,
    ServiceDescriptor PublicDescriptor,
    ServiceDescriptor ConcreteDescriptor,
    ParameterInfo[][] Constructors);
