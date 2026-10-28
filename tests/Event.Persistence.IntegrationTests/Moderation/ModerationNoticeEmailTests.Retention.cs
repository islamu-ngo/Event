using System.Data.Common;
using Explore.Application.Models;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeEmailTests
{
    [Test]
    [Arguments(0L)]
    [Arguments(1L)]
    public async Task ExpiredOriginalPurposeRejectsSmtpWithoutSelectingPrivateSnapshot(long afterCeilingTicks)
    {
        await using var fixture = await EmailFixture.CreateAsync();
        var request = await fixture.AdmitAsync();
        fixture.Notice.Clock.UtcNow = fixture.Notice.Approval.RetainUntil.AddTicks(afterCeilingTicks);
        var probe = new PrivateSnapshotReadProbe();
        await using var borrowed = fixture.Notice.Database.Context();
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(borrowed.Database.GetDbConnection()).UseSnakeCaseNamingConvention()
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance, probe).Options;
        await using var context = new ExploreDbContext(options) { TenantContext = fixture.Notice.Database };
        var result = await fixture.CreateAuthority(context).SendAsync(request,
            (_, _) => throw new InvalidOperationException("Expired original purpose reached SMTP."),
            CancellationToken.None);
        await Assert.That(result.Outcome).IsEqualTo(SmtpDeliveryOutcome.AuthorityRejected);
        await Assert.That(probe.PrivateSnapshotReads).IsEqualTo(0);
    }

    private sealed class PrivateSnapshotReadProbe : DbCommandInterceptor
    {
        public int PrivateSnapshotReads { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("snapshot_json", StringComparison.OrdinalIgnoreCase))
                PrivateSnapshotReads++;
            return ValueTask.FromResult(result);
        }
    }
}
