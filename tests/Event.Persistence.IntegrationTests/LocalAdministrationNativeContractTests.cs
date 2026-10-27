using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Native = Explore.Persistence.Security.PostgresLocalAdministrationContract;

namespace Event.Persistence.IntegrationTests;

/// <summary>
/// Restricted native DATA controls, not authenticated provider acceptance.
/// Parent fixture composition must install the native contract after S/P/route/H and
/// preserve local_administration_source through Respawn. All execution is deferred.
/// </summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed partial class LocalAdministrationNativeContractTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task RuntimeCompletesCreationBindingActivationAndResetWithExactProvenance()
    {
        var source = await SeedAsync();
        var create = Create(source);
        var accepted = Creation();
        var pending = await StageAsync(create, accepted);
        await Assert.That(pending.Outcome).IsEqualTo("Created");
        var pendingData = pending.Image["Data"]!;
        await Assert.That(pendingData["LocalIdentityUser"]!.AsArray().Count).IsEqualTo(1);
        await Assert.That(pendingData["LocalIdentityCredentialOperation"]!.AsArray().Count).IsEqualTo(1);
        await Assert.That(pendingData["CredentialToken"]!.AsArray().Count).IsEqualTo(1);
        string hash = pendingData["LocalIdentityUser"]![0]!["PasswordHash"]!.GetValue<string>();
        var op = pendingData["LocalIdentityCredentialOperation"]![0]!;
        Guid stamp = Guid.Parse(op["ConcurrencyStamp"]!.GetValue<string>());

        var read = await StageAsync(create with { Stage = LocalAdministrationStage.ReadProvisioning }, new());
        await Assert.That(JsonNode.DeepEquals(read.Image["Data"], pendingData)).IsTrue();
        // Reconciliation is performed by the current original administrator, not the
        // earlier operation initiator. Effective EF CreatedBy uses this current owner.
        Guid reconciler = await AddAdministratorAsync();
        var bind = create with
        {
            Producer = "Reconcile",
            Stage = LocalAdministrationStage.Bind,
            ActorId = reconciler,
            ExpectedOperationConcurrencyStamp = stamp,
            Email = null,
            FirstName = null,
            LastName = null,
            NormalizedUserName = null,
            NormalizedEmail = null
        };
        var bound = await StageAsync(bind, new(UserConcurrencyStamp: Guid.CreateVersion7(), ActorConcurrencyStamp: Guid.CreateVersion7()));
        await Assert.That(bound.Outcome).IsEqualTo("Bound");
        foreach (string type in new[] { "User", "Actor", "UserExternalLogin" })
            await Assert.That(bound.Image["Data"]![type]![0]!["CreatedBy"]!.GetValue<string>()).IsEqualTo(reconciler.ToString("D"));
        foreach (string type in new[] { "User", "UserPii", "Actor", "ActorPii", "UserExternalLogin" })
            await Assert.That(bound.Image["Data"]![type]!.AsArray().Count).IsEqualTo(1);

        var active = await StageAsync(bind with { Stage = LocalAdministrationStage.Activate }, Mutation());
        await Assert.That(active.Outcome).IsEqualTo("Activated");
        await Assert.That(active.Image["Data"]!["LocalIdentityUser"]![0]!["PasswordHash"]!.GetValue<string>()).IsEqualTo(hash);
        var activatedOp = active.Image["Data"]!["LocalIdentityCredentialOperation"]![0]!;
        var reset = new Native.Request("Reset", LocalAdministrationStage.Reset, source.Actor, source.Tenant,
            Guid.CreateVersion7(), accepted.LocalSubjectId, create.OperationId,
            Guid.Parse(activatedOp["ConcurrencyStamp"]!.GetValue<string>()), Reason: "Native credential recovery");
        var replacement = Mutation() with { PasswordHash = FreshHash(), PreviousOperationStamp = Guid.CreateVersion7() };
        var resetResult = await StageAsync(reset, replacement);
        await Assert.That(resetResult.Outcome).IsEqualTo("Reset");
        var operations = resetResult.Image["Data"]!["LocalIdentityCredentialOperation"]!.AsArray();
        var superseded = operations.Single(row => row!["Id"]!.GetValue<string>() == create.OperationId!.Value.ToString("D"))!;
        var replacementOp = operations.Single(row => row!["Id"]!.GetValue<string>() == reset.OperationId!.Value.ToString("D"))!;
        await Assert.That(superseded["Stage"]!.GetValue<int>()).IsEqualTo((int)LocalCredentialOperationStage.Superseded);
        await Assert.That(JsonNode.DeepEquals(replacementOp["VerifiedAt"], activatedOp["VerifiedAt"])).IsTrue();
        await Assert.That(JsonNode.DeepEquals(replacementOp["VerifiedByApplicationUserId"], activatedOp["VerifiedByApplicationUserId"])).IsTrue();
        await Assert.That(resetResult.Image["Data"]!["LocalIdentityUser"]![0]!["PasswordHash"]!.GetValue<string>())
            .IsEqualTo(replacement.PasswordHash);
        foreach (string type in new[] { "User", "UserPii", "Actor", "ActorPii", "UserExternalLogin" })
            await Assert.That(JsonNode.DeepEquals(resetResult.Image["Data"]![type], active.Image["Data"]![type])).IsTrue();

        var replay = await StageAsync(reset, new());
        await Assert.That(replay.Outcome).IsEqualTo("Replayed");
        await Assert.That(JsonNode.DeepEquals(replay.Image["Data"], resetResult.Image["Data"])).IsTrue();
        // The subsequent physical Disclose owner can read the exact reset rows
        // before source Compare calls Acquire. No new credential is substituted.
        var disclose = await StageAsync(reset with { Stage = LocalAdministrationStage.Disclose }, new());
        await Assert.That(JsonNode.DeepEquals(disclose.Image["Data"], resetResult.Image["Data"])).IsTrue();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Global)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task OtherPurposesCannotExecuteRuntimeLifecycleData(PrimaryDatabaseRole role)
    {
        var source = await SeedAsync();
        await using var caller = Open(role, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<PostgresException>(() => RawAsync(caller, "read", Create(source)));
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("request")]
    [Arguments("stage")]
    [Arguments("actor")]
    public async Task AcquiredStageCannotChangeOriginalIdentity(string component)
    {
        var source = await SeedAsync();
        var request = Create(source);
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None);
        await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, CancellationToken.None);
        var changed = component switch
        {
            "tenant" => request with { TenantId = Guid.CreateVersion7() },
            "request" => request with { OperationId = Guid.CreateVersion7() },
            "stage" => request with { Stage = LocalAdministrationStage.Disclose },
            _ => request with { ActorId = Guid.CreateVersion7() }
        };
        await Assert.ThrowsAsync<PostgresException>(() =>
            Native.CompileAsync(caller, fixture.NativeQueueEnrollment, changed, Creation(), CancellationToken.None));
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("compile")]
    [Arguments("apply")]
    [Arguments("acquire")]
    public async Task PrivatePlanAndPhysicalTombstoneCannotReplay(string action)
    {
        var source = await SeedAsync();
        var request = new Native.Request("Status", LocalAdministrationStage.Disclose, source.Actor, source.Tenant, Guid.CreateVersion7());
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None);
        await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, CancellationToken.None);
        await Native.CompileAsync(caller, fixture.NativeQueueEnrollment, request, new(), CancellationToken.None);
        if (action != "compile")
        {
            await Native.ApplyPlanAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None);
            if (action == "acquire")
                await Native.CompleteAsync(caller, fixture.NativeQueueEnrollment, request, before, CancellationToken.None);
        }
        Task attack = action switch
        {
            "compile" => Native.CompileAsync(caller, fixture.NativeQueueEnrollment, request, new(), CancellationToken.None),
            "apply" => Native.ApplyPlanAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None),
            _ => Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, CancellationToken.None)
        };
        await Assert.ThrowsAsync<PostgresException>(() => attack);
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingApplicationOrCompletionCannotCommit(bool apply)
    {
        var source = await SeedAsync();
        var request = Create(source);
        var accepted = Creation();
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None);
        await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, CancellationToken.None);
        await Native.CompileAsync(caller, fixture.NativeQueueEnrollment, request, accepted, CancellationToken.None);
        if (apply) await Native.ApplyPlanAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None);
        var rejected = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        await Assert.That(rejected!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.Set<LocalIdentityUser>().AnyAsync(user => user.Id == accepted.LocalSubjectId)).IsFalse();
        await Assert.That(await observer.Set<LocalIdentityCredentialOperation>().AnyAsync(op => op.Id == request.OperationId)).IsFalse();
    }

    [Test]
    [Arguments("PasswordHash")]
    [Arguments("IdentityConcurrencyStamp")]
    [Arguments("SecurityStamp")]
    [Arguments("OperationStamp")]
    public async Task ReadOnlyStagesRejectCredentialDerivations(string field)
    {
        var source = await SeedAsync();
        var request = new Native.Request("Status", LocalAdministrationStage.Disclose, source.Actor, source.Tenant, Guid.CreateVersion7());
        await using var caller = Open(PrimaryDatabaseRole.Runtime, source.Tenant);
        await caller.Database.OpenConnectionAsync();
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None);
        await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, CancellationToken.None);
        var payload = JsonSerializer.SerializeToNode(new Native.Accepted())!.AsObject();
        payload[field] = field == "PasswordHash" ? FreshHash() : Guid.CreateVersion7().ToString("D");
        await Assert.ThrowsAsync<PostgresException>(() => RawAsync(caller, "compile", request, payload.ToJsonString()));
        await transaction.RollbackAsync();
    }

    private async Task<Seeded> SeedAsync()
    {
        await using var migration = fixture.CreateDbContext();
        Guid administrator = await AddAdministratorAsync();
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Local native",
            Slug = $"local-native-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        migration.Add(tenant);
        await migration.SaveChangesAsync();
        return new(administrator, tenant.Id);
    }

    private async Task<Guid> AddAdministratorAsync()
    {
        await using var migration = fixture.CreateDbContext();
        var administrator = new User
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = Instant(),
            Pii = new UserPii { Email = string.Empty, FirstName = "Native", LastName = "Administrator" }
        };
        migration.Add(administrator);
        await migration.SaveChangesAsync();
        return administrator.Id;
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role, Guid tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        var context = new ExploreDbContext(options.Options) { TenantContext = new TenantIdentity(tenant) };
        context.BindTenantLease();
        return context;
    }

    private async Task<(string Outcome, JsonNode Image)> StageAsync(Native.Request request, Native.Accepted accepted)
    {
        await using var caller = Open(PrimaryDatabaseRole.Runtime, request.TenantId);
        await caller.Database.OpenConnectionAsync();
        return await caller.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.That(await caller.Database.SqlQueryRaw<string>("SELECT session_user::text AS \"Value\"").SingleAsync())
                .IsEqualTo(fixture.NativeQueueEnrollment.RuntimeLogin);
            string before = await Native.ReadAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None);
            await Native.AcquireAsync(caller, fixture.NativeQueueEnrollment, request, before, CancellationToken.None);
            await Native.CompileAsync(caller, fixture.NativeQueueEnrollment, request, accepted, CancellationToken.None);
            var applied = JsonNode.Parse(await Native.ApplyPlanAsync(caller, fixture.NativeQueueEnrollment, request, CancellationToken.None))!;
            string disclosed = applied["Image"]!.ToJsonString();
            await Native.CompleteAsync(caller, fixture.NativeQueueEnrollment, request, disclosed, CancellationToken.None);
            await owner.CommitAsync();
            return (applied["Outcome"]!.GetValue<string>(), applied["Image"]!.DeepClone());
        });
    }

    private static Native.Request Create(Seeded source)
    {
        string email = $"local-{Guid.CreateVersion7():N}@example.invalid";
        return new("Create", LocalAdministrationStage.CreatePending, source.Actor, source.Tenant,
            Guid.CreateVersion7(), Email: email, FirstName: "Local", LastName: "Native",
            NormalizedUserName: email.ToUpperInvariant(), NormalizedEmail: email.ToUpperInvariant());
    }

    private static Native.Accepted Creation() => Mutation() with
    {
        LocalSubjectId = Guid.CreateVersion7(),
        PersonalActorId = Guid.CreateVersion7(),
        ExternalLoginId = Guid.CreateVersion7(),
        PasswordHash = FreshHash()
    };

    private static Native.Accepted Mutation() => new(ChangedAt: Instant(), OperationStamp: Guid.CreateVersion7(),
        IdentityConcurrencyStamp: Guid.CreateVersion7().ToString("N"), SecurityStamp: Guid.CreateVersion7().ToString("N"));

    private static DateTime Instant()
    {
        DateTime instant = DateTime.UtcNow;
        return new DateTime(instant.Ticks - instant.Ticks % 10, DateTimeKind.Utc);
    }

    private static string FreshHash() => new PasswordHasher<LocalIdentityUser>().HashPassword(
        new LocalIdentityUser(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

    private static async Task<string> RawAsync(ExploreDbContext caller, string name, Native.Request request, string? argument = null)
    {
        await using var command = caller.Database.GetDbConnection().CreateCommand();
        command.Transaction = caller.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {Native.FunctionSql(caller, name)}(@request{(argument is null ? "" : ",@argument")})::text";
        command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request) });
        if (argument is not null) command.Parameters.Add(new NpgsqlParameter("argument", NpgsqlDbType.Jsonb) { Value = argument });
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Native result missing."));
    }

    private sealed record Seeded(Guid Actor, Guid Tenant);
    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
}
