using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Explore.Blazor.Client.Clients;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Blazor.Client.Extensions;

public static class GeneratedEventApiClients
{
    public static IReadOnlyList<(Type InterfaceType, Type ImplementationType)> ClientTypes { get; } = DiscoverClientTypes();

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026",
        Justification = "EventApiClients.TrimmerRoots.xml preserves every reflected generated client interface and implementation.")]
    private static IReadOnlyList<(Type InterfaceType, Type ImplementationType)> DiscoverClientTypes()
    {
        var clientTypes = typeof(PublicExperienceClient).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && type.Namespace == typeof(PublicExperienceClient).Namespace
                && IsNswagGenerated(type)
                && type.GetConstructor([typeof(HttpClient)]) is not null)
            .Select(implementationType =>
            {
                var interfaceType = implementationType.GetInterfaces().SingleOrDefault(candidate =>
                    candidate.Name == $"I{implementationType.Name}"
                    && IsNswagGenerated(candidate));
                return (InterfaceType: interfaceType, ImplementationType: implementationType);
            })
            .Where(pair => pair.InterfaceType is not null)
            .Select(pair => (pair.InterfaceType!, pair.ImplementationType))
            .OrderBy(pair => pair.Item1.FullName, StringComparer.Ordinal)
            .ToArray();

        if (clientTypes.Length == 0)
        {
            throw new InvalidOperationException("No NSwag per-tag API clients were discovered.");
        }

        return clientTypes;
    }

    public static IHttpClientBuilder AddTypedApiClient(
        this IServiceCollection services,
        Type interfaceType,
        Type implementationType,
        Action<HttpClient> configureClient)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(interfaceType);
        ArgumentNullException.ThrowIfNull(implementationType);
        ArgumentNullException.ThrowIfNull(configureClient);

        if (!interfaceType.IsInterface || !interfaceType.IsAssignableFrom(implementationType))
        {
            throw new ArgumentException(
                $"{implementationType.FullName} must implement {interfaceType.FullName}.",
                nameof(implementationType));
        }

        var clientName = interfaceType.Name;
        var builder = services.AddHttpClient(clientName, configureClient);
        services.AddTransient(interfaceType, provider =>
        {
            var httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
            return ActivatorUtilities.CreateInstance(provider, implementationType, httpClient);
        });
        return builder;
    }

    private static bool IsNswagGenerated(MemberInfo type) =>
        type.CustomAttributes.Any(attribute =>
            attribute.AttributeType == typeof(System.CodeDom.Compiler.GeneratedCodeAttribute)
            && attribute.ConstructorArguments.Count > 0
            && string.Equals(attribute.ConstructorArguments[0].Value as string, "NSwag", StringComparison.Ordinal));
}
