// ABOUTME: Verifies Local Identity HTTP endpoints expose successful sessions and RFC 7807 failures.
// ABOUTME: Proves credential failures remain generic at the remaining Local login boundary.

using System.Security.Cryptography;
using Explore.API.Controllers;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.API.IntegrationTests.Controllers;

public sealed class LocalAuthControllerTests
{
    [Test]
    public async Task LoginReturnsAuthenticatedSession()
    {
        var sender = Substitute.For<ISender>();
        LocalAuthResponseDto response = LocalAuthResponseDto.Authenticated(
            Guid.CreateVersion7(),
            "admin@example.test",
            "Site",
            "Administrator",
            false,
            [],
            CreateOpaqueValue(),
            DateTimeOffset.UtcNow.AddMinutes(30));
        sender.Send(
                Arg.Any<LocalLoginCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(response);
        LocalAuthController controller = CreateController(sender);

        ActionResult<LocalAuthResponseDto> result = await controller.Login(
            new LocalAuthRequestDto("admin@example.test", CreateOpaqueValue()),
            CancellationToken.None);

        var ok = result.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(response);
    }

    [Test]
    public async Task InvalidCredentialsReturnGenericUnauthorizedProblem()
    {
        var sender = Substitute.For<ISender>();
        sender.Send(
                Arg.Any<LocalLoginCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(LocalAuthResponseDto.Failed("invalid_credentials"));
        LocalAuthController controller = CreateController(sender);

        ActionResult<LocalAuthResponseDto> result = await controller.Login(
            new LocalAuthRequestDto("admin@example.test", CreateOpaqueValue()),
            CancellationToken.None);

        var problemResult = result.Result as ObjectResult;
        var problem = problemResult?.Value as ProblemDetails;
        await Assert.That(problemResult?.StatusCode)
            .IsEqualTo(StatusCodes.Status401Unauthorized);
        await Assert.That(problem?.Extensions["code"]).IsEqualTo("invalid_credentials");
        await Assert.That(problem?.Detail).DoesNotContain("admin@example.test");
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
