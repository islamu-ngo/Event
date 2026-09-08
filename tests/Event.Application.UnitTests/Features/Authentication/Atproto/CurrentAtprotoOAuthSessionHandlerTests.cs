using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Atproto.Handlers.Queries;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Queries;
using Explore.Domain.ValueObjects;
using FluentValidation;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Authentication.Atproto;

public sealed class CurrentAtprotoOAuthSessionHandlerTests
{
    private static readonly AtprotoCurrentSessionIdentity Identity = new(
        Guid.Parse("018e4e5c-7f00-7000-8000-000000000001"),
        Guid.Parse("018e4e5c-7f00-7000-8000-000000000002"),
        AtprotoDid.Parse("did:plc:current-session"));

    [Test]
    public async Task GetReturnsOnlyTheExactlyScopedGatewaySession()
    {
        var gateway = Substitute.For<IAtprotoOAuthSecurityGateway>();
        var expected = new AtprotoCurrentOAuthSession(
            Identity.Did,
            new Uri("https://pds.example/"),
            "oauth-active",
            new byte[] { 1, 2, 3 });
        gateway.GetCurrentAsync(Identity, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await new GetCurrentAtprotoOAuthSessionQueryHandler(gateway)
            .Handle(new GetCurrentAtprotoOAuthSessionQuery(Identity), CancellationToken.None);

        await Assert.That(result).IsEqualTo(expected);
        await gateway.Received(1).GetCurrentAsync(Identity, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InvalidIdentityIsRejectedBeforeGatewayAccess()
    {
        var gateway = Substitute.For<IAtprotoOAuthSecurityGateway>();
        var invalid = Identity with { TenantId = Guid.Empty };

        await Assert.ThrowsAsync<ValidationException>(() =>
            new GetCurrentAtprotoOAuthSessionQueryHandler(gateway)
                .Handle(new GetCurrentAtprotoOAuthSessionQuery(invalid), CancellationToken.None));

        await gateway.DidNotReceiveWithAnyArgs().GetCurrentAsync(default!, default);
    }

    [Test]
    public async Task DefaultTypedDidIsRejectedBeforeGatewayAccess()
    {
        var gateway = Substitute.For<IAtprotoOAuthSecurityGateway>();
        var invalid = Identity with { Did = default };

        await Assert.ThrowsAsync<ValidationException>(() =>
            new GetCurrentAtprotoOAuthSessionQueryHandler(gateway)
                .Handle(new GetCurrentAtprotoOAuthSessionQuery(invalid), CancellationToken.None));

        await gateway.DidNotReceiveWithAnyArgs().GetCurrentAsync(default!, default);
    }
}
