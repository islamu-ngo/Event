using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Models.Tenants;
using Explore.Domain;
using Explore.Infrastructure.Webhooks;
using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
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
        var directory = Substitute.For<ITenantLookupSource>();
        directory.GetTenantLookupsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<TenantLookupRecord> { new() { TenantId = operation.TenantId, Slug = "replay" } });
        services.AddSingleton(directory);
        services.AddScoped<ITenantContextAccessor>(_ => new TenantContextAccessor(new HttpContextAccessor()));
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

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FailureDiagnosticsExportOnlyNativeCategories(bool settlementFails)
    {
        var operation = CreateOperation();
        string canary = Guid.CreateVersion7().ToString("N");
        var failure = new SensitiveReplayFailure(canary);
        var repository = Substitute.For<IWebhookBulkReplayRepository>();
        repository.GetNextQueuedForUpdateAsync(Arg.Any<CancellationToken>()).Returns(operation);
        repository.ScheduleEligibleLocalTargetsAsync(operation, UtcNow, Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw failure);
        repository.GetByTenantAndIdAsync(operation.TenantId, operation.Id, Arg.Any<CancellationToken>())
            .Returns<WebhookBulkReplayOperation?>(_ => settlementFails ? throw failure : null);
        var directory = Substitute.For<ITenantLookupSource>();
        directory.GetTenantLookupsAsync(Arg.Any<CancellationToken>()).Returns(
            new List<TenantLookupRecord> { new() { TenantId = operation.TenantId, Slug = "replay" } });
        var services = new ServiceCollection();
        services.AddSingleton(repository);
        services.AddSingleton(Substitute.For<IWebhookAuditEventWriter>());
        services.AddSingleton<IUnitOfWork>(new InlineUnitOfWork());
        services.AddSingleton(directory);
        services.AddScoped<ITenantContextAccessor>(_ => new TenantContextAccessor(new HttpContextAccessor()));
        await using var provider = services.BuildServiceProvider();
        var policy = Substitute.For<IWebhookBulkReplayPolicyResolver>();
        policy.Resolve().Returns(new WebhookBulkReplayLimits(100, 500, 30, 10, "webhook-bulk-replay-test-v1"));
        var logger = new ReplayDiagnosticCollector();
        var service = new WebhookBulkReplayService(provider.GetRequiredService<IServiceScopeFactory>(),
            policy, new FixedTimeProvider(UtcNow), logger);

        var result = await service.ProcessQueuedAsync();

        await Assert.That(result.FailedOperations).IsEqualTo(1);
        await Assert.That(result.CompletedOperations).IsEqualTo(0);
        await Assert.That(result.ScheduledTargets).IsEqualTo(0);
        await Assert.That(logger.Categories).IsEquivalentTo(settlementFails
            ? new[] { "bulk_replay_execution_failed", "bulk_replay_failure_settlement_failed" }
            : new[] { "bulk_replay_execution_failed" });
        await Assert.That(logger.Exceptions.All(value => value is null)).IsTrue();
        await Assert.That(string.Join('\n', logger.Exports)).DoesNotContain(canary);
        await Assert.That(string.Join('\n', logger.Exports)).DoesNotContain(nameof(SensitiveReplayFailure));
    }

    private sealed class SensitiveReplayFailure(string details) : Exception(details);

    private sealed class ReplayDiagnosticCollector : ILogger<WebhookBulkReplayService>
    {
        public List<string> Categories { get; } = [];
        public List<string> Exports { get; } = [];
        public List<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Exceptions.Add(exception);
            Exports.Add(formatter(state, exception));
            foreach (var field in (IEnumerable<KeyValuePair<string, object?>>)(object)state!)
            {
                Exports.Add($"{field.Key}={field.Value}");
                if (field.Key == "FailureCategory" && field.Value is string category)
                    Categories.Add(category);
            }
        }
    }

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

        public Task<T> ExecuteReadCommittedAsync<T>(
            Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
    }
}
