using System;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Atproto.Handlers.Commands;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Domain.ValueObjects;
using FluentValidation;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.Authentication.Atproto;

public sealed class RefreshAtprotoSessionCommandHandlerTests
{
    private static readonly AtprotoCurrentSessionIdentity Identity = new(
        Guid.Parse("018e4e5c-7f00-7000-8000-000000000001"),
        Guid.Parse("018e4e5c-7f00-7000-8000-000000000002"),
        AtprotoDid.Parse("did:plc:refresh-user"));

    [Test]
    public async Task SuccessfulRefreshPersistsProviderRotationBeforeIssuingPlatformToken()
    {
        var gateway = Substitute.For<IAtprotoOAuthSecurityGateway>();
        var issuer = Substitute.For<IAtprotoSessionTokenIssuer>();
        gateway.RefreshAsync(Identity, Arg.Any<CancellationToken>())
            .Returns(AtprotoOAuthRefreshResult.Refreshed());
        issuer.IssueAsync(Identity.UserId, Identity.TenantId, Identity.Did, Arg.Any<CancellationToken>())
            .Returns(new AtprotoIssuedSessionToken("replacement-jwt", DateTimeOffset.UtcNow.AddMinutes(15)));

        var result = await new RefreshAtprotoSessionCommandHandler(gateway, issuer)
            .Handle(new RefreshAtprotoSessionCommand(Identity), CancellationToken.None);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Token).IsEqualTo("replacement-jwt");
        Received.InOrder(() =>
        {
            gateway.RefreshAsync(Identity, Arg.Any<CancellationToken>());
            issuer.IssueAsync(Identity.UserId, Identity.TenantId, Identity.Did, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task MissingCorruptOrRevokedProviderSessionRequiresReauthenticationWithoutJwt()
    {
        var gateway = Substitute.For<IAtprotoOAuthSecurityGateway>();
        var issuer = Substitute.For<IAtprotoSessionTokenIssuer>();
        gateway.RefreshAsync(Identity, Arg.Any<CancellationToken>())
            .Returns(AtprotoOAuthRefreshResult.ReauthenticationRequired());

        var result = await new RefreshAtprotoSessionCommandHandler(gateway, issuer)
            .Handle(new RefreshAtprotoSessionCommand(Identity), CancellationToken.None);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("reauthentication_required");
        await issuer.DidNotReceiveWithAnyArgs().IssueAsync(default, default, default!, default);
    }

    [Test]
    public async Task InvalidAuthenticatedIdentityIsRejectedBeforeRefresh()
    {
        var gateway = Substitute.For<IAtprotoOAuthSecurityGateway>();
        var issuer = Substitute.For<IAtprotoSessionTokenIssuer>();
        var invalid = Identity with { UserId = Guid.Empty };

        await Assert.ThrowsAsync<ValidationException>(() =>
            new RefreshAtprotoSessionCommandHandler(gateway, issuer)
                .Handle(new RefreshAtprotoSessionCommand(invalid), CancellationToken.None));

        await gateway.DidNotReceiveWithAnyArgs().RefreshAsync(default!, default);
        await issuer.DidNotReceiveWithAnyArgs().IssueAsync(default, default, default!, default);
    }
}
