using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Infrastructure.Tests.Fixtures;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class EmailDispatchRetentionTelemetryTests
{
    [Test]
    public async Task CleanupTransactionStartFailureReportsSafeMetadataAndPreservesRetryableContent()
    {
        const string providerDetails = "retention-recipient@example.test\r\nprovider-diagnostic";
        var utcNow = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc);
        var dispatch = new EmailDispatchOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            SourceType = "event-registration",
            Subject = "Registration confirmation",
            Status = EmailDispatchStatus.Sent,
            SentAt = utcNow.AddDays(-31),
            RecipientEmail = "retention-recipient@example.test"
        };
        var repository = new InMemoryEmailDispatchOutboxRepository(dispatch);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<CancellationToken, Task<int>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<int>(new PrivateRetentionFailure(providerDetails)));
        var logger = new TestListLogger<EmailDispatchRetentionCleanupService>();
        var discovery = Substitute.For<IEmailRetentionTenantDiscoveryRepository>();
        discovery.GetTenantIdsAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new[] { dispatch.TenantId });
        var execution = Substitute.For<IEmailRetentionExecutionScopeFactory>();
        execution.CreateAsync(dispatch.TenantId, Arg.Any<CancellationToken>())
            .Returns(new RetentionScope(repository, unitOfWork, dispatch.TenantId));
        var service = new EmailDispatchRetentionCleanupService(discovery, execution,
            Options.Create(new EmailDispatchRetentionSettings { DryRun = false, RetentionDays = 30 }), logger);

        var result = await service.CleanupAsync(utcNow);

        await Assert.That(result.FailedTenantCount).IsEqualTo(1);
        await Assert.That(result.SucceededTenantCount).IsEqualTo(0);
        await Assert.That(result.RedactedCount).IsEqualTo(0);
        await Assert.That(dispatch.ContentRedactedAt).IsNull();
        await Assert.That(dispatch.RecipientEmail).IsEqualTo("retention-recipient@example.test");
        var warning = logger.Entries.Single(entry => entry.Level == LogLevel.Warning);
        await Assert.That(warning.Exception).IsNull();
        await Assert.That(warning.State.Single(field => field.Key == "FailureCategory").Value)
            .IsEqualTo("retention_cleanup_failed");
        foreach (var entry in logger.Entries)
        {
            foreach (var output in entry.State.Select(field => field.Value?.ToString() ?? string.Empty).Prepend(entry.Message))
            {
                await Assert.That(output).DoesNotContain("retention-recipient");
                await Assert.That(output).DoesNotContain("provider-diagnostic");
                await Assert.That(output).DoesNotContain(nameof(PrivateRetentionFailure));
                await Assert.That(output).DoesNotContain(dispatch.TenantId.ToString());
                await Assert.That(output.Any(char.IsControl)).IsFalse();
            }
        }
    }

    private sealed class PrivateRetentionFailure(string details) : InvalidOperationException(details);

    [Test]
    public async Task CleanupReportsAggregateCountsWithoutTenantIdentifiers()
    {
        var firstTenant = Guid.CreateVersion7();
        var secondTenant = Guid.CreateVersion7();
        var utcNow = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc);
        var settings = new EmailDispatchRetentionSettings
        {
            DryRun = true,
            RetentionDays = 30,
            MaxTenantsPerPass = 2,
            BatchSize = 100
        };
        var discovery = Substitute.For<IEmailRetentionTenantDiscoveryRepository>();
        discovery.GetTenantIdsAsync(
                utcNow.AddDays(-settings.RetentionDays), settings.MaxTenantsPerPass,
                Arg.Any<CancellationToken>())
            .Returns(new[] { firstTenant, secondTenant });
        var firstScope = Substitute.For<IEmailRetentionExecutionScope>();
        var secondScope = Substitute.For<IEmailRetentionExecutionScope>();
        firstScope.CountEligibleAsync(
                Arg.Any<DateTime>(), settings.BatchSize, Arg.Any<CancellationToken>())
            .Returns(3);
        secondScope.CountEligibleAsync(
                Arg.Any<DateTime>(), settings.BatchSize, Arg.Any<CancellationToken>())
            .Returns(4);
        var execution = Substitute.For<IEmailRetentionExecutionScopeFactory>();
        execution.CreateAsync(firstTenant, Arg.Any<CancellationToken>()).Returns(firstScope);
        execution.CreateAsync(secondTenant, Arg.Any<CancellationToken>()).Returns(secondScope);
        var logger = new TestListLogger<EmailDispatchRetentionCleanupService>();
        var service = new EmailDispatchRetentionCleanupService(
            discovery, execution, Options.Create(settings), logger);

        var result = await service.CleanupAsync(utcNow);

        await Assert.That(result.TenantCount).IsEqualTo(2);
        await Assert.That(result.SucceededTenantCount).IsEqualTo(2);
        await Assert.That(result.FailedTenantCount).IsEqualTo(0);
        await Assert.That(result.EligibleCount).IsEqualTo(7);
        await Assert.That(result.RedactedCount).IsEqualTo(0);
        var entry = logger.Entries.Single();
        await Assert.That(entry.Level).IsEqualTo(LogLevel.Information);
        await Assert.That(entry.Exception).IsNull();
        await Assert.That(entry.State.Single(field => field.Key == "TenantCount").Value).IsEqualTo(2);
        await Assert.That(entry.State.Single(field => field.Key == "EligibleCount").Value).IsEqualTo(7);
        await Assert.That(entry.Arguments.All(value => value is int or string or DateTime)).IsTrue();
        foreach (var tenant in new[] { firstTenant, secondTenant })
        {
            foreach (var output in entry.State.Select(field => field.Value?.ToString() ?? string.Empty)
                         .Prepend(entry.Message))
            {
                await Assert.That(output).DoesNotContain(tenant.ToString("D"));
                await Assert.That(output).DoesNotContain(tenant.ToString("N"));
            }
        }
    }

    private sealed class RetentionScope(IEmailDispatchOutboxRepository repository, IUnitOfWork unitOfWork, Guid tenantId)
        : IEmailRetentionExecutionScope
    {
        public Task<int> CountEligibleAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken) =>
            repository.CountRetentionRedactionEligible(tenantId, cutoffUtc, batchSize, cancellationToken);

        public Task<int> RedactAsync(DateTime cutoffUtc, DateTime redactedAt, int batchSize, CancellationToken cancellationToken) =>
            unitOfWork.ExecuteInTransactionAsync(
                ct => repository.RedactRetentionEligible(tenantId, cutoffUtc, redactedAt, batchSize, ct), cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
