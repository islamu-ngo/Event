using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using NSubstitute;

namespace Event.Application.UnitTests.Features.InstanceOnboarding.Commands;

public class VerifyCerbosEndpointCommandHandlerTests
{
    private readonly IAuthorizationProviderConfigurationService _configurationService;
    private readonly VerifyCerbosEndpointCommandHandler _handler;

    public VerifyCerbosEndpointCommandHandlerTests()
    {
        _configurationService = Substitute.For<IAuthorizationProviderConfigurationService>();
        _handler = new VerifyCerbosEndpointCommandHandler(_configurationService);
    }

    [Test]
    public async Task Handle_WhenEndpointIsInvalid_ReturnsFailureWithoutVerification()
    {
        var result = await _handler.Handle(new VerifyCerbosEndpointCommand
        {
            GrpcEndpoint = "not a grpc endpoint"
        }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).Contains("must be a valid URL or host:port");
        await _configurationService.DidNotReceive().VerifyCerbosEndpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Handle_WhenEndpointIsReachable_NormalizesAndReturnsSuccess()
    {
        _configurationService.VerifyCerbosEndpointAsync("https://cerbosgrpc.example.com:443", Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await _handler.Handle(new VerifyCerbosEndpointCommand
        {
            GrpcEndpoint = "cerbosgrpc.example.com:443"
        }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await _configurationService.Received(1)
            .VerifyCerbosEndpointAsync("https://cerbosgrpc.example.com:443", Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments("https://user:password@cerbosgrpc.example.com:443")]
    [Arguments("https://cerbosgrpc.example.com:443/health")]
    [Arguments("https://cerbosgrpc.example.com:443?tenant=other")]
    [Arguments("https://cerbosgrpc.example.com:443#fragment")]
    public async Task Handle_WhenEndpointContainsUnsafeUriComponents_ReturnsFailure(string endpoint)
    {
        var result = await _handler.Handle(new VerifyCerbosEndpointCommand
        {
            GrpcEndpoint = endpoint
        }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await _configurationService.DidNotReceive()
            .VerifyCerbosEndpointAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Handle_WhenEndpointIsUnreachable_ReturnsFailure()
    {
        _configurationService.VerifyCerbosEndpointAsync("https://cerbosgrpc.example.com:443", Arg.Any<CancellationToken>())
            .Returns(false);

        var result = await _handler.Handle(new VerifyCerbosEndpointCommand
        {
            GrpcEndpoint = "https://cerbosgrpc.example.com:443"
        }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Message).Contains("could not be verified");
    }
}
