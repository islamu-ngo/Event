using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EmailDispatch.Handlers.Commands;
using Explore.Application.Features.EmailDispatch.Handlers.Queries;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Features.EmailDispatch.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEmailDispatchOperationTests
{
    public static IEnumerable<(Type Request, Type Handler, Type Shape)> Operations()
    {
        yield return (typeof(SetEmailDispatchTenantPauseStateCommand), typeof(SetEmailDispatchTenantPauseStateCommandHandler), typeof(ICommand<>));
        yield return (typeof(ParkEmailDispatchCommand), typeof(ParkEmailDispatchCommandHandler), typeof(ICommand<>));
        yield return (typeof(ResolveEmailDispatchWithoutReplayCommand), typeof(ResolveEmailDispatchWithoutReplayCommandHandler), typeof(ICommand<>));
        yield return (typeof(ReconcileUnknownEmailDispatchCommand), typeof(ReconcileUnknownEmailDispatchCommandHandler), typeof(ICommand<>));
        yield return (typeof(DisableEmailDeliveryCommand), typeof(DisableEmailDeliveryCommandHandler), typeof(ICommand<>));
        yield return (typeof(PreviewEmailDeliveryDisableCommand), typeof(PreviewEmailDeliveryDisableCommandHandler), typeof(ICommand<>));
        yield return (typeof(GetEmailDispatchStatusQuery), typeof(GetEmailDispatchStatusQueryHandler), typeof(IQuery<>));
        yield return (typeof(GetEmailDispatchProcessorControlQuery), typeof(GetEmailDispatchProcessorControlQueryHandler), typeof(IQuery<>));
        yield return (typeof(ReplayEmailDispatchCommand), typeof(ReplayEmailDispatchCommandHandler), typeof(ICommand<>));
        yield return (typeof(SetEmailDispatchProcessorPauseStateCommand), typeof(SetEmailDispatchProcessorPauseStateCommandHandler), typeof(ICommand<>));
        yield return (typeof(SetEmailDispatchGlobalRateLimitOverrideCommand), typeof(SetEmailDispatchGlobalRateLimitOverrideCommandHandler), typeof(ICommand<>));
    }

    [Test]
    public async Task EntireFeatureRequestSurfaceHasExclusiveNativeContracts()
    {
        var requests = typeof(GetEmailDispatchStatusQuery).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract
                && type.Namespace?.StartsWith("Explore.Application.Features.EmailDispatch.Requests.", StringComparison.Ordinal) == true)
            .ToArray();
        await Assert.That(requests).IsNotEmpty();
        foreach (var request in requests)
        {
            await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(request)).IsFalse();
            await Assert.That(request.GetInterfaces().Count(contract => contract.IsGenericType
                && (contract.GetGenericTypeDefinition() == typeof(ICommand<>)
                    || contract.GetGenericTypeDefinition() == typeof(IQuery<>)))).IsEqualTo(1);
        }
    }

    [Test]
    [MethodDataSource(nameof(Operations))]
    public async Task DiscoveryRegistersExclusiveClosedScopedPort((Type Request, Type Handler, Type Shape) operation)
    {
        var services = new ServiceCollection();
        services.AddNativeOperations([operation.Request, operation.Handler]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Where(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)).ToArray();

        await Assert.That(ports.Length).IsEqualTo(1);
        await Assert.That(ports[0].Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(ports[0].ServiceType.GenericTypeArguments[0]).IsEqualTo(operation.Request);
        await Assert.That(operation.Request.GetInterfaces().Count(contract => contract.IsGenericType
            && contract.GetGenericTypeDefinition() == operation.Shape)).IsEqualTo(1);
        await Assert.That(typeof(MediatR.IBaseRequest).IsAssignableFrom(operation.Request)).IsFalse();
        await Assert.That(operation.Handler.GetInterfaces().Any(contract => contract.IsGenericType
            && contract.GetGenericTypeDefinition() == typeof(MediatR.IRequestHandler<,>))).IsFalse();
    }
}
