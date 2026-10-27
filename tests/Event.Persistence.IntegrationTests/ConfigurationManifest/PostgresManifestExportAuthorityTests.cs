using System.Globalization;
using System.Security.Claims;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Event.Persistence.IntegrationTests.Fixtures;
using Event.Persistence.IntegrationTests.TenantIsolation;
using Explore.API.Extensions;
using Explore.Application;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Features.ConfigurationManifest.Handlers.Commands;
using Explore.Application.Features.ConfigurationManifest.Handlers.Queries;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using Explore.Domain.Settings.Documents;
using Explore.Infrastructure;
using Explore.Infrastructure.Secrets;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Entities;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.ServiceDefaults.Secrets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class PostgresManifestExportAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private const string ExcludedKey = "manifest.export.non_catalog";
    private const string TenantASlug = "z-export-tenant-a";
    private const string TenantBSlug = "a-export-tenant-b";

    [Test]
    [Arguments("resource")]
    [Arguments("tenant-setting")]
    [Arguments("branding")]
    [Arguments("tenant-identity")]
    [Arguments("paid-policy")]
    [Arguments("legal-mutation")]
    [Arguments("publication")]
    [Arguments("receipt")]
    public async Task ImportInstallersRejectUndeclaredColumnPrivilegesInOriginalTransaction(string family)
    {
        (string Owner, Func<ExploreDbContext, CancellationToken, Task> Install) contract = family switch
        {
            "resource" => (PostgresConfigurationImportEventResourceContract.OwnerRole,
                PostgresConfigurationImportEventResourceContract.ApplyAsync),
            "tenant-setting" => (PostgresConfigurationImportTenantSettingContract.OwnerRole,
                PostgresConfigurationImportTenantSettingContract.ApplyAsync),
            "branding" => (PostgresConfigurationImportBrandingContract.OwnerRole,
                PostgresConfigurationImportBrandingContract.ApplyAsync),
            "tenant-identity" => (PostgresConfigurationImportTenantIdentityContract.OwnerRole,
                PostgresConfigurationImportTenantIdentityContract.ApplyAsync),
            "paid-policy" => (PostgresConfigurationImportPaidPolicyContract.OwnerRole,
                PostgresConfigurationImportPaidPolicyContract.ApplyAsync),
            "legal-mutation" => (PostgresConfigurationImportLegalMutationContract.OwnerRole,
                PostgresConfigurationImportLegalMutationContract.ApplyAsync),
            "publication" => (PostgresConfigurationImportPublicationPolicyContract.OwnerRole,
                PostgresConfigurationImportPublicationPolicyContract.ApplyAsync),
            "receipt" => (PostgresConfigurationImportReceiptContract.OwnerRole,
                PostgresConfigurationImportReceiptContract.ApplyAsync),
            _ => throw new ArgumentOutOfRangeException(nameof(family), family, null)
        };
        var builder = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            builder, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var migration = new ExploreDbContext(builder.Options);
        await using var transaction = await migration.Database.BeginTransactionAsync();
        await contract.Install(migration, CancellationToken.None);

        var names = new HumanPrincipalSourceSql(migration);
        using var identifiers = new NpgsqlCommandBuilder();
        // GRANT accepts identifiers, not value parameters. Every identifier is quoted.
        string grant =
            $"GRANT REFERENCES ({names.Column(typeof(SystemSetting), nameof(SystemSetting.SettingKey))}) " +
            $"ON TABLE {names.Table(typeof(SystemSetting))} TO {identifiers.QuoteIdentifier(contract.Owner)}";
        await migration.Database.ExecuteSqlRawAsync(grant);
        var denied = await Assert.ThrowsAsync<PostgresException>(() => contract.Install(migration, CancellationToken.None));
        await Assert.That(denied!.SqlState).IsEqualTo("42501");
        await transaction.RollbackAsync();

        // The failed install must neither commit the injected grant nor replace
        // the caller's transaction with a separately committed installation.
        await using var cleanTransaction = await migration.Database.BeginTransactionAsync();
        await contract.Install(migration, CancellationToken.None);
        await cleanTransaction.RollbackAsync();
    }

    [Test]
    public async Task OrdinaryRuntimeTenantACannotReadTenantBOverridesEvenWithoutQueryFilters()
    {
        await using var rows =
            await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
        await SeedExportStateAsync(rows);
        await using var authority =
            await PostgresAuthenticationLookupAuthorityTests.AuthenticationGlobalAuthority.CreateAsync(fixture);
        await using var global = await authority.Factory.CreateDbContextAsync();
        var globalConnection = new NpgsqlConnectionStringBuilder(global.Database.GetConnectionString());
        await using var request = rows.Context(rows.A);
        await rows.AssertOrdinaryRoleAsync(request);
        string runtimeLogin = await CurrentLoginAsync(request);
        await SetReadPermissionsAsync(rows.Schema, runtimeLogin, globalConnection.Username!, grant: true);
        try
        {
            await using var services = CreateServices(rows.RuntimeOptions, globalConnection);
            await using var scope = services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(rows.A);
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await rows.AssertOrdinaryRoleAsync(context);
            var settings = scope.ServiceProvider.GetRequiredService<ITenantSettingRepository>();

            List<TenantSetting> own = await settings.GetByTenantAndKeys(
                rows.A, [EventSettingDefinitions.RequireApproval.Key], CancellationToken.None);
            await Assert.That(own.Single().Value).IsEqualTo(JsonSerializer.Serialize(false));
            await Assert.That(await settings.GetByTenantAndKeys(
                rows.B, [EventSettingDefinitions.RequireApproval.Key], CancellationToken.None)).IsEmpty();
            await Assert.That(await context.TenantSettingOverrides.IgnoreQueryFilters()
                .AnyAsync(row => row.TenantId == rows.B)).IsFalse();
            await Assert.That(context.TenantFilterTenantId).IsEqualTo((Guid?)rows.A);
        }
        finally
        {
            await SetReadPermissionsAsync(rows.Schema, runtimeLogin, globalConnection.Username!, grant: false);
        }
    }

    [Test]
    [Arguments(ConfigurationManifestExportView.Overrides, false)]
    [Arguments(ConfigurationManifestExportView.Portable, false)]
    [Arguments(ConfigurationManifestExportView.Overrides, true)]
    [Arguments(ConfigurationManifestExportView.Portable, true)]
    public async Task TrustedReaderExportsExactSelectedTenantOverridesFromAmbientTenantA(
        ConfigurationManifestExportView view, bool singleTenant)
    {
        await using var rows =
            await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
        await SeedExportStateAsync(rows);
        await using var authority =
            await PostgresAuthenticationLookupAuthorityTests.AuthenticationGlobalAuthority.CreateAsync(fixture);
        await using var global = await authority.Factory.CreateDbContextAsync();
        var globalConnection = new NpgsqlConnectionStringBuilder(global.Database.GetConnectionString());
        await using var request = rows.Context(rows.A);
        await rows.AssertOrdinaryRoleAsync(request);
        string runtimeLogin = await CurrentLoginAsync(request);
        await SetReadPermissionsAsync(rows.Schema, runtimeLogin, globalConnection.Username!, grant: true);
        try
        {
            await using var services = CreateServices(rows.RuntimeOptions, globalConnection);
            await using var scope = services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(rows.A);
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await rows.AssertOrdinaryRoleAsync(context);
            await Assert.That(await context.TenantSettingOverrides.IgnoreQueryFilters()
                .AnyAsync(row => row.TenantId == rows.B)).IsFalse();

            // This is the trusted reader seam, not an HTTP authorization assertion.
            var reader = scope.ServiceProvider.GetRequiredService<ConfigurationManifestCurrentStateReader>();
            var result = singleTenant
                ? await reader.ReadTenantAsync(rows.B, view, CancellationToken.None)
                : await reader.ReadAsync(view, CancellationToken.None);

            using JsonDocument exported = JsonDocument.Parse(result.Utf8Json);
            JsonElement spec = exported.RootElement.GetProperty("spec");
            JsonElement[] tenants = spec.GetProperty("tenants").EnumerateArray().ToArray();
            await Assert.That(tenants.Length).IsEqualTo(singleTenant ? 1 : 2);
            await Assert.That(tenants[0].GetProperty("metadata").GetProperty("name").GetString())
                .IsEqualTo(TenantBSlug);
            string key = EventSettingDefinitions.RequireApproval.Key;
            await Assert.That(tenants[0].GetProperty("spec").GetProperty("settings")
                .GetProperty(key).GetBoolean()).IsTrue();
            if (!singleTenant)
            {
                await Assert.That(tenants[1].GetProperty("metadata").GetProperty("name").GetString())
                    .IsEqualTo(TenantASlug);
                await Assert.That(tenants[1].GetProperty("spec").GetProperty("settings")
                    .GetProperty(key).GetBoolean()).IsFalse();
            }
            JsonElement instanceSettings = spec.GetProperty("instance").GetProperty("settings");
            await Assert.That(instanceSettings.GetProperty(key).GetBoolean()).IsFalse();
            await Assert.That(instanceSettings.TryGetProperty(ExcludedKey, out _)).IsFalse();
            foreach (JsonElement tenant in tenants)
            {
                JsonElement settings = tenant.GetProperty("spec").GetProperty("settings");
                await Assert.That(settings.TryGetProperty(ExcludedKey, out _)).IsFalse();
                await Assert.That(settings.EnumerateObject().All(
                    entry => ConfigurationManifestCatalog.TryGetTenantSetting(entry.Name, out _))).IsTrue();
            }
            await Assert.That(instanceSettings.EnumerateObject().All(
                entry => ConfigurationManifestCatalog.TryGetInstanceSetting(entry.Name, out _))).IsTrue();
            await Assert.That(context.TenantFilterTenantId).IsEqualTo((Guid?)rows.A);
            await Assert.That(context.IsTenantFilterBypassed).IsFalse();
            await Assert.That(scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().TenantId)
                .IsEqualTo((Guid?)rows.A);
            await Assert.That(await context.TenantSettingOverrides.IgnoreQueryFilters()
                .AnyAsync(row => row.TenantId == rows.B)).IsFalse();
        }
        finally
        {
            await SetReadPermissionsAsync(rows.Schema, runtimeLogin, globalConnection.Username!, grant: false);
        }
    }

    [Test]
    [Arguments(ConfigurationManifestExportView.Overrides)]
    [Arguments(ConfigurationManifestExportView.Portable)]
    public async Task ImportOwnerSnapshotIncludesForeignTenantWithoutRetargetingRuntimeCaller(
        ConfigurationManifestExportView view)
    {
        await using var rows =
            await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
        await SeedExportStateAsync(rows);
        await using var authority =
            await PostgresAuthenticationLookupAuthorityTests.AuthenticationGlobalAuthority.CreateAsync(
                fixture, PostgresConfigurationImportSnapshotContract.GlobalRole);
        await using var global = await authority.Factory.CreateDbContextAsync();
        var globalConnection = new NpgsqlConnectionStringBuilder(global.Database.GetConnectionString());
        await EnrollLegalSourceAsync(globalConnection.Username!);
        await using var request = rows.Context(rows.A);
        await rows.AssertOrdinaryRoleAsync(request);
        string runtimeLogin = await CurrentLoginAsync(request);
        await SetReadPermissionsAsync(rows.Schema, runtimeLogin, globalConnection.Username!, grant: true);
        try
        {
            PostgresException? denied = await Assert.ThrowsAsync<PostgresException>(() =>
                global.TenantSettingOverrides.IgnoreQueryFilters().AnyAsync());
            await Assert.That(denied?.SqlState).IsEqualTo("42501");
            await using var services = CreateServices(rows.RuntimeOptions, globalConnection);
            await using var scope = services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(rows.A);
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await rows.AssertOrdinaryRoleAsync(context);
            var executor = scope.ServiceProvider.GetRequiredService<IConfigurationInstanceImportExecutor>();
            await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable);
                var result = await executor.ReadSnapshotAsync(view, default);

                using JsonDocument snapshot = JsonDocument.Parse(result.Utf8Json);
                JsonElement[] tenants = snapshot.RootElement.GetProperty("spec")
                    .GetProperty("tenants").EnumerateArray().ToArray();
                await Assert.That(tenants.Length).IsEqualTo(2);
                JsonElement foreign = tenants.Single(tenant =>
                    tenant.GetProperty("metadata").GetProperty("name").GetString() == TenantBSlug);
                await Assert.That(foreign.GetProperty("spec").GetProperty("settings")
                    .GetProperty(EventSettingDefinitions.RequireApproval.Key).GetBoolean()).IsTrue();
                await Assert.That(context.TenantFilterTenantId).IsEqualTo((Guid?)rows.A);
                await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(await context.TenantSettingOverrides.IgnoreQueryFilters()
                    .AnyAsync(row => row.TenantId == rows.B)).IsFalse();
                await transaction.RollbackAsync();
            });
        }
        finally
        {
            await SetReadPermissionsAsync(rows.Schema, runtimeLogin, globalConnection.Username!, grant: false);
        }
    }

    [Test]
    public Task NativeInstancePreviewReadsItsPersistedSessionThroughTheSelectedOwner() =>
        ExecuteNativeImportAsync(apply: false);

    [Test]
    public Task NativeInstanceStagingRejectsDirectAccessStaleRevisionAndCancelledReplay() =>
        ExecuteNativeImportAsync(apply: false, stateFences: true);

    [Test]
    public Task NativeInstanceUploadAndCancellationHandlersUseOwnerWithoutRuntimeStagingWrites() =>
        ExecuteNativeImportAsync(apply: false, stateFences: true, nativeStagingRoutes: true);

    [Test]
    public Task NativeInstanceApplyCommitsSelectedSettingSessionReceiptAndBoundEffectTogether() =>
        ExecuteNativeImportAsync(apply: true, preserveFederation: true);

    [Test]
    public Task NativeInstanceResourceImportPreservesExactNotificationBytesAndCommittedDigest() =>
        ExecuteNativeImportAsync(apply: true, resourceSettings: true);

    [Test]
    public Task NativeInstanceFailureAfterPersistedEffectRollsBackWritesAndKeepsSanitizedReceipt() =>
        ExecuteNativeImportAsync(apply: true, failAfterEffect: true, preserveFederation: true);

    [Test]
    public Task NativeInstanceReceiptHistoryAndForwardRollbackUseDedicatedOwner() =>
        ExecuteNativeImportAsync(apply: true, receiptAndRollback: true);

    [Test]
    public Task NativeInstanceRollbackCancellationDeletesFreshBytesButPreservesRetainedSnapshot() =>
        ExecuteNativeImportAsync(apply: true, receiptAndRollback: true, rollbackCancellation: true);

    [Test]
    public Task NativeInstancePortableSettingsApplyCommitsCatalogDefaultsAndBoundEffectTogether() =>
        ExecuteNativeImportAsync(apply: true, exportView: ConfigurationManifestExportView.Portable);

    [Test]
    public Task NativeInstanceLegalApplyCreatesReviewRequiredSourceWithAtomicReceiptAndEffect() =>
        ExecuteNativeImportAsync(apply: true, importLegal: true);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task NativeInstanceLegalRevisionPreservesPublishedAndRetiredHistory(bool retired) =>
        ExecuteNativeImportAsync(apply: true, importLegal: true,
            legalRevision: retired ? LegalDocumentLifecycleState.Retired : LegalDocumentLifecycleState.Published);

    [Test]
    public Task NativeInstancePaidPolicyApplyPreservesContentWithAtomicReceiptAndEffect() =>
        ExecuteNativeImportAsync(apply: true, importPolicy: true);

    [Test]
    public Task NativeInstanceCeilingChangeRejectsOmittedInactiveTenantPolicy() =>
        ExecuteNativeImportAsync(apply: true, importPolicy: true, rejectCeiling: true);

    [Test]
    public Task NativeInstanceTenantLegalApplyPreservesRuntimeScopeWithAtomicReceiptAndEffect() =>
        ExecuteNativeImportAsync(apply: true, importLegal: true, tenantLegal: true);

    [Test]
    public Task NativeInstanceTenantSettingsApplyChangesMappedForeignTenantAtomically() =>
        ExecuteNativeImportAsync(apply: true, tenantSettings: true);

    [Test]
    public Task NativeInstancePortableTenantSettingsApplyCommitsCatalogOverridesAndBoundEffectTogether() =>
        ExecuteNativeImportAsync(apply: true, tenantSettings: true,
            exportView: ConfigurationManifestExportView.Portable);

    [Test]
    public Task NativeInstanceTenantDocumentsApplyPreservesPolicyHistoryAndBrandingAtomically() =>
        ExecuteNativeImportAsync(apply: true, tenantDocuments: true);

    [Test]
    public Task NativeInstanceTenantDocumentsCreateBrandingWithAtomicPolicyReceiptAndEffect() =>
        ExecuteNativeImportAsync(apply: true, tenantDocuments: true, createBranding: true);

    private async Task ExecuteNativeImportAsync(
        bool apply, bool importLegal = false, bool importPolicy = false, bool tenantLegal = false,
        bool tenantSettings = false, bool tenantDocuments = false, bool createBranding = false,
        ConfigurationManifestExportView exportView = ConfigurationManifestExportView.Overrides,
        LegalDocumentLifecycleState? legalRevision = null, bool failAfterEffect = false,
        bool rejectCeiling = false, bool receiptAndRollback = false, bool stateFences = false,
        bool nativeStagingRoutes = false, bool rollbackCancellation = false, bool resourceSettings = false,
        bool preserveFederation = false)
    {
        LegalDocumentKind importedLegalKind = tenantLegal
            ? LegalDocumentKind.TenantTerms : LegalDocumentKind.TermsOfService;
        long originalDisclosureEpoch = 0;
        TenantSettingsDocument? originalBranding = null;
        LegalDocument? originalLegal = null;
        Guid? writtenEffectId = null;
        string? inactiveSlug = null;
        await using var rows =
            await PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.CreateAsync(fixture);
        await SeedExportStateAsync(rows);
        FederationManifestStorageSnapshot? originalFederation = preserveFederation
            ? await FederationManifestStorageSnapshot.SeedAsync(
                fixture, [rows.A, rows.B, rows.Inactive], rows.UserId, TestContext.Current!.Execution.CancellationToken)
            : null;
        await using (var seed = fixture.CreateDbContext())
        {
            if (rejectCeiling)
            {
                var defaults = PaidEventPolicyVersion.CreateDefaultInstance();
                var current = await seed.PaidEventPolicyVersions.SingleAsync(row =>
                    row.TenantId == null && row.IsActive);
                var enabled = current.CreateRevision(true, defaults.AllowedOrganizerKinds,
                    defaults.RequiresLocalVerification, defaults.AllowedCurrencyCodes, "USD",
                    defaults.RefundProtections, defaults.CurrencyRiskLimits,
                    defaults.RequiresFirstPaidEventReview, defaults.FarFutureReviewThresholdDays);
                seed.PaidEventPolicyVersions.Add(enabled);
                seed.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateTenant(
                    rows.Inactive, true, defaults.AllowedOrganizerKinds,
                    defaults.RequiresLocalVerification, defaults.AllowedCurrencyCodes, "USD",
                    defaults.RefundProtections, defaults.CurrencyRiskLimits,
                    defaults.RequiresFirstPaidEventReview, defaults.FarFutureReviewThresholdDays));
                inactiveSlug = (await seed.Tenants.SingleAsync(row => row.Id == rows.Inactive)).Slug;
            }
            if (legalRevision is not null)
            {
                DateTime at = PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.Now;
                originalLegal = LegalDocument.CreateImportedDraft(
                    LegalDocumentScope.Instance, null, importedLegalKind, LegalDocumentAudience.Public,
                    [LegalDocumentLocalizedSource.Create("en", "Original terms", "Original summary", "Original source")],
                    null, $"sha256:{new string('a', 64)}", true, at);
                originalLegal.CreatedBy = rows.UserId;
                originalLegal.BindAccountableIdentity("instance:original-operator", at);
                originalLegal.Approve(rows.UserId, "Original review", at);
                originalLegal.Schedule(at, at);
                originalLegal.Publish(at);
                if (legalRevision == LegalDocumentLifecycleState.Retired)
                    originalLegal.Retire(at);
                seed.LegalDocuments.Add(originalLegal);
            }
            if (tenantDocuments)
            {
                string key = SettingsDocumentKeys.Tenant.Branding;
                var catalog = ConfigurationManifestCatalog.TenantDocuments[key];
                var branding = TenantSettingsDocument.Create(
                    rows.B, key, catalog.SchemaVersion, catalog.DefaultsVersion!,
                    """{"displayName":"Original branding"}""");
                branding.Id = Guid.CreateVersion7();
                branding.CreatedAt = DateTime.UtcNow;
                if (!createBranding)
                {
                    originalBranding = branding;
                    seed.TenantSettingsDocuments.Add(branding);
                }
                var defaults = PaidEventPolicyVersion.CreateDefaultInstance();
                seed.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateTenant(
                    rows.B, defaults.IsPaymentsEnabled, defaults.AllowedOrganizerKinds,
                    defaults.RequiresLocalVerification, defaults.AllowedCurrencyCodes,
                    defaults.DefaultCurrencyCode, defaults.RefundProtections,
                    defaults.CurrencyRiskLimits, defaults.RequiresFirstPaidEventReview,
                    defaults.FarFutureReviewThresholdDays));
            }
            originalDisclosureEpoch = await seed.Set<EventDiscoveryRevision>().AsNoTracking()
                .Where(row => row.TenantId == rows.B).Select(row => row.DisclosureEpoch).SingleOrDefaultAsync();
            int roleId = await seed.Roles.Where(role =>
                role.RoleScopeId == (int)RoleScopeEnum.Platform && role.MasterCode == "platform.admin")
                .Select(role => role.Id).SingleAsync();
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = rows.UserId,
                User = null!,
                RoleId = roleId,
                Role = null!,
                GrantedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        await using var authority =
            await PostgresAuthenticationLookupAuthorityTests.AuthenticationGlobalAuthority.CreateAsync(
                fixture, PostgresConfigurationImportSnapshotContract.GlobalRole);
        await using var global = await authority.Factory.CreateDbContextAsync();
        var globalConnection = new NpgsqlConnectionStringBuilder(global.Database.GetConnectionString());
        await EnrollLegalSourceAsync(globalConnection.Username!);
        await using (var enrollment = new NpgsqlConnection(fixture.ConnectionString))
        {
            await enrollment.OpenAsync();
            await using var membership = enrollment.CreateCommand();
            membership.CommandText =
                $"GRANT \"{PostgresConfigurationImportStateContract.GlobalRole}\" TO \"{globalConnection.Username}\" WITH INHERIT TRUE, SET FALSE";
            await membership.ExecuteNonQueryAsync();
        }
        await using var request = rows.Context(rows.A);
        await rows.AssertOrdinaryRoleAsync(request);
        string runtimeLogin = await CurrentLoginAsync(request);
        await SetReadPermissionsAsync(rows.Schema, runtimeLogin, globalConnection.Username!, grant: true);
        if (!nativeStagingRoutes)
            await SetStagingWritePermissionsAsync(runtimeLogin, grant: true);
        try
        {
            await using var services = CreateServices(rows.RuntimeOptions, globalConnection, rows.UserId,
                failAfterEffect ? id => writtenEffectId = id : null);
            await using var scope = services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(rows.A);
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await rows.AssertOrdinaryRoleAsync(context);
            var executor = scope.ServiceProvider.GetRequiredService<IConfigurationInstanceImportExecutor>();
            var snapshot = await executor.ReadSnapshotAsync(exportView, default);
            if (originalFederation is not null)
                await FederationManifestStorageSnapshot.AssertPublicExclusionAsync(snapshot.Utf8Json);
            var sessions = scope.ServiceProvider.GetRequiredService<ConfigurationImportSessionApplicationService>();
            byte[] sourceBytes = snapshot.Utf8Json.ToArray();
            if (apply)
            {
                var source = JsonNode.Parse(sourceBytes)
                    ?? throw new InvalidOperationException("The source manifest is required.");
                if (importLegal)
                {
                    JsonNode legalTarget = tenantLegal
                        ? source["spec"]!["tenants"]!.AsArray().Single(tenant =>
                            tenant!["metadata"]!["name"]!.GetValue<string>() == TenantBSlug)!["spec"]!
                        : source["spec"]!["instance"]!;
                    JsonNode importedLegal = JsonNode.Parse("""
                            {"kind":"TermsOfService","audience":"Public","lifecycleIntent":"ReviewRequired",
                             "requiresFreshAcceptance":true,"jurisdictionAssumptions":[],"templateProvenance":null,
                             "localizations":[{"languageTag":"en","title":"Imported terms",
                                "summary":"Portable summary","markdown":"Native legal source"}]}
                            """)!;
                    importedLegal["kind"] = importedLegalKind.ToString();
                    legalTarget["legalDocuments"]![importedLegalKind.ToString()] = importedLegal;
                }
                else if (rejectCeiling)
                {
                    await Assert.That(source["spec"]!["tenants"]!.AsArray().Any(tenant =>
                        tenant!["metadata"]!["name"]!.GetValue<string>() == inactiveSlug)).IsFalse();
                    source["spec"]!["instance"]!["documents"]![
                        ConfigurationManifestDocumentKeys.InstancePaidEventPolicy]!["payload"]!["isPaymentsEnabled"] = false;
                }
                else if (tenantDocuments)
                {
                    JsonNode tenant = source["spec"]!["tenants"]!.AsArray().Single(entry =>
                        entry!["metadata"]!["name"]!.GetValue<string>() == TenantBSlug)!["spec"]!;
                    tenant["documents"]![SettingsDocumentKeys.Tenant.Branding] = new JsonObject
                    {
                        ["schemaVersion"] = ConfigurationManifestCatalog.TenantDocuments[
                            SettingsDocumentKeys.Tenant.Branding].SchemaVersion,
                        ["payload"] = new JsonObject { ["displayName"] = "Imported branding" }
                    };
                }
                else if (tenantSettings)
                {
                    JsonNode tenant = source["spec"]!["tenants"]!.AsArray().Single(entry =>
                        entry!["metadata"]!["name"]!.GetValue<string>() == TenantBSlug)!["spec"]!;
                    tenant["displayName"] = "Imported foreign tenant";
                    tenant["settings"]![EventSettingDefinitions.RequireApproval.Key] = false;
                }
                else if (resourceSettings)
                    source["spec"]!["instance"]!["settings"]![EventResourceSettingDefinitions.MaxActiveResources.Key] = 499;
                else if (!importPolicy)
                    source["spec"]!["instance"]!["settings"]![EventSettingDefinitions.RequireApproval.Key] = true;
                sourceBytes = JsonSerializer.SerializeToUtf8Bytes(source);
            }
            var session = nativeStagingRoutes
                ? await new CreateInstanceConfigurationImportSessionCommandHandler(executor)
                    .ExecuteAsync(new(sourceBytes), default)
                : await sessions.CreateInstanceAsync(sourceBytes, default);
            var selection = new ConfigurationImportPreviewRequest
            {
                SelectedSectionKeys = [importLegal ? tenantLegal ? "tenant.legal_documents" : "instance.legal_documents"
                    : importPolicy ? "instance.documents"
                    : tenantDocuments ? "tenant.documents"
                    : tenantSettings ? "tenant.settings" : "instance.settings"],
                Mappings = new Dictionary<string, string>(),
                ApplyMode = apply ? ConfigurationImportApplyMode.ApplySelected : ConfigurationImportApplyMode.PreviewOnly,
                GrantedApprovalCodes = importLegal ? ["legal-review"] : []
            };

            var preview = await executor.PreviewAsync(
                session.SessionId, session.AccessToken, selection, default);

            await Assert.That(preview.SessionId).IsEqualTo(session.SessionId);
            await Assert.That(preview.State).IsEqualTo(ConfigurationImportSessionState.PreviewReady);
            if (stateFences)
            {
                var directSession = await Assert.ThrowsAsync<PostgresException>(() =>
                    global.Set<ConfigurationImportSession>().AsNoTracking().AnyAsync());
                await Assert.That(directSession?.SqlState).IsEqualTo("42501");
                var directArtifact = await Assert.ThrowsAsync<PostgresException>(() =>
                    global.Set<ConfigurationImportStoredArtifact>().AsNoTracking().AnyAsync());
                await Assert.That(directArtifact?.SqlState).IsEqualTo("42501");
                var entity = global.Model.FindEntityType(typeof(ConfigurationImportSession))!;
                var deniedRuntime = await Assert.ThrowsAsync<PostgresException>(() =>
                    context.Database.ExecuteSqlRawAsync(
                        $"SELECT * FROM \"{entity.GetSchema()}\".configuration_import_state_read_session(@id)",
                        new NpgsqlParameter("id", session.SessionId)));
                await Assert.That(deniedRuntime?.SqlState).IsEqualTo("42501");

                global.BindConfigurationInstanceImportOwner();
                ConfigurationImportSession? stale = null;
                await global.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var transaction = await global.Database.BeginTransactionAsync();
                    stale = await PostgresConfigurationImportStateContract.ReadSessionAsync(
                        global, session.SessionId);
                    await transaction.CommitAsync();
                });
                await Assert.That(stale).IsNotNull();
                long originalRevision = stale!.Revision;
                await executor.PreviewAsync(session.SessionId, session.AccessToken, selection, default);
                stale.MarkPreviewReady(stale.PreviewBinding!, DateTime.UtcNow);
                await global.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    await using var transaction = await global.Database.BeginTransactionAsync();
                    var conflict = await Assert.ThrowsAsync<PostgresException>(() =>
                        PostgresConfigurationImportStateContract.WriteSessionAsync(
                            global, stale, originalRevision));
                    await Assert.That(conflict?.SqlState).IsEqualTo("40001");
                    await transaction.RollbackAsync();
                });
                global.ChangeTracker.Clear();
                if (nativeStagingRoutes)
                {
                    await global.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                    {
                        await using var transaction = await global.Database.BeginTransactionAsync();
                        var activeDelete = await Assert.ThrowsAsync<PostgresException>(() =>
                            PostgresConfigurationImportStateContract.DeleteArtifactAsync(
                                global, stale.ArtifactHandleId));
                        await Assert.That(activeDelete?.SqlState).IsEqualTo("42501");
                        await transaction.RollbackAsync();
                    });
                    await new CancelInstanceConfigurationImportSessionCommandHandler(executor)
                        .ExecuteAsync(new(session.SessionId, session.AccessToken), default);
                }
                else
                    await global.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                    {
                        await using var transaction = await global.Database.BeginTransactionAsync();
                        var current = await PostgresConfigurationImportStateContract.ReadSessionAsync(
                            global, session.SessionId);
                        await Assert.That(current!.Revision).IsEqualTo(originalRevision + 1);
                        long expected = current.Revision;
                        current.Cancel(DateTime.UtcNow);
                        await PostgresConfigurationImportStateContract.WriteSessionAsync(
                            global, current, expected);
                        await transaction.CommitAsync();
                    });
                var replay = await Assert.That(async () => await executor.PreviewAsync(
                    session.SessionId, session.AccessToken, selection, default))
                    .Throws<ConfigurationImportSessionException>();
                await Assert.That(replay.FailureCode).IsEqualTo(ConfigurationImportFailureCodes.Cancelled);
                await using var cancelledObserver = fixture.CreateDbContext();
                await Assert.That((await cancelledObserver.Set<ConfigurationImportSession>().AsNoTracking()
                    .SingleAsync(row => row.SessionId == session.SessionId)).State)
                    .IsEqualTo(ConfigurationImportSessionState.Cancelled);
                await Assert.That(await cancelledObserver.ConfigurationImportOperations.AnyAsync(
                    row => row.SessionId == session.SessionId)).IsFalse();
                if (nativeStagingRoutes)
                    await Assert.That(await cancelledObserver.Set<ConfigurationImportStoredArtifact>().AnyAsync(
                        row => row.Id == stale.ArtifactHandleId)).IsFalse();
            }
            if (apply)
            {
                await Assert.That(preview.IsApplyReady).IsTrue();
                PostgresException? nativeFailure = null;
                Exception? applicationFailure = null;
                EventHandler<FirstChanceExceptionEventArgs> trace = (_, arguments) =>
                {
                    if (arguments.Exception is PostgresException sql)
                        Interlocked.CompareExchange(ref nativeFailure, sql, null);
                    else if (arguments.Exception is ArgumentException or InvalidOperationException
                        && arguments.Exception is not ConfigurationImportSessionException
                        && arguments.Exception.StackTrace?.Contains("Explore.", StringComparison.Ordinal) == true)
                        Interlocked.CompareExchange(ref applicationFailure, arguments.Exception, null);
                };
                AppDomain.CurrentDomain.FirstChanceException += trace;
                ConfigurationImportOperationResult operation;
                try
                {
                    operation = await executor.ApplyAsync(
                        session.SessionId, session.AccessToken, selection, null, null, default);
                }
                catch (ConfigurationImportSessionException exception) when (
                    nativeFailure is not null || applicationFailure is not null || writtenEffectId.HasValue || rejectCeiling)
                {
                    await using var failedObserver = fixture.CreateDbContext();
                    if (rejectCeiling)
                    {
                        var failed = await failedObserver.ConfigurationImportOperations.AsNoTracking()
                            .SingleAsync(row => row.SessionId == session.SessionId);
                        await Assert.That(failed.Status).IsEqualTo(ConfigurationImportOperationStatus.Failed);
                        await Assert.That(failed.FailureCode).IsEqualTo(PaidEventPolicyMutationFailureCodes.ValidationFailed);
                        await Assert.That(failed.EffectOutboxId).IsNull();
                        var original = await failedObserver.PaidEventPolicyVersions.AsNoTracking().SingleAsync(
                            row => row.TenantId == null && row.IsActive);
                        await Assert.That(original.VersionNumber).IsEqualTo(2);
                        await Assert.That(original.IsPaymentsEnabled).IsTrue();
                        await Assert.That(await failedObserver.PaidEventPolicyVersions.AnyAsync(
                            row => row.TenantId == null && row.VersionNumber > 2)).IsFalse();
                        await Assert.That((await failedObserver.PaidEventPolicyVersions.AsNoTracking().SingleAsync(
                            row => row.TenantId == rows.Inactive && row.IsActive)).IsPaymentsEnabled).IsTrue();
                        await Assert.That((await failedObserver.Set<ConfigurationImportSession>().AsNoTracking()
                            .SingleAsync(row => row.SessionId == session.SessionId)).State)
                            .IsEqualTo(ConfigurationImportSessionState.PreviewReady);
                        return;
                    }
                    if (failAfterEffect && writtenEffectId.HasValue)
                    {
                        var failed = await failedObserver.ConfigurationImportOperations.AsNoTracking()
                            .SingleAsync(row => row.SessionId == session.SessionId);
                        await Assert.That(failed.Status).IsEqualTo(ConfigurationImportOperationStatus.Failed);
                        await Assert.That(failed.FailureCode).IsEqualTo(ConfigurationImportFailureCodes.ApplyFailed);
                        await Assert.That(failed.FidelityVerified).IsFalse();
                        await Assert.That(failed.EffectOutboxId).IsNull();
                        await Assert.That(failed.SnapshotArtifactHandleId).IsNull();
                        await Assert.That(await failedObserver.OutboxMessages.AnyAsync(row => row.Id == writtenEffectId.Value))
                            .IsFalse();
                        await Assert.That((await failedObserver.Set<ConfigurationImportSession>().AsNoTracking()
                            .SingleAsync(row => row.SessionId == session.SessionId)).State)
                            .IsEqualTo(ConfigurationImportSessionState.PreviewReady);
                        var original = await failedObserver.SystemSettings.AsNoTracking().SingleAsync(
                            row => row.SettingKey == EventSettingDefinitions.RequireApproval.Key);
                        await Assert.That(JsonSerializer.Deserialize<bool>(original.Value)).IsFalse();
                        if (originalFederation is not null)
                        {
                            await using var federationObserver = FederationManifestStorageSnapshot.OpenMigrator(fixture);
                            await originalFederation.AssertUnchangedAsync(federationObserver,
                                TestContext.Current!.Execution.CancellationToken);
                        }
                        return;
                    }
                    var persistedSession = await failedObserver.Set<ConfigurationImportSession>().AsNoTracking()
                        .SingleAsync(row => row.SessionId == session.SessionId);
                    bool committed = await failedObserver.ConfigurationImportOperations.AnyAsync(row =>
                        row.SessionId == session.SessionId
                        && (row.Status == ConfigurationImportOperationStatus.Applied
                            || row.Status == ConfigurationImportOperationStatus.RolledBack));
                    await Assert.That(persistedSession.State).IsEqualTo(committed
                        ? ConfigurationImportSessionState.Consumed : ConfigurationImportSessionState.PreviewReady);
                    if (importLegal && !committed)
                    {
                        var legal = await failedObserver.LegalDocuments.AsNoTracking()
                            .SingleOrDefaultAsync(row => row.Scope == (tenantLegal ? LegalDocumentScope.Tenant : LegalDocumentScope.Instance)
                                && row.TenantId == (tenantLegal ? rows.B : (Guid?)null)
                                && row.Kind == importedLegalKind);
                        if (originalLegal is null)
                            await Assert.That(legal).IsNull();
                        else
                        {
                            await Assert.That(legal!.State).IsEqualTo(originalLegal.State);
                            await Assert.That(legal.CurrentVersion).IsEqualTo(1);
                        }
                    }
                    if (importPolicy && !committed)
                    {
                        var original = await failedObserver.PaidEventPolicyVersions.AsNoTracking()
                            .SingleAsync(row => row.TenantId == null && row.IsActive);
                        await Assert.That(original.VersionNumber).IsEqualTo(1);
                        await Assert.That(await failedObserver.PaidEventPolicyVersions
                            .AnyAsync(row => row.TenantId == null && row.VersionNumber > 1)).IsFalse();
                    }
                    if (tenantSettings && !committed)
                    {
                        await Assert.That(await failedObserver.Set<EventDiscoveryRevision>().AsNoTracking()
                            .Where(row => row.TenantId == rows.B).Select(row => row.DisclosureEpoch).SingleOrDefaultAsync())
                            .IsEqualTo(originalDisclosureEpoch);
                        await Assert.That((await failedObserver.Tenants.AsNoTracking()
                            .SingleAsync(row => row.Id == rows.B)).FullName).IsNotEqualTo("Imported foreign tenant");
                        await Assert.That((await failedObserver.TenantSettingOverrides.AsNoTracking()
                            .SingleAsync(row => row.TenantId == rows.B
                                && row.SettingKey == EventSettingDefinitions.RequireApproval.Key)).Value).IsEqualTo("true");
                    }
                    if (tenantDocuments && !committed)
                    {
                        var branding = await failedObserver.TenantSettingsDocuments.AsNoTracking()
                            .SingleOrDefaultAsync(row => row.TenantId == rows.B
                                && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding);
                        if (createBranding)
                            await Assert.That(branding).IsNull();
                        else
                        {
                            await Assert.That(branding).IsNotNull();
                            using JsonDocument content = JsonDocument.Parse(branding!.PayloadJson);
                            await Assert.That(content.RootElement.GetProperty("displayName").GetString())
                                .IsEqualTo("Original branding");
                        }
                        await Assert.That((await failedObserver.PaidEventPolicyVersions.AsNoTracking()
                            .SingleAsync(row => row.TenantId == rows.B && row.IsActive)).VersionNumber).IsEqualTo(1);
                    }
                    throw new InvalidOperationException(
                        nativeFailure is not null
                            ? $"Native import authority: {nativeFailure.SqlState}: {nativeFailure.MessageText}\n{nativeFailure.StackTrace}"
                            : $"Import application failure: {applicationFailure}", exception);
                }
                finally
                {
                    AppDomain.CurrentDomain.FirstChanceException -= trace;
                }
                await Assert.That(failAfterEffect).IsFalse();
                await Assert.That(rejectCeiling).IsFalse();
                await Assert.That(operation.Status).IsEqualTo(ConfigurationImportOperationStatus.Applied);
                await Assert.That(operation.FidelityVerified).IsTrue();
                await using var observer = fixture.CreateDbContext();
                if (originalFederation is not null)
                {
                    await using var federationObserver = FederationManifestStorageSnapshot.OpenMigrator(fixture);
                    await originalFederation.AssertUnchangedAsync(federationObserver,
                        TestContext.Current!.Execution.CancellationToken);
                }
                if (importLegal)
                {
                    var legal = await observer.LegalDocuments.AsNoTracking()
                        .Include(row => row.Versions).ThenInclude(version => version.Sources)
                        .Include(row => row.Publications)
                        .SingleAsync(row => row.Scope == (tenantLegal ? LegalDocumentScope.Tenant : LegalDocumentScope.Instance)
                            && row.TenantId == (tenantLegal ? rows.B : (Guid?)null)
                            && row.Kind == importedLegalKind);
                    await Assert.That(legal.State).IsEqualTo(LegalDocumentLifecycleState.ReviewRequired);
                    await Assert.That(legal.CreatedBy).IsEqualTo((Guid?)rows.UserId);
                    await Assert.That(legal.Versions.Single(version => version.Version == legal.CurrentVersion)
                        .Sources.Single().Markdown)
                        .IsEqualTo("Native legal source");
                    if (originalLegal is null)
                        await Assert.That(legal.Publications).IsEmpty();
                    else
                    {
                        await Assert.That(legal.Id).IsEqualTo(originalLegal.Id);
                        await Assert.That(legal.CurrentVersion).IsEqualTo(2);
                        await Assert.That(legal.UpdatedBy).IsEqualTo((Guid?)rows.UserId);
                        var historical = legal.Versions.Single(version => version.Version == 1);
                        var original = originalLegal.Versions.Single();
                        await Assert.That(historical.Id).IsEqualTo(original.Id);
                        await Assert.That(historical.State).IsEqualTo(original.State);
                        await Assert.That(historical.ContentDigest).IsEqualTo(original.ContentDigest);
                        await Assert.That(legal.Publications.Select(publication => publication.Id).Order())
                            .IsEquivalentTo(originalLegal.Publications.Select(publication => publication.Id).Order());
                    }
                }
                else if (importPolicy)
                {
                    var policy = await observer.PaidEventPolicyVersions.AsNoTracking()
                        .SingleAsync(row => row.TenantId == null && row.IsActive);
                    await Assert.That(policy.VersionNumber).IsEqualTo(2);
                    await Assert.That(policy.IsPaymentsEnabled).IsFalse();
                    await Assert.That(policy.CreatedBy).IsEqualTo((Guid?)rows.UserId);
                    var original = await observer.PaidEventPolicyVersions.AsNoTracking()
                        .SingleAsync(row => row.TenantId == null && row.VersionNumber == 1);
                    await Assert.That(original.IsActive).IsFalse();
                    await Assert.That(original.IsPaymentsEnabled).IsFalse();
                    await Assert.That(original.Id).IsNotEqualTo(policy.Id);
                }
                else if (tenantDocuments)
                {
                    var branding = await observer.TenantSettingsDocuments.AsNoTracking()
                        .SingleAsync(row => row.TenantId == rows.B
                            && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding);
                    using JsonDocument content = JsonDocument.Parse(branding.PayloadJson);
                    await Assert.That(content.RootElement.GetProperty("displayName").GetString())
                        .IsEqualTo("Imported branding");
                    await Assert.That(createBranding ? branding.CreatedBy : branding.UpdatedBy)
                        .IsEqualTo((Guid?)rows.UserId);
                    await Assert.That(branding.ConcurrencyStamp).IsNotEqualTo(Guid.Empty);
                    if (originalBranding is not null)
                    {
                        await Assert.That(branding.Id).IsEqualTo(originalBranding.Id);
                        await Assert.That(branding.CreatedAt.Ticks / 10).IsEqualTo(originalBranding.CreatedAt.Ticks / 10);
                        await Assert.That(branding.CreatedBy).IsEqualTo(originalBranding.CreatedBy);
                        await Assert.That(branding.ConcurrencyStamp).IsNotEqualTo(originalBranding.ConcurrencyStamp);
                    }
                    await Assert.That((await observer.PaidEventPolicyVersions.AsNoTracking()
                        .SingleAsync(row => row.TenantId == rows.B && row.IsActive)).VersionNumber).IsEqualTo(2);
                    await Assert.That((await observer.PaidEventPolicyVersions.AsNoTracking()
                        .SingleAsync(row => row.TenantId == rows.B && row.VersionNumber == 1)).IsActive).IsFalse();
                }
                else if (tenantSettings)
                {
                    await Assert.That(await observer.Set<EventDiscoveryRevision>().AsNoTracking()
                        .Where(row => row.TenantId == rows.B).Select(row => row.DisclosureEpoch).SingleAsync())
                        .IsEqualTo(originalDisclosureEpoch + 1);
                    await Assert.That((await observer.Tenants.AsNoTracking()
                        .SingleAsync(row => row.Id == rows.B)).FullName).IsEqualTo("Imported foreign tenant");
                    await Assert.That((await observer.TenantSettingOverrides.AsNoTracking()
                        .SingleAsync(row => row.TenantId == rows.B
                            && row.SettingKey == EventSettingDefinitions.RequireApproval.Key)).Value).IsEqualTo("false");
                }
                else if (!resourceSettings)
                    await Assert.That((await observer.SystemSettings.SingleAsync(row =>
                        row.SettingKey == EventSettingDefinitions.RequireApproval.Key)).Value).IsEqualTo("true");
                await Assert.That((await observer.Set<ConfigurationImportSession>().AsNoTracking()
                    .SingleAsync(row => row.SessionId == session.SessionId)).State)
                    .IsEqualTo(ConfigurationImportSessionState.Consumed);
                var receipt = await observer.ConfigurationImportOperations.AsNoTracking()
                    .SingleAsync(row => row.Id == operation.OperationId);
                await Assert.That(receipt.EffectOutboxId).IsNotNull();
                OutboxMessage committedEffect = await observer.OutboxMessages.AsNoTracking().SingleAsync(row =>
                    row.Id == receipt.EffectOutboxId && row.AggregateId == receipt.Id);
                await Assert.That(receipt.EffectPayloadDigest).IsEqualTo(Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(committedEffect.Payload ?? string.Empty))));
                if (resourceSettings)
                {
                    await Assert.That(committedEffect.Payload).IsNotNull();
                    var notifications = ConfigurationImportEffectOutbox.ReadNotifications(committedEffect.Payload);
                    await Assert.That(notifications.Any(notification =>
                        notification.Key == EventResourceSettingDefinitions.MaxActiveResources.Key
                        && notification.TenantId is null && notification.ActorUserId == rows.UserId)).IsTrue();
                    await Assert.That(committedEffect.Payload).IsEqualTo(JsonSerializer.Serialize(notifications));
                }
                if (receiptAndRollback)
                {
                    var read = new GetInstanceConfigurationImportReceiptQueryHandler(executor);
                    var history = new ListInstanceConfigurationImportHistoryQueryHandler(executor);
                    var rollback = new CreateInstanceConfigurationRollbackSessionCommandHandler(executor);
                    var loaded = await read.QueryAsync(new(operation.OperationId), default);
                    await Assert.That(loaded.OperationId).IsEqualTo(operation.OperationId);
                    var entries = await history.QueryAsync(new(20), default);
                    await Assert.That(entries.Any(entry => entry.OperationId == operation.OperationId)).IsTrue();
                    var created = await rollback.ExecuteAsync(new(operation.OperationId), default);
                    var rollbackPreview = await executor.PreviewAsync(
                        created.Session.SessionId, created.Session.AccessToken, selection, default);
                    await Assert.That(rollbackPreview.IsApplyReady).IsTrue();
                    if (rollbackCancellation)
                    {
                        var cancel = new CancelInstanceConfigurationImportSessionCommandHandler(executor);
                        var invalidToken = await Assert.That(async () => await cancel.ExecuteAsync(
                            new(created.Session.SessionId, created.Session.AccessToken + "invalid"), default))
                            .Throws<ConfigurationImportSessionException>();
                        await Assert.That(invalidToken.FailureCode).IsEqualTo(ConfigurationImportFailureCodes.TokenInvalid);
                        await cancel.ExecuteAsync(new(created.Session.SessionId, created.Session.AccessToken), default);
                        await cancel.ExecuteAsync(new(created.Session.SessionId, created.Session.AccessToken), default);
                        await using var cancelled = fixture.CreateDbContext();
                        var cancelledSession = await cancelled.Set<ConfigurationImportSession>().AsNoTracking()
                            .SingleAsync(row => row.SessionId == created.Session.SessionId);
                        await Assert.That(cancelledSession.State).IsEqualTo(ConfigurationImportSessionState.Cancelled);
                        await Assert.That(await cancelled.Set<ConfigurationImportStoredArtifact>().AnyAsync(
                            row => row.Id == cancelledSession.ArtifactHandleId)).IsFalse();
                        await Assert.That(await cancelled.Set<ConfigurationImportStoredArtifact>().AnyAsync(
                            row => row.Id == receipt.SnapshotArtifactHandleId)).IsTrue();
                        await Assert.That(await cancelled.ConfigurationImportOperations.AnyAsync(
                            row => row.SessionId == created.Session.SessionId)).IsFalse();
                        await Assert.That(JsonSerializer.Deserialize<bool>((await cancelled.SystemSettings.AsNoTracking()
                            .SingleAsync(row => row.SettingKey == EventSettingDefinitions.RequireApproval.Key)).Value)).IsTrue();
                        await Assert.That(context.TenantFilterTenantId).IsEqualTo((Guid?)rows.A);
                        await Assert.That(await context.TenantSettingOverrides.IgnoreQueryFilters()
                            .AnyAsync(row => row.TenantId == rows.B)).IsFalse();
                        return;
                    }
                    var mismatchedSource = await Assert.That(async () => await executor.ApplyAsync(
                        created.Session.SessionId, created.Session.AccessToken, selection,
                        Guid.CreateVersion7(), null, default)).Throws<ConfigurationImportSessionException>();
                    await Assert.That(mismatchedSource.FailureCode)
                        .IsEqualTo(ConfigurationImportFailureCodes.RollbackUnavailable);
                    await using (var rejectedObserver = fixture.CreateDbContext())
                    {
                        await Assert.That((await rejectedObserver.Set<ConfigurationImportSession>().AsNoTracking()
                            .SingleAsync(row => row.SessionId == created.Session.SessionId)).State)
                            .IsEqualTo(ConfigurationImportSessionState.PreviewReady);
                        await Assert.That(await rejectedObserver.ConfigurationImportOperations.AnyAsync(
                            row => row.SessionId == created.Session.SessionId)).IsFalse();
                    }
                    var restored = await executor.ApplyAsync(
                        created.Session.SessionId, created.Session.AccessToken, selection,
                        created.SourceOperationId, null, default);
                    await Assert.That(restored.Status).IsEqualTo(ConfigurationImportOperationStatus.RolledBack);
                    await Assert.That(restored.FidelityVerified).IsTrue();
                    await using var rollbackObserver = fixture.CreateDbContext();
                    var restoredReceipt = await rollbackObserver.ConfigurationImportOperations.AsNoTracking()
                        .SingleAsync(row => row.Id == restored.OperationId);
                    await Assert.That(restoredReceipt.SourceOperationId).IsEqualTo((Guid?)operation.OperationId);
                    await Assert.That(restoredReceipt.EffectOutboxId).IsNotNull();
                    await Assert.That(JsonSerializer.Deserialize<bool>((await rollbackObserver.SystemSettings.AsNoTracking()
                        .SingleAsync(row => row.SettingKey == EventSettingDefinitions.RequireApproval.Key)).Value)).IsFalse();
                    await Assert.That((await rollbackObserver.Set<ConfigurationImportSession>().AsNoTracking()
                        .SingleAsync(row => row.SessionId == created.Session.SessionId)).State)
                        .IsEqualTo(ConfigurationImportSessionState.Consumed);
                }
            }
            await Assert.That(context.TenantFilterTenantId).IsEqualTo((Guid?)rows.A);
            await Assert.That(await context.TenantSettingOverrides.IgnoreQueryFilters()
                .AnyAsync(row => row.TenantId == rows.B)).IsFalse();
        }
        finally
        {
            await SetStagingWritePermissionsAsync(runtimeLogin, grant: false);
            await SetReadPermissionsAsync(rows.Schema, runtimeLogin, globalConnection.Username!, grant: false);
        }
    }

    private async Task EnrollLegalSourceAsync(string login)
    {
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using var membership = administration.CreateCommand();
        membership.CommandText =
            $"GRANT \"{PostgresConfigurationImportLegalSourceContract.GlobalRole}\", \"{PostgresConfigurationImportLegalMutationContract.GlobalRole}\", \"{PostgresConfigurationImportPublicationPolicyContract.GlobalRole}\", \"{PostgresConfigurationImportScalarSettingContract.GlobalRole}\", \"{PostgresConfigurationImportReceiptContract.GlobalRole}\", \"{PostgresConfigurationImportPaidPolicyContract.GlobalRole}\", \"{PostgresConfigurationImportTenantIdentityContract.GlobalRole}\", \"{PostgresConfigurationImportBrandingContract.GlobalRole}\", \"{PostgresConfigurationImportEventResourceContract.GlobalRole}\", \"{PostgresConfigurationImportTenantSettingContract.GlobalRole}\" TO \"{login}\" WITH INHERIT TRUE, SET FALSE";
        await membership.ExecuteNonQueryAsync();
    }

    private async Task SetStagingWritePermissionsAsync(string login, bool grant)
    {
        await using var metadata = fixture.CreateDbContext();
        string tables = string.Join(", ", new[] {
            typeof(ConfigurationImportSession), typeof(ConfigurationImportStoredArtifact)
        }.Select(type =>
        {
            var entity = metadata.Model.FindEntityType(type)!;
            return $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
        }));
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using var command = administration.CreateCommand();
        command.CommandText = grant
            ? $"GRANT INSERT, UPDATE ON {tables} TO \"{login}\""
            : $"REVOKE INSERT, UPDATE ON {tables} FROM \"{login}\"";
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedExportStateAsync(
        PostgresAuthenticationLookupAuthorityTests.AuthenticationRows rows)
    {
        string key = EventSettingDefinitions.RequireApproval.Key;
        await Assert.That(ConfigurationManifestCatalog.TryGetInstanceSetting(key, out _)).IsTrue();
        await Assert.That(ConfigurationManifestCatalog.TryGetTenantSetting(key, out _)).IsTrue();
        await Assert.That(ConfigurationManifestCatalog.TryGetInstanceSetting(ExcludedKey, out _)).IsFalse();
        await Assert.That(ConfigurationManifestCatalog.TryGetTenantSetting(ExcludedKey, out _)).IsFalse();
        await using var seed = fixture.CreateDbContext();
        Tenant a = await seed.Tenants.SingleAsync(tenant => tenant.Id == rows.A);
        Tenant b = await seed.Tenants.SingleAsync(tenant => tenant.Id == rows.B);
        a.Slug = TenantASlug;
        b.Slug = TenantBSlug;
        seed.Add(PaidEventPolicyVersion.CreateDefaultInstance());
        foreach (var settingValue in new[] { (Key: key, Value: false), (Key: ExcludedKey, Value: true) })
        {
            SystemSetting? setting = await seed.SystemSettings.SingleOrDefaultAsync(
                row => row.SettingKey == settingValue.Key);
            if (setting is null)
            {
                setting = new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = settingValue.Key,
                    Value = JsonSerializer.Serialize(settingValue.Value),
                    CreatedAt = PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.Now
                };
                seed.SystemSettings.Add(setting);
            }
            setting.Value = JsonSerializer.Serialize(settingValue.Value);
            setting.ValueType = EventSettingDefinitions.RequireApproval.ValueType;
            setting.IsLocked = false;
        }
        foreach (Guid tenantId in new[] { rows.A, rows.B, rows.Inactive })
        {
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                SettingKey = key,
                Value = JsonSerializer.Serialize(tenantId != rows.A),
                CreatedAt = PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.Now
            });
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                SettingKey = ExcludedKey,
                Value = JsonSerializer.Serialize(true),
                CreatedAt = PostgresAuthenticationLookupAuthorityTests.AuthenticationRows.Now
            });
        }
        await seed.SaveChangesAsync();
    }

    private ServiceProvider CreateServices(
        PrimaryDatabaseConnectionOptions runtime, NpgsqlConnectionStringBuilder global, Guid? actor = null,
        Action<Guid>? failAfterEffect = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = runtime.Host,
            ["Database:Port"] = runtime.Port?.ToString(CultureInfo.InvariantCulture),
            ["Database:Database"] = runtime.Database,
            ["Database:Schema"] = runtime.Schema,
            ["Database:Runtime:Username"] = runtime.Username,
            ["Database:Runtime:Password"] = runtime.Password,
            ["Database:Global:Username"] = global.Username,
            ["Database:Global:Password"] = global.Password,
            ["Database:TlsMode"] = "Disabled",
            ["Authentication:Provider"] = "Local",
            ["Authorization:Provider"] = "local",
            ["SecretProvider:Provider"] = "Environment"
        };
        foreach (var role in new[]
        {
            PrimaryDatabaseRole.Migrator,
            PrimaryDatabaseRole.EmailPolicyInstanceMutation, PrimaryDatabaseRole.EmailPolicyTenantMutation,
            PrimaryDatabaseRole.InstanceKeyRead, PrimaryDatabaseRole.InstanceKeyWrite,
            PrimaryDatabaseRole.InstanceKeyIssuance, PrimaryDatabaseRole.PlatformKeyReport
        })
        {
            var options = fixture.GetNativeQueueOptions(role, runtime.Schema);
            settings[$"Database:{role}:Username"] = options.Username;
            settings[$"Database:{role}:Password"] = options.Password;
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            EnvironmentName = Environments.Production
        }).Services;
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHybridCache();
        services.AddMetrics();
        services.AddSingleton<ProjectionMetrics>();
        services.AddSingleton<BusinessMetrics>();
        var environment = new ManifestWebEnvironment();
        services.AddSingleton<IWebHostEnvironment>(environment);
        services.AddApiAuthentication(configuration, environment, skipAuthorityWarmup: true);
        services.ConfigureApplicationServices(configuration);
        services.ConfigureInfrastructureServices(configuration);
        services.AddSingleton(_ => SecretAuthorityAcquisition.OpenLocal(
            SecretAuthoritySelection.Select(configuration, "Testing", SecretConsumerRole.Api),
            testingAuthority: configuration));
        services.AddNativeSecretResolution();
        services.ConfigurePersistenceServices(configuration, skipLookupCacheInitializer: true);
        if (failAfterEffect is not null)
            services.AddScoped<IConfigurationImportEffectOutboxRepository>(scope =>
                new FailAfterPersistedEffectOutbox(
                    ActivatorUtilities.CreateInstance<OutboxRepository>(scope), failAfterEffect));
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.AddHttpContextAccessor();
        if (actor is { } userId)
            services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "fixture"))
                }
            });
        services.AddScoped<ITenantResolverService, TenantResolverService>();
        services.AddScoped<ITenantContext, TenantContext>();
        return services.BuildIsolatedServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static Task<string> CurrentLoginAsync(ExploreDbContext context) =>
        context.Database.SqlQueryRaw<string>("SELECT current_user::text AS \"Value\"").SingleAsync();

    private sealed class ManifestWebEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = typeof(Program).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FailAfterPersistedEffectOutbox(
        IConfigurationImportEffectOutboxRepository inner,
        Action<Guid> persisted) : IConfigurationImportEffectOutboxRepository
    {
        public async Task<OutboxMessage> Create(OutboxMessage message)
        {
            await inner.Create(message);
            OutboxMessage stored = await inner.GetByIdAsync(message.Id, default)
                ?? throw new InvalidOperationException("The real import effect was not persisted.");
            persisted(stored.Id);
            throw new InvalidOperationException("Injected failure after persisted import effect.");
        }

        public Task<OutboxMessage?> GetByIdAsync(Guid messageId, CancellationToken cancellationToken) =>
            inner.GetByIdAsync(messageId, cancellationToken);

        public Task<IReadOnlyList<OutboxMessage>> GetPendingImportEffectsAsync(
            int batchSize, CancellationToken cancellationToken) =>
            inner.GetPendingImportEffectsAsync(batchSize, cancellationToken);

        public Task<DateTime?> TryClaimForProcessing(Guid id, DateTime claimedAt, CancellationToken cancellationToken) =>
            inner.TryClaimForProcessing(id, claimedAt, cancellationToken);

        public Task<bool> MarkAsCompleted(Guid id, DateTime processingLeaseExpiresAt, CancellationToken cancellationToken) =>
            inner.MarkAsCompleted(id, processingLeaseExpiresAt, cancellationToken);

        public Task<OutboxFailureTransition> MarkAsFailed(
            Guid id, DateTime processingLeaseExpiresAt, string error, bool isRetryable,
            int retryDelaySeconds, DateTime failedAt, CancellationToken cancellationToken) =>
            inner.MarkAsFailed(id, processingLeaseExpiresAt, error, isRetryable, retryDelaySeconds, failedAt, cancellationToken);
    }

    private async Task SetReadPermissionsAsync(string schema, string runtimeLogin, string globalLogin, bool grant)
    {
        await using var metadata = fixture.CreateDbContext();
        Type[] instanceTypes =
        [
            typeof(Tenant), typeof(TenantStatus), typeof(SystemSetting),
            typeof(PaidEventPolicyVersion), typeof(PaidEventPolicyAllowedOrganizerKind),
            typeof(PaidEventPolicyAllowedCurrency), typeof(PaidEventPolicyRefundProtection),
            typeof(PaidEventPolicyCurrencyRiskLimitRow), typeof(ConfigurationManifestOperation)
        ];
        string instanceTables = string.Join(", ", instanceTypes.Select(type =>
        {
            var entity = metadata.Model.FindEntityType(type)!;
            return $"\"{entity.GetSchema()}\".\"{entity.GetTableName()}\"";
        }));
        await using var administration = new NpgsqlConnection(fixture.ConnectionString);
        await administration.OpenAsync();
        await using var command = administration.CreateCommand();
        // SELECT grants do not confer table ownership, superuser, BYPASSRLS or tenant authority.
        command.CommandText = grant
            ? $"""
                GRANT USAGE ON SCHEMA "{schema}" TO "{globalLogin}";
                GRANT SELECT ON ALL TABLES IN SCHEMA "{schema}" TO "{runtimeLogin}";
                GRANT SELECT ON {instanceTables} TO "{globalLogin}";
                """
            : $"""
                REVOKE SELECT ON ALL TABLES IN SCHEMA "{schema}" FROM "{runtimeLogin}";
                REVOKE SELECT ON {instanceTables} FROM "{globalLogin}";
                REVOKE USAGE ON SCHEMA "{schema}" FROM "{globalLogin}";
                """;
        await command.ExecuteNonQueryAsync();
    }
}
