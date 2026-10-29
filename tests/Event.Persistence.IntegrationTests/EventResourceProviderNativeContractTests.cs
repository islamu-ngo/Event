using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Features.EventResourceProviderActivation.Requests.Commands;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Event.Persistence.IntegrationTests;

/// <summary>Native service-role controls; these do not manufacture managed human authority.</summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class EventResourceProviderNativeContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task OtherPurposesCannotObserveOriginalGlobalData(PrimaryDatabaseRole role)
    {
        await InstallAsync();
        await using var caller = Open(role);
        await caller.Database.OpenConnectionAsync();
        await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(caller));
    }

    [Test]
    public async Task GlobalCompletesOnlyTheCanonicalBindingActivationPair()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await ReadAsync(caller);
        var request = BindRequest(before);
        var expected = BindingAfter(before, request);
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await ExecuteAsync(caller, "acquire", request, before);
            await ExecuteAsync(caller, "apply", request, expected);
            await ExecuteAsync(caller, "complete", request, expected);
            await transaction.CommitAsync();
        }
        var after = await ReadAsync(caller);
        await Assert.That(JsonNode.DeepEquals(expected, after["Data"])).IsTrue();
        var deployment = request["Bind"]!["Binding"]!["DeploymentId"]!.GetValue<Guid>();
        await using var observer = fixture.CreateDbContext();
        var activation = await observer.Set<EventResourceProviderActivation>().AsNoTracking().SingleAsync(row => row.Id == deployment);
        await Assert.That(activation.Epoch).IsEqualTo(1L);
        await Assert.That(activation.ConcurrencyStamp).IsEqualTo(request["ConcurrencyStamp"]!.GetValue<Guid>());
        await Assert.That(activation.CreatedBy).IsEqualTo(request["Actor"]!.GetValue<Guid>());
        await Assert.That(activation.UpdatedBy).IsEqualTo(request["Actor"]!.GetValue<Guid>());
        await Assert.ThrowsAsync<PostgresException>(async () => await caller.Database.ExecuteSqlRawAsync(
            $"UPDATE {new HumanPrincipalSourceSql(caller).Table(typeof(SystemSetting))} SET value = value WHERE false"));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task IncompletePairOrSkippedNoopCannotCommit(bool unboundPublication)
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await ReadAsync(caller);
        var request = unboundPublication ? PublicationRequest() : BindRequest(before);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ExecuteAsync(caller, "acquire", request, before);
        await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
    }

    [Test]
    public async Task OriginalUnboundPublicationExplicitlyCompletesItsNoop()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await ReadAsync(caller);
        var request = PublicationRequest();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ExecuteAsync(caller, "acquire", request, before);
        await ExecuteAsync(caller, "apply", request, before["Data"]!);
        await ExecuteAsync(caller, "complete", request, before["Data"]!);
        await transaction.CommitAsync();
        await Assert.That(JsonNode.DeepEquals(before["Data"], (await ReadAsync(caller))["Data"])).IsTrue();
    }

    [Test]
    [Arguments("activation")]
    [Arguments("setting")]
    [Arguments("stamp")]
    [Arguments("actor")]
    public async Task CallerCannotSupplyAnIncompleteOrWidenedPair(string change)
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await ReadAsync(caller);
        var request = BindRequest(before);
        var expected = BindingAfter(before, request);
        if (change == "activation") expected["EventResourceProviderActivation"] = before["Data"]!["EventResourceProviderActivation"]!.DeepClone();
        else if (change == "setting") expected["SystemSetting"] = before["Data"]!["SystemSetting"]!.DeepClone();
        else
        {
            var row = expected["EventResourceProviderActivation"]!.AsArray().Single(item =>
                item!["Id"]!.GetValue<Guid>() == request["Bind"]!["Binding"]!["DeploymentId"]!.GetValue<Guid>())!;
            row[change == "stamp" ? "ConcurrencyStamp" : "UpdatedBy"] = Guid.CreateVersion7();
        }
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ExecuteAsync(caller, "acquire", request, before);
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(caller, "apply", request, expected));
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SourceAbsenceAndStaleSerializableSnapshotsCannotBeAdopted(bool staleSnapshot)
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await ReadAsync(caller);
        var request = BindRequest(before);
        await using var transaction = staleSnapshot
            ? await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        if (staleSnapshot) _ = await ReadAsync(caller);
        Guid inserted = Guid.CreateVersion7();
        await using (var writer = fixture.CreateDbContext())
        {
            writer.Set<EventResourceProviderActivation>().Add(EventResourceProviderActivation.Create(inserted, DateTime.UtcNow));
            await writer.SaveChangesAsync();
        }
        await using var later = !staleSnapshot
            ? await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(caller, "acquire", request, before));
        await (transaction ?? later!).RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalAliasesCannotBeRemovedOrReassigned(bool reassign)
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await ReadAsync(caller);
        var original = BindRequest(before);
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await ExecuteAsync(caller, "acquire", original, before);
            var expected = BindingAfter(before, original);
            await ExecuteAsync(caller, "apply", original, expected);
            await ExecuteAsync(caller, "complete", original, expected);
            await transaction.CommitAsync();
        }
        var observed = await ReadAsync(caller);
        var replacement = BindRequest(observed);
        if (reassign)
            replacement["Bind"]!["Binding"]!["Endpoints"] = original["Bind"]!["Binding"]!["Endpoints"]!.DeepClone();
        else
            replacement["Bind"]!["Binding"]!["DeploymentId"] = original["Bind"]!["Binding"]!["DeploymentId"]!.DeepClone();
        await using (var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await ExecuteAsync(caller, "acquire", replacement, observed);
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                ExecuteAsync(caller, "apply", replacement, observed["Data"]!));
            await Assert.That(rejected!.MessageText).IsEqualTo("resource_provider_binding_aliases");
            await transaction.RollbackAsync();
        }
        await Assert.That(JsonNode.DeepEquals(observed["Data"], (await ReadAsync(caller))["Data"])).IsTrue();
    }

    [Test]
    public async Task ReadOnlyTenantSettingsRetainOriginalStatementExclusion()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await ReadAsync(caller);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ExecuteAsync(caller, "acquire", BindRequest(before), before);
        await using var competitor = fixture.CreateDbContext();
        var sql = new HumanPrincipalSourceSql(competitor);
        string statement = $"UPDATE {sql.Table(typeof(TenantSetting))} SET value = value WHERE false";
        await using (var blocked = await competitor.Database.BeginTransactionAsync())
        {
            await competitor.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '100ms'");
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                competitor.Database.ExecuteSqlRawAsync(statement));
            await Assert.That(rejected!.SqlState).IsEqualTo(PostgresErrorCodes.LockNotAvailable);
            await blocked.RollbackAsync();
        }
        await owner.RollbackAsync();
        await competitor.Database.ExecuteSqlRawAsync(statement);
    }

    [Test]
    [Arguments("success")]
    [Arguments("missing-contract")]
    [Arguments("replica-mismatch")]
    [Arguments("stale-operation")]
    public async Task NativeBeginAndActivationRequireCurrentConvergenceEvidence(string scenario)
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await ReadAsync(caller);
        var binding = BindRequest(before);
        Guid deployment = binding["Bind"]!["Binding"]!["DeploymentId"]!.Deserialize<Guid>();
        await CommitOriginalAsync(caller, binding, before, BindingAfter(before, binding));
        before = await ReadAsync(caller);
        var begin = BaseRequest();
        begin["Begin"] = JsonSerializer.SerializeToNode(new BeginEventResourceProviderOperationCommand(deployment));
        await CommitOriginalAsync(caller, begin, before, AdvanceAfter(before, begin, deployment, true, 1, true));
        before = await ReadAsync(caller);
        var active = before["Data"]!["EventResourceProviderActivation"]!.AsArray()
            .Single(row => row!["Id"]!.Deserialize<Guid>() == deployment)!;
        await Assert.That(active["Epoch"]!.GetValue<long>()).IsEqualTo(2L);
        var activate = BaseRequest();
        activate["Activate"] = JsonSerializer.SerializeToNode(new ActivateEventResourceProviderCommand(
            deployment, scenario == "stale-operation" ? Guid.CreateVersion7() : active["CurrentOperationId"]!.Deserialize<Guid>(),
            2L, "resource", "default", true, 2, scenario == "replica-mismatch" ? 1 : 2,
            scenario != "missing-contract"));
        var expected = scenario == "stale-operation" ? before["Data"]!.DeepClone().AsObject()
            : AdvanceAfter(before, activate, deployment, false, scenario == "success" ? 2 : 3, false);
        await CommitOriginalAsync(caller, activate, before, expected);
        await Assert.That(JsonNode.DeepEquals(expected, (await ReadAsync(caller))["Data"])).IsTrue();
    }

    [Test]
    public async Task NativeBoundPublicationCommitsANewClosedOperation()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await ReadAsync(caller);
        var binding = BindRequest(before);
        Guid deployment = binding["Bind"]!["Binding"]!["DeploymentId"]!.Deserialize<Guid>();
        await CommitOriginalAsync(caller, binding, before, BindingAfter(before, binding));
        before = await ReadAsync(caller);
        var publication = PublicationRequest();
        publication["PublicationEndpoint"] = binding["Bind"]!["Binding"]!["Endpoints"]![0]!.DeepClone();
        var expected = AdvanceAfter(before, publication, deployment, true, 1, true);
        await CommitOriginalAsync(caller, publication, before, expected);
        await Assert.That(JsonNode.DeepEquals(expected, (await ReadAsync(caller))["Data"])).IsTrue();
    }

    [Test]
    public async Task RawScalarOriginalCannotAcquireThisFamily()
    {
        await InstallAsync();
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        var before = await ReadAsync(caller);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var scalar = new JsonObject { ["Key"] = EventResourceProviderBindingDocument.SettingKey, ["Value"] = "{}" };
        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(caller, "acquire", scalar, before));
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("DESC")]
    [Arguments("COLLATE \"C\"")]
    [Arguments("varchar_pattern_ops")]
    public async Task OriginalSettingKeyCannotBorrowDifferentIndexSemantics(string semantics)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        var sql = new HumanPrincipalSourceSql(migration);
        string indexName = migration.Model.FindEntityType(typeof(SystemSetting))!.GetIndexes()
            .Single(index => index.IsUnique).GetDatabaseName()!;
        await using (var transaction = await migration.Database.BeginTransactionAsync())
        {
            await migration.Database.ExecuteSqlRawAsync($"DROP INDEX {sql.Name(indexName)}");
            await migration.Database.ExecuteSqlRawAsync($"""
                CREATE UNIQUE INDEX resource_provider_wrong_key_semantics ON {sql.Table(typeof(SystemSetting))}
                    ({sql.Column(typeof(SystemSetting), nameof(SystemSetting.SettingKey))} {semantics})
                """);
            var rejected = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresEventResourceProviderContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
            await Assert.That(rejected!.MessageText).IsEqualTo("resource_provider_source_key_drift");
            await transaction.RollbackAsync();
        }
        await PostgresEventResourceProviderContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
    }

    [Test]
    public async Task DisabledSourceTriggerCannotPassReadiness()
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        string table = new HumanPrincipalSourceSql(migration).Table(typeof(EventResourceProviderActivation));
        await migration.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} DISABLE TRIGGER event_resource_provider_changed");
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresEventResourceProviderContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RoutineSearchPathOrExecutorPrivilegesCannotDrift(bool routine)
    {
        await InstallAsync();
        await using var migration = fixture.CreateDbContext();
        await migration.Database.OpenConnectionAsync();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        var sql = new HumanPrincipalSourceSql(migration);
        if (routine)
        {
            await migration.Database.ExecuteSqlRawAsync($"SET LOCAL ROLE {PostgresEventResourceProviderContract.OwnerRole}");
            await migration.Database.ExecuteSqlRawAsync(
                $"ALTER FUNCTION {PostgresEventResourceProviderContract.FunctionSql(migration, "read")}() SET search_path = public,pg_catalog");
            await migration.Database.ExecuteSqlRawAsync("RESET ROLE");
        }
        else
            await migration.Database.ExecuteSqlRawAsync(
                $"GRANT SELECT ON {sql.Name(PostgresEventResourceProviderContract.AttemptTable)} TO {PostgresEventResourceProviderContract.GlobalRole}");
        await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresEventResourceProviderContract.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
        await transaction.RollbackAsync();
    }

    private async Task InstallAsync()
    {
        await using var migration = fixture.CreateDbContext();
        await PostgresEventResourceProviderContract.ApplyAsync(migration, fixture.NativeQueueEnrollment);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, fixture.GetNativeQueueOptions(role));
        return new(builder.Options);
    }

    private static async Task<JsonObject> ReadAsync(ExploreDbContext context)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {PostgresEventResourceProviderContract.FunctionSql(context, "read")}()::text";
        return JsonNode.Parse((string)(await command.ExecuteScalarAsync())!)!.AsObject();
    }

    private static async Task ExecuteAsync(ExploreDbContext context, string action, JsonNode request, JsonNode image)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {PostgresEventResourceProviderContract.FunctionSql(context, action)}(@request,@image)";
        command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = request.ToJsonString() });
        command.Parameters.Add(new NpgsqlParameter("image", NpgsqlDbType.Jsonb) { Value = image.ToJsonString() });
        await command.ExecuteNonQueryAsync();
    }

    private const string Instant = "2040-01-01T12:00:00.0000000Z";
    private static async Task CommitOriginalAsync(ExploreDbContext context, JsonObject request, JsonObject before, JsonObject expected)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await ExecuteAsync(context, "acquire", request, before);
        await ExecuteAsync(context, "apply", request, expected);
        await ExecuteAsync(context, "complete", request, expected);
        await transaction.CommitAsync();
    }

    private static JsonObject AdvanceAfter(JsonObject before, JsonObject request, Guid deployment, bool advanceEpoch,
        int state, bool rewriteDocument)
    {
        var expected = before["Data"]!.DeepClone().AsObject();
        var activation = expected["EventResourceProviderActivation"]!.AsArray()
            .Single(row => row!["Id"]!.Deserialize<Guid>() == deployment)!;
        if (advanceEpoch)
        {
            activation["Epoch"] = activation["Epoch"]!.GetValue<long>() + 1;
            activation["CurrentOperationId"] = request["OperationId"]!.DeepClone();
        }
        activation["State"] = state;
        activation["ConcurrencyStamp"] = request["ConcurrencyStamp"]!.DeepClone();
        activation["UpdatedAt"] = Instant;
        activation["UpdatedBy"] = request["Actor"]!.DeepClone();
        if (rewriteDocument)
        {
            var setting = expected["SystemSetting"]!.AsArray()
                .Single(row => row!["SettingKey"]!.GetValue<string>() == EventResourceProviderBindingDocument.SettingKey)!;
            var document = EventResourceProviderBindingDocument.Parse(setting["Value"]!.GetValue<string>());
            setting["Value"] = JsonSerializer.Serialize(new EventResourceProviderBindingDocument(
                request["Revision"]!.Deserialize<Guid>(), document.Deployments), EventResourceProviderBindingDocument.JsonOptions);
            setting["UpdatedAt"] = Instant;
            setting["UpdatedBy"] = request["Actor"]!.DeepClone();
        }
        return expected;
    }

    private static JsonObject BaseRequest() => new()
    {
        ["Actor"] = Guid.CreateVersion7(),
        ["AmbientTenant"] = Guid.CreateVersion7(),
        ["OccurredAt"] = Instant,
        ["OperationId"] = Guid.CreateVersion7(),
        ["Revision"] = Guid.CreateVersion7(),
        ["SettingId"] = Guid.CreateVersion7(),
        ["ConcurrencyStamp"] = Guid.CreateVersion7(),
        ["Bind"] = null,
        ["Begin"] = null,
        ["Activate"] = null,
        ["Publication"] = false,
        ["PublicationEndpoint"] = null
    };
    private static JsonObject PublicationRequest()
    {
        var request = BaseRequest();
        request["Publication"] = true;
        request["PublicationEndpoint"] = $"https://unbound-{Guid.CreateVersion7():N}.example.test";
        return request;
    }
    private static JsonObject BindRequest(JsonObject before)
    {
        var request = BaseRequest();
        var old = before["Data"]!["SystemSetting"]!.AsArray().SingleOrDefault(row =>
            row!["SettingKey"]!.GetValue<string>() == EventResourceProviderBindingDocument.SettingKey);
        var document = EventResourceProviderBindingDocument.Parse(old?["Value"]?.GetValue<string>());
        Guid deployment = Guid.CreateVersion7();
        request["Bind"] = JsonSerializer.SerializeToNode(new BindEventResourceProviderCommand(
            new(deployment, [$"https://native-{deployment:N}.example.test"], "resource", "default"), document.Revision));
        return request;
    }
    private static JsonObject BindingAfter(JsonObject before, JsonObject request)
    {
        var expected = before["Data"]!.DeepClone().AsObject();
        var systems = expected["SystemSetting"]!.AsArray();
        var old = systems.SingleOrDefault(row => row!["SettingKey"]!.GetValue<string>() == EventResourceProviderBindingDocument.SettingKey);
        var document = EventResourceProviderBindingDocument.Parse(old?["Value"]?.GetValue<string>());
        var binding = request["Bind"]!["Binding"]!.Deserialize<EventResourceDeploymentBinding>()!;
        var next = new EventResourceProviderBindingDocument(request["Revision"]!.GetValue<Guid>(),
            document.Deployments.Append(binding).ToArray());
        var setting = old?.DeepClone().AsObject() ?? new JsonObject
        {
            ["Id"] = request["SettingId"]!.DeepClone(),
            ["SettingKey"] = EventResourceProviderBindingDocument.SettingKey,
            ["SettingValueTypeId"] = (int)SettingValueType.Json,
            ["Category"] = "Cerbos",
            ["AllowedValues"] = null,
            ["Description"] = null,
            ["DisplayOrder"] = 0,
            ["CreatedAt"] = Instant,
            ["CreatedBy"] = request["Actor"]!.DeepClone()
        };
        setting["Value"] = JsonSerializer.Serialize(next, EventResourceProviderBindingDocument.JsonOptions);
        setting["IsLocked"] = true; setting["UpdatedAt"] = Instant; setting["UpdatedBy"] = request["Actor"]!.DeepClone();
        if (old is not null) systems.Remove(old);
        systems.Add(setting);
        expected["SystemSetting"] = Ordered(systems);
        var activations = expected["EventResourceProviderActivation"]!.AsArray();
        activations.Add(new JsonObject
        {
            ["Id"] = binding.DeploymentId,
            ["Epoch"] = 1L,
            ["CurrentOperationId"] = request["OperationId"]!.DeepClone(),
            ["State"] = 1,
            ["ConcurrencyStamp"] = request["ConcurrencyStamp"]!.DeepClone(),
            ["CreatedAt"] = Instant,
            ["UpdatedAt"] = Instant,
            ["CreatedBy"] = request["Actor"]!.DeepClone(),
            ["UpdatedBy"] = request["Actor"]!.DeepClone()
        });
        expected["EventResourceProviderActivation"] = Ordered(activations);
        return expected;
    }
    private static JsonArray Ordered(JsonArray rows) => new(rows.OrderBy(row => row!["Id"]!.Deserialize<Guid>().ToString("D"), StringComparer.Ordinal)
        .Select(row => row!.DeepClone()).ToArray());
}
