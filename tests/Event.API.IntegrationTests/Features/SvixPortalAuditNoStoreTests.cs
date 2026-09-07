using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Handlers.Commands;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Domain;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.API.IntegrationTests.Features;

public sealed class SvixPortalAuditNoStoreTests
{
    [Test]
    public async Task PortalEndpoint_DeclaresNoStoreAndWriteMiddlewareEnforcesIt()
    {
        var method = typeof(WebhooksController).GetMethod(nameof(WebhooksController.OpenSvixAppPortal));
        var responseCache = method!
            .GetCustomAttributes(typeof(ResponseCacheAttribute), inherit: true)
            .Cast<ResponseCacheAttribute>()
            .Single();
        await using var factory = new CustomWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/svix/app-portal")
        {
            Content = JsonContent.Create(new
            {
                consumerId = Guid.CreateVersion7(),
                expiresInSeconds = 300
            })
        };
        using var response = await client.SendAsync(request);

        await Assert.That(responseCache.NoStore).IsTrue();
        await Assert.That(responseCache.Location).IsEqualTo(ResponseCacheLocation.None);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response.Headers.Pragma.Any(value => value.Name == "no-cache")).IsTrue();
    }

    [Test]
    public async Task AuditFailure_ProducesNoPortalResponse()
    {
        var portalService = Substitute.For<IWebhookProviderPortalService>();
        var auditWriter = Substitute.For<IWebhookAuditEventWriter>();
        portalService.CreateAccessAsync(Arg.Any<WebhookProviderPortalAccessInput>(), Arg.Any<CancellationToken>())
            .Returns(WebhookProviderPortalAccessResult.Success(
                "https://svix.example/sensitive-portal",
                "sensitive-token",
                DateTimeOffset.UtcNow.AddMinutes(15),
                Guid.CreateVersion7(),
                "policy-v1"));
        auditWriter.AppendAsync(
                Arg.Any<WebhookAuditWriteRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<WebhookAuditEvent>(new InvalidOperationException("audit unavailable")));
        var command = CreateCommand();
        var handler = CreateHandler(portalService, auditWriter, command.ConsumerId);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            command,
            CancellationToken.None));
    }

    [Test]
    public async Task SuccessfulAudit_ContainsSafeMetadataOnly()
    {
        const string portalUrl = "https://svix.example/sensitive-portal";
        const string portalToken = "sensitive-token";
        var bindingId = Guid.CreateVersion7();
        var portalService = Substitute.For<IWebhookProviderPortalService>();
        var auditWriter = Substitute.For<IWebhookAuditEventWriter>();
        portalService.CreateAccessAsync(Arg.Any<WebhookProviderPortalAccessInput>(), Arg.Any<CancellationToken>())
            .Returns(WebhookProviderPortalAccessResult.Success(
                portalUrl,
                portalToken,
                DateTimeOffset.UtcNow.AddMinutes(15),
                bindingId,
                "policy-v1"));
        WebhookAuditWriteRequest? auditRequest = null;
        auditWriter.AppendAsync(
                Arg.Do<WebhookAuditWriteRequest>(request => auditRequest = request),
                Arg.Any<CancellationToken>())
            .Returns((WebhookAuditEvent)null!);
        var command = CreateCommand();
        var handler = CreateHandler(portalService, auditWriter, command.ConsumerId);

        var result = await handler.Handle(command, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(auditRequest).IsNotNull();
        await Assert.That(auditRequest!.Action).IsEqualTo(WebhookAuditAction.PortalAccessIssued);
        await Assert.That(auditRequest.SafeAfterJson).Contains(bindingId.ToString("D"), StringComparison.OrdinalIgnoreCase);
        await Assert.That(auditRequest.ConfigurationVersion).IsEqualTo("policy-v1");
        await Assert.That(auditRequest.SafeAfterJson).DoesNotContain(portalUrl, StringComparison.Ordinal);
        await Assert.That(auditRequest.SafeAfterJson).DoesNotContain(portalToken, StringComparison.Ordinal);
    }

    [Test]
    public async Task ProviderFailure_IsAuditedWithSafeCategoryBeforeResponse()
    {
        var portalService = Substitute.For<IWebhookProviderPortalService>();
        var auditWriter = Substitute.For<IWebhookAuditEventWriter>();
        portalService.CreateAccessAsync(Arg.Any<WebhookProviderPortalAccessInput>(), Arg.Any<CancellationToken>())
            .Returns(WebhookProviderPortalAccessResult.Failure(
                "svix_provider_unavailable",
                isRetryable: true,
                "SvixApi:503"));
        WebhookAuditWriteRequest? auditRequest = null;
        auditWriter.AppendAsync(
                Arg.Do<WebhookAuditWriteRequest>(request => auditRequest = request),
                Arg.Any<CancellationToken>())
            .Returns((WebhookAuditEvent)null!);
        var command = CreateCommand();
        var handler = CreateHandler(portalService, auditWriter, command.ConsumerId);

        var result = await handler.Handle(command, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(auditRequest).IsNotNull();
        await Assert.That(auditRequest!.SafeAfterJson).Contains("provider_failure", StringComparison.Ordinal);
        await Assert.That(auditRequest.SafeAfterJson).Contains("svix_provider_unavailable", StringComparison.Ordinal);
        await Assert.That(auditRequest.SafeAfterJson).DoesNotContain("SvixApi:503", StringComparison.Ordinal);
    }

    private static OpenSvixAppPortalCommand CreateCommand() => new()
    {
        ConsumerId = Guid.CreateVersion7(),
        SessionId = "session-1"
    };

    private static OpenSvixAppPortalCommandHandler CreateHandler(
        IWebhookProviderPortalService portalService,
        IWebhookAuditEventWriter auditWriter,
        Guid consumerId)
    {
        var tenantId = Guid.CreateVersion7();
        var consumerRepository = Substitute.For<IWebhookConsumerRepository>();
        consumerRepository.GetByIdForOwnerOperationAsync(
                consumerId,
                false,
                Arg.Any<CancellationToken>())
            .Returns(new WebhookConsumer
            {
                Id = consumerId,
                TenantId = tenantId,
                ConsumerKind = WebhookConsumerKind.Tenant,
                Name = "Portal audit consumer",
                Status = WebhookConsumerStatus.Active,
                ProviderMode = WebhookProviderMode.Svix,
                CreatedAt = DateTime.UtcNow
            });

        return new OpenSvixAppPortalCommandHandler(portalService, auditWriter, consumerRepository);
    }
}
