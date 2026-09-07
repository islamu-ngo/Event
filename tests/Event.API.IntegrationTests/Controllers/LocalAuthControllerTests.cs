// ABOUTME: Verifies Local Identity HTTP endpoints expose successful sessions and RFC 7807 failures.
// ABOUTME: Proves credential failures remain generic at the remaining Local login boundary.

using System.Security.Cryptography;
using Explore.API.Controllers;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Contracts.Infrastructure;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.API.IntegrationTests.Controllers;

public sealed class LocalAuthControllerTests
{
    [Test]
    public async Task ReplacementChallengeIsReturnedWithoutAnAuthenticatedSession()
    {
        var sender = Substitute.For<ISender>();
        var challenge = new LocalIssuedReplacementChallenge(token: CreateOpaqueValue(), expiresAt: DateTimeOffset.UtcNow.AddMinutes(5));
        sender.Send(Arg.Any<LocalLoginCommand>(), Arg.Any<CancellationToken>())
            .Returns(LocalAuthResponseDto.ReplacementRequired(challenge: challenge));
        LocalAuthController controller = CreateController(sender);

        ActionResult<LocalAuthResponseDto> result = await controller.Login(
            new LocalAuthRequestDto(Email: "admin@example.test", Password: CreateOpaqueValue()), CancellationToken.None);

        var response = (result.Result as OkObjectResult)?.Value as LocalAuthResponseDto;
        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Outcome).IsEqualTo(LocalAuthOutcome.ReplacementRequired);
        await Assert.That(response.Success).IsFalse();
        await Assert.That(response.Token).IsNull();
        await Assert.That(response.ReplacementChallenge?.Token).IsEqualTo(challenge.Token);
    }

    [Test]
    public async Task LoginReturnsAuthenticatedSession()
    {
        var sender = Substitute.For<ISender>();
        LocalAuthResponseDto response = LocalAuthResponseDto.Authenticated(
            userId: Guid.CreateVersion7(),
            email: "admin@example.test",
            firstName: "Site",
            lastName: "Administrator",
            emailVerified: false,
            roles: [],
            token: CreateOpaqueValue(),
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(30));
        sender.Send(
                Arg.Any<LocalLoginCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
        LocalAuthController controller = CreateController(sender);

        ActionResult<LocalAuthResponseDto> result = await controller.Login(
            new LocalAuthRequestDto(Email: "admin@example.test", Password: CreateOpaqueValue()),
            CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(response);
    }

    [Test]
    [Arguments(LocalAuthFailure.InvalidRequest, StatusCodes.Status400BadRequest, "invalid_request")]
    [Arguments(LocalAuthFailure.InvalidCredentials, StatusCodes.Status401Unauthorized, "invalid_credentials")]
    [Arguments(LocalAuthFailure.AccountLocked, StatusCodes.Status401Unauthorized, "account_locked")]
    [Arguments(LocalAuthFailure.ProviderInactive, StatusCodes.Status409Conflict, "provider_inactive")]
    [Arguments(LocalAuthFailure.UserSynchronizationFailed, StatusCodes.Status503ServiceUnavailable, "user_sync_failed")]
    [Arguments(LocalAuthFailure.AuthenticationFailed, StatusCodes.Status503ServiceUnavailable, "authentication_failed")]
    [Arguments(LocalAuthFailure.EmailVerificationRequired, StatusCodes.Status401Unauthorized, "email_verification_required")]
    public async Task TypedFailureReturnsBoundedProblemWithoutEchoingCredentials(
        LocalAuthFailure failure, int expectedStatus, string expectedCode)
    {
        var sender = Substitute.For<ISender>();
        sender.Send(
                Arg.Any<LocalLoginCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(LocalAuthResponseDto.Failed(failure: failure));
        LocalAuthController controller = CreateController(sender);
        string password = CreateOpaqueValue();

        ActionResult<LocalAuthResponseDto> result = await controller.Login(
            new LocalAuthRequestDto(Email: "admin@example.test", Password: password),
            CancellationToken.None);

        var problemResult = result.Result as ObjectResult;
        var problem = problemResult?.Value as ProblemDetails;
        await Assert.That(problemResult?.StatusCode)
            .IsEqualTo(expectedStatus);
        await Assert.That(problem?.Extensions["code"]).IsEqualTo(expectedCode);
        await Assert.That(problem?.Detail).DoesNotContain("admin@example.test");
        await Assert.That(problem?.Detail).DoesNotContain(password);
    }

    private static LocalAuthController CreateController(ISender sender) =>
        new(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static string CreateOpaqueValue() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
