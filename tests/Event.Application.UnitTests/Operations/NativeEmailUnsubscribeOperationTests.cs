using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EmailUnsubscribe.Handlers.Commands;
using Explore.Application.Features.EmailUnsubscribe.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeEmailUnsubscribeOperationTests
{
    [Test]
    public async Task Request_HasExactlyOneNativeResultCommandContract()
    {
        var contracts = typeof(UnsubscribeFromEmailCategoryCommand).GetInterfaces();
        await Assert.That(contracts.Contains(typeof(ICommand<BaseCommandResponse<Guid>>))).IsTrue();
        await Assert.That(contracts.Any(type => type.Namespace == "MediatR")).IsFalse();
    }

    [Test]
    public async Task Discovery_RegistersTheExactClosedCommandPort()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(UnsubscribeFromEmailCategoryCommand),
            typeof(UnsubscribeFromEmailCategoryCommandHandler)
        ]);
        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)).ToArray();
        await Assert.That(ports.Length).IsEqualTo(1);
        await Assert.That(ports.Single().GetGenericArguments()).IsEquivalentTo(new[]
        {
            typeof(UnsubscribeFromEmailCategoryCommand), typeof(BaseCommandResponse<Guid>)
        });
    }
}
