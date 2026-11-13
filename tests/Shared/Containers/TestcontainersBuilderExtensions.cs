using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace Event.Testing;

/// <summary>
/// Applies bounded Docker memory limits, Keycloak JVM heap ceilings, and isolated reuse policies to Testcontainers builders.
/// </summary>
public static class TestcontainersBuilderExtensions
{
    private const string AssuranceRunIdEnvironmentVariable = "ISLAMU_TEST_RUN_ID";

    public static TBuilder ApplyBoundedResourcePolicy<TBuilder, TContainer, TConfiguration>(
        this ContainerBuilder<TBuilder, TContainer, TConfiguration> builder,
        ContainerServiceRole role,
        bool hasAssuranceOwner = false,
        bool hasExplicitContainerName = false,
        bool disableReuse = false,
        string? existingKeycloakJavaOpts = null)
        where TBuilder : ContainerBuilder<TBuilder, TContainer, TConfiguration>
        where TContainer : IContainer
        where TConfiguration : IContainerConfiguration
    {
        ArgumentNullException.ThrowIfNull(builder);

        bool effectiveAssuranceOwner = hasAssuranceOwner
            || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(AssuranceRunIdEnvironmentVariable));

        ContainerResourcePolicy policy = ContainerResourceBounds.Resolve(
            role,
            hasAssuranceOwner: effectiveAssuranceOwner,
            hasExplicitContainerName: hasExplicitContainerName);

        TBuilder configured = builder
            .WithCreateParameterModifier(parameters =>
            {
                parameters.HostConfig ??= new HostConfig();
                parameters.HostConfig.Memory = policy.MemoryLimitBytes;
            });

        if (policy.KeycloakHeapLimitMegabytes is int heapMegabytes)
        {
            configured = configured.WithEnvironment(
                "JAVA_OPTS_APPEND",
                ContainerResourceBounds.FormatKeycloakJavaOpts(existingKeycloakJavaOpts, heapMegabytes));
        }

        if (policy.EnableReuse && !disableReuse)
        {
            configured = configured.WithReuse(true);
        }

        return configured;
    }
}
