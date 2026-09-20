using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ActorSubscriptions.Handlers.Commands;
using Explore.Application.Features.ActorSubscriptions.Handlers.Queries;
using Explore.Application.Features.ActorSubscriptions.Requests.Commands;
using Explore.Application.Features.ActorSubscriptions.Requests.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeActorSubscriptionOperationTests
{
    [Test]
    public async Task SubscriptionCapability_RegistersThreeCommandsAndTwoQueries()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(SubscribeToActorCommand), typeof(SubscribeToActorCommandHandler),
            typeof(UnsubscribeFromActorCommand), typeof(UnsubscribeFromActorCommandHandler),
            typeof(UpdateActorSubscriptionNotificationLevelCommand), typeof(UpdateActorSubscriptionNotificationLevelCommandHandler),
            typeof(GetActorSubscriptionRequest), typeof(GetActorSubscriptionRequestHandler),
            typeof(GetActorSubscriptionsRequest), typeof(GetActorSubscriptionsRequestHandler)
        ]);

        var ports = services.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Select(descriptor => descriptor.ServiceType)
            .Where(type => type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
                || type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(5);
        await Assert.That(ports.Where(type => type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
            .Select(type => type.GetGenericArguments()[0])).IsEquivalentTo(new[]
            {
                typeof(SubscribeToActorCommand), typeof(UnsubscribeFromActorCommand),
                typeof(UpdateActorSubscriptionNotificationLevelCommand)
            });
        await Assert.That(ports.Where(type => type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))
            .Select(type => type.GetGenericArguments()[0])).IsEquivalentTo(new[]
            {
                typeof(GetActorSubscriptionRequest), typeof(GetActorSubscriptionsRequest)
            });
    }
}
