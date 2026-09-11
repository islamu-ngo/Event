using Explore.Application.Contracts.Operations;

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
        var failures = application.GetTypes()
            .Where(type => type.Namespace?.StartsWith("Explore.Application.Features", StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetConstructors().SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => handlers.Contains(parameter.ParameterType)
                    || (handlers.Contains(type) && parameter.ParameterType == typeof(IServiceProvider)))
                .Select(parameter => $"{type.FullName}: {parameter.ParameterType.FullName}"))
            .ToArray();

        await Assert.That(failures).IsEmpty();
    }

    private sealed record SyntheticWrite : ICommand;
    private sealed record SyntheticResultWrite : ICommand<string>;
    private sealed record SyntheticPreviewQuery : IQuery<bool>;
}
