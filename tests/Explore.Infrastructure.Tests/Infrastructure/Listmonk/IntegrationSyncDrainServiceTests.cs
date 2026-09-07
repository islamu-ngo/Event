using System.Net;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Integrations.Listmonk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Listmonk;

public sealed class IntegrationSyncDrainServiceTests
{
    [Test]
    public async Task ProcessBatchAsync_WhenListmonkAcceptsSubscriber_MarksOutboxCompleted()
    {
        var outbox = CreateOutbox();
        var fixture = new Fixture(_ => JsonResponse(HttpStatusCode.OK, "{\"data\":{\"id\":123}}"));
        fixture.Repository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([outbox]);
        fixture.Repository.TryClaimAsync(Arg.Any<IntegrationSyncClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(true);
        fixture.ConfigureActiveClaim(outbox);

        IntegrationSyncDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.Completed).IsEqualTo(1);
        await Assert.That(result.RetryScheduled).IsEqualTo(0);
        await fixture.Repository.Received(1).CompleteAsync(Arg.Any<IntegrationSyncClaimIdentity>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await fixture.Repository.DidNotReceiveWithAnyArgs().FailAsync(default!, default!, default, default, default, default, default);
    }

    [Test]
    public async Task ProcessBatchAsync_WhenClaimFails_ReturnsAlreadyClaimedWithoutHttpSend()
    {
        var outbox = CreateOutbox();
        var fixture = new Fixture(_ => throw new InvalidOperationException("HTTP should not be called."));
        fixture.Repository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([outbox]);
        fixture.Repository.TryClaimAsync(Arg.Any<IntegrationSyncClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(false);

        IntegrationSyncDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.AlreadyClaimed).IsEqualTo(1);
        await Assert.That(fixture.Handler.CallCount).IsEqualTo(0);
        await fixture.Repository.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default, default);
        await fixture.Repository.DidNotReceiveWithAnyArgs().FailAsync(default!, default!, default, default, default, default, default);
    }

