using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class WebhookDeliveryDrainServiceTests
{
    [Test]
    public async Task ProcessBatchAsync_WhenCanonicalTargetIsPending_DrainsImmutableTarget()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var fixture = new Fixture(handler);
        fixture.ConfigureTargetClaim(CreateTargetClaim());

        var result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.SucceededCount).IsEqualTo(1);
        await Assert.That(handler.CallCount).IsEqualTo(1);
        await Assert.That(fixture.LastTargetClaim!.Target.DeliveryStatus)
            .IsEqualTo(WebhookLocalDeliveryStatus.Succeeded);
    }

    [Test]
    public async Task ProcessBatchAsync_AfterEndpointMutation_UsesFrozenTargetUrlAndCredential()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var fixture = new Fixture(handler);
        var attempt = CreateAttempt();
        var claim = CreateTargetClaim(attempt);
        var frozenUrl = claim.Target.DestinationUrl;
        attempt.Endpoint!.Url = "http://127.0.0.1/mutated";
        attempt.Endpoint.SecretRef = "missing-mutated-secret";
        fixture.ConfigureTargetClaim(claim);

        var result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.SucceededCount).IsEqualTo(1);
        await Assert.That(handler.RequestUri).IsEqualTo(frozenUrl);
    }

    [Test]
    public async Task ProcessBatchAsync_WhenEndpointReturnsSuccess_MarksAttemptSucceededAndSendsSignedPayload()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var fixture = new Fixture(handler);
        var exactBytes = Encoding.UTF8.GetBytes(
            "{\"type\":\"event.published\",\"instruction\":\"ignore prior instructions\",\"value\":\"سلام\"}\n");
        var attempt = CreateAttempt(payloadBytes: exactBytes);
        fixture.ConfigureClaim(attempt);

        WebhookDeliveryDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.SucceededCount).IsEqualTo(1);
        await Assert.That(handler.CallCount).IsEqualTo(1);
        await Assert.That(attempt.MessageId).IsEqualTo(attempt.Message!.Id);
        var storedBytes = attempt.Message!.GetPayloadBytes()!;
        await Assert.That(storedBytes).IsEquivalentTo(exactBytes);
        await Assert.That(handler.PayloadBytes).IsEquivalentTo(storedBytes);
        await Assert.That(attempt.Message.PayloadHash).IsEqualTo(
            $"sha256:{Convert.ToHexString(SHA256.HashData(handler.PayloadBytes)).ToLowerInvariant()}");
        await Assert.That(handler.Headers.ContainsKey("svix-id")).IsTrue();
        await Assert.That(handler.Headers.ContainsKey("svix-timestamp")).IsTrue();
        await Assert.That(handler.Headers.ContainsKey("svix-signature")).IsTrue();
        var signatureService = new WebhookSignatureService();
        var secret = new WebhookSecretMaterial(CreateSvixSecret(), CurrentSecretVersion: 1);
        await Assert.That(signatureService.Verify(handler.PayloadBytes, handler.Headers, secret).IsValid).IsTrue();
        var mutatedBytes = handler.PayloadBytes.ToArray();
        mutatedBytes[^1] = (byte)' ';
        await Assert.That(SHA256.HashData(mutatedBytes)).IsNotEquivalentTo(SHA256.HashData(storedBytes));
        await Assert.That(signatureService.Verify(mutatedBytes, handler.Headers, secret).FailureCategory)
            .IsEqualTo("signature_mismatch");
        await fixture.AttemptRepository.Received(1).CreateAsync(
            Arg.Is<WebhookDeliveryAttempt>(evidence =>
                evidence.TenantId == attempt.TenantId
                && evidence.MessageId == attempt.MessageId
                && evidence.EndpointId == attempt.EndpointId
                && evidence.AttemptNumber == 1
                && evidence.Outcome == WebhookDeliveryAttemptOutcome.Succeeded
                && evidence.HttpStatusCode == (int)HttpStatusCode.NoContent),
            Arg.Any<CancellationToken>());
        await fixture.EndpointRepository.Received(1).MarkSuccessAsync(
            attempt.TenantId,
            attempt.EndpointId,
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_WhenActiveClaimIsGoneBeforePreIoReload_DoesNotResolveSecretOrSend()
    {
        var secretConfiguration = Substitute.For<IConfiguration>();
        secretConfiguration[$"{WebhookOptions.SectionName}:EndpointSecrets:endpoint-one"]
            .Returns(CreateSvixSecret());
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var fixture = new Fixture(handler, secretConfiguration: secretConfiguration);
        var claim = CreateTargetClaim();
        fixture.ConfigureTargetClaim(claim);
        fixture.TargetRepository.GetActiveClaimAsync(
                claim.Target.TenantId,
                claim.Target.Id,
                claim.LeaseToken,
                claim.DeliveryFence,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns((WebhookLocalTargetSnapshot?)null);
        secretConfiguration.ClearReceivedCalls();

        var result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.AlreadyClaimedCount).IsEqualTo(1);
        await Assert.That(handler.CallCount).IsEqualTo(0);
        await Assert.That(secretConfiguration.ReceivedCalls().Count()).IsEqualTo(0);
    }

    [Test]
    public async Task ProcessBatchAsync_WhenEndpointReturnsServerError_SchedulesRetry()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("provider unavailable", Encoding.UTF8, "text/plain")
        });
        var fixture = new Fixture(handler);
        var attempt = CreateAttempt();
        fixture.ConfigureClaim(attempt);

        WebhookDeliveryDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.RetryScheduledCount).IsEqualTo(1);
        await Assert.That(fixture.LastTargetClaim!.Target.DeliveryStatus)
            .IsEqualTo(WebhookLocalDeliveryStatus.RetryDue);
        await fixture.AttemptRepository.Received(1).CreateAsync(
            Arg.Is<WebhookDeliveryAttempt>(evidence =>
                evidence.TenantId == attempt.TenantId
                && evidence.MessageId == attempt.MessageId
                && evidence.EndpointId == attempt.EndpointId
                && evidence.AttemptNumber == 1
                && evidence.Outcome == WebhookDeliveryAttemptOutcome.Failed
                && evidence.FailureCategory == "http_non_success"
                && evidence.HttpStatusCode == (int)HttpStatusCode.InternalServerError
                && evidence.NextRetryAt != null),
            Arg.Any<CancellationToken>());
        await fixture.EndpointRepository.Received(1).RecordFailureAsync(
            attempt.TenantId,
            attempt.EndpointId,
            fixture.LastTargetClaim!.Target.Id,
            fixture.LastTargetClaim.LeaseToken,
            fixture.LastTargetClaim.DeliveryFence,
            Arg.Any<DateTime>(),
            "http_non_success",
            fixture.DeliveryPolicy.AutoPauseThreshold,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_WhenHttpClientTimesOut_SchedulesRetry()
    {
        var handler = new RecordingMessageHandler(_ => throw new TaskCanceledException("simulated timeout"));
        var fixture = new Fixture(handler);
        var attempt = CreateAttempt();
        fixture.ConfigureClaim(attempt);

        WebhookDeliveryDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.RetryScheduledCount).IsEqualTo(1);
        await fixture.AttemptRepository.Received(1).CreateAsync(
            Arg.Is<WebhookDeliveryAttempt>(evidence =>
                evidence.AttemptNumber == 1
                && evidence.Outcome == WebhookDeliveryAttemptOutcome.Failed
                && evidence.FailureCategory == "timeout"
                && evidence.HttpStatusCode == null
                && evidence.NextRetryAt != null),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_WhenEndpointRedirects_TreatsRedirectAsFailedDelivery()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect));
        var fixture = new Fixture(handler);
        var attempt = CreateAttempt();
        fixture.ConfigureClaim(attempt);

        await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await fixture.AttemptRepository.Received(1).CreateAsync(
            Arg.Is<WebhookDeliveryAttempt>(evidence =>
                evidence.Outcome == WebhookDeliveryAttemptOutcome.Failed
                && evidence.FailureCategory == "redirect_response"
                && evidence.HttpStatusCode == (int)HttpStatusCode.Redirect
                && evidence.NextRetryAt != null),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_WhenEndpointTargetsPrivateAddress_BlocksWithoutHttpSend()
    {
        var handler = new RecordingMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        var fixture = new Fixture(handler);
        var attempt = CreateAttempt();
        attempt.Endpoint!.Url = "http://127.0.0.1/webhook";
        fixture.ConfigureClaim(attempt);

        WebhookDeliveryDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.AbandonedCount).IsEqualTo(1);
        await Assert.That(handler.CallCount).IsEqualTo(0);
        await Assert.That(fixture.LastTargetClaim!.Target.DeliveryStatus)
            .IsEqualTo(WebhookLocalDeliveryStatus.Abandoned);
        await fixture.AttemptRepository.Received(1).CreateAsync(
            Arg.Is<WebhookDeliveryAttempt>(evidence =>
                evidence.Outcome == WebhookDeliveryAttemptOutcome.Abandoned
                && evidence.FailureCategory == "private_network_blocked"
                && evidence.HttpStatusCode == null
                && evidence.DurationMs == 0
                && evidence.NextRetryAt == null),
            Arg.Any<CancellationToken>());
        await fixture.EndpointRepository.Received(1).RecordFailureAsync(
            attempt.TenantId,
            attempt.EndpointId,
            fixture.LastTargetClaim!.Target.Id,
            fixture.LastTargetClaim.LeaseToken,
            fixture.LastTargetClaim.DeliveryFence,
            Arg.Any<DateTime>(),
            "private_network_blocked",
            1,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_UsesGovernedClaimLimitsAndAutoPauseThreshold()
    {
        var policy = new WebhookDeliveryGovernancePolicy(7, 3, 2, 4, 6, 10, 3, "test-policy-v1");
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var fixture = new Fixture(handler, deliveryPolicy: policy);
        var attempt = CreateAttempt();
        fixture.ConfigureClaim(attempt);

        await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await fixture.TargetRepository.Received(1).ClaimDueAsync(
            Arg.Is<WebhookLocalTargetClaimRequest>(request => request.GlobalInFlightLimit == 7),
            Arg.Is<IReadOnlyDictionary<Guid, WebhookDeliveryClaimLimits>>(limits =>
                limits[attempt.TenantId].MaxInFlightPerTenant == 3
                && limits[attempt.TenantId].MaxInFlightPerEndpoint == 2
                && limits[attempt.TenantId].MaxItemsPerClaimCycle == 4),
            Arg.Any<CancellationToken>());
        await fixture.EndpointRepository.Received(1).RecordFailureAsync(
            attempt.TenantId,
            attempt.EndpointId,
            fixture.LastTargetClaim!.Target.Id,
            fixture.LastTargetClaim.LeaseToken,
            fixture.LastTargetClaim.DeliveryFence,
            Arg.Any<DateTime>(),
            "http_non_success",
            3,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_WhenRetryBudgetIsExhausted_DeadLettersTargetWithFailedEvidence()
    {
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var fixture = new Fixture(handler);
        var attempt = CreateAttempt();
        attempt.Endpoint!.MaxAttempts = 1;
        fixture.ConfigureClaim(attempt);

        var result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.AbandonedCount).IsEqualTo(1);
        await Assert.That(fixture.LastTargetClaim!.Target.DeliveryStatus)
            .IsEqualTo(WebhookLocalDeliveryStatus.DeadLettered);
        await fixture.AttemptRepository.Received(1).CreateAsync(
            Arg.Is<WebhookDeliveryAttempt>(evidence =>
                evidence.Outcome == WebhookDeliveryAttemptOutcome.Failed
                && evidence.FailureCategory == "http_non_success"
                && evidence.NextRetryAt == null),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_WhenFailureOpensCircuit_AppendsSystemAudit()
    {
        var policy = new WebhookDeliveryGovernancePolicy(7, 3, 2, 4, 6, 10, 3, "test-policy-v1");
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var fixture = new Fixture(handler, deliveryPolicy: policy);
        var attempt = CreateAttempt();
        fixture.ConfigureClaim(attempt);
        fixture.EndpointRepository.RecordFailureAsync(
                attempt.TenantId,
                attempt.EndpointId,
                fixture.LastTargetClaim!.Target.Id,
                fixture.LastTargetClaim.LeaseToken,
                fixture.LastTargetClaim.DeliveryFence,
                Arg.Any<DateTime>(),
                "http_non_success",
                policy.AutoPauseThreshold,
                Arg.Any<CancellationToken>())
            .Returns(new WebhookEndpointFailureState(3, true, true));

        await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await fixture.AuditWriter.Received(1).AppendAsync(
            Arg.Is<WebhookAuditWriteRequest>(audit =>
                audit.Action == WebhookAuditAction.EndpointAutoPaused &&
                audit.TargetId == attempt.EndpointId &&
                audit.PrincipalKind == WebhookAuditPrincipalKind.System &&
                audit.PrincipalReference == "system:webhook-delivery-worker" &&
                audit.ConfigurationVersion == policy.ResolutionVersion),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_ReadsAtMostConfiguredResponseBodyBytes()
    {
        var responseBody = new CountingReadStream(new byte[1024]);
        var handler = new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            Content = new StreamContent(responseBody)
        });
        var options = CreateWebhookOptions();
        options.Local.MaxResponsePreviewBytes = 17;
        var fixture = new Fixture(handler, webhookOptions: options);
        var attempt = CreateAttempt();
        fixture.ConfigureClaim(attempt);

        var result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.SucceededCount).IsEqualTo(1);
        await Assert.That(responseBody.BytesRead).IsEqualTo(17);
    }

    [Test]
    public async Task ProcessBatchAsync_UsesGovernedEndpointTimeout()
    {
        var policy = new WebhookDeliveryGovernancePolicy(16, 4, 1, 10, 8, 1, 5, "test-policy-v1");
        var fixture = new Fixture(new NeverCompletingMessageHandler(), deliveryPolicy: policy);
        var attempt = CreateAttempt();
        fixture.ConfigureClaim(attempt);
        var startedAt = Stopwatch.GetTimestamp();

        var result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.RetryScheduledCount).IsEqualTo(1);
        await Assert.That(Stopwatch.GetElapsedTime(startedAt)).IsLessThan(TimeSpan.FromSeconds(3));
    }

    [Test]
    public async Task RecoverStaleProcessingAsync_ResetsExpiredSendingAttempts()
    {
        var fixture = new Fixture(new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)),
            new WebhookDeliveryProcessorSettings { BatchSize = 25, ProcessingLeaseTimeoutSeconds = 120 });
        DateTimeOffset? cutoff = null;
        fixture.TargetRepository.RecoverExpiredClaimsAsync(
                Arg.Do<DateTimeOffset>(value => cutoff = value),
                "processing_lease_expired",
                25,
                Arg.Any<CancellationToken>())
            .Returns(3);

        WebhookDeliveryRecoveryResult result = await fixture.Service.RecoverStaleProcessingAsync(CancellationToken.None);

        await Assert.That(result.RecoveredCount).IsEqualTo(3);
        await Assert.That(cutoff).IsNotNull();
        await Assert.That(Math.Abs((result.RecoveryCutoffUtc - cutoff!.Value).TotalMilliseconds)).IsLessThan(5);
    }

    [Test]
    public async Task ScheduleManualRetryAsync_WhenAttemptAndTargetAreTerminal_ReopensCanonicalTarget()
    {
        var fixture = new Fixture(new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)));
        var attempt = CreateAttempt();
        attempt.Outcome = WebhookDeliveryAttemptOutcome.Abandoned;
        attempt.CompletedAt = DateTime.UtcNow.AddMinutes(-5);
        fixture.AttemptRepository.GetByTenantAndIdAsync(attempt.TenantId, attempt.Id, Arg.Any<CancellationToken>())
            .Returns(attempt);
        var target = fixture.ConfigureManualTarget(attempt, WebhookLocalDeliveryStatus.Abandoned);

        WebhookDeliverySingleDrainResult result = await fixture.Service.ScheduleManualRetryAsync(
            attempt.TenantId,
            attempt.Id,
            WebhookAuditPrincipalKind.User,
            "user:018f0000-0000-7000-8000-000000000001",
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(WebhookDeliveryDrainOutcome.RetryScheduled);
        await Assert.That(result.AttemptId).IsEqualTo(attempt.Id);
        await Assert.That(target.DeliveryStatus).IsEqualTo(WebhookLocalDeliveryStatus.RetryDue);
        await fixture.AttemptRepository.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
        await fixture.AuditWriter.Received(1).AppendAsync(
            Arg.Is<WebhookAuditWriteRequest>(audit =>
                audit.Action == WebhookAuditAction.DeliveryRetryScheduled &&
                audit.TargetId == attempt.Id &&
                audit.PrincipalKind == WebhookAuditPrincipalKind.User),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ScheduleManualRetryAsync_WhenTargetRetryIsAlreadyPending_ReturnsDeferredWithoutDuplicate()
    {
        var fixture = new Fixture(new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)));
        var attempt = CreateAttempt();
        attempt.Outcome = WebhookDeliveryAttemptOutcome.Failed;
        fixture.AttemptRepository.GetByTenantAndIdAsync(attempt.TenantId, attempt.Id, Arg.Any<CancellationToken>())
            .Returns(attempt);
        fixture.ConfigureManualTarget(attempt, WebhookLocalDeliveryStatus.RetryDue);

        WebhookDeliverySingleDrainResult result = await fixture.Service.ScheduleManualRetryAsync(
            attempt.TenantId,
            attempt.Id,
            WebhookAuditPrincipalKind.User,
            "user:018f0000-0000-7000-8000-000000000001",
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(WebhookDeliveryDrainOutcome.Deferred);
        await fixture.AttemptRepository.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Test]
    public async Task ScheduleManualRetryAsync_WhenEndpointIsNotActive_SkipsWithoutScheduling()
    {
        var fixture = new Fixture(new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)));
        var attempt = CreateAttempt();
        attempt.Outcome = WebhookDeliveryAttemptOutcome.Abandoned;
        fixture.ConfigureManualTarget(attempt, WebhookLocalDeliveryStatus.Abandoned);
        attempt.Endpoint!.Status = WebhookEndpointStatus.Disabled;
        fixture.AttemptRepository.GetByTenantAndIdAsync(attempt.TenantId, attempt.Id, Arg.Any<CancellationToken>())
            .Returns(attempt);

        WebhookDeliverySingleDrainResult result = await fixture.Service.ScheduleManualRetryAsync(
            attempt.TenantId,
            attempt.Id,
            WebhookAuditPrincipalKind.User,
            "user:018f0000-0000-7000-8000-000000000001",
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(WebhookDeliveryDrainOutcome.Skipped);
        await fixture.AttemptRepository.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
    }

    [Test]
    public async Task ScheduleManualRetryAsync_WhenPayloadRetentionExpired_SkipsWithoutScheduling()
    {
        var fixture = new Fixture(new RecordingMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent)));
        var attempt = CreateAttempt(DateTime.UtcNow.AddDays(-15));
        attempt.Outcome = WebhookDeliveryAttemptOutcome.Abandoned;
        fixture.ConfigureManualTarget(attempt, WebhookLocalDeliveryStatus.Abandoned);
        fixture.AttemptRepository.GetByTenantAndIdAsync(attempt.TenantId, attempt.Id, Arg.Any<CancellationToken>())
            .Returns(attempt);

        var result = await fixture.Service.ScheduleManualRetryAsync(
            attempt.TenantId,
            attempt.Id,
            WebhookAuditPrincipalKind.User,
            "user:018f0000-0000-7000-8000-000000000001",
            CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(WebhookDeliveryDrainOutcome.Skipped);
        await fixture.AttemptRepository.DidNotReceiveWithAnyArgs().CreateAsync(default!, default);
        await fixture.AuditWriter.DidNotReceive().AppendAsync(
            Arg.Any<WebhookAuditWriteRequest>(),
            Arg.Any<CancellationToken>());
    }

    private static WebhookDeliveryAttempt CreateAttempt(
        DateTime? createdAtOverride = null,
        byte[]? payloadBytes = null)
    {
        var tenantId = Guid.CreateVersion7();
        var endpointId = Guid.CreateVersion7();
        var consumerId = Guid.CreateVersion7();
        var createdAt = createdAtOverride ?? DateTime.UtcNow;
        var message = WebhookMessage.Create(
            tenantId,
            "event.published",
            "domain-event-1",
            "Event",
            Guid.CreateVersion7(),
            consumerId,
            payloadBytes ?? "{\"type\":\"event.published\"}"u8.ToArray(),
            "application/json",
            "utf-8",
            createdAt,
            createdAt.AddDays(14),
            createdAt);
        return new WebhookDeliveryAttempt
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            MessageId = message.Id,
            Message = message,
            EndpointId = endpointId,
            Endpoint = new WebhookEndpoint
            {
                Id = endpointId,
                TenantId = tenantId,
                ConsumerId = consumerId,
                Url = "https://93.184.216.34/webhook",
                Status = WebhookEndpointStatus.Active,
                SecretRef = "endpoint-one",
                SecretVersion = 1,
                MaxAttempts = 8,
                TimeoutSeconds = 15,
                CreatedAt = DateTime.UtcNow
            },
            AttemptNumber = 1,
            Outcome = WebhookDeliveryAttemptOutcome.Scheduled,
            ScheduledAt = createdAt.AddSeconds(-1),
            CreatedAt = createdAt
        };
    }

    private static WebhookLocalTargetClaim CreateTargetClaim()
    {
        var now = DateTimeOffset.UtcNow;
        var tenantId = Guid.CreateVersion7();
        var consumerId = Guid.CreateVersion7();
        var message = WebhookMessage.Create(
            tenantId,
            "event.published",
            "canonical-local-target",
            "Event",
            Guid.CreateVersion7(),
            consumerId,
            "{\"type\":\"event.published\"}"u8,
            "application/json",
            "utf-8",
            now.UtcDateTime,
            now.AddDays(14).UtcDateTime,
            now.UtcDateTime);
        var plan = WebhookDeliveryPlanSnapshot.Create(
            tenantId,
            message.Id,
            consumerId,
            WebhookProviderMode.Local,
            "consumer-v1",
            "contract-v1",
            "standard",
            "retention-v1",
            now.AddDays(14),
            now.AddDays(30),
            now.AddDays(90),
            now.AddDays(90),
            now.AddDays(30),
            now);
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ConsumerId = consumerId,
            Url = "https://93.184.216.34/webhook",
            Status = WebhookEndpointStatus.Active,
            SecretRef = "endpoint-one",
            SecretVersion = 1,
            SecretActivatedAt = now.AddDays(-1).UtcDateTime,
            ConfigurationVersion = 1,
            MaxAttempts = 8,
            TimeoutSeconds = 15,
            CreatedAt = now.AddDays(-1).UtcDateTime
        };
        var target = WebhookLocalTargetSnapshot.Create(
            plan,
            endpoint,
            endpoint.ConfigurationVersion,
            now.AddDays(-1),
            null,
            now);
        var leaseToken = Guid.CreateVersion7();
        target.ClaimForDelivery("test-worker", leaseToken, now.AddMinutes(2), now);
        return new WebhookLocalTargetClaim(
            target,
            message,
            leaseToken,
            target.DeliveryFence,
            now,
            now.AddMinutes(2));
    }

    private static WebhookLocalTargetClaim CreateTargetClaim(WebhookDeliveryAttempt attempt)
    {
        var endpoint = attempt.Endpoint!;
        var message = attempt.Message!;
        var capturedAt = new DateTimeOffset(DateTime.SpecifyKind(attempt.CreatedAt, DateTimeKind.Utc));
        var plan = WebhookDeliveryPlanSnapshot.Create(
            attempt.TenantId,
            message.Id,
            endpoint.ConsumerId,
            WebhookProviderMode.Local,
            "consumer-v1",
            "contract-v1",
            "standard",
            "retention-v1",
            capturedAt.AddDays(14),
            capturedAt.AddDays(30),
            capturedAt.AddDays(90),
            capturedAt.AddDays(90),
            capturedAt.AddDays(30),
            capturedAt);
        endpoint.SecretActivatedAt = capturedAt.AddDays(-1).UtcDateTime;
        endpoint.ConfigurationVersion = Math.Max(1, endpoint.ConfigurationVersion);
        var target = WebhookLocalTargetSnapshot.Create(
            plan,
            endpoint,
            endpoint.ConfigurationVersion,
            capturedAt.AddDays(-1),
            null,
            capturedAt);
        var claimedAt = DateTimeOffset.UtcNow;
        var leaseToken = Guid.CreateVersion7();
        target.ClaimForDelivery("test-worker", leaseToken, claimedAt.AddMinutes(2), claimedAt);
        return new WebhookLocalTargetClaim(
            target,
            message,
            leaseToken,
            target.DeliveryFence,
            claimedAt,
            claimedAt.AddMinutes(2));
    }

    private static string CreateSvixSecret() =>
        "whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes("local-webhook-signing-secret"));

    private static WebhookOptions CreateWebhookOptions() => new()
    {
        Enabled = true,
        Provider = WebhookOptions.ProviderLocal,
        Local = new WebhookLocalOptions
        {
            BlockPrivateNetworks = true,
            MaxAttempts = 8,
            MaxPayloadBytes = 1024 * 1024,
            MaxResponsePreviewBytes = 4096,
            TimeoutSeconds = 15,
            ConnectTimeoutSeconds = 3
        }
    };

    private sealed class Fixture
    {
        public Fixture(
            HttpMessageHandler handler,
            WebhookDeliveryProcessorSettings? settings = null,
            WebhookOptions? webhookOptions = null,
            WebhookDeliveryGovernancePolicy? deliveryPolicy = null,
            IConfiguration? secretConfiguration = null)
        {
            AttemptRepository = Substitute.For<IWebhookDeliveryAttemptRepository>();
            TargetRepository = Substitute.For<IWebhookLocalTargetRepository>();
            EndpointRepository = Substitute.For<IWebhookEndpointRepository>();
            MessageRepository = Substitute.For<IWebhookMessageRepository>();
            var services = new ServiceCollection();
            services.AddSingleton(AttemptRepository);
            services.AddSingleton(TargetRepository);
            services.AddSingleton(EndpointRepository);
            services.AddSingleton(MessageRepository);
            services.AddSingleton(Substitute.For<ITenantContextAccessor>());
            AuditWriter = Substitute.For<IWebhookAuditEventWriter>();
            services.AddSingleton(AuditWriter);
            services.AddSingleton<IUnitOfWork>(new InlineUnitOfWork());
            GovernanceResolver = Substitute.For<IWebhookDeliveryGovernanceResolver>();
            DeliveryPolicy = deliveryPolicy ?? new WebhookDeliveryGovernancePolicy(
                16,
                4,
                1,
                10,
                8,
                15,
                5,
                "test-policy-v1");
            GovernanceResolver.ResolveAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(DeliveryPolicy);
            services.AddSingleton(GovernanceResolver);
            ServiceProvider = services.BuildServiceProvider();

            EndpointRepository.RecordFailureAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<Guid>(),
                    Arg.Any<Guid>(),
                    Arg.Any<Guid>(),
                    Arg.Any<long>(),
                    Arg.Any<DateTime>(),
                    Arg.Any<string>(),
                    Arg.Any<int>(),
                    Arg.Any<CancellationToken>())
                .Returns(new WebhookEndpointFailureState(1, false));

            var configuration = secretConfiguration ?? new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [$"{WebhookOptions.SectionName}:EndpointSecrets:endpoint-one"] = CreateSvixSecret()
                })
                .Build();
            var meterFactory = Substitute.For<IMeterFactory>();
            meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
            var options = webhookOptions ?? CreateWebhookOptions();

            Service = new WebhookDeliveryDrainService(
                ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                new StaticHttpClientFactory(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }),
                new WebhookSignatureService(),
                new WebhookEndpointSafetyPolicy(new StaticOptionsMonitor<WebhookOptions>(options)),
                new WebhookRetryScheduler(),
                new WebhookEndpointSecretResolver(configuration),
                Options.Create(settings ?? new WebhookDeliveryProcessorSettings()),
                new StaticOptionsMonitor<WebhookOptions>(options),
                new BusinessMetrics(meterFactory),
                NullLogger<WebhookDeliveryDrainService>.Instance);
        }

        public IWebhookDeliveryAttemptRepository AttemptRepository { get; }

        public IWebhookLocalTargetRepository TargetRepository { get; }

        public IWebhookEndpointRepository EndpointRepository { get; }

        public IWebhookMessageRepository MessageRepository { get; }

        public IWebhookAuditEventWriter AuditWriter { get; }

        public IWebhookDeliveryGovernanceResolver GovernanceResolver { get; }

        public WebhookDeliveryGovernancePolicy DeliveryPolicy { get; }

        public ServiceProvider ServiceProvider { get; }

        public WebhookDeliveryDrainService Service { get; }

        public WebhookLocalTargetClaim? LastTargetClaim { get; private set; }

        public void ConfigureClaim(WebhookDeliveryAttempt attempt)
        {
            ConfigureTargetClaim(CreateTargetClaim(attempt));
        }

        public WebhookLocalTargetSnapshot ConfigureManualTarget(
            WebhookDeliveryAttempt attempt,
            WebhookLocalDeliveryStatus status)
        {
            var claim = CreateTargetClaim(attempt);
            var transitionedAt = claim.ClaimedAtUtc;
            switch (status)
            {
                case WebhookLocalDeliveryStatus.Delivering:
                    break;
                case WebhookLocalDeliveryStatus.RetryDue:
                    claim.Target.ScheduleRetry(
                        claim.LeaseToken,
                        claim.DeliveryFence,
                        transitionedAt.AddMinutes(1),
                        transitionedAt);
                    break;
                case WebhookLocalDeliveryStatus.DeadLettered:
                    claim.Target.DeadLetter(claim.LeaseToken, claim.DeliveryFence, transitionedAt);
                    break;
                case WebhookLocalDeliveryStatus.Abandoned:
                    claim.Target.Abandon(claim.LeaseToken, claim.DeliveryFence, transitionedAt);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status));
            }

            TargetRepository.GetByMessageAndEndpointForUpdateAsync(
                    attempt.TenantId,
                    attempt.MessageId,
                    attempt.EndpointId,
                    Arg.Any<CancellationToken>())
                .Returns(claim.Target);
            return claim.Target;
        }

        public void ConfigureTargetClaim(WebhookLocalTargetClaim claim)
        {
            typeof(WebhookLocalTargetSnapshot)
                .GetProperty(nameof(WebhookLocalTargetSnapshot.WebhookMessage))!
                .SetValue(claim.Target, claim.Message);
            LastTargetClaim = claim;
            TargetRepository.GetDueTenantIdsAsync(
                    Arg.Any<int>(),
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<CancellationToken>())
                .Returns([claim.Target.TenantId]);
            TargetRepository.CountDueAsync(
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<CancellationToken>())
                .Returns(1);
            TargetRepository.ClaimDueAsync(
                    Arg.Any<WebhookLocalTargetClaimRequest>(),
                    Arg.Any<IReadOnlyDictionary<Guid, WebhookDeliveryClaimLimits>>(),
                    Arg.Any<CancellationToken>())
                .Returns([claim]);
            TargetRepository.GetActiveClaimAsync(
                    claim.Target.TenantId,
                    claim.Target.Id,
                    claim.LeaseToken,
                    claim.DeliveryFence,
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<CancellationToken>())
                .Returns(claim.Target);
            AttemptRepository.CreateAsync(
                    Arg.Any<WebhookDeliveryAttempt>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo => callInfo.Arg<WebhookDeliveryAttempt>());
        }
    }

    private sealed class InlineUnitOfWork : IUnitOfWork
    {
        public async Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default) =>
            await operation(ct);

        public async Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) =>
            await operation(ct);

        public Task<T> ExecuteSerializableAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => ExecuteInTransactionAsync(operation, ct);
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public byte[] PayloadBytes { get; private set; } = [];
        public string? RequestUri { get; private set; }
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri?.AbsoluteUri;
            PayloadBytes = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            foreach (var header in request.Headers)
            {
                Headers[header.Key] = string.Join(" ", header.Value);
            }

            return responseFactory(request);
        }
    }

    private sealed class NeverCompletingMessageHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable after cancellation.");
        }
    }

    private sealed class CountingReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int BytesRead { get; private set; }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var bytesRead = base.Read(buffer.Span);
            BytesRead += bytesRead;
            return ValueTask.FromResult(bytesRead);
        }
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T currentValue)
        {
            CurrentValue = currentValue;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
