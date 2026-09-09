using Explore.Application.Contracts.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class EmailDispatchRetentionTelemetryTests
{
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
        var repository = Substitute.For<IEmailDispatchOutboxRepository>();
        repository.GetRetentionTenantIds(
                utcNow.AddDays(-settings.RetentionDays), settings.MaxTenantsPerPass,
                Arg.Any<CancellationToken>())
            .Returns(new[] { firstTenant, secondTenant });
        repository.CountRetentionRedactionEligible(
                firstTenant, Arg.Any<DateTime>(), settings.BatchSize, Arg.Any<CancellationToken>())
            .Returns(3);
        repository.CountRetentionRedactionEligible(
                secondTenant, Arg.Any<DateTime>(), settings.BatchSize, Arg.Any<CancellationToken>())
            .Returns(4);
        var logger = new TestListLogger<EmailDispatchRetentionCleanupService>();
        var service = new EmailDispatchRetentionCleanupService(
            repository, Substitute.For<IUnitOfWork>(), Options.Create(settings), logger);

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
}
