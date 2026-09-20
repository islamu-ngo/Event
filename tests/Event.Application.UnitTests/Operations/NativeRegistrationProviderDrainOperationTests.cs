using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Services.Registration.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeRegistrationProviderDrainOperationTests
{
    [Test]
    public async Task DrainRegistrationProviderSubmissionWriteEffectsCommand_ImplementsNativeCommandContract()
    {
        Type command = typeof(DrainRegistrationProviderSubmissionWriteEffectsCommand);
        Type handler = typeof(DrainRegistrationProviderSubmissionWriteEffectsCommandHandler);

        await Assert.That(typeof(ICommand<int>).IsAssignableFrom(command)).IsTrue();
        await Assert.That(typeof(ICommandHandler<DrainRegistrationProviderSubmissionWriteEffectsCommand, int>).IsAssignableFrom(handler)).IsTrue();
        await Assert.That(command.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
        await Assert.That(handler.GetInterfaces().Any(type => type.Namespace == "MediatR")).IsFalse();
    }

    [Test]
    public async Task DrainRegistrationProviderSubmissionWriteEffects_RegistersInScopedServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations([
            typeof(DrainRegistrationProviderSubmissionWriteEffectsCommand),
            typeof(DrainRegistrationProviderSubmissionWriteEffectsCommandHandler)
        ]);

        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(ICommandHandler<DrainRegistrationProviderSubmissionWriteEffectsCommand, int>));

        await Assert.That(descriptor).IsNotNull();
        await Assert.That(descriptor!.Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        await Assert.That(descriptor.ImplementationFactory).IsNotNull();
    }
}
