using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;
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
using Model = Explore.Persistence.Security.ManagedProviderProvisioningDataModel;
using Native = Explore.Persistence.Security.PostgresManagedProviderProvisioningContract;

namespace Event.Persistence.IntegrationTests;

/// <summary>
/// Restricted PostgreSQL DATA controls, not human HTTP acceptance. Parent fixture
/// composition installs the contract after original sources and preserves its epoch
/// during cleanup. No test repairs or installs a missing production prerequisite.
/// All execution is deferred.
/// </summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class HumanManagedProviderProvisioningNativeTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments("ASC NULLS LAST")]
    [Arguments("DESC NULLS LAST")]
    public async Task BootstrapGenerationUniqueIndexRequiresDeclaredOrder(string order)
    {
        await using var setup = fixture.CreateDbContext();
        await setup.Database.OpenConnectionAsync();
        await Native.RequireReadyAsync(setup, fixture.NativeQueueEnrollment);
        var sql = new HumanPrincipalSourceSql(setup);
        string table = sql.Table(typeof(InstanceBootstrapState));
        string index = await setup.Database.SqlQueryRaw<string>(
            """
            SELECT pg_catalog.format('%I.%I', n.nspname, c.relname) AS "Value"
            FROM pg_catalog.pg_index i
            JOIN pg_catalog.pg_class c ON c.oid = i.indexrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE i.indrelid = pg_catalog.to_regclass({0})
                AND i.indisunique AND NOT i.indisprimary
            """, table).SingleAsync();
        await using (var transaction = await setup.Database.BeginTransactionAsync())
        {
            await setup.Database.ExecuteSqlRawAsync($"DROP INDEX {index}");
            await setup.Database.ExecuteSqlRawAsync($"""
                CREATE UNIQUE INDEX bootstrap_generation_wrong_order ON {table}
                    ({sql.Quote(sql.ColumnName(typeof(InstanceBootstrapState), nameof(InstanceBootstrapState.Generation)))} {order})
                """);
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                Native.RequireReadyAsync(setup, fixture.NativeQueueEnrollment));
            await Assert.That(error!.MessageText).IsEqualTo("human_provisioning_key_drift");
            await transaction.RollbackAsync();
        }
        await Native.RequireReadyAsync(setup, fixture.NativeQueueEnrollment);
    }

    [Test]
    [Arguments(0, false)]
    [Arguments(1, false)]
    [Arguments(2, false)]
    [Arguments(0, true)]
    public async Task OriginalGlobalCompilesCreatesAndReplaysTheExternalAdministratorGraph(int organizer, bool activate)
    {
        var request = await Request(organizer, activate);
        var created = await Stage(request);
        var rows = created.Rows;
        await Assert.That(rows.AlreadyProvisioned).IsFalse();
        await Assert.That(rows.TenantId).IsEqualTo(request.Seeds.Tenant);
        await Assert.That(rows.UserId).IsEqualTo(request.Seeds.User);
        foreach (string type in new[] { "Tenant", "User", "Actor", "UserExternalLogin", "TenantUser", "TenantUserProfile", "TenantUserRoleGrant" })
            await Assert.That(created.Image["Data"]![type]!.AsArray().Any(row =>
                row!["CreatedBy"]!.GetValue<string>() == request.Human.ToString("D"))).IsTrue();
        await Assert.That(created.Image["Data"]!["TenantSettingsDocument"]!.AsArray().Count).IsEqualTo(2);
        await Assert.That(created.Image["Data"]!["ExternalBinding"]!.AsArray().Count).IsEqualTo(organizer == 0 ? 6 : 8);
        var revision = created.Image["Data"]!["EventDiscoveryRevision"]![0]!;
        await Assert.That(revision["DisclosureEpoch"]!.GetValue<long>()).IsEqualTo(1L);
        await Assert.That(revision["IdentityEpoch"]!.GetValue<long>()).IsEqualTo(0L);
        var replayRequest = request with { Seeds = Model.Seeds.Create(), At = request.At.AddMinutes(1) };
        var replay = await Stage(replayRequest);
        await Assert.That(replay.Rows.AlreadyProvisioned).IsTrue();
        await Assert.That(replay.Rows.TenantId).IsEqualTo(rows.TenantId);
        await Assert.That(JsonNode.DeepEquals(created.Image["Data"], replay.Image["Data"])).IsTrue();
    }

    [Test]
    public async Task ExistingExternalUserOnlyReceivesTheIntendedEmailVerificationPromotion()
    {
        var request = await Request(0);
        Guid userId;
        JsonObject beforeUser;
        await using (var seed = fixture.CreateDbContext())
        {
            var user = new User
            {
                Id = Guid.CreateVersion7(),
                EmailVerified = false,
                Pii = new UserPii { Email = "existing@protocol.invalid", FirstName = "Existing", LastName = "Identity" }
            };
            seed.Users.Add(user);
            seed.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                User = user,
                UserId = user.Id,
                AuthenticationProvider = null!,
                AuthenticationProviderId = request.Provider,
                ProviderKey = request.Account,
                ProviderDisplayName = "Retained"
            });
            await seed.SaveChangesAsync();
            userId = user.Id;
        }
        await using (var reader = Open(PrimaryDatabaseRole.Global))
        {
            await reader.Database.OpenConnectionAsync();
            beforeUser = JsonNode.Parse(await Native.InvokeAsync(reader, fixture.NativeQueueEnrollment, "read", request, null, default))!
                ["Data"]!["User"]![0]!.AsObject();
        }
        var result = await Stage(request);
        await Assert.That(result.Rows.UserId).IsEqualTo(userId);
        var actual = result.Image["Data"]!["User"]![0]!.AsObject();
        await Assert.That(actual["EmailVerified"]!.GetValue<bool>()).IsTrue();
        foreach (string field in beforeUser.Select(pair => pair.Key).Except(["EmailVerified", "ConcurrencyStamp", "UpdatedAt", "UpdatedBy"]))
            await Assert.That(JsonNode.DeepEquals(beforeUser[field], actual[field])).IsTrue();
        await Assert.That(result.Image["Data"]!["UserPii"]![0]!["Email"]!.GetValue<string>()).IsEqualTo("existing@protocol.invalid");
    }

    [Test]
    [Arguments(LocalCredentialState.ChangeRequired)]
    [Arguments(LocalCredentialState.Ready)]
    public async Task LocalTargetRetainsEveryCredentialAndApplicationIdentityColumn(LocalCredentialState state)
    {
        var external = await Request(0);
        Guid subject = Guid.CreateVersion7();
        Guid actor = Guid.CreateVersion7();
        Guid login = Guid.CreateVersion7();
        Guid operation = Guid.CreateVersion7();
        await using (var seed = fixture.CreateDbContext())
        {
            var identity = new LocalIdentityUser
            {
                Id = subject,
                UserName = "local-" + subject.ToString("N"),
                NormalizedUserName = ("local-" + subject.ToString("N")).ToUpperInvariant(),
                FirstName = "Local",
                LastName = "Target",
                CreatedAt = external.At
            };
            identity.PasswordHash = new PasswordHasher<LocalIdentityUser>().HashPassword(
                identity, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
            seed.Set<LocalIdentityUser>().Add(identity);
            seed.Users.Add(new User
            {
                Id = subject,
                Pii = new UserPii { Email = string.Empty, FirstName = "Local", LastName = "Target" }
            });
            seed.Actors.Add(new Actor
            {
                Id = actor,
                ActorTypeId = (int)ActorTypeEnum.User,
                ActorType = null!,
                UserId = subject,
                Pii = new ActorPii { DisplayName = "Local target" }
            });
            seed.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = login,
                UserId = subject,
                User = null!,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
                AuthenticationProvider = null!,
                ProviderKey = subject.ToString("D")
            });
            seed.Set<LocalIdentityCredentialOperation>().Add(new(new LocalCredentialOperationReceipt(
                operation, LocalCredentialOperationKind.Create, LocalCredentialOperationStage.ProvisioningPending,
                external.Human, subject, actor, login, external.At), Guid.CreateVersion7()));
            seed.Set<IdentityUserToken<Guid>>().Add(new()
            {
                UserId = subject,
                LoginProvider = LocalCredentialStateMetadata.TokenLoginProvider,
                Name = LocalCredentialStateMetadata.TokenName,
                Value = JsonSerializer.Serialize(new LocalCredentialStateMetadata(1, state, operation, subject))
            });
            await seed.SaveChangesAsync();
            await seed.Set<LocalIdentityCredentialOperation>().Where(row => row.Id == operation)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Stage,
                    state == LocalCredentialState.Ready ? LocalCredentialOperationStage.Replaced : LocalCredentialOperationStage.ChangeRequired));
        }
        var provisioning = JsonSerializer.Deserialize<ManagedProviderClientProvisioningDto>(
            JsonSerializer.Serialize(external.Input, Model.Json), Model.Json)!;
        var request = await Model.CaptureAsync(new()
        {
            ProvisioningDto = provisioning with
            {
                ExternalAdmin = null,
                LocalIdentity = new() { LocalSubjectId = subject }
            }
        }, external.Human, TimeProvider.System, default);
        JsonObject before;
        await using (var reader = Open(PrimaryDatabaseRole.Global))
        {
            await reader.Database.OpenConnectionAsync();
            before = JsonNode.Parse(await Native.InvokeAsync(reader, fixture.NativeQueueEnrollment, "read", request, null, default))!
                ["Data"]!.AsObject();
        }
        var result = await Stage(request);
        await Assert.That(result.Rows.UserId).IsEqualTo(subject);
        await Assert.That(result.Rows.UserActorId).IsEqualTo(actor);
        await Assert.That(result.Rows.UserExternalLoginId).IsEqualTo(login);
        foreach (string field in new[] { "LocalIdentityUser", "LocalIdentityCredentialOperation", "IdentityUserToken`1",
            "User", "UserPii", "Actor", "ActorPii", "UserExternalLogin" })
            await Assert.That(JsonNode.DeepEquals(before[field], result.Image["Data"]![field])).IsTrue();
    }

    [Test]
    [Arguments(PrimaryDatabaseRole.Runtime)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyInstanceMutation)]
    [Arguments(PrimaryDatabaseRole.EmailPolicyTenantMutation)]
    [Arguments(PrimaryDatabaseRole.Migrator)]
    public async Task NonGlobalLoginCannotBorrowTheHumanProvisioningDataProgram(PrimaryDatabaseRole role)
    {
        var request = await Request(0);
        await using var caller = Open(role);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<PostgresException>(() => Raw(caller, "read", request));
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments("human")]
    [Arguments("customer")]
    [Arguments("seeds")]
    [Arguments("account")]
    public async Task AcquiredNativeAttemptCannotReplaceAnyOriginalBinding(string change)
    {
        var request = await Request(0);
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        string before = await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "read", request, null, default);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "acquire", request, before, default);
        var changed = change switch
        {
            "human" => request with { Human = Guid.CreateVersion7() },
            "customer" => request with { Input = request.Input with { ExternalCustomerId = Guid.CreateVersion7().ToString("D") } },
            "seeds" => request with { Seeds = Model.Seeds.Create() },
            _ => request with { Account = request.Account + "-changed" }
        };
        await Assert.ThrowsAsync<PostgresException>(() => Raw(caller, "compile", changed));
        await owner.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingNativeCompletionCannotCommitAPartialOrCompleteBusinessGraph(bool apply)
    {
        var request = await Request(0);
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        string before = await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "read", request, null, default);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "acquire", request, before, default);
        await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "compile", request, null, default);
        if (apply) await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "apply", request, null, default);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => owner.CommitAsync());
        await Assert.That(failure!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.Tenants.AnyAsync(row => row.Id == request.Seeds.Tenant)).IsFalse();
        await Assert.That(await observer.Users.AnyAsync(row => row.Id == request.Seeds.User)).IsFalse();
    }

    [Test]
    [Arguments("compile")]
    [Arguments("apply")]
    [Arguments("complete")]
    public async Task EachPrivateNativeStageIsSingleUse(string stage)
    {
        var request = await Request(0);
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        string before = await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "read", request, null, default);
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "acquire", request, before, default);
        await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "compile", request, null, default);
        if (stage != "compile") await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "apply", request, null, default);
        if (stage == "complete") await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "complete", request, null, default);
        await Assert.ThrowsAsync<PostgresException>(() => Raw(caller, stage, request));
        await owner.RollbackAsync();
    }

    [Test]
    public async Task GlobalCannotSupplyACompiledSuccessImageOrCallThePrivateCompiler()
    {
        var request = await Request(0);
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<PostgresException>(() => Raw(caller, "program", request, "{}"));
        await owner.RollbackAsync();
        await using var second = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<PostgresException>(() => Raw(caller, "compile", request, "{\"Rows\":{\"AlreadyProvisioned\":false}}"));
        await second.RollbackAsync();
    }

    private async Task<Model.Request> Request(int organizer, bool activate = false)
    {
        Guid human;
        await using (var seed = fixture.CreateDbContext())
        {
            var administrator = new User
            {
                Id = Guid.CreateVersion7(),
                Pii = new UserPii { Email = string.Empty, FirstName = "Native", LastName = "DATA" }
            };
            seed.Users.Add(administrator);
            await seed.SaveChangesAsync();
            human = administrator.Id;
        }
        return await Model.CaptureAsync(new EnsureManagedProviderClientProvisionedCommand
        {
            ProvisioningDto = new()
            {
                ProviderKey = "native-data",
                ExternalSystem = "native-data",
                ExternalCustomerId = Guid.CreateVersion7().ToString("D"),
                TenantFullName = "Native DATA tenant",
                TenantSlug = "native-" + Guid.CreateVersion7().ToString("N"),
                ActivateTenant = activate,
                DirectoryOperatorIdentity = !activate ? null : new()
                {
                    PublicName = "Native DATA",
                    LegalName = "Native DATA operator",
                    OperatorKindCode = "unincorporated_association",
                    JurisdictionCountryCode = "US",
                    PublicContactEmail = "operator@protocol.invalid",
                    LegalNoticeUrl = "https://protocol.invalid/legal",
                    PrivacyUrl = "https://protocol.invalid/privacy"
                },
                ExternalAdmin = new()
                {
                    IdentityProvider = "https://issuer.protocol.invalid",
                    Subject = Guid.CreateVersion7().ToString("D"),
                    Email = "administrator@protocol.invalid",
                    FirstName = "External",
                    LastName = "Administrator",
                    EmailVerified = true
                },
                Organizer = organizer == 0 ? null : new()
                {
                    Kind = (ManagedProviderOrganizerKindDto)organizer,
                    FullName = "Native organizer",
                    Description = "Closed description"
                }
            }
        }, human, TimeProvider.System, default);
    }

    private async Task<(HumanManagedProviderProvisioningRows Rows, JsonObject Image)> Stage(Model.Request request)
    {
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        string before = await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "read", request, null, default);
        var portable = ManagedProviderProvisioningCompiler.Compile(caller, request, JsonNode.Parse(before)!["Data"]!.AsObject());
        await using var owner = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "acquire", request, before, default);
        var compiled = JsonNode.Parse(await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "compile", request, null, default))!;
        await Assert.That(JsonNode.DeepEquals(compiled["Data"], portable.Postimage)).IsTrue();
        var rows = compiled["Rows"]!.Deserialize<HumanManagedProviderProvisioningRows>()!;
        await Assert.That(rows).IsEqualTo(portable.Rows);
        var applied = JsonNode.Parse(await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "apply", request, null, default))!.AsObject();
        var completed = JsonNode.Parse(await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "complete", request, null, default));
        await Assert.That(JsonNode.DeepEquals(applied, completed)).IsTrue();
        await owner.CommitAsync();
        return (rows, applied);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private static async Task<string> Raw(ExploreDbContext context, string name, Model.Request request, string? additional = null)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {Native.FunctionSql(context, name)}(@request{(additional is null ? "" : ",@additional")})::text";
        command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = Model.SerializeRequest(request) });
        if (additional is not null) command.Parameters.Add(new NpgsqlParameter("additional", NpgsqlDbType.Jsonb) { Value = additional });
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Native DATA result missing."));
    }
}
