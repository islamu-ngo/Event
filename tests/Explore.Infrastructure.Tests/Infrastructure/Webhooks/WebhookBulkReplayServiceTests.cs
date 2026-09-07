using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Domain;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class WebhookBulkReplayServiceTests
{
    private static readonly DateTime UtcNow =
        new(2026, 7, 14, 16, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ProcessQueuedAsync_ReopensBoundedTargetsAndCompletesWithSystemAudit()
    {
        var operation = CreateOperation();
        var repository = Substitute.For<IWebhookBulkReplayRepository>();
        repository.GetNextQueuedForUpdateAsync(Arg.Any<CancellationToken>())
            .Returns(operation, (WebhookBulkReplayOperation?)null);
        repository.ScheduleEligibleLocalTargetsAsync(
                operation,
                UtcNow,
                Arg.Any<CancellationToken>())
            .Returns(2);
        repository.UpdateAsync(operation, Arg.Any<CancellationToken>()).Returns(operation);
        var auditWriter = Substitute.For<IWebhookAuditEventWriter>();
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddSingleton(auditWriter);
        services.AddSingleton<IUnitOfWork>(new InlineUnitOfWork());
        await using var provider = services.BuildServiceProvider();
        var policyResolver = Substitute.For<IWebhookBulkReplayPolicyResolver>();
        policyResolver.Resolve().Returns(new WebhookBulkReplayLimits(
            100,
            500,
            30,
            10,
            "webhook-bulk-replay-test-v1"));
        var service = new WebhookBulkReplayService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            policyResolver,
            new FixedTimeProvider(UtcNow),
            NullLogger<WebhookBulkReplayService>.Instance);

        var result = await service.ProcessQueuedAsync(CancellationToken.None);

        await Assert.That(result.CompletedOperations).IsEqualTo(1);
        await Assert.That(result.ScheduledTargets).IsEqualTo(2);
        await Assert.That(result.FailedOperations).IsEqualTo(0);
        await Assert.That(operation.Status).IsEqualTo(WebhookBulkReplayStatus.Completed);
        await Assert.That(operation.ScheduledCount).IsEqualTo(2);
        await auditWriter.Received(1).AppendAsync(
            Arg.Is<WebhookAuditWriteRequest>(audit =>
                audit.Action == WebhookAuditAction.BulkReplayCompleted &&
                audit.TargetId == operation.Id &&
                audit.PrincipalKind == WebhookAuditPrincipalKind.System &&
                audit.PrincipalReference == "system:webhook-bulk-replay" &&
                audit.SafeAfterJson != null &&
                !audit.SafeAfterJson.Contains("tenant", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
    }

    private static WebhookBulkReplayOperation CreateOperation() =>
        WebhookBulkReplayOperation.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            $"sha256:{new string('c', 64)}",
            UtcNow.AddDays(-1),
            UtcNow,
            null,
            null,
            null,
            10,
            "operator.recovery",
            new WebhookBulkReplayPreviewSnapshot(3, 0, 0, 0, 0, 0, 0, 0, 0),
            UtcNow.AddMinutes(-1));

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class InlineUnitOfWork : IUnitOfWork
    {
        public Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken ct = default) => operation(ct);

        public Task<T> ExecuteInTransactionAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => operation(ct);

        public Task<T> ExecuteSerializableAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default) => operation(ct);
    }
}
