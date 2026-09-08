using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Atproto.Handlers.Commands;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Domain.ValueObjects;
using FluentValidation;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Authentication.Atproto;

public sealed class RevokeAtprotoSessionCommandHandlerTests
{
    private static readonly AtprotoCurrentSessionIdentity Identity = new(
        Guid.Parse("018e4e5c-7f00-7000-8000-000000000001"),
        Guid.Parse("018e4e5c-7f00-7000-8000-000000000002"),
        AtprotoDid.Parse("did:plc:revoke-session"));

    [Test]
    [Arguments(AtprotoSessionRevocationOutcome.Revoked)]
    [Arguments(AtprotoSessionRevocationOutcome.AlreadyAbsent)]
    [Arguments(AtprotoSessionRevocationOutcome.RemoteFailedLocalCleared)]
    public async Task ReturnsOnlyTheBoundedGatewayOutcome(AtprotoSessionRevocationOutcome outcome)
    {
        var gateway = Substitute.For<IAtprotoOAuthSecurityGateway>();
        gateway.RevokeCurrentAsync(Identity, Arg.Any<CancellationToken>())
            .Returns(new AtprotoSessionRevocationResult(outcome));
        var handler = new RevokeAtprotoSessionCommandHandler(gateway);

        var result = await handler.Handle(
            new RevokeAtprotoSessionCommand(Identity),
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(outcome);
        await gateway.Received(1).RevokeCurrentAsync(Identity, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RejectsInvalidAuthenticatedScopeBeforeGatewayAccess()
    {
        var gateway = Substitute.For<IAtprotoOAuthSecurityGateway>();
        var invalid = Identity with { UserId = Guid.Empty };

        await Assert.ThrowsAsync<ValidationException>(() =>
            new RevokeAtprotoSessionCommandHandler(gateway).Handle(
                new RevokeAtprotoSessionCommand(invalid),
                CancellationToken.None));

        await gateway.DidNotReceiveWithAnyArgs().RevokeCurrentAsync(default!, default);
    }
}
