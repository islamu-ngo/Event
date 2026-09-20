using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.UiShell;
using Explore.Application.Features.UiShell.Handlers.Queries;
using Explore.Application.Features.UiShell.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeUiShellOperationTests
{
    [Test]
    public async Task RequestAndHandler_ExposeOnlyTheNativeQueryContract()
    {
        await Assert.That(typeof(IQuery<UiShellContextDto>).IsAssignableFrom(typeof(GetUiShellContextRequest))).IsTrue();
        await Assert.That(typeof(GetUiShellContextRequest).GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        var contracts = typeof(GetUiShellContextRequestHandler).GetInterfaces();
        await Assert.That(contracts.Length).IsEqualTo(1);
        await Assert.That(contracts[0].GetGenericTypeDefinition()).IsEqualTo(typeof(IQueryHandler<,>));
        await Assert.That(contracts[0].GenericTypeArguments.SequenceEqual(
            [typeof(GetUiShellContextRequest), typeof(UiShellContextDto)])).IsTrue();
    }

    [Test]
    public async Task Discovery_RegistersExactlyOneScopedQueryWithoutLegacyDispatch()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations([typeof(GetUiShellContextRequest), typeof(GetUiShellContextRequestHandler)]);
        var ports = services.Where(descriptor => OperationServicesRegistration.IsHandlerContract(descriptor.ServiceType)).ToArray();
        await Assert.That(ports.Length).IsEqualTo(1);
        await Assert.That(ports[0].Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(ports[0].ServiceType.GetGenericTypeDefinition()).IsEqualTo(typeof(IQueryHandler<,>));
        await Assert.That(ports[0].ServiceType.GenericTypeArguments.SequenceEqual(
            [typeof(GetUiShellContextRequest), typeof(UiShellContextDto)])).IsTrue();
    }
}
