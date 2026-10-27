using System.Collections.Concurrent;
using System.Data.Common;
using System.Security.Cryptography;
using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using Respawn.Graph;
using Testcontainers.PostgreSql;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests.Fixtures;

/// <summary>
/// Shared PostgreSQL container fixture that provides production-faithful schema via MigrateAsync
/// and native DELETE state reset between tests.
/// Supports dynamic schema-per-run isolation (SET search_path = test_<run_id>) for race-free test execution.
/// </summary>
public class PostgreSqlContainerFixture : IAsyncInitializer, IAsyncDisposable
{
    private readonly PostgreSqlContainer _container;
    private readonly IdentityDatabaseTopology _identityTopology;
    private readonly object _initializationLock = new();
    private Task? _initialization;
    private Task? _localIdentityWriterPreparation;
    private readonly ConcurrentBag<string> _isolatedSchemas = new();
    private readonly MemoryCache _metadataCache = new(new MemoryCacheOptions());
    private string? _runtimeConnectionString;
    private PostgreSqlDatabaseReset? _reset;
    private readonly IReadOnlyDictionary<PrimaryDatabaseRole, (string Username, string Password)> _nativeQueueLogins;
    public PostgresNativeQueueEnrollment NativeQueueEnrollment { get; }

    public PostgreSqlContainerFixture() : this(IdentityDatabaseTopology.Colocated)
    {
    }

