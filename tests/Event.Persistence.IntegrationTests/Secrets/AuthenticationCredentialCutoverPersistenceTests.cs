using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Secrets;

public sealed class AuthenticationCredentialCutoverPersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task TrackedEvidenceUpdatesSurviveFreshContext()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = CreateOperation();
        await using (var writer = database.CreateContext())
        {
            var repository = new AuthenticationCredentialCutoverRepository(writer);
            await using var transaction = await writer.Database.BeginTransactionAsync();
            await repository.AddAsync(operation);
            operation.StartCollecting(operation.ConcurrencyStamp, Now.AddSeconds(1));
            await writer.SaveChangesAsync();
            var member = operation.RequiredReplacements[0];
            operation.ReportActivation(new(operation.InstanceId, operation.Id, operation.DeploymentRevision,
                member.ReplicaId, member.ProcessBootId, member.Consumer, member.Generation),
                operation.ConcurrencyStamp, Now.AddSeconds(2));
            operation.AttestFence(new(operation.InstanceId, operation.Id, operation.DeploymentRevision,
                operation.CreatedBy, true, true, true), operation.ConcurrencyStamp, Now.AddSeconds(3));
            await writer.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await using var reader = database.CreateContext();
        var restored = (await new AuthenticationCredentialCutoverRepository(reader)
            .GetAsync(operation.InstanceId, operation.Id))!;
        await Assert.That(restored.Activations).IsEquivalentTo(operation.Activations);
        await Assert.That(restored.Fence).IsEqualTo(operation.Fence);
        await Assert.That(restored.State).IsEqualTo(AuthenticationCredentialCutover.CutoverState.ReadyForCleanup);
        await Assert.That(restored.ConcurrencyStamp).IsEqualTo(operation.ConcurrencyStamp);
    }

    [Test]
    public async Task MaximumMembershipAndNullableFenceRoundTrip()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = CreateOperation(memberCount: 1024);
        await SeedAsync(database, operation);
        await using var reader = database.CreateContext();
        var restored = (await new AuthenticationCredentialCutoverRepository(reader)
            .GetAsync(operation.InstanceId, operation.Id))!;
        await Assert.That(restored.RequiredActivationCount).IsEqualTo(1024);
        await Assert.That(restored.RequiredReplacements).IsEquivalentTo(operation.RequiredReplacements);
        await Assert.That(restored.Activations).IsEmpty();
        await Assert.That(restored.Fence).IsNull();
        await Assert.That(restored.CleanedSettingCount).IsNull();
        await Assert.That(restored.CleanedBindingCount).IsNull();
        await Assert.That(restored.Authority).IsEqualTo(operation.Authority);
    }

    [Test]
    public async Task RepositoryRejectsStaleOperationStamp()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = CreateOperation();
        await SeedAsync(database, operation);
        await using var firstContext = database.CreateContext();
        await using var staleContext = database.CreateContext();
        var firstRepository = new AuthenticationCredentialCutoverRepository(firstContext);
        var staleRepository = new AuthenticationCredentialCutoverRepository(staleContext);
        var first = (await firstRepository.GetAsync(operation.InstanceId, operation.Id))!;
        var stale = (await staleRepository.GetAsync(operation.InstanceId, operation.Id))!;
        Guid stamp = first.ConcurrencyStamp;
        first.StartCollecting(stamp, Now.AddSeconds(1));
        await using (var transaction = await firstContext.Database.BeginTransactionAsync())
        {
            await firstRepository.SaveAsync(first, stamp);
            await transaction.CommitAsync();
        }
        stale.Fail(stamp, Now.AddSeconds(2));
        await using var staleTransaction = await staleContext.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleRepository.SaveAsync(stale, stamp));
    }

    [Test]
    public async Task HeadReplacementRetainsReceiptsAndRejectsStaleHeadStamp()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CreateOperation();
        await SeedAsync(database, original, withHead: true);
        await using var writer = database.CreateContext();
        await using var staleContext = database.CreateContext();
        var repository = new AuthenticationCredentialCutoverRepository(writer);
        var staleRepository = new AuthenticationCredentialCutoverRepository(staleContext);
        var head = (await repository.GetHeadAsync(original.InstanceId))!;
        var stale = (await staleRepository.GetHeadAsync(original.InstanceId))!;
        Guid stamp = head.ConcurrencyStamp;
        var replacement = CreateOperation(original.InstanceId);
        await using (var transaction = await writer.Database.BeginTransactionAsync())
        {
            await repository.AddAsync(replacement);
            head.Replace(replacement, original.Id, stamp);
            await repository.SaveHeadAsync(head, stamp);
            await transaction.CommitAsync();
        }
        stale.Guard(original.Id, stamp);
        await using (var transaction = await staleContext.Database.BeginTransactionAsync())
        {
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleRepository.SaveHeadAsync(stale, stamp));
        }
        await using var reader = database.CreateContext();
        var verification = new AuthenticationCredentialCutoverRepository(reader);
        var restored = (await verification.GetHeadAsync(original.InstanceId))!;
        await Assert.That(restored.CurrentOperationId).IsEqualTo(replacement.Id);
        await Assert.That(restored.ConcurrencyStamp).IsEqualTo(head.ConcurrencyStamp);
        await Assert.That(await verification.GetAsync(original.InstanceId, original.Id)).IsNotNull();
        await Assert.That(await verification.GetAsync(original.InstanceId, replacement.Id)).IsNotNull();
        await Assert.That(await verification.GetAsync(Guid.CreateVersion7(), original.Id)).IsNull();
    }

    [Test]
    public async Task ReloadedEntitiesSaveInAlreadyTrackedScope()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var writer = database.CreateContext();
        var repository = new AuthenticationCredentialCutoverRepository(writer);
        var operation = CreateOperation();
        var head = AuthenticationCredentialCutoverHead.Create(operation);
        await using (var transaction = await writer.Database.BeginTransactionAsync())
        {
            await repository.AddAsync(operation);
            await repository.AddHeadAsync(head);
            var reloaded = (await repository.GetAsync(operation.InstanceId, operation.Id))!;
            Guid stamp = reloaded.ConcurrencyStamp;
            reloaded.StartCollecting(stamp, Now.AddSeconds(1));
            var member = reloaded.RequiredReplacements[0];
            reloaded.ReportActivation(new(reloaded.InstanceId, reloaded.Id, reloaded.DeploymentRevision,
                member.ReplicaId, member.ProcessBootId, member.Consumer, member.Generation),
                reloaded.ConcurrencyStamp, Now.AddSeconds(2));
            await repository.SaveAsync(reloaded, stamp);
            var reloadedHead = (await repository.GetHeadAsync(operation.InstanceId))!;
            Guid headStamp = reloadedHead.ConcurrencyStamp;
            reloadedHead.Guard(operation.Id, headStamp);
            await repository.SaveHeadAsync(reloadedHead, headStamp);
            await transaction.CommitAsync();
        }
        await using var reader = database.CreateContext();
        var verification = new AuthenticationCredentialCutoverRepository(reader);
        var restored = (await verification.GetAsync(operation.InstanceId, operation.Id))!;
        await Assert.That(restored.ActivationCount).IsEqualTo(1);
        await Assert.That(restored.ConcurrencyStamp).IsEqualTo(operation.ConcurrencyStamp);
        await Assert.That((await verification.GetHeadAsync(operation.InstanceId))!.ConcurrencyStamp)
            .IsEqualTo(head.ConcurrencyStamp);
    }

    [Test]
    public async Task SameInstanceForeignKeyRejectsCrossInstancePointerAndReceiptDeletion()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = CreateOperation();
        var foreign = CreateOperation();
        await SeedAsync(database, original, withHead: true);
        await SeedAsync(database, foreign);
        await using var context = database.CreateContext();
        // Exercise malformed database input without bypassing the Domain's replacement guard.
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE ie_authentication_credential_cutover_heads SET current_operation_id = {foreign.Id} WHERE instance_id = {original.InstanceId}"));
        await Assert.ThrowsAsync<SqliteException>(() => context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM ie_authentication_credential_cutover_receipts WHERE id = {original.Id}"));
        var repository = new AuthenticationCredentialCutoverRepository(context);
        await Assert.That((await repository.GetHeadAsync(original.InstanceId))!.CurrentOperationId).IsEqualTo(original.Id);
        await Assert.That(await repository.GetAsync(original.InstanceId, original.Id)).IsNotNull();
    }

    [Test]
    public async Task RepositoryRequiresCallerTransactionAndDoesNotCommitIt()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = CreateOperation();
        await using (var writer = database.CreateContext())
        {
            var repository = new AuthenticationCredentialCutoverRepository(writer);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddAsync(operation));
            await using var transaction = await writer.Database.BeginTransactionAsync();
            await repository.AddAsync(operation);
            await repository.AddHeadAsync(AuthenticationCredentialCutoverHead.Create(operation));
            await transaction.RollbackAsync();
        }
        await using var reader = database.CreateContext();
        var verification = new AuthenticationCredentialCutoverRepository(reader);
        await Assert.That(await verification.GetAsync(operation.InstanceId, operation.Id)).IsNull();
        await Assert.That(await verification.GetHeadAsync(operation.InstanceId)).IsNull();
    }

    [Test]
    public async Task CompletedCutoverEvidenceSurvivesFreshContext()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        Guid instanceId = Guid.CreateVersion7();
        Guid operationId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();
        Guid deploymentRevision = Guid.CreateVersion7();
        var replacement = new AuthenticationCredentialCutover.Replacement(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            AuthenticationCredentialCutover.CredentialConsumer.GoogleOidc, Guid.CreateVersion7());
        var operation = AuthenticationCredentialCutover.Create(
            instanceId, operationId, actorId, now, now.AddMinutes(10),
            deploymentRevision, AuthenticationCredentialCutover.CredentialAuthority.Infisical,
            Guid.CreateVersion7(), [replacement]);
        operation.StartCollecting(operation.ConcurrencyStamp, now.AddSeconds(1));
        operation.ReportActivation(new(instanceId, operationId, deploymentRevision,
            replacement.ReplicaId, replacement.ProcessBootId, replacement.Consumer, replacement.Generation),
            operation.ConcurrencyStamp, now.AddSeconds(2));
        operation.AttestFence(new(instanceId, operationId, deploymentRevision, actorId, true, true, true),
            operation.ConcurrencyStamp, now.AddSeconds(3));
        operation.CompleteCleanup(operationId, operation.ConcurrencyStamp, 2, 1, now.AddSeconds(4));

        string databaseName = $"authentication_cutover_{Guid.CreateVersion7():N}";
        string connectionString = $"Data Source=file:{databaseName}?mode=memory&cache=shared";
        await using var anchor = new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        var options = new DbContextOptionsBuilder<ExploreDbContext>()
            .UseSqlite(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance)
            .Options;
        Exception? persistenceFailure = null;
        AuthenticationCredentialCutover? restored = null;
        try
        {
            await using (var writer = new ExploreDbContext(options))
            {
                await writer.Database.EnsureCreatedAsync();
                writer.Set<AuthenticationCredentialCutover>().Add(operation);
                await writer.SaveChangesAsync();
            }
            await using var reader = new ExploreDbContext(options);
            restored = await reader.Set<AuthenticationCredentialCutover>()
                .AsNoTracking().SingleAsync(candidate => candidate.Id == operationId);
        }
        catch (InvalidOperationException exception)
        {
            persistenceFailure = exception;
        }

        await Assert.That(persistenceFailure).IsNull()
            .Because("A committed cutover receipt and its exact evidence must survive a process/context restart.");
        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.InstanceId).IsEqualTo(instanceId);
        await Assert.That(restored.CreatedBy).IsEqualTo(actorId);
        await Assert.That(restored.CreatedAt).IsEqualTo(operation.CreatedAt);
        await Assert.That(restored.Deadline).IsEqualTo(operation.Deadline);
        await Assert.That(restored.UpdatedAt).IsEqualTo(operation.UpdatedAt);
        await Assert.That(restored.DeploymentRevision).IsEqualTo(deploymentRevision);
        await Assert.That(restored.AuthorityProfileRevision).IsEqualTo(operation.AuthorityProfileRevision);
        await Assert.That(restored.ConcurrencyStamp).IsEqualTo(operation.ConcurrencyStamp);
        await Assert.That(restored.State).IsEqualTo(AuthenticationCredentialCutover.CutoverState.Cleaned);
        await Assert.That(restored.RequiredReplacements).IsEquivalentTo(operation.RequiredReplacements);
        await Assert.That(restored.Activations).IsEquivalentTo(operation.Activations);
        await Assert.That(restored.Fence).IsEqualTo(operation.Fence);
        await Assert.That(restored.CleanedSettingCount).IsEqualTo(2);
        await Assert.That(restored.CleanedBindingCount).IsEqualTo(1);
    }

    private static AuthenticationCredentialCutover CreateOperation(Guid? instanceId = null, int memberCount = 1) =>
        AuthenticationCredentialCutover.Create(instanceId ?? Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Now, Now.AddMinutes(10), Guid.CreateVersion7(),
            AuthenticationCredentialCutover.CredentialAuthority.Infisical, Guid.CreateVersion7(),
            Enumerable.Range(0, memberCount).Select(_ => new AuthenticationCredentialCutover.Replacement(
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                AuthenticationCredentialCutover.CredentialConsumer.GoogleOidc, Guid.CreateVersion7())));

    private static async Task SeedAsync(TestDatabase database, AuthenticationCredentialCutover operation,
        bool withHead = false)
    {
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var repository = new AuthenticationCredentialCutoverRepository(context);
        await repository.AddAsync(operation);
        if (withHead)
            await repository.AddHeadAsync(AuthenticationCredentialCutoverHead.Create(operation));
        await transaction.CommitAsync();
    }

    private sealed class TestDatabase(SqliteConnection anchor, DbContextOptions<ExploreDbContext> options)
        : IAsyncDisposable
    {
        public ExploreDbContext CreateContext() => new(options);

        public static async Task<TestDatabase> CreateAsync()
        {
            string connectionString = $"Data Source=file:cutover_{Guid.CreateVersion7():N}?mode=memory&cache=shared";
            var anchor = new SqliteConnection(connectionString);
            try
            {
                await anchor.OpenAsync();
                var options = new DbContextOptionsBuilder<ExploreDbContext>().UseSqlite(connectionString)
                    .UseSnakeCaseNamingConvention()
                    .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance).Options;
                var database = new TestDatabase(anchor, options);
                await using var context = database.CreateContext();
                await context.Database.EnsureCreatedAsync();
                return database;
            }
            catch
            {
                await anchor.DisposeAsync();
                throw;
            }
        }

        public ValueTask DisposeAsync() => anchor.DisposeAsync();
    }
}
