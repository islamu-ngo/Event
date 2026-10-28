using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Learning;
using Explore.Persistence;
using Explore.Persistence.Privacy;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Event.Persistence.IntegrationTests.Learning;

public sealed class LearningPersistenceSqliteTests
{
    internal static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ExplicitStartPersistsPrivateStateButReadNeverCreatesIt()
    {
        await using var database = await LearningDatabase.CreateAsync();
        await using var context = database.Open(database.TenantId);
        var repository = new LearningRepository(context);
        await Assert.That(await repository.GetSpaceAsync(database.TenantId, false, default)).IsNull();
        await Assert.That(await repository.GetStateAsync(database.TenantId, database.UserId, false, default)).IsNull();
        var space = TenantLearningSpace.Create(Guid.CreateVersion7(), database.TenantId, Now);
        var state = UserLearningState.Create(Guid.CreateVersion7(), database.TenantId, database.UserId, Now);
        await repository.AddSpaceAsync(space, default);
        await repository.AddStateAsync(state, default);
        await repository.SaveChangesAsync(default);
        context.ChangeTracker.Clear();
        var persisted = await repository.GetStateAsync(database.TenantId, database.UserId, false, default);
        await Assert.That(persisted).IsNotNull();
        await Assert.That(persisted!.Id).IsEqualTo(state.Id);
        await Assert.That((await repository.GetSpaceAsync(database.TenantId, false, default))!.Id).IsEqualTo(space.Id);
    }

    [Test]
    public async Task SameTenantOtherLearnerAndUnboundTenantCannotReadPrivateState()
    {
        await using var database = await LearningDatabase.CreateAsync();
        await database.SeedLearningAsync();
        await using var context = database.Open(database.TenantId);
        var repository = new LearningRepository(context);
        await Assert.That(await repository.GetStateAsync(database.TenantId, database.UserId, false, default)).IsNotNull();
        await Assert.That(await repository.GetStateAsync(database.TenantId, Guid.CreateVersion7(), false, default)).IsNull();
        await using var foreign = database.Open(database.OtherTenantId);
        await Assert.That(await new LearningRepository(foreign)
            .GetStateAsync(database.TenantId, database.UserId, false, default)).IsNull();
        await using var unbound = database.Open(null);
        await Assert.That(await new LearningRepository(unbound)
            .GetStateAsync(database.TenantId, database.UserId, false, default)).IsNull();
    }

