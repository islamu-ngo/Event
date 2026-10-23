using System.Collections.Immutable;
using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Services;
using Explore.Persistence.Security;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class ResourceImportNativeConsumptionTests(PostgreSqlContainerFixture fixture)
{
    private const string ResourceKey = GovernanceSettingKeys.EventResources.MaxActiveResources;

    [Test]
    public async Task NativeSnapshotPreservesLatestBootstrapMetadataWithoutRawHistoryAccess()
    {
        await using var human = await NativeImportHuman.CreateAsync(fixture);
        var completed = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        var suspended = new Tenant
        {
            Id = Guid.CreateVersion7(),
            Slug = $"suspended-{Guid.CreateVersion7():N}",
            FullName = "Suspended import target",
            TenantStatusId = (int)TenantStatusEnum.Suspended,
            TenantStatus = null!
        };
        ConfigurationManifestOperation latest;
        await using (var seed = fixture.CreateDbContext())
        {
            ConfigurationManifestOperation Receipt(int generation, DateTime timestamp) =>
                ConfigurationManifestOperation.Create(ConfigurationManifestAuditMode.Bootstrap,
                    ConfigurationManifestContractMetadata.ApiVersion,
                    ConfigurationManifestContractMetadata.Kind, $"bootstrap-{generation}",
                    new string('a', 64), ConfigurationManifestOperationStatus.Applied,
                    1, 1, 0, 0, null, null, timestamp, timestamp,
                    new string('b', 64), generation, [ResourceKey], ["paid_event_policy"]);
            latest = Receipt(2, completed);
            seed.Tenants.Add(suspended);
            seed.ConfigurationManifestOperations.AddRange(latest, Receipt(1, completed.AddDays(1)));
            await seed.SaveChangesAsync();
        }
        await using var owner = await human.Services.GetRequiredService<IGlobalExploreDbContextFactory>()
            .CreateDbContextAsync();
        var denied = await Assert.ThrowsAsync<PostgresException>(async () =>
            await owner.ConfigurationManifestOperations.AsNoTracking().AnyAsync());
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        owner.BindConfigurationInstanceImportOwner();
        await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = new Explore.Persistence.Repositories.ConfigurationManifestOperationRepository(owner);
        var restored = await repository.GetLatestAppliedBootstrapAsync(default);
        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.Id).IsEqualTo(latest.Id);
        await Assert.That(restored.ManifestName).IsEqualTo("bootstrap-2");
        await Assert.That(restored.BootstrapGeneration).IsEqualTo((int?)2);
        await Assert.That(restored.Digest).IsEqualTo(new string('a', 64));
        await Assert.That(restored.InstanceSectionDigest).IsEqualTo(new string('b', 64));
        await Assert.That(restored.CompletedAt).IsEqualTo(completed);
        await Assert.That(restored.InstanceChangedSettingKeyNames).IsEquivalentTo([ResourceKey]);
        await Assert.That(restored.InstanceChangedDocumentKeyNames).IsEquivalentTo(["paid_event_policy"]);
        var tenants = new Explore.Persistence.Repositories.TenantRepository(owner);
        var targets = await tenants.GetBySlugsAsNoTrackingAsync(
            [$"resource-{human.Tenant:N}", suspended.Slug, "missing-import-target"], default);
        await Assert.That(targets.Select(tenant => tenant.Id)).IsEquivalentTo([human.Tenant, suspended.Id]);
        await Assert.That(targets.Single(tenant => tenant.Id == suspended.Id).TenantStatusId)
            .IsEqualTo((int)TenantStatusEnum.Suspended);
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalNativeImportCommitsOnlyWithItsExactHumanAuditActor(bool foreignAuditActor)
    {
        await using var human = await NativeImportHuman.CreateAsync(fixture);
        var other = await human.ProvisionAsync();
        if (foreignAuditActor)
            human.Probe.AuditActor = other.Actor;
        await human.InRequestAsync(async () =>
        {
            var executor = human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>();
            var import = await PrepareAsync(executor, human.Tenant);
            var apply = executor.ApplyAsync(import.SessionId, import.AccessToken, import.Request, null, null, default);
            if (foreignAuditActor)
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => apply);
            else
                await Assert.That((await apply).Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
            await Assert.That(human.Probe.Reached).IsTrue();
            await Assert.That(human.Probe.Actor).IsEqualTo(human.Actor);
            await using var observer = fixture.CreateDbContext();
            var stored = await observer.TenantSettingOverrides.AsNoTracking().SingleOrDefaultAsync(row =>
                row.TenantId == human.Tenant && row.SettingKey == ResourceKey);
            if (foreignAuditActor)
                await Assert.That(stored).IsNull();
            else
            {
                await Assert.That(stored).IsNotNull();
                await Assert.That(stored!.Value).IsEqualTo("12");
                await Assert.That(stored.UpdatedBy).IsEqualTo((Guid?)human.Actor);
            }
            await Assert.That(await observer.ConfigurationImportOperations.AnyAsync(row =>
                row.SessionId == import.SessionId)).IsEqualTo(!foreignAuditActor);
            await Assert.That(await observer.Set<ConfigurationImportSession>().Where(row =>
                    row.SessionId == import.SessionId).Select(row => row.State).SingleAsync())
                .IsEqualTo(foreignAuditActor
                    ? ConfigurationImportSessionState.PreviewReady : ConfigurationImportSessionState.Consumed);
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ManagedResourceWriteCannotReplaceRevokedHumanWithAuditActor(bool substituteAuditActor)
    {
        await using var human = await NativeImportHuman.CreateAsync(fixture);
        var other = await human.ProvisionAsync();
        human.Probe.AuditActor = substituteAuditActor ? other.Actor : human.Actor;
        await human.InRequestAsync(async () =>
        {
            var executor = human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>();
            var import = await PrepareAsync(executor, human.Tenant);
            await using (var revoke = fixture.CreateDbContext())
                await revoke.PlatformUserRoles.Where(row => row.Id == human.Grant).ExecuteDeleteAsync();
            var denial = await Assert.ThrowsAsync<AuthorizationException>(() =>
                executor.ApplyAsync(import.SessionId, import.AccessToken, import.Request, null, null, default));
            await Assert.That(denial!.Resource).IsEqualTo(ResourceKinds.InstanceSetting);
            await Assert.That(denial.Action).IsEqualTo(AuthorizationActions.InstanceSettings.View);
            // Admission rejects the revoked original before any resource consumer can
            // substitute another still-authorized administrator into audit metadata.
            await Assert.That(human.Probe.Reached).IsFalse();
            await AssertUnchangedAsync(import.SessionId, human.Tenant);
            await using var observer = fixture.CreateDbContext();
            await Assert.That(await observer.PlatformUserRoles.AnyAsync(row => row.Id == other.Grant)).IsTrue();
        });
    }

    [Test]
    public async Task OriginalResourceImportRetainsHumanFenceAndRollsBackBorrowedChanges()
    {
        await using var human = await NativeImportHuman.CreateAsync(fixture);
        await using (var revokeTenant = fixture.CreateDbContext())
            await revokeTenant.TenantUserRoleGrants.Where(row => row.Id == human.TenantGrant)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, DateTime.UtcNow));
        bool fenceObserved = false;
        human.Probe.AfterWrite = async (services, token) =>
        {
            var context = services.GetRequiredService<ExploreDbContext>();
            await using var contender = fixture.CreateDbContext();
            await using var attempt = await contender.Database.BeginTransactionAsync(token);
            var entity = contender.Model.FindEntityType(typeof(PlatformUserRole))!;
            string table = contender.GetService<ISqlGenerationHelper>()
                .DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
            var conflict = await Assert.ThrowsAsync<PostgresException>(async () =>
                await contender.Database.ExecuteSqlRawAsync(
                    $"SELECT id FROM {table} WHERE id = {{0}} FOR UPDATE NOWAIT", human.Grant));
            await Assert.That(conflict!.SqlState).IsEqualTo("55P03");
            await attempt.RollbackAsync(token);
            await Assert.That(context.Database.CurrentTransaction).IsNotNull();
            fenceObserved = true;
            throw new InvalidOperationException("A later import consumer rejects the borrowed transaction.");
        };
        await human.InRequestAsync(async () =>
        {
            var executor = human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>();
            var import = await PrepareAsync(executor, human.Tenant);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                executor.ApplyAsync(import.SessionId, import.AccessToken, import.Request, null, null, default));
            await Assert.That(human.Probe.Written).IsTrue();
            await Assert.That(fenceObserved).IsTrue();
            await AssertUnchangedAsync(import.SessionId, human.Tenant);
            await using var restricted = await human.Services.GetRequiredService<IGlobalExploreDbContextFactory>()
                .CreateDbContextAsync();
            var privateRead = await Assert.ThrowsAsync<PostgresException>(async () =>
                await restricted.Users.AsNoTracking().AnyAsync());
            await Assert.That(privateRead!.SqlState).IsEqualTo("42501");
        });
    }

    [Test]
    public async Task RevokedInstanceAdministrationDeniesNativeResourceImportBeforeMutation()
    {
        await using var human = await NativeImportHuman.CreateAsync(fixture);
        await human.InRequestAsync(async () =>
        {
            var executor = human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>();
            var import = await PrepareAsync(executor, human.Tenant);
            await using (var revoke = fixture.CreateDbContext())
                await revoke.PlatformUserRoles.Where(row => row.Id == human.Grant).ExecuteDeleteAsync();
            var denial = await Assert.ThrowsAsync<AuthorizationException>(() =>
                executor.ApplyAsync(import.SessionId, import.AccessToken, import.Request, null, null, default));
            await Assert.That(denial!.Resource).IsEqualTo(ResourceKinds.InstanceSetting);
            await Assert.That(denial.Action).IsEqualTo(AuthorizationActions.InstanceSettings.View);
            await Assert.That(human.Probe.Reached).IsFalse();
            await AssertUnchangedAsync(import.SessionId, human.Tenant);
        });
    }

    [Test]
    public async Task GlobalSettingsOwnerCannotMasqueradeAsNativeInstanceImport()
    {
        await using var human = await NativeImportHuman.CreateAsync(fixture);
        bool denied = false;
        await human.InRequestAsync(async () =>
        {
            // Select a genuine ordinary owner before the import starts. Selecting from
            // inside its callback would borrow the import owner rather than create another.
            await using var ordinary = await human.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                .CreateAsync(PolicyMutationScope.Instance, default);
            var subject = ordinary.Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!;
            await Assert.That(subject.PrincipalId).IsEqualTo(human.Actor);
            human.Probe.BeforeWrite = async (_, mutations, token) =>
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    ordinary.Services.GetRequiredService<IEventResourceSettingsWriter>().ApplyAsync(
                        mutations, subject, SettingsMutationOperation.ConfigurationInstanceImport, human.Actor, token));
                denied = true;
                throw new InvalidOperationException("The wrong owner cannot consume the original import.");
            };
            var executor = human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>();
            var import = await PrepareAsync(executor, human.Tenant);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                executor.ApplyAsync(import.SessionId, import.AccessToken, import.Request, null, null, default));
            await Assert.That(human.Probe.Reached).IsTrue();
            await Assert.That(denied).IsTrue();
            await Assert.That(human.Probe.Written).IsFalse();
            await AssertUnchangedAsync(import.SessionId, human.Tenant);
        });
    }

    private static async Task<PreparedImport> PrepareAsync(IConfigurationInstanceImportExecutor executor, Guid tenant)
    {
        // This scenario imports one stored override, not a replacement for flattened defaults.
        var snapshot = await executor.ReadSnapshotAsync(ConfigurationManifestExportView.Overrides, default);
        var manifest = JsonNode.Parse(snapshot.Utf8Json.Span)!;
        var tenants = manifest["spec"]!["tenants"]!.AsArray();
        foreach (var source in tenants)
            source!["spec"]!["settings"] = new JsonObject();
        var target = tenants.Single(source => source!["metadata"]!["name"]!.GetValue<string>() == $"resource-{tenant:N}")!;
        target["spec"]!["settings"] = new JsonObject { [ResourceKey] = 12 };
        var created = await executor.CreateAsync(JsonSerializer.SerializeToUtf8Bytes(manifest), default);
        var request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = ["tenant.settings"],
            Mappings = new Dictionary<string, string>(),
            ApplyMode = ConfigurationImportApplyMode.ApplySelected,
            GrantedApprovalCodes = []
        };
        await Assert.That((await executor.PreviewAsync(created.SessionId, created.AccessToken, request, default))
            .IsApplyReady).IsTrue();
        return new(created.SessionId, created.AccessToken, request);
    }

    private async Task AssertUnchangedAsync(Guid session, Guid tenant)
    {
        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.TenantSettingOverrides.AnyAsync(row =>
            row.TenantId == tenant && row.SettingKey == ResourceKey)).IsFalse();
        await Assert.That(await observer.ConfigurationImportOperations.AnyAsync(row => row.SessionId == session)).IsFalse();
        await Assert.That(await observer.Set<ConfigurationImportSession>().Where(row => row.SessionId == session)
            .Select(row => row.State).SingleAsync()).IsEqualTo(ConfigurationImportSessionState.PreviewReady);
    }

    private sealed record PreparedImport(Guid SessionId, string AccessToken, ConfigurationImportPreviewRequest Request);

    /// <summary>Observes the real selected writer inside the original acquired Apply callback.</summary>
    private sealed class ResourceProbe
    {
        internal Guid? AuditActor { get; set; }
        internal Guid Actor { get; private set; }
        internal bool Reached { get; private set; }
        internal bool Written { get; private set; }
        internal Func<IServiceProvider, ImmutableArray<EventResourceSettingMutation>, CancellationToken, Task>? BeforeWrite { get; set; }
        internal Func<IServiceProvider, CancellationToken, Task>? AfterWrite { get; set; }

        internal async Task<EventResourceSettingsWriteResult> ApplyAsync(IServiceProvider services,
            IEventResourceSettingsWriter writer, ImmutableArray<EventResourceSettingMutation> mutations,
            HumanCommitSubject subject, SettingsMutationOperation operation, Guid? actor, CancellationToken token)
        {
            var context = services.GetRequiredService<ExploreDbContext>();
            if (!context.IsConfigurationInstanceImportOwner)
                return await writer.ApplyAsync(mutations, subject, operation, actor, token);
            var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
            var issuer = services.GetRequiredService<IHumanConfigurationImportAdmission>();
            await Assert.That(context.Database.IsNpgsql()).IsTrue();
            await Assert.That(context.IsConfigurationInstanceImportOwner).IsTrue();
            await Assert.That(ReferenceEquals(lease.HumanScope!.Subject, subject)).IsTrue();
            await Assert.That(lease.HumanLifetime!.LocalSession).IsNotNull();
            await Assert.That(actor).IsEqualTo((Guid?)subject.PrincipalId);
            var owner = context.Database.CurrentTransaction!;
            await Assert.That(owner).IsNotNull();
            await Assert.That(owner.GetDbTransaction().IsolationLevel).IsEqualTo(IsolationLevel.Serializable);
            var proof = issuer.CurrentProof;
            await Assert.That(proof).IsNotNull();
            foreach (var mutation in mutations)
                await issuer.RequireOwnedAsync(proof!, new(SettingScope.Tenant, mutation.TenantId, mutation.Key,
                    HumanSettingsMutationKind.SetValue, mutation.Value), token);
            Actor = subject.PrincipalId;
            Reached = true;
            if (BeforeWrite is not null)
                await BeforeWrite(services, mutations, token);
            var result = await writer.ApplyAsync(mutations, subject, operation, AuditActor ?? actor, token);
            result.EnsureAccepted();
            Written = true;
            await Assert.That(ReferenceEquals(owner, context.Database.CurrentTransaction)).IsTrue();
            await Assert.That(ReferenceEquals(proof, issuer.CurrentProof)).IsTrue();
            if (AfterWrite is not null)
                await AfterWrite(services, token);
            return result;
        }
    }

    private sealed class ObservedResourceWriter(
        IServiceProvider services, SelectedEventResourceSettingsWriter writer, ResourceProbe probe) : IEventResourceSettingsWriter
    {
        public Task<EventResourceSettingsWriteResult> ApplyAsync(ImmutableArray<EventResourceSettingMutation> mutations,
            HumanCommitSubject subject, SettingsMutationOperation operation, Guid? actorUserId, CancellationToken cancellationToken = default) =>
            probe.ApplyAsync(services, writer, mutations, subject, operation, actorUserId, cancellationToken);
        public Task<EventResourceSettingsWriteResult> ApplyCurrentHumanAsync(ImmutableArray<EventResourceSettingMutation> mutations,
            SettingsMutationOperation operation, Guid? actorUserId, CancellationToken cancellationToken = default) =>
            writer.ApplyCurrentHumanAsync(mutations, operation, actorUserId, cancellationToken);
        public Task<EventResourceSettingsWriteResult> ApplyInternalAsync(ImmutableArray<EventResourceSettingMutation> mutations,
            Guid? actorUserId, CancellationToken cancellationToken = default) =>
            writer.ApplyInternalAsync(mutations, actorUserId, cancellationToken);
    }

    /// <summary>
    /// Uses the signed Local provisioning/login path from OperatorIdentityEditorNativeFixture.
    /// Import ownership, native source admission and effect proofs remain production-owned.
    /// Private source reads and mutations use the enrolled native capabilities.
    /// Ordinary instance settings remain readable by the selected Global login.
    /// </summary>
    private sealed class NativeImportHuman : IAsyncDisposable
    {
        private readonly PostgreSqlContainerFixture postgres;
        private readonly TenantBrandingNativeAuthenticationHost host;
        private readonly OperatorIdentityEditorNativeFixture.EffectClock clock = new();
        private HttpClient? client;
        private AsyncServiceScope requestScope;
        private bool hasRequest;
        private IHttpContextAccessor accessor = null!;
        private DefaultHttpContext request = null!;
        internal ResourceProbe Probe { get; } = new();
        internal Guid Tenant { get; } = Guid.CreateVersion7();
        internal Guid Actor { get; private set; }
        internal Guid Grant { get; private set; }
        internal Guid TenantGrant { get; private set; }
        internal IServiceProvider Services => requestScope.ServiceProvider;

        private NativeImportHuman(PostgreSqlContainerFixture postgres)
        {
            this.postgres = postgres;
            host = new(postgres, Tenant, [], clock: clock, configureServices: services =>
            {
                services.AddSingleton(Probe);
                services.AddScoped<SelectedEventResourceSettingsWriter>();
                services.Replace(ServiceDescriptor.Scoped<IEventResourceSettingsWriter, ObservedResourceWriter>());
            });
        }

        internal static async Task<NativeImportHuman> CreateAsync(PostgreSqlContainerFixture postgres)
        {
            await postgres.ResetAsync();
            var human = new NativeImportHuman(postgres);
            try { await human.InitializeAsync(); return human; }
            catch { await human.DisposeAsync(); throw; }
        }

        private async Task InitializeAsync()
        {
            await using (var seed = postgres.CreateDbContext())
            {
                seed.Tenants.Add(new Tenant
                {
                    Id = Tenant,
                    Slug = $"resource-{Tenant:N}",
                    FullName = "Native resource import",
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!
                });
                foreach (var (key, value, type) in new[]
                {
                    (GovernanceSettingKeys.Security.AuthorizationProvider, "\"local\"", SettingValueType.String),
                    (ResourceKey, "100", SettingValueType.Integer)
                })
                {
                    var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                    if (setting is null)
                    {
                        setting = new SystemSetting { SettingKey = key, Value = value, ValueType = type };
                        seed.SystemSettings.Add(setting);
                    }
                    setting.Value = value;
                    setting.ValueType = type;
                    setting.IsLocked = false;
                }
                if (!await seed.PaidEventPolicyVersions.AnyAsync())
                    seed.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateDefaultInstance());
                await seed.SaveChangesAsync();
                var sql = new HumanPrincipalSourceSql(seed);
                string settingsRead = $"GRANT SELECT ON {sql.Table(typeof(SystemSetting))} TO "
                    + sql.Quote(postgres.NativeQueueEnrollment.GlobalLogin);
                await seed.Database.ExecuteSqlRawAsync(settingsRead);
            }
            client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
            await host.EnsurePrivacyAuthorityReadyAsync();
            var provisioned = await ProvisionAsync();
            Actor = provisioned.Actor;
            Grant = provisioned.Grant;
            TenantGrant = provisioned.TenantGrant;
            client.DefaultRequestHeaders.Add("X-Tenant-Slug", $"resource-{Tenant:N}");
            using var response = await client.PostAsJsonAsync("/api/auth/local/login", provisioned.Login);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await Assert.That(body.RootElement.GetProperty("success").GetBoolean()).IsTrue();
            host.UseRuntimeRequestContexts();
            requestScope = host.Services.CreateAsyncScope();
            hasRequest = true;
            request = new DefaultHttpContext { RequestServices = Services };
            request.Request.Method = HttpMethods.Post;
            request.Request.Path = "/api/instance-configuration-import";
            request.Request.Headers.Authorization = "Bearer " + body.RootElement.GetProperty("token").GetString()!;
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(Tenant);
            accessor = Services.GetRequiredService<IHttpContextAccessor>();
            await InRequestAsync(async () =>
            {
                var authentication = await request.AuthenticateAsync();
                await Assert.That(authentication.Succeeded).IsTrue();
                request.User = authentication.Principal!;
            });
        }

        internal async Task<ProvisionedHuman> ProvisionAsync()
        {
            await using var scope = host.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(Tenant);
            await using var setup = postgres.CreateDbContext();
            setup.TenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            setup.ClearTenantFilterBypass();
            setup.BindTenantLease();
            scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>().Bind(setup, Tenant);
            var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var credentials = scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            DateTime now = clock.GetUtcNow().UtcDateTime;
            var initiator = new User
            {
                Id = Guid.CreateVersion7(),
                CreatedAt = now,
                Pii = new UserPii { Email = string.Empty, FirstName = "Credential", LastName = "Provisioner" }
            };
            database.Users.Add(initiator);
            if (!await database.InstanceBootstrapStates.AnyAsync())
            {
                var bootstrap = InstanceBootstrapState.CreateInteractivePending(Guid.CreateVersion7(), DeploymentMode.SingleTenant, now);
                bootstrap.CompleteInteractive(initiator.Id, now);
                database.InstanceBootstrapStates.Add(bootstrap);
            }
            await database.SaveChangesAsync();
            string email = $"resource-{Guid.CreateVersion7():N}@example.test";
            string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
            var created = await credentials.CreatePendingAsync(new LocalCredentialCreateRequest(
                Guid.CreateVersion7(), initiator.Id, email, "Resource", "Administrator"), default);
            await Assert.That(created.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
            var receipt = created.Receipt!;
            Guid actor = receipt.LocalSubjectId;
            Guid grant = Guid.CreateVersion7();
            Guid tenantGrant = Guid.CreateVersion7();
            var user = new User
            {
                Id = actor,
                EmailVerified = true,
                CreatedAt = now,
                Pii = new UserPii { Email = email, FirstName = "Resource", LastName = "Administrator" }
            };
            var member = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = Tenant,
                Tenant = null!,
                UserId = actor,
                User = user,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = now
            };
            database.AddRange(user,
                new Actor
                {
                    Id = receipt.PersonalActorId,
                    UserId = actor,
                    User = user,
                    ActorTypeId = (int)ActorTypeEnum.User,
                    ActorType = null!,
                    Pii = new ActorPii { DisplayName = "Resource Administrator" },
                    CreatedAt = now
                },
                new UserExternalLogin
                {
                    Id = receipt.ExternalLoginId,
                    UserId = actor,
                    User = user,
                    AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
                    AuthenticationProvider = null!,
                    ProviderKey = actor.ToString("D"),
                    CreatedAt = now
                },
                new PlatformUserRole { Id = grant, UserId = actor, User = user, RoleId = (int)RoleEnum.Admin, Role = null!, GrantedAt = now },
                member,
                new TenantUserRoleGrant
                {
                    Id = tenantGrant,
                    TenantId = Tenant,
                    Tenant = null!,
                    TenantUserId = member.Id,
                    TenantUser = member,
                    RoleId = (int)RoleEnum.TenantAdmin,
                    Role = null!,
                    RoleScopeId = (int)RoleScopeEnum.Tenant,
                    GrantedAt = now
                });
            await database.SaveChangesAsync();
            var pending = (await credentials.ReadProvisioningAsync(receipt.OperationId, default))!;
            await Assert.That(await credentials.ActivateChangeRequiredAsync(
                new(receipt.OperationId, pending.OperationConcurrencyStamp), default)).IsEqualTo(LocalCredentialActivationOutcome.Activated);
            var local = (await manager.FindByIdAsync(actor.ToString("D")))!;
            await database.Entry(local).ReloadAsync();
            DateTimeOffset issued = clock.GetUtcNow();
            await Assert.That(await credentials.ReplaceAsync(new LocalCredentialReplacementRequest(
                new LocalCredentialReplacementAuthority(new(actor, receipt.OperationId, local.SecurityStamp!), issued, issued.AddMinutes(5)),
                password), default)).IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
            await database.Entry(local).ReloadAsync();
            local.EmailConfirmed = true;
            await Assert.That((await manager.UpdateAsync(local)).Succeeded).IsTrue();
            await Assert.That((await credentials.ReadLinkedIdentityAsync(actor, default))!.CredentialState).IsEqualTo(LocalCredentialState.Ready);
            return new(actor, grant, tenantGrant, new(email, password));
        }

        internal async Task InRequestAsync(Func<Task> action)
        {
            var previous = accessor.HttpContext;
            accessor.HttpContext = request;
            try { await action(); }
            catch (ConfigurationImportSessionException rejection)
            {
                Console.WriteLine($"import_rejection={rejection.FailureCode}; resource_reached={Probe.Reached}; resource_written={Probe.Written}");
                throw;
            }
            finally { accessor.HttpContext = previous; }
        }

        public async ValueTask DisposeAsync()
        {
            if (hasRequest) await requestScope.DisposeAsync();
            client?.Dispose();
            await host.DisposeAsync();
            host.PrivacyKeys.Dispose();
        }

        internal sealed record ProvisionedHuman(Guid Actor, Guid Grant, Guid TenantGrant, LocalAuthRequestDto Login);
    }
}