    protected PostgreSqlContainerFixture(
        IdentityDatabaseTopology identityTopology, bool distinctMigrator = false, string? globalLoginSuffix = null)
    {
        if (!Enum.IsDefined(identityTopology))
            throw new ArgumentOutOfRangeException(nameof(identityTopology));
        _identityTopology = identityTopology;
        _container = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("explore_db_test")
            .WithUsername("postgres")
            .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)))
            .Build();
        string suffix = Guid.CreateVersion7().ToString("N");
        var purposes = new[]
        {
            PrimaryDatabaseRole.Global, PrimaryDatabaseRole.Runtime,
            PrimaryDatabaseRole.EmailPolicyInstanceMutation, PrimaryDatabaseRole.EmailPolicyTenantMutation,
            PrimaryDatabaseRole.InstanceKeyRead, PrimaryDatabaseRole.InstanceKeyWrite,
            PrimaryDatabaseRole.InstanceKeyIssuance, PrimaryDatabaseRole.PlatformKeyReport,
            PrimaryDatabaseRole.LocalIdentityWriter
        }.ToList();
        if (distinctMigrator) purposes.Add(PrimaryDatabaseRole.Migrator);
        _nativeQueueLogins = purposes.ToDictionary(role => role, role => (
            Username: $"queue_{(int)role}_{suffix}" + (role == PrimaryDatabaseRole.Global ? globalLoginSuffix : null),
            Password: Convert.ToHexString(RandomNumberGenerator.GetBytes(24))));
        NativeQueueEnrollment = new PostgresNativeQueueEnrollment(
            _nativeQueueLogins.TryGetValue(PrimaryDatabaseRole.Migrator, out var migrator)
                ? migrator.Username : "postgres",
            _nativeQueueLogins[PrimaryDatabaseRole.Global].Username,
            _nativeQueueLogins[PrimaryDatabaseRole.Runtime].Username,
            _nativeQueueLogins[PrimaryDatabaseRole.EmailPolicyInstanceMutation].Username,
            _nativeQueueLogins[PrimaryDatabaseRole.EmailPolicyTenantMutation].Username);
    }

    /// <summary>
    /// Connection string for the running PostgreSQL container.
    /// </summary>
    public string ConnectionString => _runtimeConnectionString ?? _container.GetConnectionString();

    public Task InitializeAsync()
    {
        lock (_initializationLock)
            return _initialization ??= InitializeCoreAsync();
    }

    /// <summary>
    /// Activates the API host's declared writer profile; independent installer tests retain a fresh profile.
    /// </summary>
    internal Task PrepareLocalIdentityWriterAsync()
    {
        lock (_initializationLock)
            return _localIdentityWriterPreparation ??= PrepareLocalIdentityWriterCoreAsync();
    }

    private async Task PrepareLocalIdentityWriterCoreAsync()
    {
        await InitializeAsync();
        await using var migrator = CreateDbContextInternal(PrimaryDatabaseRole.Migrator);
        migrator.BindTenantLease();
        var enrollment = new PostgresLocalIdentityWriterEnrollment(
            NativeQueueEnrollment.MigratorLogin,
            NativeQueueEnrollment.RuntimeLogin,
            _nativeQueueLogins[PrimaryDatabaseRole.LocalIdentityWriter].Username,
            NativeQueueEnrollment.GlobalLogin);
        await PostgresLocalIdentityWriterPermissions.ApplyAsync(migrator, enrollment,
            _identityTopology, LocalIdentityWriterInventory.Store.Application);
    }

    private async Task InitializeCoreAsync()
    {
        await _container.StartAsync();
        await ProvisionNativeQueueLoginsAsync();

        // Apply all migrations for production-faithful schema
        await using (var migratorContext = CreateDbContextInternal(PrimaryDatabaseRole.Migrator))
        {
            migratorContext.BindTenantLease();
            await migratorContext.Database.MigrateAsync();
            await PostgresModelConstraintApplier.ApplyAsync(migratorContext);
            await PostgresTenantRowLevelSecurityModel.ApplyAsync(migratorContext);
            await PostgresDiscoveryDisclosureContract.ApplyAsync(migratorContext);
            await PostgresDiscoveryTraversalContract.ApplyAsync(
                migratorContext, NativeQueueEnrollment);
            await PostgresPublicEventDiscoveryContract.ApplyAsync(
                migratorContext, NativeQueueEnrollment);
            await PostgresIdempotencyReceiptContract.ApplyAsync(
                migratorContext, NativeQueueEnrollment);
            await PostgresAiActorContextReadContract.ApplyAsync(
                migratorContext, NativeQueueEnrollment);
            await PostgresEventCreationPublisherReadContract.ApplyAsync(
                migratorContext, NativeQueueEnrollment);
            await PostgresAiConversationOwnerFenceContract.ApplyAsync(
                migratorContext, NativeQueueEnrollment);
            await LookupTableSeeder.SeedAsync(migratorContext);
            await PostgresTenantLookupContract.ApplyAsync(migratorContext);
            await migratorContext.Database.ExecuteSqlRawAsync(
                $"""
                GRANT "{PostgresTenantLookupContract.GlobalRole}"
                TO "{NativeQueueEnrollment.GlobalLogin.Replace("\"", "\"\"", StringComparison.Ordinal)}"
                WITH INHERIT TRUE, SET FALSE, ADMIN FALSE
                """);
            await PostgresSubjectPrivacyErasureContract.ApplyAsync(migratorContext);
            await PostgresPdsSettlementContract.ApplyAsync(migratorContext);
            await PostgresPdsQueueAdmissionContract.ApplyAsync(migratorContext);
            await PostgresAtprotoModerationCompensationContract.ApplyAsync(migratorContext);
            await PostgresIntegrationSyncReadPermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresEmailDispatchAdmissionContract.ApplyAsync(migratorContext);
            await PostgresAtprotoMaterializationContract.ApplyAsync(migratorContext);
            await PostgresTenantApiKeyStoragePermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresExternalApiKeyAuthenticationContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresInstanceApiKeyManagementContract.ApplyAsync(migratorContext);
            await PostgresInstanceApiKeyIssuanceContract.ApplyAsync(migratorContext);
            await PostgresPlatformApiKeyUsageReportContract.ApplyAsync(migratorContext);
            foreach (var (role, executor) in new[]
            {
                (PrimaryDatabaseRole.InstanceKeyRead, PostgresInstanceApiKeyManagementContract.ReadExecutor),
                (PrimaryDatabaseRole.InstanceKeyWrite, PostgresInstanceApiKeyManagementContract.WriteExecutor),
                (PrimaryDatabaseRole.InstanceKeyIssuance, PostgresInstanceApiKeyIssuanceContract.ExecutorRole),
                (PrimaryDatabaseRole.PlatformKeyReport, PostgresPlatformApiKeyUsageReportContract.ExecutorRole)
            })
            {
                await migratorContext.Database.ExecuteSqlRawAsync(
                    $"""
                    GRANT "{executor}" TO "{_nativeQueueLogins[role].Username.Replace("\"", "\"\"", StringComparison.Ordinal)}"
                    WITH INHERIT TRUE, SET FALSE, ADMIN FALSE
                    """);
            }
            await PostgresAdmissionScannerAuthenticationContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresUserTenantMembershipReadPermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresExternalLoginReadPermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresAdministrativeAuthorityContract.ApplyAsync(migratorContext);
            foreach (string executor in new[]
            {
                PostgresAdministrativeAuthorityContract.GlobalRole
            })
            {
                await migratorContext.Database.ExecuteSqlRawAsync(
                    $"""
                    GRANT "{executor}"
                    TO "{NativeQueueEnrollment.GlobalLogin.Replace("\"", "\"\"", StringComparison.Ordinal)}"
                    WITH INHERIT TRUE, SET FALSE, ADMIN FALSE
                    """);
            }
            await PostgresActorConsolidationContract.ApplyAsync(migratorContext);
            await PostgresManagedTenantDomainContract.ApplyAsync(migratorContext);
            await PostgresNotificationFanoutAdmissionContract.ApplyAsync(migratorContext);
            await PostgresNotificationOutboxAdmissionContract.ApplyAsync(migratorContext);
            await PostgresGenericOutboxAdmissionContract.ApplyAsync(migratorContext);
            await PostgresOutboxExecutionContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresPublicationFanoutExecutionContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresPublicEventDetailContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresPrivateEventManagementContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresRegistrationPrivateReadContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresPublicEventResponseClockContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresLookupCatalogueReadPermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresAtprotoFederationSettingPermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await migratorContext.Database.ExecuteSqlRawAsync(
                $"""
                GRANT "{PostgresNotificationOutboxAdmissionContract.GlobalRole}", "{PostgresGenericOutboxAdmissionContract.GlobalRole}"
                TO "{NativeQueueEnrollment.GlobalLogin.Replace("\"", "\"\"", StringComparison.Ordinal)}"
                WITH INHERIT TRUE, SET FALSE, ADMIN FALSE
                """);
            await PostgresWebPushDispatchAdmissionContract.ApplyAsync(migratorContext);
            await PostgresQueueDrainHealthContract.ApplyAsync(migratorContext);
            await PostgresLocationPrivacyReviewContract.ApplyAsync(migratorContext);
            await PostgresLocationPrivacyGovernanceContract.ApplyAsync(migratorContext);
            await PostgresManifestReplayContract.ApplyAsync(migratorContext);
            await PostgresConfigurationManifestBootstrapContract.ApplyAsync(
                migratorContext, NativeQueueEnrollment.GlobalLogin);
            await migratorContext.Database.ExecuteSqlRawAsync(
                $"""
                GRANT "{PostgresConfigurationManifestBootstrapContract.GlobalRole}"
                TO "{NativeQueueEnrollment.GlobalLogin.Replace("\"", "\"\"", StringComparison.Ordinal)}"
                WITH INHERIT TRUE, SET FALSE, ADMIN FALSE
                """);
            await PostgresConfigurationImportSnapshotContract.ApplyAsync(migratorContext);
            await PostgresConfigurationImportStateContract.ApplyAsync(migratorContext);
            await PostgresConfigurationImportLegalSourceContract.ApplyAsync(migratorContext);
            await PostgresConfigurationImportLegalMutationContract.ApplyAsync(migratorContext);
            await PostgresConfigurationImportPublicationPolicyContract.ApplyAsync(migratorContext);
            await PostgresConfigurationImportScalarSettingContract.ApplyAsync(migratorContext);
            await PostgresConfigurationImportReceiptContract.ApplyAsync(migratorContext);
            await PostgresConfigurationImportPaidPolicyContract.ApplyAsync(migratorContext);
            await PostgresConfigurationImportTenantIdentityContract.ApplyAsync(migratorContext);
            await PostgresConfigurationImportBrandingContract.ApplyAsync(migratorContext);
            await PostgresConfigurationImportEventResourceContract.ApplyAsync(migratorContext);
            // Enrollment is separate from installation: exercise the actual restricted
            // Global capability, not an unrelated missing-role denial.
            await migratorContext.Database.ExecuteSqlRawAsync(
                $"""
                GRANT "{PostgresConfigurationImportEventResourceContract.GlobalRole}"
                TO "{NativeQueueEnrollment.GlobalLogin.Replace("\"", "\"\"", StringComparison.Ordinal)}"
                WITH INHERIT TRUE, SET FALSE, ADMIN FALSE
                """);
            await PostgresConfigurationImportTenantSettingContract.ApplyAsync(migratorContext);
            foreach (string role in new[]
            {
                PostgresConfigurationImportSnapshotContract.GlobalRole,
                PostgresConfigurationImportStateContract.GlobalRole,
                PostgresConfigurationImportLegalSourceContract.GlobalRole,
                PostgresConfigurationImportLegalMutationContract.GlobalRole,
                PostgresConfigurationImportPublicationPolicyContract.GlobalRole,
                PostgresConfigurationImportScalarSettingContract.GlobalRole,
                PostgresConfigurationImportReceiptContract.GlobalRole,
                PostgresConfigurationImportPaidPolicyContract.GlobalRole,
                PostgresConfigurationImportTenantIdentityContract.GlobalRole,
                PostgresConfigurationImportBrandingContract.GlobalRole,
                PostgresConfigurationImportTenantSettingContract.GlobalRole
            })
                await migratorContext.Database.ExecuteSqlRawAsync(
                    $"""
                    GRANT "{role}" TO "{NativeQueueEnrollment.GlobalLogin.Replace("\"", "\"\"", StringComparison.Ordinal)}"
                    WITH INHERIT TRUE, SET FALSE, ADMIN FALSE
                    """);
            await PostgresIncomingWebhookQueueContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresEventReportOperationsContract.ApplyAsync(migratorContext);
            await PostgresEmailRetentionReadPermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresEventResourceAuditReadPermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresWebhookLocalDeliveryAdmissionContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresWebhookClaimEndpointStateContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresWebhookLocalDeliveryHealthContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresHumanAdministratorCommitAuthorityContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresPolicySourceSetContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresHumanPrincipalSourceContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresHumanAuthorizationRouteContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresHumanEventAssignmentSourceContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresHumanSupportAccessSourceContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            if (_identityTopology == IdentityDatabaseTopology.Colocated)
            {
                await PostgresLocalAdministrationContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
                await PostgresSupportAdministrationContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
                await PostgresManagedProviderProvisioningContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            }
            await PostgresHumanEmailSourceContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresPlatformApiKeySourceContract.ApplyAsync(migratorContext,
                new PlatformApiKeySourceEnrollment(NativeQueueEnrollment,
                    _nativeQueueLogins[PrimaryDatabaseRole.InstanceKeyRead].Username,
                    _nativeQueueLogins[PrimaryDatabaseRole.InstanceKeyWrite].Username,
                    _nativeQueueLogins[PrimaryDatabaseRole.PlatformKeyReport].Username));
            await PostgresHumanWebhookResourceSourceContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresHumanEventProviderSourceContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresRegistrationFinalizationQueueContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresRegistrationProviderWriteQueueContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresRegistrationProviderSubscriptionClaimContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresPaymentReconciliationContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresCheckoutDispatchContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresRefundReconciliationHealthContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresTicketingRecoveryHealthContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresInventoryHoldExpiryReadPermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresVisitorPolicyAuthorityContract.ApplyAsync(migratorContext);
            await PostgresEmailDeliveryPolicyContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresOperatorIdentityEditorContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresResolverConfigurationContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresStorageProbeSourceContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresStorageConfigurationContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresProviderConfigurationContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresMixedSettingsContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresOrdinarySettingsContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresLocalizationSecretStatusReadPermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresTenantPreferencePermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresUserTenantNavigationReadPermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresEventResourceProviderContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresPaidPolicyCeilingContract.ApplyAsync(migratorContext);
            await PostgresInstanceStorageAdministrationContract.ApplyAsync(migratorContext);
            await PostgresStorageReconciliationContract.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresLocalStorageQuarantineContract.ApplyAsync(migratorContext);
            await PostgresDiscoveryMetadataPermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresTenantMembershipMutationPermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresActorOwnershipReadPermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
            await PostgresSetupLiveStoragePermissions.ApplyAsync(migratorContext, NativeQueueEnrollment);
        }

        // Retain source custody metadata; ordinary DELETE must invalidate the original rows.
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        _reset = await PostgreSqlDatabaseReset.CreateAsync(connection, ApplicationSchema);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_isolatedSchemas.IsEmpty)
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync();
                foreach (var schema in _isolatedSchemas)
                {
                    try
                    {
                        await using var cmd = connection.CreateCommand();
                        cmd.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;";
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch (DbException)
                    {
                        // Best-effort schema cleanup before container disposal
                    }
                }
            }
        }
        catch (DbException)
        {
            // Container teardown takes precedence
        }
        finally
        {
            try
            {
                await _container.StopAsync();
                await _container.DisposeAsync();
            }
            finally
            {
                _metadataCache.Dispose();
                GC.SuppressFinalize(this);
            }
        }
    }

    /// <summary>
    /// Creates a fresh DbContext connected to the test container.
    /// Schema and lookup data are already present from initialization.
    /// </summary>
    public ExploreDbContext CreateDbContext(params IInterceptor[] interceptors)
    {
        var context = CreateDbContextInternal(PrimaryDatabaseRole.Runtime, interceptors);
        context.EnableTenantFilterBypass("Persistence integration test system context.");
        return context;
    }

    /// <summary>
    /// Creates a dedicated dynamic PostgreSQL schema (e.g. test_<guid>) to isolate test runs
    /// and prevent concurrency collisions with other test classes.
    /// </summary>
    public async Task<string> CreateIsolatedSchemaAsync(string schemaPrefix = "test_")
    {
        var schemaName = $"{schemaPrefix}{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE SCHEMA IF NOT EXISTS \"{schemaName}\";";
        await command.ExecuteNonQueryAsync();
        _isolatedSchemas.Add(schemaName);
        return schemaName;
    }

    /// <summary>
    /// Drops an isolated schema and all its objects.
    /// </summary>
    public async Task DropIsolatedSchemaAsync(string schemaName)
    {
        if (string.IsNullOrWhiteSpace(schemaName) ||
            schemaName == ApplicationSchema ||
            schemaName == RelationalModelNamespace.DefaultSchema)
        {
            return;
        }

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE;";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Creates a fresh DbContext with dynamic schema-per-run isolation.
    /// Sets search_path to the isolated schema with fallback to the seeded application schema.
    /// </summary>
    public async Task<ExploreDbContext> CreateIsolatedDbContextAsync(
        string? schemaPrefix = "test_",
        params IInterceptor[] interceptors)
    {
        var schema = await CreateIsolatedSchemaAsync(schemaPrefix ?? "test_");
        var searchPathInterceptor = new PostgresSearchPathInterceptor(schema, ApplicationSchema);
        var combinedInterceptors = interceptors.Length > 0
            ? interceptors.Append(searchPathInterceptor).ToArray()
            : [searchPathInterceptor];

        var context = CreateDbContextInternal(PrimaryDatabaseRole.Runtime, combinedInterceptors, schema);
        context.EnableTenantFilterBypass("Persistence integration test isolated schema context.");
        return context;
    }

    /// <summary>
    /// Creates a DbContext configured to use a specific schema and search path.
    /// </summary>
    public ExploreDbContext CreateDbContextForSchema(
        string schema,
        params IInterceptor[] interceptors)
    {
        var searchPathInterceptor = new PostgresSearchPathInterceptor(schema, ApplicationSchema);
        var combinedInterceptors = interceptors.Length > 0
            ? interceptors.Append(searchPathInterceptor).ToArray()
            : [searchPathInterceptor];

        var context = CreateDbContextInternal(PrimaryDatabaseRole.Runtime, combinedInterceptors, schema);
        context.EnableTenantFilterBypass("Persistence integration test explicit schema context.");
        return context;
    }

    /// <summary>
    /// Creates a DbContext with tenant filters enforced. Use for tenant-isolation tests.
    /// </summary>
    public ExploreDbContext CreateTenantFilteredDbContext(
        ITenantContext? tenantContext = null,
        params IInterceptor[] interceptors)
    {
        var context = CreateDbContextInternal(PrimaryDatabaseRole.Runtime, interceptors);
        context.TenantContext = tenantContext;
        return context;
    }

    /// <summary>
    /// Resets the database to a clean state, preserving schema and lookup data.
    /// Call at the start of tests that need deterministic state.
    /// </summary>
    public async Task ResetAsync()
    {
        if (_reset is null)
            throw new InvalidOperationException("Fixture not initialized. Call InitializeAsync first.");

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _reset.ResetAsync(connection);

        await using var context = CreateDbContextInternal();
        context.BindTenantLease();
        await LookupTableSeeder.SeedAsync(context);
    }

    /// <summary>Stable declared purpose accounts; test-specific grants never extend this enrollment.</summary>
    public PrimaryDatabaseConnectionOptions GetNativeQueueOptions(PrimaryDatabaseRole role, string? schema = null)
    {
        PrimaryDatabaseConnectionOptions options = CreateDatabaseOptions(role, schema);
        if (role == PrimaryDatabaseRole.Migrator)
            return options;
        if (!_nativeQueueLogins.TryGetValue(role, out var login))
            throw new ArgumentOutOfRangeException(nameof(role), role, null);
        return options with { Username = login.Username, Password = login.Password };
    }

    /// <summary>Uses the actual enrolled Runtime login and enforced original tenant, without test bypass.</summary>
    public ExploreDbContext CreateEnrolledTenantDbContext(
        ITenantContext tenantContext, params IInterceptor[] interceptors)
    {
        ArgumentNullException.ThrowIfNull(tenantContext);
        if (tenantContext.TenantId == Guid.Empty)
            throw new ArgumentException("An enrolled Runtime owner requires its exact tenant.", nameof(tenantContext));
        return CreateEnrolledRuntimeDbContext(tenantContext, interceptors);
    }

    /// <summary>Uses the actual Runtime account with an explicitly absent tenant for subject/instance work.</summary>
    public ExploreDbContext CreateEnrolledTenantlessDbContext(params IInterceptor[] interceptors) =>
        CreateEnrolledRuntimeDbContext(null, interceptors);

    private ExploreDbContext CreateEnrolledRuntimeDbContext(ITenantContext? tenantContext, IInterceptor[] interceptors)
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>().UseMemoryCache(_metadataCache);
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder,
            GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        builder.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
        builder.AddInterceptors(interceptors);
        return new ExploreDbContext(builder.Options) { TenantContext = tenantContext };
    }

    private async Task ProvisionNativeQueueLoginsAsync()
    {
        NativeQueueEnrollment.Validate();
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        foreach (var (role, login) in _nativeQueueLogins)
        {
            await using var command = connection.CreateCommand();
            string username = "\"" + login.Username.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            string password = "'" + login.Password.Replace("'", "''", StringComparison.Ordinal) + "'";
            // Only the opt-in migration administrator is demoted after creating its own schema.
            string attributes = role == PrimaryDatabaseRole.Migrator
                ? "SUPERUSER CREATEDB CREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS"
                : "NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS";
            command.CommandText = $"CREATE ROLE {username} LOGIN {attributes} PASSWORD {password}";
            await command.ExecuteNonQueryAsync();
        }
    }

    private ExploreDbContext CreateDbContextInternal(
        PrimaryDatabaseRole role = PrimaryDatabaseRole.Runtime,
        IReadOnlyList<IInterceptor>? interceptors = null,
        string? schema = null)
    {
        // Provider/schema and interceptor variants belong to this fixture, not EF's process-wide cache.
        var optionsBuilder = TestDbContextOptions.Create<ExploreDbContext>()
            .UseMemoryCache(_metadataCache);
        PrimaryDatabaseConnectionResult database = PrimaryDatabaseProviderComposition.ConfigureApplication(
            optionsBuilder,
            CreateDatabaseOptions(role, schema));
        optionsBuilder.ConfigureWarnings(warnings =>
        {
            warnings.Ignore(RelationalEventId.PendingModelChangesWarning);
        });
        if (interceptors is { Count: > 0 })
        {
            optionsBuilder.AddInterceptors(interceptors);
        }
        if (role == PrimaryDatabaseRole.Runtime && schema is null)
        {
            _runtimeConnectionString = database.ConnectionString;
        }

        return new ExploreDbContext(optionsBuilder.Options);
    }

    private PrimaryDatabaseConnectionOptions CreateDatabaseOptions(
        PrimaryDatabaseRole role,
        string? schema = null)
    {
        var container = new NpgsqlConnectionStringBuilder(_container.GetConnectionString());
        bool declaredMigrator = _nativeQueueLogins.TryGetValue(PrimaryDatabaseRole.Migrator, out var migrator);
        return new PrimaryDatabaseConnectionOptions
        {
            Role = role,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = container.Host,
            Port = container.Port,
            Database = container.Database,
            Schema = schema ?? RelationalModelNamespace.DefaultSchema,
            Username = declaredMigrator ? migrator.Username : container.Username,
            Password = declaredMigrator ? migrator.Password : container.Password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled,
        };
    }

    /// <summary>
    /// Application schema shared by migration and reset.
    /// </summary>
    private string ApplicationSchema => GetApplicationSchema(ConnectionString);

    private static string GetApplicationSchema(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString).SearchPath?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
        ?? RelationalModelNamespace.DefaultSchema;

}

