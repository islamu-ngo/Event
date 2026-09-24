using Explore.Application.Contracts.Persistence;
using Explore.Domain.Keycloak;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Keycloak;

public sealed class KeycloakOperationPersistenceTests
{
    private static readonly Guid InstanceId =
        Guid.Parse("22222222-2222-7222-8222-222222222222");
    private static readonly DateTimeOffset Now =
        new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Model_UsesPortableReceiptSafetyShape()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using ExploreDbContext context = database.CreateContext();

        var operation = context.Model.FindEntityType(typeof(KeycloakOperation))!;
        var target = context.Model.FindEntityType(typeof(KeycloakTarget))!;

        await Assert.That(operation.GetTableName())
            .IsEqualTo("ie_KeycloakOperationReceipts");
        await Assert.That(operation
                .FindProperty(nameof(KeycloakOperation.ConcurrencyStamp))!
                .IsConcurrencyToken)
            .IsTrue();
        await Assert.That(target.GetIndexes().Any(index =>
                index.Properties.Select(property => property.Name)
                    .SequenceEqual(
                    [
                        nameof(KeycloakTarget.InstanceId),
                        nameof(KeycloakTarget.AuthorityKey),
                        nameof(KeycloakTarget.Realm)
                    ])))
            .IsTrue();
    }

    [Test]
    public async Task Repository_RoundTripsCredentialFreeAggregate()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        KeycloakOperation operation = CreateOperation(
            createdAt: Now,
            steps:
            [
                KeycloakStep.CreateClient,
                KeycloakStep.CreateMapper
            ]);
        operation.AuthorizeApply(
            "actor",
            7,
            operation.Target,
            "digest",
            Now.AddMinutes(1));
        var clientOutcome = new KeycloakStepOutcome(
            "step-1",
            KeycloakStepOutcomeKind.Applied,
            providerResourceId: "client-42",
            observedFingerprint: "client-observed");
        var mapperOutcome = new KeycloakStepOutcome(
            "step-2",
            KeycloakStepOutcomeKind.Conflict,
            providerResourceId: "mapper-7",
            observedFingerprint: "mapper-observed");
        operation.RecordStepOutcome(clientOutcome);
        operation.RecordStepOutcome(mapperOutcome);

        await using (ExploreDbContext writeContext = database.CreateContext())
        {
            var repository = new KeycloakOperationRepository(writeContext);
            await repository.AddAsync(operation);
        }

        await using ExploreDbContext readContext = database.CreateContext();
        var reader = new KeycloakOperationRepository(readContext);
        KeycloakOperation persisted =
            (await reader.GetAsync(operation.Id))!;

        await Assert.That(persisted.Target.InstanceId).IsEqualTo(InstanceId);
        await Assert.That(persisted.Target.Authority)
            .IsEqualTo("https://identity.example.test");
        await Assert.That(
                persisted.ChangeSet.Steps.Select(step => step.Kind))
            .IsEquivalentTo(
            [
                KeycloakStep.CreateClient,
                KeycloakStep.CreateMapper
            ]);
        await Assert.That(persisted.State)
            .IsEqualTo(KeycloakOperationState.Applying);
        await Assert.That(persisted.SettledAtUtc).IsNull();
        await Assert.That(persisted.StepOutcomes.Items)
            .IsEquivalentTo([clientOutcome, mapperOutcome]);
    }

    [Test]
    public async Task Repository_RejectsStaleConcurrencyStamp()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        KeycloakOperation operation = CreateOperation(Now);
        await using (ExploreDbContext seedContext = database.CreateContext())
        {
            await new KeycloakOperationRepository(seedContext)
                .AddAsync(operation);
        }

        await using ExploreDbContext firstContext = database.CreateContext();
        await using ExploreDbContext staleContext = database.CreateContext();
        var firstRepository = new KeycloakOperationRepository(firstContext);
        var staleRepository = new KeycloakOperationRepository(staleContext);
        KeycloakOperation first = (await firstRepository.GetAsync(operation.Id))!;
        KeycloakOperation stale = (await staleRepository.GetAsync(operation.Id))!;
        Guid firstStamp = first.ConcurrencyStamp;
        Guid staleStamp = stale.ConcurrencyStamp;

        first.RequestCancellation(Now.AddMinutes(1));
        await firstRepository.SaveAsync(first, firstStamp);
        stale.RequestCancellation(Now.AddMinutes(2));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            staleRepository.SaveAsync(stale, staleStamp));
    }

    [Test]
    public async Task Repository_ReloadAndSaveInSameScope_UpdatesTrackedReceipt()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using ExploreDbContext context = database.CreateContext();
        var repository = new KeycloakOperationRepository(context);
        KeycloakOperation operation = CreateOperation(Now);
        await repository.AddAsync(operation);
        KeycloakOperation reloaded =
            (await repository.GetAsync(operation.Id))!;
        Guid expectedStamp = reloaded.ConcurrencyStamp;

        reloaded.RequestCancellation(Now.AddMinutes(1));
        await repository.SaveAsync(reloaded, expectedStamp);

        await using ExploreDbContext verificationContext =
            database.CreateContext();
        KeycloakOperation persisted =
            (await new KeycloakOperationRepository(verificationContext)
                .GetAsync(operation.Id))!;
        await Assert.That(persisted.State)
            .IsEqualTo(KeycloakOperationState.Cancelled);
        await Assert.That(persisted.SettledAtUtc)
            .IsEqualTo(Now.AddMinutes(1));
    }

    [Test]
    public async Task Repository_BlocksUnresolvedRealmOnlyWithinSameInstance()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        KeycloakOperation unresolved = CreateApplyingOperation(
            InstanceId,
            createdAt: Now.AddDays(-60));
        MarkOutcomeUnknown(unresolved);
        KeycloakOperation otherInstance = CreateApplyingOperation(
            Guid.Parse("33333333-3333-7333-8333-333333333333"),
            createdAt: Now.AddDays(-60));

        await using ExploreDbContext context = database.CreateContext();
        var repository = new KeycloakOperationRepository(context);
        await repository.AddAsync(unresolved);
        await repository.AddAsync(otherInstance);

        bool sameInstance = await repository.HasUnresolvedOverlapAsync(
            Target(InstanceId));
        bool differentInstance = await repository.HasUnresolvedOverlapAsync(
            Target(Guid.Parse("44444444-4444-7444-8444-444444444444")));

        await Assert.That(sameInstance).IsTrue();
        await Assert.That(differentInstance).IsFalse();
    }

    [Test]
    public async Task Repository_RetainsUnresolvedAndSelectsOnlyOldSettledReceipts()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        KeycloakOperation unresolved = CreateApplyingOperation(
            InstanceId,
            createdAt: Now.AddDays(-90));
        MarkOutcomeUnknown(unresolved);
        KeycloakOperation oldSettled = CreateApplyingOperation(
            InstanceId,
            createdAt: Now.AddDays(-60),
            client: "old-settled");
        RecordSuccessfulOutcome(oldSettled);
        oldSettled.MarkVerified(Now.AddDays(-40));
        KeycloakOperation recentSettled = CreateApplyingOperation(
            InstanceId,
            createdAt: Now.AddDays(-20),
            client: "recent-settled");
        RecordSuccessfulOutcome(recentSettled);
        recentSettled.MarkVerified(Now.AddDays(-10));

        await using ExploreDbContext context = database.CreateContext();
        var repository = new KeycloakOperationRepository(context);
        await repository.AddAsync(unresolved);
        await repository.AddAsync(oldSettled);
        await repository.AddAsync(recentSettled);

        IReadOnlyList<KeycloakOperation> eligible =
            await repository.FindSettledRetentionEligibleAsync(
                Now.AddDays(-30));

        await Assert.That(eligible.Select(operation => operation.Id))
            .IsEquivalentTo([oldSettled.Id]);
    }

    [Test]
    public async Task Coordinator_CancellationPersistsOutsideOutstandingRealmLock()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        KeycloakOperation applying = CreateApplyingOperation(InstanceId, Now);
        await using ExploreDbContext coordinatorContext = database.CreateContext();
        var repository = new KeycloakOperationRepository(coordinatorContext);
        await repository.AddAsync(applying);
        var coordinator = new RelationalKeycloakOperationCoordinator(
            coordinatorContext,
            repository,
            database);
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool secondStepReached = false;

        Task<int> outstanding = coordinator.ExecuteAsync(
            applying,
            async _ =>
            {
                entered.SetResult();
                await release.Task;
                await using ExploreDbContext refreshContext =
                    database.CreateContext();
                KeycloakOperation refreshed =
                    (await new KeycloakOperationRepository(refreshContext)
                        .GetAsync(applying.Id))!;
                if (refreshed.IsCancellationRequested)
                {
                    return 0;
                }

                secondStepReached = true;
                return 1;
            });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await coordinator.RequestCancellationAsync(
                applying.Id,
                Now.AddMinutes(2))
            .WaitAsync(TimeSpan.FromSeconds(5));

        await using ExploreDbContext verificationContext =
            database.CreateContext();
        KeycloakOperation persisted =
            (await new KeycloakOperationRepository(verificationContext)
                .GetAsync(applying.Id))!;
        await Assert.That(persisted.State)
            .IsEqualTo(KeycloakOperationState.Applying);
        await Assert.That(persisted.IsCancellationRequested).IsTrue();

        release.SetResult();
        await Assert.That(
                await outstanding.WaitAsync(TimeSpan.FromSeconds(5)))
            .IsEqualTo(0);
        await Assert.That(secondStepReached).IsFalse();
    }

    [Test]
    public async Task Coordinator_SerializesCooperatingOperationsForSameRealm()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        KeycloakOperation first = CreateApplyingOperation(
            InstanceId,
            Now,
            client: "event-bff");
        KeycloakOperation second = CreateApplyingOperation(
            InstanceId,
            Now,
            client: "event-api");
        await using ExploreDbContext firstContext = database.CreateContext();
        var firstRepository = new KeycloakOperationRepository(firstContext);
        await firstRepository.AddAsync(first);
        var firstCoordinator = new RelationalKeycloakOperationCoordinator(
            firstContext,
            firstRepository,
            database);
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task<int> firstTask = firstCoordinator.ExecuteAsync(
            first,
            async _ =>
            {
                firstEntered.SetResult();
                await releaseFirst.Task;
                Guid expectedStamp = first.ConcurrencyStamp;
                RecordSuccessfulOutcome(first);
                first.MarkVerified(Now.AddMinutes(2));
                await firstRepository.SaveAsync(first, expectedStamp);
                return 1;
            });
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using ExploreDbContext secondContext = database.CreateContext();
        var secondRepository = new KeycloakOperationRepository(secondContext);
        await secondRepository.AddAsync(second);
        var secondCoordinator = new RelationalKeycloakOperationCoordinator(
            secondContext,
            secondRepository,
            database);
        Task<int> secondTask = secondCoordinator.ExecuteAsync(
            second,
            _ => Task.FromResult(2));

        await Assert.That(secondTask.IsCompleted).IsFalse();
        releaseFirst.SetResult();
        await Assert.That(
                await firstTask.WaitAsync(TimeSpan.FromSeconds(5)))
            .IsEqualTo(1);
        await Assert.That(
                await secondTask.WaitAsync(TimeSpan.FromSeconds(5)))
            .IsEqualTo(2);
    }

    [Test]
    public async Task Coordinator_SerializesApplyAndReconciliation()
    {
        await using TestDatabase database =
            await TestDatabase.CreateAsync();
        KeycloakOperation operation =
            CreateApplyingOperation(
                InstanceId,
                Now,
                client: "event-bff");
        await using ExploreDbContext applyContext =
            database.CreateContext();
        var applyRepository =
            new KeycloakOperationRepository(applyContext);
        await applyRepository.AddAsync(operation);
        var applyCoordinator =
            new RelationalKeycloakOperationCoordinator(
                applyContext,
                applyRepository,
                database);
        var applyEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseApply = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> applyTask = applyCoordinator.ExecuteAsync(
            operation,
            async _ =>
            {
                applyEntered.SetResult();
                await releaseApply.Task;
                return 1;
            });
        await applyEntered.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        await using ExploreDbContext reconcileContext =
            database.CreateContext();
        var reconcileRepository =
            new KeycloakOperationRepository(
                reconcileContext);
        var reconcileCoordinator =
            new RelationalKeycloakOperationCoordinator(
                reconcileContext,
                reconcileRepository,
                database);
        Task<int> reconcileTask =
            reconcileCoordinator
                .ExecuteReconciliationAsync(
                    operation,
                    _ => Task.FromResult(2));

        await Assert.That(reconcileTask.IsCompleted)
            .IsFalse();
        releaseApply.SetResult();
        await Assert.That(
                await applyTask.WaitAsync(
                    TimeSpan.FromSeconds(5)))
            .IsEqualTo(1);
        await Assert.That(
                await reconcileTask.WaitAsync(
                    TimeSpan.FromSeconds(5)))
            .IsEqualTo(2);
    }

    [Test]
    public async Task Coordinator_RejectsProviderSendWithoutPersistedIntent()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        KeycloakOperation applying = CreateApplyingOperation(InstanceId, Now);
        await using ExploreDbContext context = database.CreateContext();
        var repository = new KeycloakOperationRepository(context);
        var coordinator = new RelationalKeycloakOperationCoordinator(
            context,
            repository,
            database);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(
                applying,
                _ => Task.FromResult(1)));
    }

    [Test]
    public async Task Coordinator_UnresolvedOperationBlocksOverlappingCallback()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        KeycloakOperation unresolved = CreateApplyingOperation(
            InstanceId,
            Now,
            client: "event-bff");
        MarkOutcomeUnknown(unresolved);
        KeycloakOperation candidate = CreateApplyingOperation(
            InstanceId,
            Now.AddMinutes(2),
            client: "event-api");
        await using ExploreDbContext context = database.CreateContext();
        var repository = new KeycloakOperationRepository(context);
        await repository.AddAsync(unresolved);
        await repository.AddAsync(candidate);
        var coordinator = new RelationalKeycloakOperationCoordinator(
            context,
            repository,
            database);
        bool callbackReached = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.ExecuteAsync(
                candidate,
                _ =>
                {
                    callbackReached = true;
                    return Task.FromResult(1);
                }));

        await Assert.That(callbackReached).IsFalse();
    }

    private static KeycloakOperation CreateApplyingOperation(
        Guid instanceId,
        DateTimeOffset createdAt,
        string client = "event-bff")
    {
        KeycloakOperation operation = CreateOperation(
            createdAt,
            instanceId,
            client);
        operation.AuthorizeApply(
            "actor",
            7,
            operation.Target,
            "digest",
            createdAt.AddMinutes(1));
        return operation;
    }

    private static void RecordSuccessfulOutcome(
        KeycloakOperation operation)
    {
        foreach (KeycloakChangeStep step in operation.ChangeSet.Steps)
        {
            operation.RecordStepOutcome(new KeycloakStepOutcome(
                step.StepId,
                KeycloakStepOutcomeKind.Verified,
                observedFingerprint: step.DesiredFingerprint));
        }
    }

    private static void MarkOutcomeUnknown(KeycloakOperation operation)
    {
        KeycloakChangeStep step = operation.ChangeSet.Steps.Single();
        operation.RecordStepOutcome(new KeycloakStepOutcome(
            step.StepId,
            KeycloakStepOutcomeKind.OutcomeUnknown));
        operation.MarkOutcomeUnknown();
    }

    private static KeycloakOperation CreateOperation(
        DateTimeOffset createdAt,
        Guid? instanceId = null,
        string client = "event-bff",
        IReadOnlyList<KeycloakStep>? steps = null) =>
        new(
            new KeycloakChangeSet(
                (steps ?? [KeycloakStep.CreateClient])
                .Select((step, index) => ChangeStep(
                    step,
                    $"step-{index + 1}",
                    client))),
            Target(instanceId ?? InstanceId, client),
            "actor",
            7,
            "digest",
            createdAt,
            createdAt.AddDays(1));

    private static KeycloakChangeStep ChangeStep(
        KeycloakStep kind,
        string stepId,
        string client) =>
        new(
            stepId,
            kind,
            kind is KeycloakStep.CreateMapper or KeycloakStep.UpdateMapper
                ? KeycloakResourceKind.ProtocolMapper
                : kind == KeycloakStep.CreateRealm
                    ? KeycloakResourceKind.Realm
                    : KeycloakResourceKind.Client,
            kind is KeycloakStep.CreateMapper or KeycloakStep.UpdateMapper
                ? $"{client}:audience"
                : kind == KeycloakStep.CreateRealm
                    ? "operators"
                    : client,
            kind is KeycloakStep.UpdateMapper
                ? KeycloakStepPrecondition.MustMatchFingerprint
                : KeycloakStepPrecondition.MustBeAbsent,
            kind is KeycloakStep.UpdateMapper
                ? "expected-fingerprint"
                : null,
            kind is KeycloakStep.UpdateMapper
                ? "expected-identity-fingerprint"
                : null,
            "desired-fingerprint",
            "binding-fingerprint",
            kind switch
            {
                KeycloakStep.CreateRealm =>
                    KeycloakDesiredProjection.Realm(
                        "operators",
                        "11111111-1111-7111-8111-111111111111"),
                KeycloakStep.CreateClient =>
                    KeycloakDesiredProjection.ConfidentialClient(
                        client,
                        [],
                        []),
                KeycloakStep.CreateMapper
                    or KeycloakStep.UpdateMapper =>
                    KeycloakDesiredProjection.Mapper(
                        $"{client}:audience",
                        KeycloakMapperSemantic.Audience,
                        "event-api",
                        kind == KeycloakStep.CreateMapper
                            ? "33333333-3333-7333-8333-333333333333"
                            : null),
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            });

    private static KeycloakTarget Target(
        Guid instanceId,
        string client = "event-bff") =>
        new(
            instanceId,
            "https://identity.example.test",
            "operators",
            client);

    private sealed class TestDatabase :
        IDbContextFactory<ExploreDbContext>,
        IAsyncDisposable
    {
        private readonly SqliteConnection _anchor;
        private readonly DbContextOptions<ExploreDbContext> _options;

        private TestDatabase(
            SqliteConnection anchor,
            DbContextOptions<ExploreDbContext> options)
        {
            _anchor = anchor;
            _options = options;
        }

        public static async Task<TestDatabase> CreateAsync()
        {
            string databaseName = $"keycloak_receipts_{Guid.CreateVersion7():N}";
            var anchor = new SqliteConnection(
                $"Data Source=file:{databaseName}?mode=memory&cache=shared");
            await anchor.OpenAsync();
            var options = new DbContextOptionsBuilder<ExploreDbContext>()
                .UseSqlite(
                    $"Data Source=file:{databaseName}?mode=memory&cache=shared")
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(
                    SqliteNamedLockTransactionInterceptor.Instance)
                .Options;
            var database = new TestDatabase(anchor, options);
            await using ExploreDbContext context = database.CreateContext();
            await context.Database.EnsureCreatedAsync();
            return database;
        }

        public ExploreDbContext CreateContext() => new(_options);

        public ExploreDbContext CreateDbContext() => CreateContext();

        public Task<ExploreDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateContext());

        public ValueTask DisposeAsync() => _anchor.DisposeAsync();
    }
}
