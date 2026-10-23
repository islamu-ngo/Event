using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Model = Explore.Persistence.Security.ManagedProviderProvisioningDataModel;
using Native = Explore.Persistence.Security.PostgresManagedProviderProvisioningContract;

namespace Event.Persistence.IntegrationTests;

/// <summary>
/// Source-only composition controls. Uses the fully installed parent fixture and
/// declared enrollment; no test installs a missing prerequisite or infers a login.
/// Catalog mutations are transaction-local and always rolled back.
/// </summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class HumanProvisioningSharedWriterCompositionTests(PostgreSqlContainerFixture fixture)
{
    private static readonly Type[] Principal =
    [
        typeof(User), typeof(UserExternalLogin), typeof(TenantUser), typeof(TenantUserRoleGrant),
        typeof(OrganizationMember), typeof(GroupMember), typeof(Role), typeof(Tenant), typeof(TenantStatus),
        typeof(OrganizationTenant), typeof(Organization), typeof(GroupTenant), typeof(Group),
        typeof(LocalIdentityUser), typeof(IdentityUserToken<Guid>), typeof(LocalIdentityCredentialOperation), typeof(Actor)
    ];

    private static readonly Type[] Local =
    [
        typeof(LocalIdentityUser), typeof(LocalIdentityCredentialOperation), typeof(IdentityUserToken<Guid>),
        typeof(User), typeof(UserPii), typeof(Actor), typeof(ActorPii), typeof(UserExternalLogin)
    ];

    private static readonly Type[] Provisioning =
    [
        typeof(Tenant), typeof(TenantSettingsDocument), typeof(User), typeof(UserPii), typeof(Actor), typeof(ActorPii),
        typeof(UserExternalLogin), typeof(TenantUser), typeof(TenantUserProfile), typeof(TenantUserRoleGrant),
        typeof(Organization), typeof(OrganizationPii), typeof(Group), typeof(OrganizationTenant), typeof(GroupTenant),
        typeof(OrganizationMember), typeof(GroupMember), typeof(ExternalBinding), typeof(EventDiscoveryRevision), typeof(Role),
        typeof(LocalIdentityUser), typeof(IdentityUserToken<Guid>), typeof(LocalIdentityCredentialOperation),
        typeof(InstanceBootstrapState), typeof(ManagedTenantProvisioningOperation), typeof(TenantStatus)
    ];

    private static readonly (string Trigger, string Function, string Owner, string Stamp, Type[] Targets)[] Writers =
    [
        ("human_principal_source_write", "human_principal_source_write", PostgresHumanPrincipalSourceContract.OwnerRole,
            "human-principal-source:v1:", Principal),
        ("local_administration_changed", "local_administration_changed", PostgresLocalAdministrationContract.OwnerRole,
            "local-administration:v1:", Local),
        ("support_administration_changed", "support_administration_changed", PostgresSupportAdministrationContract.OwnerRole,
            "support-administration:v1:", [typeof(Tenant), typeof(TenantUser)]),
        ("zz_human_event_assignment_source_write", "human_event_assignment_source_write", PostgresHumanEventAssignmentSourceContract.OwnerRole,
            "human-event-assignment-source:v1:", [typeof(Role), typeof(Actor), typeof(TenantUser), typeof(OrganizationMember),
                typeof(OrganizationTenant), typeof(GroupMember), typeof(GroupTenant), typeof(User), typeof(UserPii)]),
        ("zz_human_webhook_resource_source_write", "human_webhook_resource_source_write", PostgresHumanWebhookResourceSourceContract.OwnerRole,
            "human-webhook-resource-source:v1:", [typeof(InstanceBootstrapState), typeof(Organization), typeof(OrganizationTenant),
                typeof(Group), typeof(GroupTenant), typeof(TenantUser)]),
        ("storage_probe_source_changed", "storage_probe_source_changed", PostgresStorageProbeSourceContract.OwnerRole,
            "storage-probe-source:v1:", [typeof(InstanceBootstrapState)]),
        ("provider_configuration_changed", "provider_configuration_changed", PostgresProviderConfigurationContract.OwnerRole,
            "provider-configuration:v1:", [typeof(InstanceBootstrapState), typeof(User), typeof(UserExternalLogin)]),
        ("mixed_settings_changed", "mixed_settings_changed", PostgresMixedSettingsContract.OwnerRole,
            "mixed-settings:v1:", [.. Principal, typeof(InstanceBootstrapState), typeof(TenantSettingsDocument)]),
        ("ordinary_settings_tenant_changed", "ordinary_settings_tenant_changed", PostgresOrdinarySettingsContract.OwnerRole,
            "ordinary-settings:v1:", [typeof(Tenant)]),
        ("ordinary_settings_tenant_truncated", "ordinary_settings_tenant_truncated", PostgresOrdinarySettingsContract.OwnerRole,
            "ordinary-settings:v1:", [typeof(Tenant)]),
        ("human_provisioning_changed", "human_provisioning_changed", Native.OwnerRole, "human-provisioning:v1:", Provisioning)
    ];

    [Test]
    public async Task FullyInstalledCompositionHasExactlyTheReviewedRelationWriterIntersection()
    {
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        var names = new HumanPrincipalSourceSql(migration);
        foreach (var type in Provisioning)
        {
            string actual = await migration.Database.SqlQueryRaw<string>("""
                SELECT COALESCE(string_agg(t.tgname, ',' ORDER BY t.tgname), '') AS "Value"
                FROM pg_catalog.pg_trigger t WHERE t.tgrelid = pg_catalog.to_regclass({0}) AND NOT t.tgisinternal
                """, names.Table(type)).SingleAsync();
            string expected = string.Join(",", Writers.Where(writer => writer.Targets.Contains(type))
                .Select(writer => writer.Trigger).Order(StringComparer.Ordinal));
            await Assert.That(actual).IsEqualTo(expected);
        }
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        await Native.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
    }

    [Test]
    public async Task ReadinessAndRerunnableInstallationLeaveBusinessRowsAndEpochsUnchanged()
    {
        var request = await Request();
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        string epoch = await Epoch(migration);
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        string before = await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "read", request, null, default);
        await Native.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
        await Native.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await Native.RequireReadyAsync(caller, fixture.NativeQueueEnrollment);
        string after = await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "read", request, null, default);
        await Assert.That(JsonNode.DeepEquals(JsonNode.Parse(before), JsonNode.Parse(after))).IsTrue();
        await Assert.That(await Epoch(migration)).IsEqualTo(epoch);
    }

    public sealed record WriterDrift(int Writer, string Mutation);

    public static IEnumerable<WriterDrift> WriterDrifts()
    {
        for (int writer = 0; writer < Writers.Length; writer++)
            foreach (string mutation in new[] { "disabled", "body-restamped" })
                yield return new(writer, mutation);
        foreach (string mutation in new[] { "missing", "flags", "arguments", "function", "public-execute", "search-path", "strict" })
            yield return new(Writers.Length - 1, mutation);
    }

    [Test]
    [MethodDataSource(nameof(WriterDrifts))]
    public async Task ExactWriterDriftFailsReadinessAndReinstallationWithoutEffects(WriterDrift drift)
    {
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        await migration.Database.OpenConnectionAsync();
        string epoch = await Epoch(migration);
        var writer = Writers[drift.Writer];
        var names = new HumanPrincipalSourceSql(migration);
        string table = names.Table(writer.Targets[0]);
        string function = names.Name(writer.Function);
        string migrator = names.Quote(fixture.NativeQueueEnrollment.MigratorLogin);
        await using var transaction = await migration.Database.BeginTransactionAsync();
        string change = drift.Mutation switch
        {
            "missing" => $"DROP TRIGGER {writer.Trigger} ON {table}",
            "disabled" => $"ALTER TABLE {table} DISABLE TRIGGER {writer.Trigger}",
            "flags" or "arguments" or "function" => $"""
                DROP TRIGGER {writer.Trigger} ON {table};
                SET LOCAL ROLE {writer.Owner};
                GRANT EXECUTE ON FUNCTION {function}() TO {migrator};
                RESET ROLE;
                CREATE FUNCTION {names.Name("composition_counterfeit")}() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RETURN NULL; END;';
                CREATE TRIGGER {writer.Trigger} BEFORE {(drift.Mutation == "flags" ? "UPDATE" : "INSERT OR UPDATE OR DELETE OR TRUNCATE")}
                    ON {table} FOR EACH STATEMENT EXECUTE FUNCTION
                    {(drift.Mutation == "function" ? names.Name("composition_counterfeit") : function)}({(drift.Mutation == "arguments" ? "'unexpected'" : "")});
                ALTER TABLE {table} ENABLE ALWAYS TRIGGER {writer.Trigger};
                SET LOCAL ROLE {writer.Owner};
                REVOKE EXECUTE ON FUNCTION {function}() FROM {migrator};
                RESET ROLE;
                """,
            "body-restamped" => $"""
                GRANT CREATE ON SCHEMA {names.Quote(names.Schema)} TO {writer.Owner};
                SET LOCAL ROLE {writer.Owner};
                CREATE OR REPLACE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql VOLATILE SECURITY DEFINER
                    SET search_path = pg_catalog AS 'BEGIN RETURN NULL; END;';
                DO $stamp$ DECLARE fingerprint text; BEGIN
                    SELECT '{writer.Stamp}' || pg_catalog.encode(pg_catalog.sha256(pg_catalog.convert_to(prosrc,'UTF8')),'hex')
                        INTO fingerprint FROM pg_catalog.pg_proc WHERE oid = pg_catalog.to_regprocedure('{function}()');
                    EXECUTE pg_catalog.format('COMMENT ON FUNCTION {function}() IS %L',fingerprint);
                END $stamp$;
                RESET ROLE;
                REVOKE CREATE ON SCHEMA {names.Quote(names.Schema)} FROM {writer.Owner};
                """,
            "public-execute" => $"SET LOCAL ROLE {writer.Owner}; GRANT EXECUTE ON FUNCTION {function}() TO PUBLIC; RESET ROLE;",
            "search-path" => $"SET LOCAL ROLE {writer.Owner}; ALTER FUNCTION {function}() SET search_path = public; RESET ROLE;",
            "strict" => $"SET LOCAL ROLE {writer.Owner}; ALTER FUNCTION {function}() STRICT; RESET ROLE;",
            _ => throw new InvalidOperationException("Unknown composition drift control.")
        };
        await migration.Database.ExecuteSqlRawAsync(change);
        await transaction.CreateSavepointAsync("drift");
        var ready = await Assert.ThrowsAsync<PostgresException>(() => Native.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
        await Assert.That(ready!.SqlState).IsEqualTo("55000");
        await transaction.RollbackToSavepointAsync("drift");
        var reinstall = await Assert.ThrowsAsync<PostgresException>(() => Native.ApplyAsync(migration, fixture.NativeQueueEnrollment));
        await Assert.That(reinstall!.SqlState).IsEqualTo("55000");
        await transaction.RollbackAsync();
        await Assert.That(await Epoch(migration)).IsEqualTo(epoch);
    }

    [Test]
    [Arguments("policy_source_set_write")]
    [Arguments("zz_human_support_access_source_write")]
    [Arguments("zz_human_event_provider_source_write")]
    [Arguments("human_email_source_write")]
    [Arguments("mixed_settings_counterfeit")]
    public async Task KnownNamesAndPrefixesOnUnsupportedRelationsAreNotExceptions(string trigger)
    {
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        await migration.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        string epoch = await Epoch(migration);
        await using var transaction = await migration.Database.BeginTransactionAsync();
        await migration.Database.ExecuteSqlRawAsync($"""
            CREATE FUNCTION {names.Name("composition_counterfeit")}() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RETURN NULL; END;';
            CREATE TRIGGER {trigger} BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE ON {names.Table(typeof(ExternalBinding))}
                FOR EACH STATEMENT EXECUTE FUNCTION {names.Name("composition_counterfeit")}();
            ALTER TABLE {names.Table(typeof(ExternalBinding))} ENABLE ALWAYS TRIGGER {trigger};
            """);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => Native.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
        await Assert.That(failure!.SqlState).IsEqualTo("55000");
        await transaction.RollbackAsync();
        await Assert.That(await Epoch(migration)).IsEqualTo(epoch);
    }

    [Test]
    public async Task MissingOptionalFunctionDoesNotAdmitItsCounterfeitNamedTrigger()
    {
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        await migration.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        await using var transaction = await migration.Database.BeginTransactionAsync();
        string function = names.Name("storage_probe_source_changed");
        await migration.Database.ExecuteSqlRawAsync($"""
            GRANT CREATE ON SCHEMA {names.Quote(names.Schema)} TO {PostgresStorageProbeSourceContract.OwnerRole};
            SET LOCAL ROLE {PostgresStorageProbeSourceContract.OwnerRole};
            ALTER FUNCTION {function}() RENAME TO composition_hidden_writer;
            RESET ROLE;
            REVOKE CREATE ON SCHEMA {names.Quote(names.Schema)} FROM {PostgresStorageProbeSourceContract.OwnerRole};
            """);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => Native.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
        await Assert.That(failure!.SqlState).IsEqualTo("55000");
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task LaterWriterAbsenceIsMigrationOnlyAndCannotHideANamedCounterfeit()
    {
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        await migration.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        string epoch = await Epoch(migration);
        await using var transaction = await migration.Database.BeginTransactionAsync();
        // Reconstruct the exact pre-storage-probe installation state without
        // committing or repairing anything. Rollback restores all original OIDs.
        await migration.Database.ExecuteSqlRawAsync($"""
            DROP OWNED BY {PostgresStorageProbeSourceContract.OwnerRole},
                {PostgresStorageProbeSourceContract.MigratorRole},
                {PostgresStorageProbeSourceContract.RuntimeRole},
                {PostgresStorageProbeSourceContract.GlobalRole} CASCADE;
            DROP TABLE {names.Name(PostgresStorageProbeSourceContract.SourceTable)};
            DROP ROLE {PostgresStorageProbeSourceContract.OwnerRole},
                {PostgresStorageProbeSourceContract.MigratorRole},
                {PostgresStorageProbeSourceContract.RuntimeRole},
                {PostgresStorageProbeSourceContract.GlobalRole};
            """);
        await Native.RequireReadyAsync(migration, fixture.NativeQueueEnrollment);
        await Native.ApplyAsync(migration, fixture.NativeQueueEnrollment);
        await Assert.That(await Epoch(migration)).IsEqualTo(epoch);
        await transaction.CreateSavepointAsync("absent");
        await migration.Database.ExecuteSqlRawAsync($"SET LOCAL ROLE {names.Quote(fixture.NativeQueueEnrollment.GlobalLogin)}");
        var runtime = await Assert.ThrowsAsync<PostgresException>(() => Native.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
        await Assert.That(runtime!.SqlState).IsEqualTo("55000");
        await transaction.RollbackToSavepointAsync("absent");
        await migration.Database.ExecuteSqlRawAsync($"""
            CREATE FUNCTION {names.Name("composition_counterfeit")}() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RETURN NULL; END;';
            CREATE TRIGGER storage_probe_source_changed BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
                ON {names.Table(typeof(InstanceBootstrapState))}
                FOR EACH STATEMENT EXECUTE FUNCTION {names.Name("composition_counterfeit")}();
            ALTER TABLE {names.Table(typeof(InstanceBootstrapState))} ENABLE ALWAYS TRIGGER storage_probe_source_changed;
            """);
        var counterfeit = await Assert.ThrowsAsync<PostgresException>(() => Native.RequireReadyAsync(migration, fixture.NativeQueueEnrollment));
        await Assert.That(counterfeit!.SqlState).IsEqualTo("55000");
        await transaction.RollbackAsync();
        await Assert.That(await Epoch(migration)).IsEqualTo(epoch);
    }

    [Test]
    public async Task AbsentColocatedProvisioningDoesNotDisablePrincipalButCounterfeitDoes()
    {
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        await migration.Database.OpenConnectionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        var enrollment = fixture.NativeQueueEnrollment;
        await using var transaction = await migration.Database.BeginTransactionAsync();
        await migration.Database.ExecuteSqlRawAsync($"""
            DROP OWNED BY {Native.OwnerRole}, {Native.MigratorRole}, {Native.GlobalRole} CASCADE;
            DROP TABLE {names.Name(Native.SourceTable)}, {names.Name("human_provisioning_attempt")};
            DROP ROLE {Native.OwnerRole}, {Native.MigratorRole}, {Native.GlobalRole};
            SET LOCAL ROLE {names.Quote(enrollment.RuntimeLogin)};
            """);
        await migration.Database.ExecuteSqlRawAsync(
            PostgresHumanPrincipalSourceContract.AdmissionSql(migration, enrollment));
        await migration.Database.ExecuteSqlRawAsync($"""
            RESET ROLE;
            CREATE FUNCTION {names.Name("composition_counterfeit")}() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RETURN NULL; END;';
            CREATE TRIGGER human_provisioning_changed BEFORE INSERT OR UPDATE OR DELETE OR TRUNCATE
                ON {names.Table(typeof(User))} FOR EACH STATEMENT EXECUTE FUNCTION {names.Name("composition_counterfeit")}();
            ALTER TABLE {names.Table(typeof(User))} ENABLE ALWAYS TRIGGER human_provisioning_changed;
            SET LOCAL ROLE {names.Quote(enrollment.RuntimeLogin)};
            """);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => migration.Database.ExecuteSqlRawAsync(
            PostgresHumanPrincipalSourceContract.AdmissionSql(migration, enrollment)));
        await Assert.That(failure!.SqlState).IsEqualTo("55000");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("principal")]
    [Arguments("assignment")]
    [Arguments("webhook")]
    [Arguments("email")]
    [Arguments("local")]
    [Arguments("support")]
    [Arguments("provider")]
    [Arguments("storage-probe")]
    [Arguments("mixed")]
    public async Task SiblingAdmissionRejectsProvisioningWriterDrift(string sibling)
    {
        await using var migration = Open(PrimaryDatabaseRole.Migrator);
        await migration.Database.OpenConnectionAsync();
        await using var transaction = await migration.Database.BeginTransactionAsync();
        var names = new HumanPrincipalSourceSql(migration);
        await migration.Database.ExecuteSqlRawAsync(
            $"ALTER TABLE {names.Table(typeof(User))} DISABLE TRIGGER human_provisioning_changed");
        var enrollment = fixture.NativeQueueEnrollment;
        Task Admit() => sibling switch
        {
            "principal" => migration.Database.ExecuteSqlRawAsync(PostgresHumanPrincipalSourceContract.AdmissionSql(migration, enrollment)),
            "assignment" => migration.Database.ExecuteSqlRawAsync(PostgresHumanEventAssignmentSourceContract.AdmissionSql(migration, enrollment)),
            "webhook" => migration.Database.ExecuteSqlRawAsync(PostgresHumanWebhookResourceSourceContract.AdmissionSql(migration, enrollment)),
            "email" => migration.Database.ExecuteSqlRawAsync(PostgresHumanEmailSourceContract.AdmissionSql(migration, enrollment)),
            "local" => PostgresLocalAdministrationContract.RequireReadyAsync(migration, enrollment),
            "support" => PostgresSupportAdministrationContract.RequireReadyAsync(migration, enrollment),
            "provider" => PostgresProviderConfigurationContract.RequireReadyAsync(migration, enrollment),
            "storage-probe" => PostgresStorageProbeSourceContract.RequireReadyAsync(migration, enrollment),
            "mixed" => PostgresMixedSettingsContract.RequireReadyAsync(migration, enrollment),
            _ => throw new InvalidOperationException("Unknown composition sibling.")
        };
        var failure = await Assert.ThrowsAsync<PostgresException>(Admit);
        await Assert.That(failure!.SqlState).IsEqualTo("55000");
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task SharedWritersPermitCreationAndExactNoEffectReplay()
    {
        var request = await Request();
        var first = await Stage(request);
        await Assert.That(first.AlreadyProvisioned).IsFalse();
        var replay = await Stage(request with { Seeds = Model.Seeds.Create(), At = request.At.AddMinutes(1) });
        await Assert.That(replay.AlreadyProvisioned).IsTrue();
        await Assert.That(JsonNode.DeepEquals(first.Image["Data"], replay.Image["Data"])).IsTrue();
        await Assert.That(JsonNode.DeepEquals(first.Image["Revision"], replay.Image["Revision"])).IsTrue();
    }

    private async Task<(bool AlreadyProvisioned, JsonObject Image)> Stage(Model.Request request)
    {
        await using var caller = Open(PrimaryDatabaseRole.Global);
        await caller.Database.OpenConnectionAsync();
        string before = await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "read", request, null, default);
        await using var transaction = await caller.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "acquire", request, before, default);
        var compiled = JsonNode.Parse(await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "compile", request, null, default))!.AsObject();
        var applied = JsonNode.Parse(await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "apply", request, null, default))!.AsObject();
        var completed = JsonNode.Parse(await Native.InvokeAsync(caller, fixture.NativeQueueEnrollment, "complete", request, null, default));
        await Assert.That(JsonNode.DeepEquals(applied["Data"], compiled["Data"])).IsTrue();
        await Assert.That(JsonNode.DeepEquals(applied, completed)).IsTrue();
        await transaction.CommitAsync();
        return (compiled["Rows"]!.Deserialize<HumanManagedProviderProvisioningRows>()!.AlreadyProvisioned, applied);
    }

    private async Task<Model.Request> Request()
    {
        Guid human = Guid.CreateVersion7();
        await using var seed = Open(PrimaryDatabaseRole.Migrator);
        seed.Users.Add(new User { Id = human, Pii = new UserPii { Email = string.Empty, FirstName = "Composition", LastName = "Owner" } });
        await seed.SaveChangesAsync();
        return await Model.CaptureAsync(new EnsureManagedProviderClientProvisionedCommand
        {
            ProvisioningDto = new()
            {
                ProviderKey = "composition",
                ExternalSystem = "composition",
                ExternalCustomerId = Guid.CreateVersion7().ToString("D"),
                TenantFullName = "Composition tenant",
                TenantSlug = "composition-" + Guid.CreateVersion7().ToString("N"),
                DirectoryOperatorIdentity = new()
                {
                    PublicName = "Composition tenant",
                    LegalName = "Composition operator",
                    OperatorKindCode = "individual",
                    JurisdictionCountryCode = "BE",
                    PublicContactEmail = "composition@protocol.invalid",
                    LegalNoticeUrl = "https://protocol.invalid/legal",
                    PrivacyUrl = "https://protocol.invalid/privacy"
                },
                ExternalAdmin = new()
                {
                    IdentityProvider = "https://issuer.protocol.invalid",
                    Subject = Guid.CreateVersion7().ToString("D"),
                    Email = "composition@protocol.invalid",
                    FirstName = "Composition",
                    LastName = "Target",
                    EmailVerified = true
                }
            }
        }, human, TimeProvider.System, default);
    }

    private ExploreDbContext Open(PrimaryDatabaseRole role)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(role));
        options.UseNpgsql(provider => provider.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        var context = new ExploreDbContext(options.Options);
        context.BindTenantLease();
        return context;
    }

    private static async Task<string> Epoch(ExploreDbContext context) =>
        await context.Database.SqlQueryRaw<string>(
            $"SELECT revision::text AS \"Value\" FROM {new HumanPrincipalSourceSql(context).Name(Native.SourceTable)} WHERE id = 1").SingleAsync();
}
