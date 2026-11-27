using System.Data.Common;
using System.Diagnostics;
using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Domain.Enums;
using Explore.Domain;
using Explore.Domain.Settings.Definitions;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments(ConfigurationManifestExportView.Overrides)]
    [Arguments(ConfigurationManifestExportView.Portable)]
    public async Task Default_instance_export_reads_instance_state_without_access_to_tenant_sources(
        ConfigurationManifestExportView view)
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        var publication = RebindExportRequest(database.Request(), database.Scope.Parent!);
        await Assert.That((await database.PublishAsync(publication)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using (var seed = database.Context())
        {
            seed.LegalDocuments.AddRange(
                LegalDocument.CreateImportedDraft(LegalDocumentScope.Instance, null,
                    LegalDocumentKind.TermsOfService, LegalDocumentAudience.Public,
                    [LegalDocumentLocalizedSource.Create("en", "Instance terms", "Instance meaning", "Instance content.")],
                    null, "instance-export-witness", false, Now.UtcDateTime),
                LegalImportDraft(database.TenantId));
            await seed.SaveChangesAsync();
            await SeedExportPaidPolicyAsync(seed);
        }
        await using (var tenantSeed = database.Context())
        {
            tenantSeed.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(), TenantId = database.TenantId, Tenant = null!,
                SettingKey = EventSettingDefinitions.RequireApproval.Key, Value = "true", CreatedAt = Now.UtcDateTime
            });
            await tenantSeed.SaveChangesAsync();
        }

        var boundary = new InstanceExportStorageBoundary(
            database.Scope.Parent!.Id, publication.Proposed.PublicationId, database.EpochId, database.TenantId);
        await using var context = database.Context(boundary);
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(),
            new ImportExportOwnerClock());
        await human.InitializeAsync();
        await Assert.That(await human.Admin.IsInstanceAdminAsync()).IsTrue();
        using var observation = boundary.ObserveRequest(
            human.Services.GetRequiredService<IHttpContextAccessor>());
        boundary.Armed = true;

        var artifact = await human.Services.GetRequiredService<IConfigurationManifestExportExecutor>()
            .ExportAsync(view, ConfigurationManifestExportScope.Instance, CancellationToken.None);
        var manifest = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(artifact.Utf8Json);

        await Assert.That(boundary.DeniedSource).IsNull();
        await Assert.That(boundary.SawTenantlessLegalRead).IsTrue();
        await Assert.That(manifest.Metadata.Export!.AuthorityScope)
            .IsEqualTo(ConfigurationManifestExportMetadataValues.InstanceAuthorityScope);
        await Assert.That(manifest.Metadata.Export!.EffectiveValuesFlattened)
            .IsEqualTo(view == ConfigurationManifestExportView.Portable);
        await Assert.That(manifest.Spec.Tenants).IsEmpty();
        await Assert.That(manifest.Spec.Instance.Moderation).IsNotNull();
        await Assert.That(manifest.Spec.Instance.LegalDocuments["TermsOfService"].Localizations.Single().Markdown)
            .IsEqualTo("Instance content.");
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
    }

    [Test]
    public async Task Combined_export_beyond_tenant_limit_is_rejected_as_too_large()
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        var publication = RebindExportRequest(database.Request(), database.Scope.Parent!);
        await Assert.That((await database.PublishAsync(publication)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        int limit = Explore.Application.Features.ConfigurationManifest.Validation
            .ConfigurationManifestValidator.MaximumTenantCount;
        await using (var policySeed = database.Context())
            await SeedExportPaidPolicyAsync(policySeed);
        // Other tenants are created by the tenantless Global owner, not the tenant-bound Runtime context.
        await using (var seed = database.MaintenanceContext())
        {
            int active = await seed.Tenants.IgnoreQueryFilters()
                .CountAsync(tenant => tenant.TenantStatusId == (int)TenantStatusEnum.Active);
            seed.Tenants.AddRange(Enumerable.Range(0, limit + 1 - active).Select(index => new Tenant
            {
                Id = Guid.CreateVersion7(),
                Slug = $"overflow-{index:D3}",
                FullName = $"Overflow {index}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            }));
            await seed.SaveChangesAsync();
            await Assert.That(await seed.Tenants.IgnoreQueryFilters()
                .CountAsync(tenant => tenant.TenantStatusId == (int)TenantStatusEnum.Active)).IsEqualTo(limit + 1);
        }

        await using var context = database.Context();
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(),
            new ImportExportOwnerClock());
        await human.InitializeAsync();
        await Assert.That(await human.Admin.IsInstanceAdminAsync()).IsTrue();

        await Assert.ThrowsAsync<ConfigurationManifestExportTooLargeException>(() =>
            human.Services.GetRequiredService<IConfigurationManifestExportExecutor>().ExportAsync(
                ConfigurationManifestExportView.Overrides, ConfigurationManifestExportScope.InstanceAndTenants,
                CancellationToken.None));
    }

    // Interdict actual provider commands, not artifact shape or calls to repository methods.
    private sealed class InstanceExportStorageBoundary(
        Guid instanceId, Guid instancePublicationId, Guid instanceEpochId, Guid foreignTenantId)
        : DbCommandInterceptor, IObserver<DiagnosticListener>,
            IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly List<IDisposable> subscriptions = [];
        private IHttpContextAccessor? requests;
        private HttpContext? originalRequest;
        internal bool Armed { get; set; }
        internal string? DeniedSource { get; private set; }
        internal bool SawTenantlessLegalRead { get; private set; }

        internal IDisposable ObserveRequest(IHttpContextAccessor accessor)
        {
            requests = accessor;
            originalRequest = accessor.HttpContext
                ?? throw new InvalidOperationException("The export witness requires its authenticated request.");
            subscriptions.Add(DiagnosticListener.AllListeners.Subscribe(this));
            return this;
        }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == DbLoggerCategory.Name)
                subscriptions.Add(listener.Subscribe(this,
                    name => name == RelationalEventId.CommandExecuting.Name));
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is CommandEventData command
                && ReferenceEquals(requests?.HttpContext, originalRequest))
                Check(command.Command, command);
        }

        public void OnCompleted() { }
        public void OnError(Exception error) =>
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();

        public void Dispose()
        {
            Armed = false;
            foreach (IDisposable subscription in subscriptions)
                subscription.Dispose();
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Check(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Check(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Check(command, eventData);
            return ValueTask.FromResult(result);
        }

        private void Check(DbCommand command, CommandEventData eventData)
        {
            if (!Armed || eventData.Context is not ExploreDbContext context)
                return;

            string sql = command.CommandText;
            if (context.TenantFilterTenantId is null && Touches<LegalDocument>()
                && FiltersToNull<LegalDocument>(nameof(LegalDocument.TenantId)))
                SawTenantlessLegalRead = true;
            if (Touches<Tenant>() || Touches<TenantStatus>() || Touches<TenantSetting>()
                || Touches<TenantSettingsDocument>())
                Deny("tenant catalogue, fence or configuration");
            if (Touches<LegalDocument>() && !FiltersToNull<LegalDocument>(nameof(LegalDocument.TenantId)))
                Deny("legal documents outside exact instance scope");
            if (Touches<PaidEventPolicyVersion>()
                && !FiltersToNull<PaidEventPolicyVersion>(nameof(PaidEventPolicyVersion.TenantId)))
                Deny("paid policy outside exact instance scope");
            if ((Touches<ModerationPolicySelection>() || Touches<ModerationPolicyPublication>())
                && (HasCoordinate(foreignTenantId)
                    || !(FiltersToNull<ModerationPolicyPublication>(nameof(ModerationPolicyPublication.JurisdictionTenantId))
                        || HasCoordinate(instanceId) || HasCoordinate(instancePublicationId))))
                Deny("moderation definitions outside exact instance scope");
            // Instance publications retain provenance in their exact source epoch. Do not allow
            // a scan of tenant epochs or a fence on a different tenant's source.
            if (Touches<ModerationQualificationEpoch>() && !HasCoordinate(instanceEpochId))
                Deny("moderation qualification outside the instance publication source");
            bool Touches<TEntity>() => sql.Contains($"\"{Table(typeof(TEntity)).Name}\"", StringComparison.Ordinal);
            bool FiltersToNull<TEntity>(string property)
            {
                var table = Table(typeof(TEntity));
                string? column = context.Model.FindEntityType(typeof(TEntity))!.FindProperty(property)!
                    .GetColumnName(table);
                return sql.Contains($"\"{column}\" IS NULL", StringComparison.OrdinalIgnoreCase);
            }
            StoreObjectIdentifier Table(Type type)
            {
                var entity = context.Model.FindEntityType(type)!;
                return StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            }
            bool HasCoordinate(Guid id) =>
                sql.Contains(id.ToString("D"), StringComparison.OrdinalIgnoreCase)
                || command.Parameters.Cast<DbParameter>().Any(parameter =>
                    parameter.Value is Guid key && key == id
                    || parameter.Value is string text && text.Contains(id.ToString("D"), StringComparison.OrdinalIgnoreCase));
            void Deny(string source)
            {
                DeniedSource ??= source;
                throw new InvalidOperationException($"Instance export attempted to access {source}.");
            }
        }
    }

    [Test]
    public async Task Native_instance_import_snapshot_exports_authorized_definitions_without_preparing_the_parent_tenant()
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        var publication = RebindExportRequest(database.Request(), database.Scope.Parent!);
        await Assert.That((await database.PublishAsync(publication)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(),
            new ImportExportOwnerClock());
        await human.InitializeAsync();
        var parent = human.Services.GetRequiredService<IHumanCommitScopePreparation>();
        await Assert.That(parent.Subject).IsNull();

        var artifact = await human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>()
            .ReadSnapshotAsync(ConfigurationManifestExportView.Overrides,
                ConfigurationManifestExportScope.InstanceAndTenants, CancellationToken.None);
        var manifest = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(artifact.Utf8Json);

        await Assert.That(manifest.Spec.Instance.Moderation).IsNotNull();
        await Assert.That(manifest.Spec.Tenants.Single(row => row.Metadata.Name == $"export-{foreign.TenantId:N}")
            .Spec.Moderation).IsNotNull();
        await Assert.That(manifest.Spec.Tenants.Single(row => row.Metadata.Name == $"publication-{database.TenantId:N}")
            .Spec.Moderation).IsNull();
        await Assert.That(parent.Subject).IsNull();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(context.ModerationTrustDomainId).IsNull();
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
    }

    [Test]
    [Arguments("unauthenticated", false)]
    [Arguments("machine", false)]
    [Arguments("tenant-administrator", false)]
    [Arguments("expired-credential", false)]
    [Arguments("unauthenticated", true)]
    [Arguments("machine", true)]
    [Arguments("tenant-administrator", true)]
    [Arguments("expired-credential", true)]
    public async Task Native_instance_import_snapshot_denies_callers_without_current_platform_human_authority(
        string caller, bool publicExport)
    {
        await using var database = await Database.CreateAsync(physicalFile: publicExport);
        await database.PublishAsync(database.Request());
        if (caller == "tenant-administrator")
            await DemoteExportAdministratorAsync(database);
        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        var clock = new ImportExportOwnerClock();
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(),
            clock);
        await human.InitializeAsync(authenticate: caller != "unauthenticated");
        if (caller == "expired-credential")
        {
            var principal = human.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User;
            var lifetime = await human.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                .ObserveAsync(principal, CancellationToken.None)
                ?? throw new InvalidOperationException("The regression requires the genuine validated credential lifetime.");
            clock.Current = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
        }
        if (caller == "machine")
        {
            var principal = human.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User;
            var identity = (ClaimsIdentity)principal.Identity!;
            identity.AddClaims([
                new(ApiAuthenticationClaimTypes.ApiKeyId, Guid.CreateVersion7().ToString("N")),
                new(ApiAuthenticationClaimTypes.OwnerType, nameof(ExternalApiKeyOwnerType.InstanceAdmin)),
                new(ApiAuthenticationClaimTypes.OwnerId, database.AdminId.ToString("D"))
            ]);
            await Assert.That(human.Machine.IsMachineCaller).IsTrue();
        }

        try
        {
            if (publicExport)
                await human.Services.GetRequiredService<IConfigurationManifestExportExecutor>()
                    .ExportAsync(ConfigurationManifestExportView.Overrides,
                        ConfigurationManifestExportScope.Instance, CancellationToken.None);
            else
                await human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>()
                    .ReadSnapshotAsync(ConfigurationManifestExportView.Overrides,
                        ConfigurationManifestExportScope.InstanceAndTenants, CancellationToken.None);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            || caller != "expired-credential" && exception is ModerationConfigurationImportException
            { Outcome: ModerationPolicyPublicationOutcome.Denied or ModerationPolicyPublicationOutcome.Unauthenticated })
        {
            await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
            await Assert.That(context.IsTenantFilterBypassed).IsFalse();
            return;
        }
        throw new InvalidOperationException("Instance definitions must not be disclosed without current platform human authority.");
    }

    [Test]
    public async Task Null_tenant_global_context_without_native_import_selection_cannot_export_definitions()
    {
        await using var database = await Database.CreateAsync();
        await database.PublishAsync(database.Request());
        await using var context = database.Context();
        var clock = new ImportExportOwnerClock();
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(), clock);
        await human.InitializeAsync();
        await using var global = await human.Services.GetRequiredService<IGlobalExploreDbContextFactory>()
            .CreateDbContextAsync(CancellationToken.None);
        await Assert.That(global.TenantFilterTenantId).IsNull();
        var publications = new ModerationPolicyPublicationRepository(global, human.Admin, human.Machine,
            database.Inventory, null, clock);
        var exporter = new ModerationConfigurationExportService(publications, new EfCoreUnitOfWork(global));

        await Assert.That(async () => await exporter.ReadAsync(
            ConfigurationImportTarget.ForInstance(), ConfigurationManifestExportScope.InstanceAndTenants,
            CancellationToken.None))
            .Throws<ModerationConfigurationImportException>();
        await Assert.That(global.IsTenantFilterBypassed).IsFalse();
        await Assert.That(global.TenantFilterTenantId).IsNull();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Credential_expiry_after_the_final_native_authority_wait_prevents_instance_disclosure(bool publicExport)
    {
        await using var database = await Database.CreateAsync(physicalFile: publicExport);
        await database.PublishAsync(database.Request());
        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        var clock = new ImportExportOwnerClock();
        var expiry = new FinalExportExpiry();
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(), clock,
            services =>
            {
                var descriptor = services.Last(row => row.ServiceType == typeof(IInstanceAdministratorCommitAuthority));
                var nativeFactory = descriptor.ImplementationFactory
                    ?? throw new InvalidOperationException("The regression requires the native scoped authority factory.");
                services.Remove(descriptor);
                services.AddScoped<IInstanceAdministratorCommitAuthority>(provider =>
                    new ExpiringAfterNativeAuthority((IInstanceAdministratorCommitAuthority)nativeFactory(provider),
                        provider.GetRequiredService<ExploreDbContext>(), clock, expiry));
            });
        await human.InitializeAsync();
        var principal = human.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User;
        expiry.Lifetime = await human.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(principal, CancellationToken.None)
            ?? throw new InvalidOperationException("The regression requires the original validated credential lifetime.");

        await Assert.That(async () =>
        {
            if (publicExport)
                await human.Services.GetRequiredService<IConfigurationManifestExportExecutor>()
                    .ExportAsync(ConfigurationManifestExportView.Overrides,
                        ConfigurationManifestExportScope.Instance, CancellationToken.None);
            else
                await human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>()
                    .ReadSnapshotAsync(ConfigurationManifestExportView.Overrides,
                        ConfigurationManifestExportScope.InstanceAndTenants, CancellationToken.None);
        }).Throws<UnauthorizedAccessException>();
        await Assert.That(expiry.ReachedFinalAuthority).IsTrue();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("expiry")]
    [Arguments("principal")]
    public async Task Credential_change_during_a_later_snapshot_read_prevents_returning_the_artifact(string change)
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        await database.PublishAsync(database.Request());
        var clock = new ImportExportOwnerClock();
        var boundary = new LateSnapshotCredentialChange(clock, change);
        await using var context = database.Context(boundary);
        await SeedExportPaidPolicyAsync(context);
        await using var human = ImportHuman(database, context, new EphemeralDataProtectionProvider(), clock);
        await human.InitializeAsync();
        boundary.Request = human.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
        boundary.Lifetime = await human.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(boundary.Request.User, CancellationToken.None)
            ?? throw new InvalidOperationException("The regression requires the original validated credential lifetime.");

        await Assert.That(async () => await human.Services.GetRequiredService<IConfigurationInstanceImportExecutor>()
            .ReadSnapshotAsync(ConfigurationManifestExportView.Overrides,
                ConfigurationManifestExportScope.InstanceAndTenants, CancellationToken.None))
            .Throws<UnauthorizedAccessException>();
        await Assert.That(boundary.ReachedLaterRead).IsTrue();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private sealed class LateSnapshotCredentialChange(ImportExportOwnerClock clock, string change) : DbCommandInterceptor
    {
        internal HttpContext? Request { get; set; }
        internal HumanAuthenticationLifetime? Lifetime { get; set; }
        internal bool ReachedLaterRead { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (!ReachedLaterRead && Request is { } request && Lifetime is { } lifetime
                && eventData.Context is ExploreDbContext
                {
                    IsConfigurationInstanceImportOwner: true,
                    ModerationTrustDomainId: null
                } owner
                && owner.Model.FindEntityType(typeof(LegalDocument))?.GetTableName() is { } table
                && command.CommandText.Contains(table, StringComparison.Ordinal))
            {
                ReachedLaterRead = true;
                if (change == "expiry")
                    clock.Current = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
                else
                    request.User = new ClaimsPrincipal(request.User.Identities);
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FinalExportExpiry
    {
        internal HumanAuthenticationLifetime? Lifetime { get; set; }
        internal bool ReachedFinalAuthority { get; set; }
    }

    private sealed class ExpiringAfterNativeAuthority(
        IInstanceAdministratorCommitAuthority inner, ExploreDbContext owner,
        ImportExportOwnerClock clock, FinalExportExpiry expiry) : IInstanceAdministratorCommitAuthority
    {
        public async Task<bool> IsInstanceAdministratorForCommitAsync(Guid principalId, CancellationToken cancellationToken)
        {
            bool allowed = await inner.IsInstanceAdministratorForCommitAsync(principalId, cancellationToken);
            if (allowed && owner.ModerationTrustDomainId is not null)
            {
                var lifetime = expiry.Lifetime
                    ?? throw new InvalidOperationException("Original credential custody was not observed.");
                expiry.ReachedFinalAuthority = true;
                clock.Current = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
            }
            return allowed;
        }
    }
}
