using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationRecognitionReplicaOwnershipTests
{
    [Test]
    public async Task ExclusiveOwnerRejectsCompetitorUntilDisposed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"recognition-owner-{Guid.CreateVersion7():N}.sqlite");
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = path, Pooling = false }.ToString());
            await connection.OpenAsync();
            await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(connection).Options);
            using var first = new ModerationRecognitionKeyLifecycleReplicaOwner();
            using var second = new ModerationRecognitionKeyLifecycleReplicaOwner();
            var identity = first.Acquire(context);
            await Assert.That(first.Acquire(context)).IsEqualTo(identity);
            await Assert.That(() => second.Acquire(context)).Throws<InvalidOperationException>()
                .WithMessage("moderation_recognition_topology_unavailable");
            first.Dispose();
            await Assert.That(second.Acquire(context)).IsNotEqualTo(identity);
            await Assert.That(() => first.Acquire(context)).Throws<ObjectDisposedException>();
        }
        finally
        {
            File.Delete(path);
        }
    }
}
