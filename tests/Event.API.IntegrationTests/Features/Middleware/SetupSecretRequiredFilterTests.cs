using Explore.API.Filters;
using Explore.Application.Contracts.Services;
using Explore.Application.Onboarding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features.Middleware;

public class SetupSecretRequiredFilterTests
{
    [Test]
    public async Task OnActionExecutionAsync_DurableCompletionRejectsStaleActiveProvider()
    {
        var setupSecretProvider = Substitute.For<ISetupSecretProvider>();
        setupSecretProvider.ValidateSecretAsync("valid-secret", Arg.Any<CancellationToken>())
            .Returns(SetupSecretValidationOutcome.SetupCompleted);
        var auditLogger = Substitute.For<IInstanceBootstrapAuditLogger>();
        var filter = new SetupSecretRequiredAttribute.SetupSecretRequiredFilter(
            setupSecretProvider,
            auditLogger,
            false);
        var context = CreateExecutingContext("valid-secret");
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, CreateNext(context, () => nextCalled = true));

        await Assert.That(nextCalled).IsFalse();
        var result = context.Result as ObjectResult;
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.StatusCode).IsEqualTo(StatusCodes.Status410Gone);
    }

    [Test]
    public async Task OnActionExecutionAsync_SetupNotActive_ReturnsGone()
    {
        var setupSecretProvider = Substitute.For<ISetupSecretProvider>();
        setupSecretProvider.ValidateSecretAsync("valid-secret", Arg.Any<CancellationToken>())
            .Returns(SetupSecretValidationOutcome.SetupCompleted);
        var auditLogger = Substitute.For<IInstanceBootstrapAuditLogger>();

        var filter = CreateFilter(setupSecretProvider, auditLogger);
        var context = CreateExecutingContext("valid-secret");
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, CreateNext(context, () => nextCalled = true));

        await Assert.That(nextCalled).IsEqualTo(false);
        await Assert.That(context.Result).IsTypeOf<ObjectResult>();
        var result = (ObjectResult)context.Result!;
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status410Gone);
        await Assert.That(result.ContentTypes).Contains("application/problem+json");

        var problem = result.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Title).IsEqualTo("Setup already completed");
        await Assert.That(problem.Extensions["code"]?.ToString()).IsEqualTo("setup_already_completed");
        auditLogger.Received(1).Log(Arg.Is<InstanceBootstrapAuditEvent>(auditEvent =>
            auditEvent.EventType == InstanceBootstrapAuditEventType.SetupModeInactive
            && auditEvent.Operation == "setup_secret_gate"
            && auditEvent.Outcome == "inactive"
            && auditEvent.FailureCode == "setup_already_completed"));
    }

    [Test]
    public async Task OnActionExecutionAsync_SetupActiveWrongSecret_ReturnsForbidden()
    {
        var setupSecretProvider = Substitute.For<ISetupSecretProvider>();
        setupSecretProvider.IsSetupModeActive.Returns(true);
        setupSecretProvider.ValidateSecretAsync("wrong-secret", Arg.Any<CancellationToken>())
            .Returns(SetupSecretValidationOutcome.Rejected);
        var auditLogger = Substitute.For<IInstanceBootstrapAuditLogger>();

        var filter = CreateFilter(setupSecretProvider, auditLogger);
        var context = CreateExecutingContext("wrong-secret");
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, CreateNext(context, () => nextCalled = true));

        await Assert.That(nextCalled).IsEqualTo(false);
        await Assert.That(context.Result).IsTypeOf<ObjectResult>();
        var result = (ObjectResult)context.Result!;
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        await Assert.That(result.ContentTypes).Contains("application/problem+json");

        var problem = result.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Title).IsEqualTo("Invalid setup secret");
        await Assert.That(problem.Extensions["code"]?.ToString()).IsEqualTo("forbidden");
        auditLogger.Received(1).Log(Arg.Is<InstanceBootstrapAuditEvent>(auditEvent =>
            auditEvent.EventType == InstanceBootstrapAuditEventType.SetupSecretRejected
            && auditEvent.Operation == "setup_secret_gate"
            && auditEvent.Outcome == "rejected"
            && auditEvent.FailureCode == "invalid_setup_secret"));
    }

    [Test]
    public async Task OnActionExecutionAsync_SetupActiveValidSecret_CallsNext()
    {
        var setupSecretProvider = Substitute.For<ISetupSecretProvider>();
        setupSecretProvider.IsSetupModeActive.Returns(true);
        setupSecretProvider.ValidateSecretAsync("valid-secret", Arg.Any<CancellationToken>())
            .Returns(SetupSecretValidationOutcome.Accepted);
        var auditLogger = Substitute.For<IInstanceBootstrapAuditLogger>();

        var filter = CreateFilter(setupSecretProvider, auditLogger);
        var context = CreateExecutingContext("valid-secret");
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, CreateNext(context, () => nextCalled = true));

        await Assert.That(nextCalled).IsEqualTo(true);
        await Assert.That(context.Result).IsNull();
        auditLogger.Received(1).Log(Arg.Is<InstanceBootstrapAuditEvent>(auditEvent =>
            auditEvent.EventType == InstanceBootstrapAuditEventType.SetupSecretAccepted
            && auditEvent.Operation == "setup_secret_gate"
            && auditEvent.Outcome == "accepted"));
    }

    [Test]
    public async Task OnActionExecutionAsync_SetupActiveNoHeader_ReturnsForbidden()
    {
        var setupSecretProvider = Substitute.For<ISetupSecretProvider>();
        setupSecretProvider.IsSetupModeActive.Returns(true);
        setupSecretProvider.IsSetupSecretRequired.Returns(true);
        setupSecretProvider.ValidateSecretAsync(null, Arg.Any<CancellationToken>())
            .Returns(SetupSecretValidationOutcome.Rejected);
        var auditLogger = Substitute.For<IInstanceBootstrapAuditLogger>();

        var filter = CreateFilter(setupSecretProvider, auditLogger);
        var context = CreateExecutingContext();
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, CreateNext(context, () => nextCalled = true));

        await Assert.That(nextCalled).IsEqualTo(false);
        await Assert.That(context.Result).IsTypeOf<ObjectResult>();
        var result = (ObjectResult)context.Result!;
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        await Assert.That(result.Value).IsTypeOf<ProblemDetails>();
        auditLogger.Received(1).Log(Arg.Is<InstanceBootstrapAuditEvent>(auditEvent =>
            auditEvent.EventType == InstanceBootstrapAuditEventType.SetupSecretRejected
            && auditEvent.Operation == "setup_secret_gate"
            && auditEvent.Outcome == "rejected"
            && auditEvent.FailureCode == "invalid_setup_secret"));
    }

    [Test]
    public async Task OnActionExecutionAsync_SetupSecretNotRequiredStillDoesNotAllowAnonymousSetupEndpoint()
    {
        var setupSecretProvider = Substitute.For<ISetupSecretProvider>();
        setupSecretProvider.IsSetupModeActive.Returns(true);
        setupSecretProvider.IsSetupSecretRequired.Returns(false);
        setupSecretProvider.ValidateSecretAsync(null, Arg.Any<CancellationToken>())
            .Returns(SetupSecretValidationOutcome.Rejected);
        var auditLogger = Substitute.For<IInstanceBootstrapAuditLogger>();

        var filter = CreateFilter(setupSecretProvider, auditLogger);
        var context = CreateExecutingContext();
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, CreateNext(context, () => nextCalled = true));

        await Assert.That(nextCalled).IsEqualTo(false);
        await Assert.That(context.Result).IsTypeOf<ObjectResult>();
        var result = (ObjectResult)context.Result!;
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        await Assert.That(result.Value).IsTypeOf<ProblemDetails>();
        auditLogger.Received(1).Log(Arg.Is<InstanceBootstrapAuditEvent>(auditEvent =>
            auditEvent.EventType == InstanceBootstrapAuditEventType.SetupSecretRejected
            && auditEvent.Operation == "setup_secret_gate"
            && auditEvent.Outcome == "rejected"
            && auditEvent.FailureCode == "invalid_setup_secret"));
    }

    [Test]
    public async Task OnActionExecutionAsync_SetupActiveEmptyHeader_ReturnsForbidden()
    {
        var setupSecretProvider = Substitute.For<ISetupSecretProvider>();
        setupSecretProvider.IsSetupModeActive.Returns(true);
        setupSecretProvider.ValidateSecretAsync(string.Empty, Arg.Any<CancellationToken>())
            .Returns(SetupSecretValidationOutcome.Rejected);
        var auditLogger = Substitute.For<IInstanceBootstrapAuditLogger>();

        var filter = CreateFilter(setupSecretProvider, auditLogger);
        var context = CreateExecutingContext(string.Empty);
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, CreateNext(context, () => nextCalled = true));

        await Assert.That(nextCalled).IsEqualTo(false);
        await Assert.That(context.Result).IsTypeOf<ObjectResult>();
        var result = (ObjectResult)context.Result!;
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
        await Assert.That(result.Value).IsTypeOf<ProblemDetails>();
        auditLogger.Received(1).Log(Arg.Is<InstanceBootstrapAuditEvent>(auditEvent =>
            auditEvent.EventType == InstanceBootstrapAuditEventType.SetupSecretRejected
            && auditEvent.Operation == "setup_secret_gate"
            && auditEvent.Outcome == "rejected"
            && auditEvent.FailureCode == "invalid_setup_secret"));
    }

    private static IAsyncActionFilter CreateFilter(
        ISetupSecretProvider setupSecretProvider,
        IInstanceBootstrapAuditLogger auditLogger)
    {
        return new SetupSecretRequiredAttribute.SetupSecretRequiredFilter(
            setupSecretProvider,
            auditLogger,
            false);
    }

    private static ActionExecutingContext CreateExecutingContext(string? setupSecretHeader = null)
    {
        var httpContext = new DefaultHttpContext();
        if (setupSecretHeader is not null)
        {
            httpContext.Request.Headers["X-Setup-Secret"] = setupSecretHeader;
        }

        var actionDescriptor = new ActionDescriptor
        {
            AttributeRouteInfo = new AttributeRouteInfo { Name = "TestSetupSecretRoute" }
        };
        var actionContext = new ActionContext(httpContext, new RouteData(), actionDescriptor);
        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), null!);
    }

    private static ActionExecutionDelegate CreateNext(ActionExecutingContext context, Action onCall)
    {
        return () =>
        {
            onCall();
            return Task.FromResult(new ActionExecutedContext(context, new List<IFilterMetadata>(), null!));
        };
    }
}
