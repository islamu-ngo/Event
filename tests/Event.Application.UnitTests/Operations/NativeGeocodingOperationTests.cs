using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Geocoding;
using Explore.Application.Features.Geocoding.Handlers.Commands;
using Explore.Application.Features.Geocoding.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class NativeGeocodingOperationTests
{
    [Test]
    public async Task BoundedDiscovery_RegistersTokenIssuanceAndPromotionAsCommands()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations(
        [
            typeof(CreateAddressSuggestionsCommand), typeof(CreateAddressSuggestionsCommandHandler),
            typeof(PromoteLocationAddressCommand), typeof(PromoteLocationAddressCommandHandler)
        ]);
        var ports = services
            .Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType.IsGenericType)
            .Where(descriptor => descriptor.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
            .ToArray();

        await Assert.That(ports.Length).IsEqualTo(2);
        await Assert.That(ports.All(port => port.Lifetime == ServiceLifetime.Scoped)).IsTrue();
        await Assert.That(ports.Single(port =>
            port.ServiceType.GetGenericArguments()[0] == typeof(CreateAddressSuggestionsCommand))
            .ServiceType.GetGenericArguments()[1]).IsEqualTo(typeof(AddressSuggestionsResponseDto));
        await Assert.That(ports.Single(port =>
            port.ServiceType.GetGenericArguments()[0] == typeof(PromoteLocationAddressCommand))
            .ServiceType.GetGenericArguments()[1]).IsEqualTo(typeof(BaseCommandResponse<Guid>));
    }
}
