using System.Security.Claims;
using System.Text.Json;
using Explore.API.Authentication;
using Explore.API.Controllers;
using Explore.API.Models;
using Explore.Application.Constants;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Requests.Queries;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.API.IntegrationTests.Authentication;

public sealed class AtprotoSessionControllerTests
{
    [Test]
    public async Task BootstrapSessionRejectsCanonicalActorTargetBodyClaimParityMismatch()
    {
        var bootstrapHandler = Substitute.For<ICommandHandler<BootstrapAtprotoSessionCommand, AtprotoSessionBootstrapResult>>();
        var currentSessionHandler = Substitute.For<IQueryHandler<GetCurrentAtprotoOAuthSessionQuery, AtprotoCurrentOAuthSession?>>();
        var refreshHandler = Substitute.For<ICommandHandler<RefreshAtprotoSessionCommand, AtprotoSessionRefreshResult>>();
        var revokeHandler = Substitute.For<ICommandHandler<RevokeAtprotoSessionCommand, AtprotoSessionRevocationResult>>();
        var tenantContext = Substitute.For<ITenantContext>();
        var canonicalActorId = Guid.NewGuid();
        var expectedConcurrencyStamp = Guid.NewGuid();
        var controller = new AtprotoSessionController(
            bootstrapHandler,
            currentSessionHandler,
            refreshHandler,
            revokeHandler,
            tenantContext)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateContext(canonicalActorId, expectedConcurrencyStamp)
            }
        };
        using var document = JsonDocument.Parse("{}");

        var result = await controller.BootstrapSession(new(
            "did:plc:alice",
            "https://pds.example/",
            "oauth-active",
            "person",
            document.RootElement.Clone(),
            canonicalActorId,
            Guid.NewGuid()), CancellationToken.None);

        await Assert.That(result.Result).IsTypeOf<ObjectResult>();
        await Assert.That(((ObjectResult)result.Result!).StatusCode).IsEqualTo(StatusCodes.Status401Unauthorized);
        await bootstrapHandler.DidNotReceive().ExecuteAsync(
            Arg.Any<BootstrapAtprotoSessionCommand>(),
            Arg.Any<CancellationToken>());
    }

    private static DefaultHttpContext CreateContext(Guid canonicalActorId, Guid stamp)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(AtprotoJwtOptions.DidClaim, "did:plc:alice"),
            new Claim(AtprotoJwtOptions.ClassificationClaim, "person"),
            new Claim(AtprotoJwtOptions.CanonicalActorIdClaim, canonicalActorId.ToString("D")),
            new Claim(AtprotoJwtOptions.ExpectedCanonicalActorConcurrencyStampClaim, stamp.ToString("D"))
        ], ApiAuthenticationSchemeNames.AtprotoBootstrap));
        return context;
    }
}
