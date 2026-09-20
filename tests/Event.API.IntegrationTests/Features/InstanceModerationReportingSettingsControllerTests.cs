using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public sealed class InstanceModerationReportingSettingsControllerAnonymousTests
{
    private const string LocksPath = "/api/instance/settings/moderation-reporting/locks";
    private readonly ApiTestFixture _fixture;

    public InstanceModerationReportingSettingsControllerAnonymousTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task UpdateLocks_WithoutAuth_ShouldReturnUnauthorized()
    {
        var response = await _fixture.Client.PatchAsJsonAsync(LocksPath, new UpdateReportingProviderLocksDto());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }
}

public sealed class InstanceModerationReportingSettingsControllerAuthorizedTests
{
    private const string LocksPath = "/api/instance/settings/moderation-reporting/locks";

    [Test]
    public async Task UpdateLocks_RouteAndAuthorizationContract()
    {
        var method = typeof(InstanceModerationReportingSettingsController).GetMethod(nameof(InstanceModerationReportingSettingsController.UpdateLocks))!;
        await Assert.That(typeof(InstanceModerationReportingSettingsController).GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(typeof(InstanceModerationReportingSettingsController).GetCustomAttribute<EndpointClassificationAttribute>()?.Class).IsEqualTo(EndpointClass.Authenticated);
        var patch = method.GetCustomAttribute<HttpPatchAttribute>();
        await Assert.That(patch).IsNotNull();
        await Assert.That(patch!.Template).IsEqualTo("locks");
        await Assert.That(patch.Name).IsEqualTo(RouteNames.UpdateInstanceModerationReportingProviderLocks);
    }

    [Test]
    public async Task UpdateLocks_WithAuth_ShouldSendCommand()
    {
        var handler = new LockCommandHandler(allowUpdate: true);
        var controller = CreateController(handler);
        var dto = new UpdateReportingProviderLocksDto
        {
            General = new ReportingProviderLockUpdateDto { Locked = false },
            Coop = new ReportingProviderLockUpdateDto { Locked = true }
        };

        var actionResult = await controller.UpdateLocks(dto, CancellationToken.None);

        var okResult = actionResult.Result as OkObjectResult;
        await Assert.That(okResult).IsNotNull();
        await Assert.That(okResult!.StatusCode).IsEqualTo(StatusCodes.Status200OK);
        await Assert.That(handler.LastCommand).IsNotNull();
        await Assert.That(handler.LastCommand!.Locks.General!.Locked).IsFalse();
        await Assert.That(handler.LastCommand.Locks.Osprey).IsNull();
        await Assert.That(handler.LastCommand.Locks.Coop!.Locked).IsTrue();
    }

    [Test]
    public async Task UpdateLocks_WhenCommandDeniesAdmin_ShouldReturnForbidden()
    {
        var controller = CreateController(new LockCommandHandler(allowUpdate: false));
        var dto = new UpdateReportingProviderLocksDto();

        var actionResult = await controller.UpdateLocks(dto, CancellationToken.None);

        var problem = actionResult.Result as ObjectResult;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
    }

    [Test]
    public async Task UpdateLocks_WhenFailureMessageIsUnclassified_ShouldReturnBadRequest()
    {
        var controller = CreateController(new LockCommandHandler(
            allowUpdate: false,
            failureMessage: "Moderation reporting provider lock update failed."));
        var dto = new UpdateReportingProviderLocksDto();

        var actionResult = await controller.UpdateLocks(dto, CancellationToken.None);

        var badRequest = actionResult.Result as ObjectResult;
        await Assert.That(badRequest).IsNotNull();
        await Assert.That(badRequest!.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
    }

    private static InstanceModerationReportingSettingsController CreateController(
        ICommandHandler<UpdateReportingProviderLocksCommand, BaseCommandResponse<Guid>> handler)
    {
        var userId = Guid.NewGuid();
        var identityQuery = Substitute.For<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>();
        identityQuery.QueryAsync(Arg.Any<ResolveCurrentUserIdByIdentityRequest>(), Arg.Any<CancellationToken>())
            .Returns(userId);

        var controller = new InstanceModerationReportingSettingsController(handler, identityQuery);
        var httpContext = new DefaultHttpContext();
        var claims = new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()) };
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "TestAuth"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private sealed class LockCommandHandler(bool allowUpdate, string? failureMessage = null)
        : ICommandHandler<UpdateReportingProviderLocksCommand, BaseCommandResponse<Guid>>
    {
        public UpdateReportingProviderLocksCommand? LastCommand { get; private set; }

        public Task<BaseCommandResponse<Guid>> ExecuteAsync(
            UpdateReportingProviderLocksCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;

            var response = allowUpdate
                ? BaseCommandResponse.Success(Guid.Empty, "Updated")
                : failureMessage is null
                    ? BaseCommandResponse.Authorization<Guid>("Only instance administrators can update moderation reporting provider locks.")
                    : BaseCommandResponse.Validation<Guid>([failureMessage], failureMessage);

            return Task.FromResult(response);
        }
    }
}
