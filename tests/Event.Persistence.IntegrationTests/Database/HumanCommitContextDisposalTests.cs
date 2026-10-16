using Explore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class HumanCommitContextDisposalTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DisposalClearsHumanAttemptOwnership(bool asynchronous)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite("Data Source=:memory:").UseSnakeCaseNamingConvention();
        var context = new ExploreDbContext(options.Options)
        {
            HumanCommitAttemptOwnsTransaction = true
        };

        if (asynchronous) await context.DisposeAsync();
        else context.Dispose();

        await Assert.That(context.HumanCommitAttemptOwnsTransaction).IsFalse();
        await Assert.That(context.HasHumanCommitContextLease).IsFalse();
        await Assert.That(context.HumanCommitAuditSubject).IsNull();
    }
}