/// <summary>
/// Deletes referencing foreign-key components first, with one statement per cycle.
/// Native guards and OLD-image invalidation run unchanged; no schema or privilege changes occur.
/// This helper is also compiled into the API suite through its existing fixture source link.
/// </summary>
internal sealed class PostgreSqlDatabaseReset(string[] tables, IReadOnlyDictionary<string, string[]> dependencies)
{
    public static async Task<PostgreSqlDatabaseReset> CreateAsync(NpgsqlConnection connection, string schema)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.relname
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema AND c.relkind IN ('r', 'p') AND NOT c.relispartition
            ORDER BY c.relname COLLATE "C"
            """;
        command.Parameters.AddWithValue("schema", schema);
        var preserved = UnqualifiedLookupTables.Select(table => table.Name).ToHashSet(StringComparer.Ordinal);
        var tables = new List<string>();
        using var identifiers = new NpgsqlCommandBuilder();
        await using (var rows = await command.ExecuteReaderAsync())
        {
            while (await rows.ReadAsync())
            {
                string table = rows.GetString(0);
                if (!preserved.Contains(table))
                    tables.Add($"{identifiers.QuoteIdentifier(schema)}.{identifiers.QuoteIdentifier(table)}");
            }
        }
        var references = tables.ToDictionary(table => table, _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        command.CommandText = """
            SELECT child.relname, parent.relname
            FROM pg_catalog.pg_constraint constraint_row
            JOIN pg_catalog.pg_class child ON child.oid = constraint_row.conrelid
            JOIN pg_catalog.pg_namespace child_namespace ON child_namespace.oid = child.relnamespace
            JOIN pg_catalog.pg_class parent ON parent.oid = constraint_row.confrelid
            JOIN pg_catalog.pg_namespace parent_namespace ON parent_namespace.oid = parent.relnamespace
            WHERE constraint_row.contype = 'f'
                AND child_namespace.nspname = @schema AND parent_namespace.nspname = @schema
            """;
        await using (var rows = await command.ExecuteReaderAsync())
        {
            while (await rows.ReadAsync())
            {
                string child = $"{identifiers.QuoteIdentifier(schema)}.{identifiers.QuoteIdentifier(rows.GetString(0))}";
                string parent = $"{identifiers.QuoteIdentifier(schema)}.{identifiers.QuoteIdentifier(rows.GetString(1))}";
                if (references.TryGetValue(child, out var parents) && references.ContainsKey(parent))
                    parents.Add(parent);
            }
        }
        return new(tables.ToArray(), references.ToDictionary(pair => pair.Key,
            pair => pair.Value.ToArray(), StringComparer.Ordinal));
    }

    public async Task ResetAsync(NpgsqlConnection connection)
    {
        if (tables.Length == 0)
            throw new InvalidOperationException("The reset schema contains no transactional tables.");

        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Empty DELETE statements still execute native statement guards. Select actual
        // populated relations first rather than planning every possible source mutation.
        command.CommandText = "SELECT " + string.Join(", ", tables.Select(table =>
            $"EXISTS (SELECT 1 FROM {table})"));
        var populated = new List<string>();
        await using (var rows = await command.ExecuteReaderAsync())
        {
            if (!await rows.ReadAsync())
                throw new InvalidOperationException("Native reset could not observe its transactional tables.");
            for (int index = 0; index < tables.Length; index++)
                if (rows.GetBoolean(index))
                    populated.Add(tables[index]);
        }
        if (populated.Count != 0)
        {
            var pending = populated.ToHashSet(StringComparer.Ordinal);
            var reachable = populated.ToDictionary(table => table, table =>
            {
                var visited = new HashSet<string>(StringComparer.Ordinal);
                void Visit(string current)
                {
                    if (!visited.Add(current))
                        return;
                    foreach (string parent in dependencies[current])
                        if (pending.Contains(parent))
                            Visit(parent);
                }
                Visit(table);
                return visited;
            }, StringComparer.Ordinal);
            while (pending.Count != 0)
            {
                bool deleted = false;
                foreach (string table in populated.Where(pending.Contains))
                {
                    // Mutual reachability is exactly one strongly connected FK component.
                    var component = pending.Where(other =>
                        reachable[table].Contains(other) && reachable[other].Contains(table))
                        .ToHashSet(StringComparer.Ordinal);
                    if (pending.Except(component).Any(child => dependencies[child].Any(component.Contains)))
                        continue;

                    // Delete referencing components first; only a true cycle needs a
                    // multi-table statement. All groups retain one owning transaction.
                    command.CommandText = "WITH " + string.Join(",\n",
                        component.Order(StringComparer.Ordinal).Select((relation, index) =>
                            $"reset_{index} AS (DELETE FROM {relation})")) + "\nSELECT 1;";
                    await command.ExecuteNonQueryAsync();
                    pending.ExceptWith(component);
                    deleted = true;
                    break;
                }
                if (!deleted)
                    throw new InvalidOperationException("Native reset could not order its foreign-key components.");
            }
        }

        // Fail and roll back rather than silently accepting a trigger that retained business rows.
        command.CommandText = "SELECT " + string.Join(" OR ", tables.Select(table =>
            $"EXISTS (SELECT 1 FROM {table})"));
        if (await command.ExecuteScalarAsync() is not false)
            throw new InvalidOperationException("Native reset left transactional rows behind.");
        await transaction.CommitAsync();
    }

    private static readonly Table[] UnqualifiedLookupTables =
    [
        new("__EFMigrationsHistory"),
        new("__EFDataProtectionMigrationsHistory"),
        new("__EFPrivacyErasureAuthorityMigrationsHistory"),
        new("policy_source_set"),
        new("resolver_configuration_source"),
        new("storage_probe_source"),
        new("storage_configuration_source"),
        new("provider_configuration_source"),
        new("mixed_settings_source"),
        new("ordinary_settings_source"),
        new("event_resource_provider_source"),
        new("local_administration_source"),
        new("support_administration_source"),
        new("human_provisioning_source"),
        new("human_principal_source_epoch"),
        new("human_event_assignment_source_epoch"),
        new("human_support_access_source_epoch"),
        new("human_webhook_resource_source_epoch"),
        new("human_event_provider_source_epochs"),
        new("account_authority_kinds"),
        new("ai_conversation_statuses"),
        new("ai_message_roles"),
        new("ai_proposed_action_kinds"),
        new("ai_proposed_action_statuses"),
        new("ai_reference_kinds"),
        new("ai_run_statuses"),
        new("actor_types"),
        new("analytics_providers"),
        new("approval_statuses"),
        new("admission_ticket_statuses"),
        new("admission_ticket_credential_statuses"),
        new("admission_ticket_transition_reasons"),
        new("audience_ages"),
        new("audience_genders"),
        new("did_custody_types"),
        new("event_formats"),
        new("participation_handling_modes"),
        new("advance_registration_obligations"),
        new("identity_access_modes"),
        new("event_organizer_claim_statuses"),
        new("event_provenance_types"),
        new("event_public_action_health_states"),
        new("event_public_action_kinds"),
        new("event_registration_policies"),
        new("event_session_kinds"),
        new("event_session_statuses"),
        new("event_statuses"),
        new("event_types"),
        new("external_api_key_credit_periods"),
        new("external_api_key_owner_types"),
        new("external_api_key_statuses"),
        new("external_workflow_provider_kinds"),
        new("file_types"),
        new("group_positions"),
        new("languages"),
        new("location_address_sources"),
        new("location_address_visibilities"),
        new("location_disclosure_audiences"),
        new("location_kinds"),
        new("location_privacy_states"),
        new("madhabs"),
        new("module_definitions"),
        new("notification_categories"),
        new("notification_delivery_policies"),
        new("notification_delivery_statuses"),
        new("notification_entity_types"),
        new("notification_external_delegation_statuses"),
        new("notification_intent_statuses"),
        new("notification_ownership_types"),
        new("notification_preference_categories"),
        new("notification_preference_channels"),
        new("notification_recipient_kinds"),
        new("notification_reasons"),
        new("notification_scope_types"),
        new("notification_types"),
        new("organization_positions"),
        new("permissions"),
        new("registration_modes"),
        new("registration_answer_sync_modes"),
        new("registration_attempt_statuses"),
        new("registration_requirement_completion_effects"),
        new("registration_requirement_criticalities"),
        new("registration_requirement_subject_types"),
        new("registration_submission_statuses"),
        new("registration_scopes"),
        new("role_scopes"),
        new("roles"),
        new("role_permissions"),
        new("schedule_item_kinds"),
        new("secret_source_types"),
        new("secret_validation_statuses"),
        new("setting_scopes"),
        new("setting_value_types"),
        new("system_settings"),
        new("support_access_audit_event_types"),
        new("support_access_end_reasons"),
        new("support_access_modes"),
        new("support_access_session_statuses"),
        new("tag_types"),
        new("tenant_footer_link_groups"),
        new("tenant_footer_links"),
        new("tenant_plan_application_statuses"),
        new("tenant_plan_assignment_statuses"),
        new("tenant_plan_statuses"),
        new("tenant_statuses"),
        new("ui_theme_presets"),
        new("visibility_types"),
        new("incoming_webhook_message_statuses"),
        new("incoming_webhook_processing_attempt_outcomes"),
        new("incoming_webhook_redrive_results"),
        new("incoming_webhook_settlement_sources"),
        new("webhook_consumer_kinds"),
        new("webhook_consumer_statuses"),
        new("webhook_delivery_attempt_outcomes"),
        new("webhook_endpoint_statuses"),
        new("webhook_local_delivery_statuses"),
        new("webhook_payload_provenances"),
        new("webhook_provider_binding_verification_states"),
        new("webhook_provider_kinds"),
        new("webhook_provider_modes"),
        new("webhook_provider_publication_attempt_outcomes"),
        new("webhook_provider_publication_statuses"),
    ];
}

/// <summary>
/// EF Core connection interceptor that sets PostgreSQL search_path on each connection open for dynamic schema isolation.
/// </summary>
public sealed class PostgresSearchPathInterceptor(string schema, string? fallbackSchema = null) : DbConnectionInterceptor
{
    private readonly string _fallbackSchema = fallbackSchema ?? RelationalModelNamespace.DefaultSchema;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SET search_path TO \"{schema}\", \"{_fallbackSchema}\", public;";
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SET search_path TO \"{schema}\", \"{_fallbackSchema}\", public;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