    [Test]
    public async Task ProcessBatchAsync_WhenClaimDisappearsAfterClaim_ReturnsAlreadyClaimedWithoutListmonkCall()
    {
        var outbox = CreateOutbox();
        var fixture = new Fixture(_ => throw new InvalidOperationException("HTTP should not be called."));
        fixture.Repository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([outbox]);
        fixture.Repository.TryClaimAsync(Arg.Any<IntegrationSyncClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(true);
        fixture.ConfigureMissingActiveClaim(outbox);

        IntegrationSyncDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.AlreadyClaimed).IsEqualTo(1);
        await Assert.That(fixture.Handler.CallCount).IsEqualTo(0);
        await fixture.PrivacyErasureStateRepository.DidNotReceiveWithAnyArgs().GetBySubjectAsync(default, default);
        await fixture.Repository.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default, default);
        await fixture.Repository.DidNotReceiveWithAnyArgs().FailAsync(default!, default!, default, default, default, default, default);
    }

    [Test]
    public async Task ProcessBatchAsync_WhenUserIsFencedAfterClaim_DeadLettersWithoutListmonkCall()
    {
        var outbox = CreateOutbox();
        var fixture = new Fixture(_ => throw new InvalidOperationException("HTTP should not be called."));
        fixture.Repository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([outbox]);
        fixture.Repository.TryClaimAsync(Arg.Any<IntegrationSyncClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(true);
        fixture.ConfigureActiveClaim(outbox);
        fixture.PrivacyErasureStateRepository.GetBySubjectAsync(outbox.UserId!.Value, Arg.Any<CancellationToken>())
            .Returns(CreateFencedSaga(outbox.UserId.Value));

        IntegrationSyncDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.DeadLettered).IsEqualTo(1);
        await Assert.That(fixture.Handler.CallCount).IsEqualTo(0);
        await fixture.PrivacyErasureStateRepository.Received(1).GetBySubjectAsync(outbox.UserId.Value, Arg.Any<CancellationToken>());
        await fixture.Repository.Received(1).FailAsync(
            Arg.Any<IntegrationSyncClaimIdentity>(),
            "Integration sync was not sent because the subscriber is subject to privacy erasure.",
            false,
            Arg.Any<TimeSpan>(),
            5,
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_WhenClaimHasNoUser_DeadLettersWithoutListmonkCall()
    {
        var outbox = CreateOutbox();
        outbox.UserId = null;
        var fixture = new Fixture(_ => throw new InvalidOperationException("HTTP should not be called."));
        fixture.Repository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([outbox]);
        fixture.Repository.TryClaimAsync(Arg.Any<IntegrationSyncClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(true);
        fixture.ConfigureActiveClaim(outbox);

        IntegrationSyncDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.DeadLettered).IsEqualTo(1);
        await Assert.That(fixture.Handler.CallCount).IsEqualTo(0);
        await fixture.Repository.Received(1).FailAsync(
            Arg.Any<IntegrationSyncClaimIdentity>(),
            "Integration sync has no durable user identity.",
            false,
            TimeSpan.Zero,
            5,
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_WhenFenceCheckIsCancelled_DoesNotCallListmonkOrSettleClaim()
    {
        var outbox = CreateOutbox();
        var fixture = new Fixture(_ => throw new InvalidOperationException("HTTP should not be called."));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        fixture.Repository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([outbox]);
        fixture.Repository.TryClaimAsync(Arg.Any<IntegrationSyncClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(true);
        fixture.ConfigureActiveClaim(outbox);
        fixture.PrivacyErasureStateRepository.GetBySubjectAsync(outbox.UserId!.Value, Arg.Any<CancellationToken>())
            .Returns(Task.FromCanceled<PrivacyErasureSaga?>(cancellation.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Service.ProcessBatchAsync(cancellation.Token));

        await Assert.That(fixture.Handler.CallCount).IsEqualTo(0);
        await fixture.Repository.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default, default);
        await fixture.Repository.DidNotReceiveWithAnyArgs().FailAsync(default!, default!, default, default, default, default, default);
    }

    [Test]
    public async Task ProcessBatchAsync_WhenListmonkOutcomeIsAmbiguous_ParksWithoutRetry()
    {
        var outbox = CreateOutbox();
        var fixture = new Fixture(_ => JsonResponse(HttpStatusCode.InternalServerError, "{\"error\":\"down\"}"));
        fixture.Repository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([outbox]);
        fixture.Repository.TryClaimAsync(Arg.Any<IntegrationSyncClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(true);
        fixture.ConfigureActiveClaim(outbox);

        IntegrationSyncDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.Ambiguous).IsEqualTo(1);
        await fixture.Repository.Received(1).ParkAmbiguousAsync(
            Arg.Any<IntegrationSyncClaimIdentity>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_WhenStaleProviderHandoffIsInDoubt_ParksWithoutProviderReplay()
    {
        var outbox = CreateOutbox();
        outbox.Status = IntegrationSyncStatus.Processing;
        outbox.ProcessingLeaseToken = Guid.CreateVersion7();
        outbox.ProcessingStartedAt = DateTime.UtcNow.AddMinutes(-10);
        outbox.LastError = IntegrationSyncFailureCodes.ProviderHandoffInDoubt;
        var fixture = new Fixture(_ => throw new InvalidOperationException("HTTP should not be called."));
        fixture.Repository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([outbox]);

        IntegrationSyncDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.Ambiguous).IsEqualTo(1);
        await Assert.That(fixture.Handler.CallCount).IsEqualTo(0);
        await fixture.Repository.DidNotReceiveWithAnyArgs().TryClaimAsync(default!, default);
        await fixture.Repository.Received(1).ParkAmbiguousAsync(
            Arg.Is<IntegrationSyncClaimIdentity>(claim =>
                claim.TenantId == outbox.TenantId &&
                claim.OutboxId == outbox.Id &&
                claim.LeaseToken == outbox.ProcessingLeaseToken!.Value &&
                claim.ProcessingStartedAt == outbox.ProcessingStartedAt!.Value),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProcessBatchAsync_WhenPayloadIsInvalid_DeadLettersWithoutHttpSend()
    {
        var outbox = CreateOutbox("{");
        var fixture = new Fixture(_ => throw new InvalidOperationException("HTTP should not be called."));
        fixture.Repository.GetPendingBatch(Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([outbox]);
        fixture.Repository.TryClaimAsync(Arg.Any<IntegrationSyncClaimRequest>(), Arg.Any<CancellationToken>())
            .Returns(true);
        fixture.ConfigureActiveClaim(outbox);

        IntegrationSyncDrainResult result = await fixture.Service.ProcessBatchAsync(CancellationToken.None);

        await Assert.That(result.DeadLettered).IsEqualTo(1);
        await Assert.That(fixture.Handler.CallCount).IsEqualTo(0);
        await fixture.Repository.Received(1).FailAsync(
            Arg.Any<IntegrationSyncClaimIdentity>(),
            Arg.Any<string>(),
            false,
            Arg.Any<TimeSpan>(),
            5,
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    private static IntegrationSyncOutbox CreateOutbox(string? payloadJson = null)
    {
        return new IntegrationSyncOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            Kind = IntegrationKind.Listmonk,
            SourceType = "registration_order",
            SourceId = Guid.CreateVersion7(),
            SubscriberEmail = "attendee@example.test",
            SubscriberName = "Attendee Example",
            SubscriberPayloadJson = payloadJson ?? "{\"email\":\"attendee@example.test\"}",
            ListmonkListId = 42,
            PreconfirmSubscriptions = true,
            AttemptCount = 1,
            MaxAttempts = 5
        };
    }

    private static PrivacyErasureSaga CreateFencedSaga(Guid userId)
    {
        DateTime nowUtc = DateTime.UtcNow;
        PrivacyErasureIntent intent = PrivacyErasureIntent.Record(
            Guid.CreateVersion7(),
            1,
            PrivacyErasureSubjectKind.User,
            userId,
            PrivacyErasureReasonCode.AccountDeletion,
            1,
            nowUtc,
            nowUtc);
        return PrivacyErasureSaga.Start(intent, 1, new byte[32], nowUtc.AddMinutes(5), nowUtc);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class Fixture
    {
        public Fixture(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            Handler = new RecordingMessageHandler(responseFactory);
            Repository = Substitute.For<IIntegrationSyncOutboxRepository>();
            PrivacyErasureStateRepository = Substitute.For<IPrivacyErasureStateRepository>();
            PrivacyErasureStateRepository.GetBySubjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns((PrivacyErasureSaga?)null);
            Repository.CompleteAsync(Arg.Any<IntegrationSyncClaimIdentity>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(true);
            Repository.FailAsync(
                    Arg.Any<IntegrationSyncClaimIdentity>(),
                    Arg.Any<string>(),
                    Arg.Any<bool>(),
                    Arg.Any<TimeSpan>(),
                    Arg.Any<int>(),
                    Arg.Any<DateTime>(),
                    Arg.Any<CancellationToken>())
                .Returns(true);
            Repository.MarkProviderHandoffStartedAsync(
                    Arg.Any<IntegrationSyncClaimIdentity>(),
                    Arg.Any<DateTime>(),
                    Arg.Any<CancellationToken>())
                .Returns(true);
            Repository.ParkAmbiguousAsync(
                    Arg.Any<IntegrationSyncClaimIdentity>(),
                    Arg.Any<DateTime>(),
                    Arg.Any<CancellationToken>())
                .Returns(true);
            Repository.ParkMalformedProcessingAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<Guid>(),
                    Arg.Any<DateTime>(),
                    Arg.Any<CancellationToken>())
                .Returns(true);

            var settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
            settingsResolver.ResolveAsync<string>(
                    GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl,
                    Arg.Any<SettingContext>(),
                    Arg.Any<CancellationToken>())
                .Returns("https://listmonk.example.test");

            var secretResolver = Substitute.For<ISecretResolver>();
            secretResolver.ResolveAsync(
                    SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiUsername,
                    Arg.Any<Guid?>(),
                    Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret(SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiUsername,
            "listmonk-user",
            SecretSourceType.EnvironmentVariable,
            SecretScope.Tenant,
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow)));
            secretResolver.ResolveAsync(
                    SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiKey,
                    Arg.Any<Guid?>(),
                    Arg.Any<CancellationToken>()).Returns(SecretResolutionResult.Resolved(new ResolvedSecret(SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiKey,
            "listmonk-key",
            SecretSourceType.EnvironmentVariable,
            SecretScope.Tenant,
            Guid.CreateVersion7(),
            DateTimeOffset.UtcNow)));

            var httpClientFactory = Substitute.For<IHttpClientFactory>();
            httpClientFactory.CreateClient(ListmonkSyncService.HttpClientName)
                .Returns(_ => new HttpClient(Handler));

            var services = new ServiceCollection();
            services.AddSingleton(Repository);
            services.AddSingleton(PrivacyErasureStateRepository);
            services.AddSingleton(settingsResolver);
            services.AddSingleton(secretResolver);
            services.AddSingleton(httpClientFactory);
            services.AddSingleton(Substitute.For<ITenantContextAccessor>());
            services.AddSingleton<ILogger<ListmonkSyncService>>(NullLogger<ListmonkSyncService>.Instance);
            services.AddScoped<ListmonkSyncService>();
            ServiceProvider = services.BuildServiceProvider();

            Service = new IntegrationSyncDrainService(
                ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new IntegrationSyncProcessorSettings
                {
                    BatchSize = 10,
                    MaxAttemptCount = 5,
                    InitialRetryDelaySeconds = 5,
                    MaxRetryDelaySeconds = 60
                }),
                NullLogger<IntegrationSyncDrainService>.Instance);
        }

        public RecordingMessageHandler Handler { get; }
        public IIntegrationSyncOutboxRepository Repository { get; }
        public IPrivacyErasureStateRepository PrivacyErasureStateRepository { get; }
        public IntegrationSyncDrainService Service { get; }
        private ServiceProvider ServiceProvider { get; }

        public void ConfigureActiveClaim(IntegrationSyncOutbox outbox)
        {
            Repository.GetActiveClaimAsync(Arg.Any<IntegrationSyncClaimIdentity>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    var claim = callInfo.ArgAt<IntegrationSyncClaimIdentity>(0);
                    outbox.Status = IntegrationSyncStatus.Processing;
                    outbox.ProcessingLeaseToken = claim.LeaseToken;
                    outbox.ProcessingStartedAt = claim.ProcessingStartedAt;
                    return outbox;
                });
        }

        public void ConfigureMissingActiveClaim(IntegrationSyncOutbox outbox)
        {
            Repository.GetActiveClaimAsync(Arg.Any<IntegrationSyncClaimIdentity>(), Arg.Any<CancellationToken>())
                .Returns((IntegrationSyncOutbox?)null);
        }
    }

    private sealed class RecordingMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(responseFactory(request));
        }
    }
}
