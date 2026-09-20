using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EmailDispatch.Handlers.Commands;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEmailDeliveryDisableOperationTests
{
    [Test]
    public async Task ConfirmationIssuanceAndDisable_RegisterAsClosedNativeCommands()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(PreviewEmailDeliveryDisableCommand), typeof(PreviewEmailDeliveryDisableCommandHandler),
            typeof(DisableEmailDeliveryCommand), typeof(DisableEmailDeliveryCommandHandler)
        ]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(2);
        await Assert.That(ports.All(type => type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))).IsTrue();
        await Assert.That(ports.Select(type => type.GetGenericArguments()[0]))
            .IsEquivalentTo(new[] { typeof(PreviewEmailDeliveryDisableCommand), typeof(DisableEmailDeliveryCommand) });
    }
}