    [Test]
    public async Task TwoFirstStartsHaveOneDatabaseEnforcedOwner()
    {
        await using var database = await LearningDatabase.CreateAsync();
        await using var first = database.Open(database.TenantId);
        await using var second = database.Open(database.TenantId);
        var firstRepository = new LearningRepository(first);
        var secondRepository = new LearningRepository(second);
        await firstRepository.AddStateAsync(
            UserLearningState.Create(Guid.CreateVersion7(), database.TenantId, database.UserId, Now), default);
        await secondRepository.AddStateAsync(
            UserLearningState.Create(Guid.CreateVersion7(), database.TenantId, database.UserId, Now), default);
        await firstRepository.SaveChangesAsync(default);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => secondRepository.SaveChangesAsync(default));
        await Assert.That(await firstRepository.GetStateAsync(database.TenantId, database.UserId, false, default)).IsNotNull();
    }

    [Test]
    public async Task StaleRevisionCannotOverwriteTheWinningPreferences()
    {
        await using var database = await LearningDatabase.CreateAsync();
        await database.SeedLearningAsync();
        await using var first = database.Open(database.TenantId);
        await using var second = database.Open(database.TenantId);
        var firstRepository = new LearningRepository(first);
        var secondRepository = new LearningRepository(second);
        var winner = (await firstRepository.GetStateAsync(database.TenantId, database.UserId, true, default))!;
        var loser = (await secondRepository.GetStateAsync(database.TenantId, database.UserId, true, default))!;
        winner.UpdatePreferences(true, false, false, winner.Revision, Now);
        loser.UpdatePreferences(false, true, true, loser.Revision, Now);
        await firstRepository.SaveChangesAsync(default);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => secondRepository.SaveChangesAsync(default));
        var stored = (await firstRepository.GetStateAsync(database.TenantId, database.UserId, false, default))!;
        await Assert.That(stored.SuppressAutomaticTours).IsTrue();
        await Assert.That(stored.IsHidden).IsFalse();
    }

    [Test]
    public async Task TwoLoadedTourClaimsCannotBothSettle()
    {
        await using var database = await LearningDatabase.CreateAsync();
        await database.SeedLearningAsync();
        await using var first = database.Open(database.TenantId);
        await using var second = database.Open(database.TenantId);
        var firstRepository = new LearningRepository(first);
        var secondRepository = new LearningRepository(second);
        var winner = (await firstRepository.GetStateAsync(database.TenantId, database.UserId, true, default))!;
        var loser = (await secondRepository.GetStateAsync(database.TenantId, database.UserId, true, default))!;
        Guid winningClaim = Guid.CreateVersion7();
        winner.ClaimTour(Guid.CreateVersion7(), LearningTourFamily.Shell, 1, winningClaim,
            Guid.CreateVersion7(), winner.Revision, Now);
        loser.ClaimTour(Guid.CreateVersion7(), LearningTourFamily.Studio, 1, Guid.CreateVersion7(),
            Guid.CreateVersion7(), loser.Revision, Now);
        await firstRepository.SaveChangesAsync(default);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => secondRepository.SaveChangesAsync(default));
        await Assert.That((await firstRepository.GetStateAsync(database.TenantId, database.UserId, false, default))!
            .ActiveClaimId).IsEqualTo(winningClaim);
    }

    [Test]
    public async Task IntentionalSpaceRemovalSurvivesReadAndContextReplacement()
    {
        await using var database = await LearningDatabase.CreateAsync();
        await database.SeedLearningAsync();
        await using (var context = database.Open(database.TenantId))
        {
            var repository = new LearningRepository(context);
            var space = (await repository.GetSpaceAsync(database.TenantId, true, default))!;
            space.Remove(space.Revision, Now);
            await repository.SaveChangesAsync(default);
        }
        await using var refreshed = database.Open(database.TenantId);
        var removed = await new LearningRepository(refreshed).GetSpaceAsync(database.TenantId, false, default);
        await Assert.That(removed).IsNotNull();
        await Assert.That(removed!.IsRemoved).IsTrue();
    }

    [Test]
    public async Task AbsenceVerificationRefusesUnpurgedPrivateState()
    {
        await using var database = await LearningDatabase.CreateAsync();
        await database.SeedLearningAsync();
        await using var context = database.Open(null);
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        var store = new LearningPrivacyErasureStore(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RequireAbsentAsync(Intent(database.UserId), default));
    }

    [Test]
    public async Task PurgeClearsExactInternalOwnerAcrossTenantsAndPreservesOtherLearners()
    {
        await using var database = await LearningDatabase.CreateAsync();
        await database.SeedLearningAsync();
        await database.SeedLearningAsync(database.OtherTenantId);
        await database.SeedLearningAsync(userId: database.OtherUserId);
        await using (var context = database.Open(null))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var store = new LearningPrivacyErasureStore(context);
            var intent = Intent(database.UserId);
            await store.PurgeAsync(intent, default);
            await store.RequireAbsentAsync(intent, default);
            await transaction.CommitAsync();
        }
        await using var tenantA = database.Open(database.TenantId);
        await using var tenantB = database.Open(database.OtherTenantId);
        var repository = new LearningRepository(tenantA);
        await Assert.That(await repository.GetStateAsync(database.TenantId, database.UserId, false, default)).IsNull();
        await Assert.That(await repository.GetStateAsync(database.TenantId, database.OtherUserId, false, default)).IsNotNull();
        await Assert.That(await new LearningRepository(tenantB)
            .GetStateAsync(database.OtherTenantId, database.UserId, false, default)).IsNull();
        await Assert.That(await repository.GetSpaceAsync(database.TenantId, false, default)).IsNotNull();
    }

    [Test]
    public async Task RepeatedPurgeIsIdempotentAndRollbackRetainsPrivateRows()
    {
        await using var database = await LearningDatabase.CreateAsync();
        await database.SeedLearningAsync();
        await using (var context = database.Open(null))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var store = new LearningPrivacyErasureStore(context);
            var intent = Intent(database.UserId);
            await store.PurgeAsync(intent, default);
            await store.PurgeAsync(intent, default);
            await store.RequireAbsentAsync(intent, default);
            await transaction.RollbackAsync();
        }
        await using var restored = database.Open(database.TenantId);
        await Assert.That(await new LearningRepository(restored)
            .GetStateAsync(database.TenantId, database.UserId, false, default)).IsNotNull();
    }

    private static PrivacyErasureIntent Intent(Guid subjectId) => PrivacyErasureIntent.Record(
        Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User, subjectId,
        PrivacyErasureReasonCode.SubjectErasureRequest, 1, Now, Now);

    internal sealed class LearningDatabase : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"learning-{Guid.CreateVersion7():N}.db");
        private IModel? _model;
        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid OtherTenantId { get; } = Guid.CreateVersion7();
        public Guid UserId { get; } = Guid.CreateVersion7();
        public Guid OtherUserId { get; } = Guid.CreateVersion7();

        public static async Task<LearningDatabase> CreateAsync()
        {
            var database = new LearningDatabase();
            await using var context = database.Open(null);
            context.EnableTenantFilterBypass(TenantFilterBypassReasons.DatabaseSeeding);
            await context.Database.EnsureCreatedAsync();
            database._model = context.Model;
            context.Add(new TenantStatus
            {
                Id = (int)TenantStatusEnum.Active, MasterCode = "Active", FullName = "Active", IsActiveState = true
            });
            foreach (Guid tenantId in new[] { database.TenantId, database.OtherTenantId })
                context.Add(new Tenant
                {
                    Id = tenantId, FullName = "Learning storage fixture", Slug = $"learning-{tenantId:N}",
                    TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
                });
            context.AddRange(new User { Id = database.UserId, Pii = null! },
                new User { Id = database.OtherUserId, Pii = null! });
            await using var humanOwner = new HumanSecuritySourceGate(context);
            await using var human = await humanOwner.AcquireAsync();
            await using var discoveryOwner = new EventDiscoveryEnrollmentGate(context);
            await using var discovery = await discoveryOwner.AcquireAsync();
            await context.SaveChangesAsync();
            return database;
        }

        public ExploreDbContext Open(Guid? tenantId)
        {
            var connection = new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString();
            var options = TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(connection).UseSnakeCaseNamingConvention();
            if (_model is not null) options.UseModel(_model);
            var context = new ExploreDbContext(options.Options);
            if (tenantId.HasValue) context.TenantContext = new SelectedTenant(tenantId.Value);
            context.BindTenantLease();
            return context;
        }

        public async Task SeedLearningAsync(Guid? tenantId = null, Guid? userId = null)
        {
            Guid tenant = tenantId ?? TenantId;
            await using var context = Open(tenant);
            var repository = new LearningRepository(context);
            if (await repository.GetSpaceAsync(tenant, false, default) is null)
                await repository.AddSpaceAsync(TenantLearningSpace.Create(Guid.CreateVersion7(), tenant, Now), default);
            await repository.AddStateAsync(
                UserLearningState.Create(Guid.CreateVersion7(), tenant, userId ?? UserId, Now), default);
            await repository.SaveChangesAsync(default);
        }

        public ValueTask DisposeAsync()
        {
            File.Delete(_path);
            File.Delete(_path + "-shm");
            File.Delete(_path + "-wal");
            return ValueTask.CompletedTask;
        }

        private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
    }
}
