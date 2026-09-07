using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Geocoding;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Locations;

public sealed class AddressGovernancePolicyResolverMutationTests
{
    [Test]
    public async Task ResolveAsync_WhenAlreadyCancelled_ThrowsBeforeReadingPolicy()
    {
        var settings = Substitute.For<IHierarchicalSettingsResolver>();
        var authorization = Substitute.For<IAuthorizationProvider>();
        var resolver = new AddressGovernancePolicyResolver(settings, authorization);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var request = new AddressGovernancePolicyRequest(
            Guid.Parse("019b0000-0030-7000-8000-000000000001"),
            Guid.Parse("019b0000-0030-7000-8000-000000000002"),
            Guid.Parse("019b0000-0030-7000-8000-000000000003"));

        await Assert.That(async () => await resolver.ResolveAsync(request, cancellation.Token))
            .Throws<OperationCanceledException>();
        await Assert.That(settings.ReceivedCalls()).IsEmpty();
        await Assert.That(authorization.ReceivedCalls()).IsEmpty();
    }
}
