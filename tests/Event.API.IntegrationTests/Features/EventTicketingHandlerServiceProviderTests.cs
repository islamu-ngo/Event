using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Features.EventTicketing.Handlers.Commands;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Phase43Ticketing)]
public sealed class EventTicketingHandlerServiceProviderTests
{
    [Test]
    public async Task ServiceProvider_ResolvesEveryEventTicketingRequestHandler()
    {
        await using var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider()
        };
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();

        Type[] handlerServices = typeof(PublishEventTicketCatalogCommandHandler).Assembly.DefinedTypes
            .Where(type => type.Namespace?.StartsWith(
                "Explore.Application.Features.EventTicketing.Handlers.",
                StringComparison.Ordinal) == true && !type.IsAbstract)
            .SelectMany(type => type.ImplementedInterfaces)
            .Where(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
            .Distinct()
            .ToArray();

        await Assert.That(handlerServices).IsNotEmpty();

        foreach (Type handlerService in handlerServices)
        {
            object handler = scope.ServiceProvider.GetRequiredService(handlerService);
            await Assert.That(handler).IsNotNull();
        }
    }
}
