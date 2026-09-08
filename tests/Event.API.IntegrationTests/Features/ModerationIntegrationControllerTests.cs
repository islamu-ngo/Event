using System.Diagnostics.Metrics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.Authentication;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Services;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Infrastructure.Configuration;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationIntegrationControllerTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IIncomingWebhookIntakeService _incomingWebhookIntakeService = Substitute.For<IIncomingWebhookIntakeService>();

    [Test]
    public async Task Routes_UseStableNamesApiKeyAuthorizationAndRateLimitPolicy()
    {
        var controllerType = typeof(ModerationIntegrationController);
        var ospreyCallback = controllerType.GetMethod(nameof(ModerationIntegrationController.RecordOspreyCallback))!;
        var coopCallback = controllerType.GetMethod(nameof(ModerationIntegrationController.RecordCoopCallback))!;

        await Assert.That(controllerType.GetCustomAttribute<AuthorizeAttribute>()).IsNull();
        await Assert.That(controllerType.GetCustomAttribute<EndpointClassificationAttribute>()?.Class).IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(controllerType.GetCustomAttribute<RouteAttribute>()?.Template).IsEqualTo("api/integrations/moderation");
        await AssertRoute(ospreyCallback, typeof(HttpPostAttribute), "osprey/callback", RouteNames.ModerationIntegrationOspreyCallback);
        await AssertRoute(coopCallback, typeof(HttpPostAttribute), "coop/callback", RouteNames.ModerationIntegrationCoopCallback);
        await Assert.That(ospreyCallback.GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(ModerationIntegrationAuthorizationPolicies.OspreyCallback);
        await Assert.That(coopCallback.GetCustomAttribute<AuthorizeAttribute>()?.Policy).IsEqualTo(ModerationIntegrationAuthorizationPolicies.CoopCallback);
        await Assert.That(ospreyCallback.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(coopCallback.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo(RateLimitingExtensions.WritePolicy);
    }

    [Test]
    public async Task RecordOspreyCallback_MapsBodyToCommand()
    {
        var reportId = Guid.CreateVersion7();
        var request = new OspreySignalCallbackRequestDto
        {
            TenantId = Guid.CreateVersion7(),
            ReportId = reportId,
            EventId = Guid.CreateVersion7(),
            ProviderSignalId = "osp-signal-1",
            CorrelationId = "corr-osprey-1",
            Signals =
            [
                new OspreySignalCallbackItemDto
                {
                    SignalType = "policy_match",
                    PolicyCode = "trust.high_risk",
                    Verdict = "urgent",
                    RecommendedAction = "recommend_heavy_redact"
                }
            ]
        };
        _mediator.Send(Arg.Any<RecordOspreySignalCallbackCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                reportId,
                "ok"));
        var controller = CreateController();

        var response = await controller.RecordOspreyCallback(request, CancellationToken.None);

        var ok = response.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await _mediator.Received(1).Send(
            Arg.Is<RecordOspreySignalCallbackCommand>(command => ReferenceEquals(command.Request, request)),
            Arg.Any<CancellationToken>());
        await _mediator.DidNotReceive().Send(
            Arg.Any<ExecuteReportDecisionCommand>(),
            Arg.Any<CancellationToken>());
        await _mediator.DidNotReceive().Send(
            Arg.Any<ProcessCoopDecisionCallbackCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RecordCoopCallback_WithValidSignature_CapturesBodyForDurableProcessing()
    {
        var reportId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var caseId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var body = JsonSerializer.Serialize(new
        {
            tenant_id = tenantId,
            report_id = reportId,
            event_id = eventId,
            case_id = caseId,
            provider_decision_id = "coop-decision-1",
            action = new { id = "light_moderate" }
        });
        var incoming = IncomingWebhookReadResult.Success(
            "coop",
            body,
            Encoding.UTF8.GetBytes(body),
            DateTimeOffset.UtcNow,
            ComputePayloadHash(body),
            "application/json",
            "utf-8",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            IncomingWebhookVerificationResult.VerifiedTenantCredential(
                tenantId,
                "coop-decision-1",
                "moderation.coop.decision",
                "coop-decision-1"));
        _incomingWebhookIntakeService.ReadAndVerifyAsync(Arg.Any<HttpRequest>(), "coop", Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(incoming);
        _incomingWebhookIntakeService.CaptureAsync(
                incoming,
                Arg.Any<CancellationToken>())
            .Returns(IncomingWebhookCaptureResult.Captured(Guid.CreateVersion7(), "coop-decision-1", "coop-decision-1"));
        var controller = CreateController();

        var response = await controller.RecordCoopCallback(CancellationToken.None);

        var ok = response.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await _incomingWebhookIntakeService.Received(1).CaptureAsync(
            incoming,
            Arg.Any<CancellationToken>());
        await _mediator.DidNotReceive().Send(
            Arg.Any<ProcessCoopDecisionCallbackCommand>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RecordCoopCallback_WhenDuplicateCapture_DoesNotDispatchCommandAgain()
    {
        var tenantId = Guid.CreateVersion7();
        var capturedMessageId = Guid.CreateVersion7();
        var body = JsonSerializer.Serialize(new
        {
            tenant_id = tenantId,
            report_id = Guid.CreateVersion7(),
            event_id = Guid.CreateVersion7(),
            case_id = Guid.CreateVersion7(),
            provider_decision_id = "coop-decision-duplicate",
            action = new { id = "light_moderate" }
        });
        var incoming = IncomingWebhookReadResult.Success(
            "coop",
            body,
            Encoding.UTF8.GetBytes(body),
            DateTimeOffset.UtcNow,
            ComputePayloadHash(body),
            "application/json",
            "utf-8",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            IncomingWebhookVerificationResult.VerifiedTenantCredential(
                tenantId,
                "coop-decision-duplicate",
                "moderation.coop.decision",
                "coop-decision-duplicate"));
        _incomingWebhookIntakeService.ReadAndVerifyAsync(Arg.Any<HttpRequest>(), "coop", Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(incoming);
        _incomingWebhookIntakeService.CaptureAsync(
                incoming,
                Arg.Any<CancellationToken>())
            .Returns(IncomingWebhookCaptureResult.Duplicate(capturedMessageId, "coop-decision-duplicate", "coop-decision-duplicate"));
        var controller = CreateController();

        var response = await controller.RecordCoopCallback(CancellationToken.None);

        var ok = response.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        var bodyResponse = ok!.Value as BaseCommandResponse<Guid>;
        await Assert.That(bodyResponse!.IsSuccess).IsTrue();
        await Assert.That(bodyResponse.Id).IsEqualTo(capturedMessageId);
        await _mediator.DidNotReceive().Send(Arg.Any<ProcessCoopDecisionCallbackCommand>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CoopWebhookSignatureValidator_WithValidTimestampedHmac_ReturnsVerifiedBody()
    {
        const string secret = "coop-secret";
        const string body = "{\"action\":{\"id\":\"light_moderate\"}}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var context = CreateSignedHttpContext(secret, timestamp, body, validSignature: true);
        var validator = CreateSignatureValidator(secret);

        var result = await validator.ReadAndValidateAsync(context.Request, CancellationToken.None);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Body).IsEqualTo(body);
        await Assert.That(context.Request.Body.Position).IsEqualTo(0);
    }

    [Test]
    public async Task CoopWebhookSignatureValidator_WithInvalidSignature_ReturnsUnauthorized()
    {
        const string secret = "coop-secret";
        const string body = "{\"action\":{\"id\":\"light_moderate\"}}";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var context = CreateSignedHttpContext(secret, timestamp, body, validSignature: false);
        var validator = CreateSignatureValidator(secret);

        var result = await validator.ReadAndValidateAsync(context.Request, CancellationToken.None);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.StatusCode).IsEqualTo(StatusCodes.Status401Unauthorized);
        await Assert.That(result.Code).IsEqualTo("coop_webhook_signature_invalid");
    }

    private ModerationIntegrationController CreateController()
        => new(
            _mediator,
            _incomingWebhookIntakeService,
            new StaticOptionsMonitor<CoopProviderOptions>(new CoopProviderOptions
            {
                WebhookMaxBodyBytes = 65_536
            }),
            CreateMetrics(),
            NullLogger<ModerationIntegrationController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));

        return new BusinessMetrics(meterFactory);
    }

    private static async Task AssertRoute(MethodInfo method, Type attributeType, string? template, string routeName)
    {
        var attribute = method.GetCustomAttributes().Single(value => value.GetType() == attributeType) as HttpMethodAttribute;
        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Template).IsEqualTo(template);
        await Assert.That(attribute.Name).IsEqualTo(routeName);
    }

    private static CoopWebhookSignatureValidator CreateSignatureValidator(string secret) => new(
        new StaticOptionsMonitor<CoopProviderOptions>(new CoopProviderOptions
        {
            WebhookSecret = secret
        }),
        NullLogger<CoopWebhookSignatureValidator>.Instance);

    private static DefaultHttpContext CreateSignedHttpContext(
        string secret,
        string timestamp,
        string body,
        bool validSignature)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.Headers["X-Coop-Timestamp"] = timestamp;
        context.Request.Headers["X-Coop-Signature"] = validSignature
            ? $"sha256={ComputeSignature(secret, timestamp, body)}"
            : "sha256=invalid";
        return context;
    }

    private static string ComputeSignature(string secret, string timestamp, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return Convert.ToHexString(signature).ToLowerInvariant();
    }

    private static string ComputePayloadHash(string payload) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant()}";

    private sealed class StaticOptionsMonitor<TOptions>(TOptions currentValue) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue => currentValue;

        public TOptions Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
