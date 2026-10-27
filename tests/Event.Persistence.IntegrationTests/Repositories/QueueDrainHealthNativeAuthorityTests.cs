using System.Security.Cryptography;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class QueueDrainHealthNativeAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private const string AggregateRole = PostgresQueueDrainHealthContract.GlobalRole;
    private const string OwnerRole = PostgresQueueDrainHealthContract.OwnerRole;
    private const string MigratorRole = PostgresQueueDrainHealthContract.MigratorRole;
    private static readonly DateTime ObservedAt = new(2026, 1, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StaleBefore = ObservedAt.AddMinutes(-5);

    [Test]
    public async Task IndependentGlobalAuthorityReturnsAllThirteenCountsAcrossTwoTenants()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        seed.BindTenantLease();
        await SeedTenantAsync(seed, "queue-health-a");
        await SeedTenantAsync(seed, "queue-health-b");
        var expected = new QueueDrainHealthSnapshot(8, 4, 2, 8, 4, 2, 2, 8, 4, 6, 8, 2, 6);
        await PostgresQueueDrainHealthContract.ApplyAsync(seed);
        await PostgresQueueDrainHealthContract.ApplyAsync(seed);

        // Establish positive, migrated data before testing the restricted authority.
        var administrator = new AdministratorGlobalFactory(
            (DbContextOptions<ExploreDbContext>)seed.GetService<IDbContextOptions>());
        await Assert.That(await new QueueDrainHealthRepository(administrator).GetSnapshotAsync(
            ObservedAt, StaleBefore, CancellationToken.None)).IsEqualTo(expected);
        await using var authority = await CreateAuthorityAsync(seed);
        await using var global = await authority.Factory.CreateDbContextAsync();
        await RequirePrivateReadDenialsAsync(global, seed);
        await Assert.That(global.TenantContext).IsNull();
        var repository = new QueueDrainHealthRepository(authority.Factory);
        await Assert.That(await repository.GetSnapshotAsync(
            ObservedAt, StaleBefore, CancellationToken.None)).IsEqualTo(expected);

        // Moving only the integration cutoff must not alter webhook/PDS lease expiry.
        await Assert.That(await repository.GetSnapshotAsync(
            ObservedAt, StaleBefore.AddSeconds(1), CancellationToken.None))
            .IsEqualTo(expected with { IntegrationStale = 6 });
        await Assert.That(await repository.GetSnapshotAsync(
            ObservedAt.AddSeconds(1), StaleBefore, CancellationToken.None))
            .IsEqualTo(expected with
            {
                IntegrationDue = 12,
                IncomingDue = 12,
                IncomingStale = 6,
                ProviderPublicationDue = 12,
                ProviderPublicationStale = 6,
                PdsDue = 12,
                PdsStale = 4
            });
    }

    [Test]
    public async Task GlobalLoginCannotReadPrivateRowsElevateOrInheritContractOwnership()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        await PostgresQueueDrainHealthContract.ApplyAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed);
        await using var global = await authority.Factory.CreateDbContextAsync();
        await global.Database.OpenConnectionAsync();
        await using (var command = global.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText = """
                SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
                    AND NOT rolcreaterole AND NOT rolreplication
                FROM pg_catalog.pg_roles WHERE rolname = current_user
                """;
            await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsTrue();
            command.CommandText = $"""
                SELECT count(*)::integer FROM pg_catalog.pg_roles
                WHERE rolname IN ('{OwnerRole}', '{MigratorRole}', '{AggregateRole}')
                    AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
                    AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication
                """;
            await Assert.That((int)(await command.ExecuteScalarAsync())!).IsEqualTo(3);
            command.CommandText = $"""
                SELECT pg_has_role(current_user, '{OwnerRole}', 'MEMBER')
                    OR pg_has_role(current_user, '{MigratorRole}', 'MEMBER')
                """;
            await Assert.That((bool)(await command.ExecuteScalarAsync())!).IsFalse();
        }
        await RequirePrivateReadDenialsAsync(global, seed);
        await RequireDenialAsync(global, $"SET ROLE {OwnerRole}");
        await RequireDenialAsync(global, $"SET ROLE {MigratorRole}");
        await RequireDenialAsync(global, "SET ROLE postgres");
        await RequireAggregateShapeAsync(global);
        await RequireOwnerColumnBoundsAsync(global, seed);
    }

    [Test]
    public async Task UnentitledGlobalLoginCannotInvokeTheAggregate()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        await PostgresQueueDrainHealthContract.ApplyAsync(seed);
        await using var authority = await CreateAuthorityAsync(seed, grantAggregate: false);
        string? state = null;
        try
        {
            await new QueueDrainHealthRepository(authority.Factory).GetSnapshotAsync(
                ObservedAt, StaleBefore, CancellationToken.None);
        }
        catch (PostgresException exception) { state = exception.SqlState; }
        await Assert.That(state).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static async Task RequireAggregateShapeAsync(ExploreDbContext global)
    {
        await using var command = global.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT * FROM {PostgresQueueDrainHealthContract.FunctionSql(global)}(@observed, @stale)";
        var observed = command.CreateParameter();
        observed.ParameterName = "observed";
        observed.Value = ObservedAt;
        var stale = command.CreateParameter();
        stale.ParameterName = "stale";
        stale.Value = StaleBefore;
        command.Parameters.Add(observed);
        command.Parameters.Add(stale);
        await using var reader = await command.ExecuteReaderAsync();
        await Assert.That(reader.FieldCount).IsEqualTo(13);
        await Assert.That(Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)).IsEquivalentTo(
        [
            nameof(QueueDrainHealthSnapshot.IntegrationDue), nameof(QueueDrainHealthSnapshot.IntegrationStale),
            nameof(QueueDrainHealthSnapshot.IntegrationAmbiguous), nameof(QueueDrainHealthSnapshot.IncomingDue),
            nameof(QueueDrainHealthSnapshot.IncomingStale), nameof(QueueDrainHealthSnapshot.BulkReplayQueued),
            nameof(QueueDrainHealthSnapshot.BulkReplayExecuting), nameof(QueueDrainHealthSnapshot.ProviderPublicationDue),
            nameof(QueueDrainHealthSnapshot.ProviderPublicationStale), nameof(QueueDrainHealthSnapshot.ProviderPublicationUnknown),
            nameof(QueueDrainHealthSnapshot.PdsDue), nameof(QueueDrainHealthSnapshot.PdsStale),
            nameof(QueueDrainHealthSnapshot.PdsDeadLettered)
        ]);
        for (int index = 0; index < reader.FieldCount; index++)
            await Assert.That(reader.GetFieldType(index)).IsEqualTo(typeof(int));
        await Assert.That(await reader.ReadAsync()).IsTrue();
        await Assert.That(await reader.ReadAsync()).IsFalse();
    }

    private static async Task RequireOwnerColumnBoundsAsync(ExploreDbContext global, ExploreDbContext model)
    {
        (Type Type, string Predicate, string Private)[] columns =
        [
            (typeof(IntegrationSyncOutbox), nameof(IntegrationSyncOutbox.Status), nameof(IntegrationSyncOutbox.SubscriberEmail)),
            (typeof(IntegrationSyncOutbox), nameof(IntegrationSyncOutbox.EvidenceKind), nameof(IntegrationSyncOutbox.LastError)),
            (typeof(IncomingWebhookMessage), nameof(IncomingWebhookMessage.StatusId), nameof(IncomingWebhookMessage.PayloadHash)),
            (typeof(WebhookBulkReplayOperation), nameof(WebhookBulkReplayOperation.StatusId), nameof(WebhookBulkReplayOperation.RequestHash)),
            (typeof(WebhookProviderPublication), nameof(WebhookProviderPublication.StatusId), nameof(WebhookProviderPublication.CredentialReference)),
            (typeof(PdsSyncOutbox), nameof(PdsSyncOutbox.Status), nameof(PdsSyncOutbox.Payload))
        ];
        foreach (var column in columns)
        {
            var entity = model.Model.FindEntityType(column.Type)!;
            var mapping = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(
                entity.GetTableName()!, entity.GetSchema());
            string predicate = entity.FindProperty(column.Predicate)!.GetColumnName(mapping)!;
            string privateColumn = entity.FindProperty(column.Private)!.GetColumnName(mapping)!;
            await using var command = global.Database.GetDbConnection().CreateCommand();
            command.CommandText = $"""
                SELECT has_table_privilege('{OwnerRole}', '{Table(model, column.Type)}', 'SELECT'),
                    has_column_privilege('{OwnerRole}', '{Table(model, column.Type)}', '{predicate}', 'SELECT'),
                    has_column_privilege('{OwnerRole}', '{Table(model, column.Type)}', '{privateColumn}', 'SELECT')
                """;
            await using var reader = await command.ExecuteReaderAsync();
            await Assert.That(await reader.ReadAsync()).IsTrue();
            await Assert.That(reader.GetBoolean(0)).IsFalse();
            await Assert.That(reader.GetBoolean(1)).IsTrue();
            await Assert.That(reader.GetBoolean(2)).IsFalse();
        }
    }

    private static async Task RequirePrivateReadDenialsAsync(ExploreDbContext global, ExploreDbContext model)
    {
        foreach (Type type in new[]
        {
            typeof(IntegrationSyncOutbox), typeof(IncomingWebhookMessage), typeof(WebhookBulkReplayOperation),
            typeof(WebhookProviderPublication), typeof(PdsSyncOutbox), typeof(UserPii)
        })
        {
            await RequireDenialAsync(global, $"SELECT * FROM {Table(model, type)}");
            await RequireDenialAsync(global, $"SELECT count(*) FROM {Table(model, type)}");
        }
    }

    private static async Task RequireDenialAsync(ExploreDbContext context, string statement)
    {
        string? state = null;
        try { await context.Database.ExecuteSqlRawAsync(statement); }
        catch (PostgresException exception) { state = exception.SqlState; }
        await Assert.That(state).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
    }

    private async Task<Authority> CreateAuthorityAsync(ExploreDbContext seed, bool grantAggregate = true)
    {
        string schema = seed.Model.FindEntityType(typeof(IntegrationSyncOutbox))!.GetSchema()!;
        string role = $"health_global_{Guid.CreateVersion7():N}";
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await using var administrator = new NpgsqlConnection(fixture.ConnectionString);
        await administrator.OpenAsync();
        await using var command = administrator.CreateCommand();
        command.CommandText = $"""
            CREATE ROLE "{role}" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD '{password}';
            GRANT USAGE ON SCHEMA "{schema}" TO "{role}";
            """;
        await command.ExecuteNonQueryAsync();
        if (grantAggregate)
        {
            command.CommandText = $"GRANT {AggregateRole} TO \"{role}\" WITH INHERIT TRUE, SET FALSE";
            await command.ExecuteNonQueryAsync();
        }
        var connection = new NpgsqlConnectionStringBuilder(fixture.ConnectionString);
        var options = new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Global,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            Schema = schema,
            Username = role,
            Password = password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled
        };
        return new Authority(fixture.ConnectionString, schema, role, options);
    }

    private sealed class Authority(
        string administratorConnection, string schema, string role,
        PrimaryDatabaseConnectionOptions options) : IAsyncDisposable
    {
        internal GlobalExploreDbContextFactory Factory { get; } = new(options, poolSize: 1);

        public async ValueTask DisposeAsync()
        {
            using var pool = new NpgsqlConnection(PrimaryDatabaseConfiguration.BuildConnectionString(options).ConnectionString);
            NpgsqlConnection.ClearPool(pool);
            await using var administrator = new NpgsqlConnection(administratorConnection);
            await administrator.OpenAsync();
            await using var command = administrator.CreateCommand();
            command.CommandText = $"""
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{role}";
                DROP ROLE "{role}";
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class AdministratorGlobalFactory(DbContextOptions<ExploreDbContext> options)
        : PooledDbContextFactory<ExploreDbContext>(options, poolSize: 1), IGlobalExploreDbContextFactory;

    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
    }

    private static async Task SeedTenantAsync(ExploreDbContext context, string slug)
    {
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = slug,
            Slug = $"{slug}-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = ObservedAt
        };
        var user = new User
        {
            Pii = new UserPii { Email = $"{Guid.CreateVersion7():N}@example.test", FirstName = "Health", LastName = "Seed" },
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.AddRange(tenant, user);
        context.Add(new TenantUser
        {
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            StatusId = (int)TenantUserStatusEnum.Active,
            CreatedAt = ObservedAt
        });
        var consumer = new WebhookConsumer
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            ConsumerKind = WebhookConsumerKind.Tenant,
            Name = slug,
            Status = WebhookConsumerStatus.Active,
            ProviderMode = WebhookProviderMode.Svix,
            CreatedAt = ObservedAt
        };
        var profile = WebhookProviderCapabilityProfile.Create(
            WebhookProviderKind.Svix, "1.84.0", WebhookProviderCapability.EndpointManagement,
            "queue-health-v1", new DateTimeOffset(ObservedAt));
        var binding = WebhookConsumerProviderBinding.CreatePending(
            tenant.Id, consumer.Id, Guid.CreateVersion7(), "managed-eu",
            profile, WebhookProviderCapability.EndpointManagement);
        context.AddRange(consumer, binding);
        foreach (IntegrationSyncStatus status in Enum.GetValues<IntegrationSyncStatus>())
        {
            for (int index = 0; index < 3; index++)
            {
                context.Add(new IntegrationSyncOutbox
                {
                    TenantId = tenant.Id,
                    Tenant = tenant,
                    Kind = IntegrationKind.Listmonk,
                    SourceType = "health",
                    SourceId = Guid.CreateVersion7(),
                    SubscriberEmail = "health@example.test",
                    SubscriberPayloadJson = "{}",
                    ListmonkListId = 1,
                    Status = status,
                    NextAttemptAt = Boundary(index, ObservedAt),
                    ProcessingStartedAt = Boundary(index, StaleBefore),
                    EvidenceKind = status == IntegrationSyncStatus.DeadLettered && index == 0
                        ? IntegrationSyncEvidenceKind.ProviderOutcomeAmbiguous
                        : IntegrationSyncEvidenceKind.None,
                    LastError = index == 0 ? null : "other_failure",
                    CreatedAt = ObservedAt
                });
            }
        }
        context.Add(new IntegrationSyncOutbox
        {
            TenantId = tenant.Id,
            Tenant = tenant,
            Kind = IntegrationKind.Listmonk,
            SourceType = "health",
            SourceId = Guid.CreateVersion7(),
            SubscriberEmail = "deleted@example.test",
            SubscriberPayloadJson = "{}",
            ListmonkListId = 1,
            Status = IntegrationSyncStatus.Pending,
            IsDeleted = true,
            DeletedAt = ObservedAt,
            CreatedAt = ObservedAt
        });
        foreach (IncomingWebhookMessageStatus status in Enum.GetValues<IncomingWebhookMessageStatus>())
        {
            for (int index = 0; index < 3; index++)
            {
                var message = IncomingWebhookMessage.CreateVerified(
                    tenant.Id, "health", Guid.CreateVersion7().ToString("N"), null, "health",
                    Encoding.UTF8.GetBytes("{}"), $"sha256:{new string('a', 64)}", "application/json", "utf-8",
                    null, ObservedAt, ObservedAt, ObservedAt.AddDays(7), "health-v1",
                    ObservedAt.AddDays(30), ObservedAt.AddDays(90), ObservedAt.AddDays(7), ObservedAt.AddDays(30));
                context.Add(message);
                context.Entry(message).Property(row => row.StatusId).CurrentValue = (int)status;
                context.Entry(message).Property(row => row.NextAttemptAt).CurrentValue = Boundary(index, ObservedAt);
                context.Entry(message).Property(row => row.ProcessingLeaseExpiresAt).CurrentValue = Boundary(index, ObservedAt);
            }
        }
        foreach (WebhookBulkReplayStatus status in Enum.GetValues<WebhookBulkReplayStatus>())
        {
            var replay = WebhookBulkReplayOperation.Create(
                tenant.Id, Guid.CreateVersion7(), $"sha256:{new string('b', 64)}",
                ObservedAt.AddHours(-1), ObservedAt, null, null, null, 1, "health",
                new WebhookBulkReplayPreviewSnapshot(1, 0, 0, 0, 0, 0, 0, 0, 0), ObservedAt);
            switch (status)
            {
                case WebhookBulkReplayStatus.Executing:
                    replay.Start(ObservedAt);
                    break;
                case WebhookBulkReplayStatus.Completed:
                    replay.Start(ObservedAt);
                    replay.Complete(0, ObservedAt);
                    break;
                case WebhookBulkReplayStatus.Cancelled:
                    replay.Cancel("health_cancelled", ObservedAt);
                    break;
                case WebhookBulkReplayStatus.Failed:
                    replay.Start(ObservedAt);
                    replay.Fail("health_failure", ObservedAt);
                    break;
            }
            context.Add(replay);
        }
        foreach (WebhookProviderPublicationStatus status in Enum.GetValues<WebhookProviderPublicationStatus>())
        {
            for (int index = 0; index < 3; index++)
            {
                var message = WebhookMessage.Create(
                    tenant.Id, "health", Guid.CreateVersion7().ToString("N"), "health", Guid.CreateVersion7(),
                    consumer.Id, Encoding.UTF8.GetBytes("{}"), "application/json", "utf-8",
                    ObservedAt, ObservedAt.AddDays(7), ObservedAt);
                var plan = WebhookDeliveryPlanSnapshot.Create(
                    tenant.Id, message.Id, consumer.Id, WebhookProviderMode.Svix,
                    "health-v1", "contract-v1", "default", "retention-v1",
                    new DateTimeOffset(ObservedAt.AddDays(7)), new DateTimeOffset(ObservedAt.AddDays(30)),
                    new DateTimeOffset(ObservedAt.AddDays(90)), new DateTimeOffset(ObservedAt.AddDays(90)),
                    new DateTimeOffset(ObservedAt.AddDays(30)), new DateTimeOffset(ObservedAt));
                var publication = WebhookProviderPublication.Create(
                    tenant.Id, message.Id, plan.Id, WebhookProviderKind.Svix, binding.Id, "1.84.0",
                    Guid.CreateVersion7().ToString("N"), Guid.CreateVersion7().ToString("N"),
                    $"sha256:{new string('c', 64)}", "health-application", "health-provider-application", "managed-eu",
                    "secret:health-provider", "credential-v1", WebhookProviderMode.Svix, "health-v1", 1,
                    "retention-v1", ObservedAt.AddDays(7), ObservedAt.AddDays(30), ObservedAt.AddHours(12), ObservedAt);
                context.AddRange(message, plan, publication);
                context.Entry(publication).Property(row => row.StatusId).CurrentValue = (int)status;
                context.Entry(publication).Property(row => row.NextActionAt).CurrentValue = Boundary(index, ObservedAt);
                context.Entry(publication).Property(row => row.ProcessingLeaseExpiresAt).CurrentValue = Boundary(index, ObservedAt);
            }
        }
        PdsSyncOutbox? replacement = null;
        foreach (PdsSyncStatus status in Enum.GetValues<PdsSyncStatus>())
        {
            for (int index = 0; index < 3; index++)
            {
                var pds = new PdsSyncOutbox
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant.Id,
                    UserId = user.Id,
                    Did = "did:plc:health",
                    Collection = "app.health.record",
                    RecordKey = Guid.CreateVersion7().ToString("N"),
                    Operation = PdsSyncOperation.Create,
                    Payload = "{}",
                    PayloadHash = new string('d', 64),
                    IdempotencyKey = Guid.CreateVersion7().ToString("N"),
                    PdsHost = "https://pds.example.test",
                    SourceEntityType = "health",
                    SourceEntityId = Guid.CreateVersion7(),
                    SourceVersion = Guid.CreateVersion7(),
                    MaxRetries = 5,
                    CreatedAt = ObservedAt,
                    Status = status,
                    NextRetryAt = Boundary(index, ObservedAt),
                    LeaseOwner = status == PdsSyncStatus.Processing ? "health" : null,
                    LeaseToken = status == PdsSyncStatus.Processing ? Guid.CreateVersion7() : null,
                    LeaseExpiresAt = status == PdsSyncStatus.Processing ? ObservedAt.AddSeconds(index) : null,
                    ProcessedAt = status == PdsSyncStatus.Completed ? ObservedAt : null,
                    SettledUri = status == PdsSyncStatus.Completed ? "at://did:plc:health/app.health.record/settled" : null,
                    SettledCid = status == PdsSyncStatus.Completed ? "health-cid" : null,
                    SupersededById = status == PdsSyncStatus.Superseded ? replacement!.Id : null,
                    SupersededAt = status == PdsSyncStatus.Superseded ? ObservedAt : null
                };
                context.Add(pds);
                replacement ??= pds;
            }
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    private static DateTime? Boundary(int index, DateTime boundary) => index switch
    {
        0 => null,
        1 => boundary,
        _ => boundary.AddSeconds(1)
    };
}
