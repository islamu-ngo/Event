using System.Collections.Immutable;
using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Handlers.Queries;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Full_current_state_export_carries_instance_and_foreign_tenant_definitions_in_actual_artifact()
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        var foreign = await PublishExportTenantAsync(database);
        var request = RebindExportRequest(database.Request(), database.Scope.Parent!);
        request = request with { ExpectedQualificationRevision = 1 };
        await Assert.That((await database.PublishAsync(request)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        var result = await ReadCombinedManifestAsync(database, context);
        var manifest = ConfigurationPortabilityJsonCodec.ParseConfigurationManifest(result.Utf8Json);

        await Assert.That(manifest.Spec.Instance.Moderation).IsNotNull();
        await Assert.That(manifest.Spec.Tenants.Single(row => row.Metadata.Name == $"export-{foreign.TenantId:N}")
            .Spec.Moderation).IsNotNull();
        await Assert.That(manifest.Spec.Tenants.Single(row => row.Metadata.Name == $"publication-{database.TenantId:N}")
            .Spec.Moderation).IsNull();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
    }

    [Test]
    public async Task Tenant_package_handler_works_without_global_authority_despite_malformed_foreign_definition()
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        var foreign = await PublishExportTenantAsync(database);
        await database.PublishAsync(database.Request());
        await DemoteExportAdministratorAsync(database);
        await using (var seedOwner = database.Context())
        await using (var foreignContext = DetachedContext(seedOwner, new ExportTenantContext(foreign.TenantId)))
        {
            foreignContext.ModerationTrustDomainId = database.Scope.TrustDomainId;
            await foreignContext.Set<ModerationPolicyPublicationPlan>().ExecuteDeleteAsync();
        }
        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        var handler = new ExportTenantConfigurationPackageQueryHandler(CurrentExportReader(database, context));
        var result = await handler.QueryAsync(new ExportTenantConfigurationPackageQuery(
            database.TenantId, ConfigurationManifestExportView.Overrides), CancellationToken.None);
        var package = ConfigurationPortabilityJsonCodec.ParseTenantConfigurationPackage(result.Utf8Json);

        await Assert.That(package.Spec.Moderation).IsNotNull();
        await Assert.That(package.Metadata.Name).IsEqualTo($"publication-{database.TenantId:N}");
        await Assert.That(package.Metadata.Export!.AuthorityScope)
            .IsEqualTo(ConfigurationManifestExportMetadataValues.TenantAuthorityScope);
        var json = System.Text.Encoding.UTF8.GetString(result.Utf8Json.Span);
        await Assert.That(json.Contains($"export-{foreign.TenantId:N}", StringComparison.Ordinal)).IsFalse();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
    }

    [Test]
    public async Task Current_state_export_without_native_installation_omits_optional_section()
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        await using var context = database.Context();
        context.ModerationQualificationEpochs.Remove(await context.ModerationQualificationEpochs.SingleAsync());
        context.ModerationHostBindings.Remove(await context.ModerationHostBindings.SingleAsync());
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await SeedExportPaidPolicyAsync(context);
        var result = await ReadCombinedManifestAsync(database, context);
        using var json = JsonDocument.Parse(result.Utf8Json);

        await Assert.That(json.RootElement.GetProperty("spec").GetProperty("instance")
            .TryGetProperty("moderation", out _)).IsFalse();
        foreach (var tenant in json.RootElement.GetProperty("spec").GetProperty("tenants").EnumerateArray())
            await Assert.That(tenant.GetProperty("spec").TryGetProperty("moderation", out _)).IsFalse();
    }

    [Test]
    public async Task Instance_definition_export_reads_other_tenant_without_changing_request_authority()
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        var foreign = await PublishExportTenantAsync(database);
        await using var context = database.Context();
        var priorTrustDomain = context.ModerationTrustDomainId;
        var exported = await ReadCombinedDefinitionsAsync(database, context);

        await Assert.That(exported.Tenants.Count).IsEqualTo(1);
        await Assert.That(exported.Tenants.ContainsKey(foreign.TenantId)).IsTrue();
        await Assert.That(exported.Instance).IsNull();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
        await Assert.That(context.ModerationTrustDomainId).IsEqualTo(priorTrustDomain);
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
    }

    [Test]
    public async Task Tenant_definition_export_never_reads_foreign_selected_graph_or_instance_section()
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        var foreign = await PublishExportTenantAsync(database);
        await database.PublishAsync(database.Request());
        await DemoteExportAdministratorAsync(database);
        await using var context = database.Context();
        var exported = await new TenantlessPackageModeration(database, context)
            .ReadAsync(database.TenantId, CancellationToken.None);

        await Assert.That(exported.Instance).IsNull();
        await Assert.That(exported.Tenants.Count).IsEqualTo(1);
        await Assert.That(exported.Tenants.ContainsKey(database.TenantId)).IsTrue();
        await Assert.That(exported.Tenants.ContainsKey(foreign.TenantId)).IsFalse();
        await Assert.That(exported.PublicationIds.Count).IsEqualTo(1);
        await Assert.That(async () => await Exporter(database, context).ReadAsync(
            ConfigurationImportTarget.ForInstance(), ConfigurationManifestExportScope.InstanceAndTenants, CancellationToken.None))
            .Throws<ModerationConfigurationImportException>();
        // Tenant targets no longer read on a request-tenant context; packages use the tenantless owner.
        await Assert.That(async () => await Exporter(database, context).ReadAsync(
            ConfigurationImportTarget.ForTenant(database.TenantId), null, CancellationToken.None))
            .Throws<ModerationConfigurationImportException>();
        await Assert.That(async () => await Exporter(database, context)
            .ReadTenantPackageAsync(database.TenantId, CancellationToken.None))
            .Throws<ModerationConfigurationImportException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Definition_export_omits_unselected_scope_without_installing_native_defaults(bool instance)
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        await using var context = database.Context();
        var exported = instance
            ? await ReadCombinedDefinitionsAsync(database, context)
            : await new TenantlessPackageModeration(database, context).ReadAsync(
                database.TenantId, CancellationToken.None);

        await Assert.That(exported.Instance).IsNull();
        await Assert.That(exported.Tenants.Count).IsEqualTo(0);
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    public async Task Definition_export_retains_all_profiles_and_thirteen_recipe_dimensions_without_private_coordinates()
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        var request = database.Request(Enum.GetValues<ModerationReferenceProfileId>());
        request = request with { Proposed = request.Proposed with { SupportedProfiles = [] } };
        await Assert.That((await database.PublishAsync(request)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var context = database.Context();
        var exported = await new TenantlessPackageModeration(database, context).ReadAsync(
            database.TenantId, CancellationToken.None);
        var document = exported.Tenants[database.TenantId];
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document,
            ConfigurationPortabilityJsonContext.Default.ConfigurationModerationDocumentV1);
        var native = ModerationConfigurationDocumentProjection.Parse(bytes);

        await Assert.That(native.Profiles.Count).IsEqualTo(27);
        await Assert.That(native.Profiles.Select(profile => profile.Bundle).Distinct().Count()).IsEqualTo(10);
        await Assert.That(native.SupportedProfiles.Count).IsEqualTo(0);
        using var json = JsonDocument.Parse(bytes);
        foreach (var profile in json.RootElement.GetProperty("profiles").EnumerateArray())
            foreach (var recipe in profile.GetProperty("recipes").EnumerateArray())
                await Assert.That(recipe.EnumerateObject().Count()).IsEqualTo(14);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        foreach (var id in new[] { database.AdminId, database.HostId, database.TenantId,
                     database.EpochId, request.OperationId, request.Proposed.PolicyId, request.Proposed.PublicationId })
            await Assert.That(text.Contains(id.ToString(), StringComparison.OrdinalIgnoreCase)).IsFalse();
        foreach (var forbidden in new[] { "publishedActive", "inventoryFingerprint", "authoritySourceId",
                     "principalId", "evidence", "jurisdiction", "hostBindingId", "profileId" })
            await Assert.That(json.RootElement.TryGetProperty(forbidden, out _)).IsFalse();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
    }

    [Test]
    [Arguments("recipe")]
    [Arguments("digest")]
    [Arguments("source")]
    public async Task Instance_definition_export_fails_closed_on_selected_foreign_graph_corruption(string corruption)
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        var foreign = await PublishExportTenantAsync(database);
        await using (var seedOwner = database.Context())
        await using (var changed = DetachedContext(seedOwner, new ExportTenantContext(foreign.TenantId)))
        {
            changed.ModerationTrustDomainId = database.Scope.TrustDomainId;
            if (corruption == "recipe")
                await changed.Set<ModerationPolicyPublicationPlan>().ExecuteDeleteAsync();
            else if (corruption == "digest")
                await changed.ModerationPolicyPublications.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.DeclaredStateDigest, new string('0', 64)));
            else
                await changed.ModerationQualificationEpochs.ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.Revision, 1));
        }
        await using var context = database.Context();
        await Assert.That(async () => await ReadCombinedDefinitionsAsync(database, context))
            .Throws<ModerationConfigurationImportException>();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
        await Assert.That(context.ModerationTrustDomainId).IsNull();
    }

    [Test]
    public async Task Definition_export_rejects_machine_callers_even_with_native_human_role()
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        await using var context = database.Context();
        var exporter = new ModerationConfigurationExportService(
            database.Writer(context, machineCaller: true), new EfCoreUnitOfWork(context));
        await Assert.That(async () => await exporter.ReadAsync(
            ConfigurationImportTarget.ForTenant(database.TenantId), null, CancellationToken.None))
            .Throws<ModerationConfigurationImportException>();
    }

    [Test]
    public async Task Tenant_package_rejects_moderation_that_changes_across_the_runtime_snapshot()
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        await database.PublishAsync(database.Request());
        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        var moderation = Exporter(database, context);
        var changing = new RepublishedBetweenReads(new TenantlessPackageModeration(database, context));
        var reader = CurrentExportReader(database, context, moderation,
            new ExportTenantReadScopes(database, context, moderation), changing);

        await Assert.That(async () => await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(
                token => reader.ReadTenantInCurrentTransactionAsync(
                    database.TenantId, ConfigurationManifestExportView.Overrides, token), CancellationToken.None))
            .Throws<ModerationConfigurationImportException>();
        await Assert.That(changing.Reads).IsEqualTo(2);
    }

    [Test]
    public async Task Native_definition_export_requires_serializable_original_transaction_and_clean_tracker()
    {
        await using var database = await Database.CreateAsync(physicalFile: true);
        await using var owner = database.Context();
        await using var context = DetachedContext(owner);
        var repository = database.Writer(context);
        await Assert.That(async () => await repository.AcquireTenantPackageExportAsync(
            database.TenantId, CancellationToken.None))
            .Throws<ModerationConfigurationImportException>();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        (await context.Tenants.IgnoreQueryFilters().SingleAsync(row => row.Id == database.TenantId)).FullName =
            "Unflushed foreign work";
        await Assert.That(async () => await repository.AcquireTenantPackageExportAsync(
            database.TenantId, CancellationToken.None))
            .Throws<ModerationConfigurationImportException>();
    }

    private static Task<ConfigurationManifestExportResult> ReadCombinedManifestAsync(Database database, ExploreDbContext context) =>
        WithInstanceReadOwnerAsync(database, context, HumanAdministratorCommitPurpose.ConfigurationManifestExport,
            (owner, exporter, token) => CurrentExportReader(database, owner, exporter,
                    new ExportTenantReadScopes(database, owner, exporter))
                .ReadInCurrentTransactionAsync(ConfigurationManifestExportView.Overrides,
                    ConfigurationManifestExportScope.InstanceAndTenants, token));

    private static Task<ModerationConfigurationExport> ReadCombinedDefinitionsAsync(Database database, ExploreDbContext context) =>
        WithInstanceReadOwnerAsync(database, context, HumanAdministratorCommitPurpose.ConfigurationManifestExport,
            (_, exporter, token) => exporter.ReadPublicManifestInCurrentTransactionAsync(
                ConfigurationManifestExportScope.InstanceAndTenants, token));

    private static async Task<T> WithInstanceReadOwnerAsync<T>(Database database, ExploreDbContext parent,
        HumanAdministratorCommitPurpose purpose,
        Func<ExploreDbContext, ModerationConfigurationExportService, CancellationToken, Task<T>> read)
    {
        var clock = new ImportExportOwnerClock();
        await using var human = ImportHuman(database, parent, new EphemeralDataProtectionProvider(), clock);
        await human.InitializeAsync();
        var subject = await PolicyMutationExecutionScopeFactory.ResolveSubjectAsync(human.Services, CancellationToken.None)
            ?? throw new InvalidOperationException("The definition witness requires an authenticated human subject.");
        var lifetime = await PolicyMutationExecutionScopeFactory.ResolveLifetimeAsync(human.Services, CancellationToken.None)
            ?? throw new InvalidOperationException("The definition witness requires a validated credential lifetime.");
        var requests = human.Services.GetRequiredService<IHttpContextAccessor>();
        await using var owner = await human.Services.GetRequiredService<IGlobalExploreDbContextFactory>()
            .CreateDbContextAsync(CancellationToken.None);
        if (purpose == HumanAdministratorCommitPurpose.ConfigurationImport)
            owner.BindConfigurationInstanceImportOwner();
        var lease = new ExploreDbContextExecutionLease();
        lease.Bind(owner, null);
        lease.BindHuman(subject, purpose, null, lifetime);
        lease.BindOriginalCredential(requests.HttpContext!);
        var authority = new ExternalApiKeyIssuanceAuthority(owner, requests, lease.HumanScope!);
        var repository = new ModerationPolicyPublicationRepository(owner, human.Admin, human.Machine,
            database.Inventory, null, clock, lease, requests, authority);
        var unit = new EfCoreUnitOfWork(owner);
        var exporter = new ModerationConfigurationExportService(repository, unit);
        return await unit.ExecuteSerializableAsync(token => read(owner, exporter, token), CancellationToken.None);
    }

    private static ModerationConfigurationExportService Exporter(Database database, ExploreDbContext context) =>
        new(database.Writer(context), new EfCoreUnitOfWork(context));

    private static async Task<(Guid TenantId, Guid PublicationId)> PublishExportTenantAsync(Database database)
    {
        var tenantId = Guid.CreateVersion7();
        var epochId = Guid.CreateVersion7();
        await using var owner = database.Context();
        await using var context = DetachedContext(owner, new ExportTenantContext(tenantId));
        var status = await context.TenantStatuses.SingleAsync(row => row.IsActiveState);
        var tenant = new Tenant
        {
            Id = tenantId,
            Slug = $"export-{tenantId:N}",
            FullName = "Foreign definition owner",
            TenantStatusId = status.Id,
            TenantStatus = status
        };
        context.Tenants.Add(tenant);
        var host = await context.ModerationHostBindings.SingleAsync();
        context.ModerationQualificationEpochs.Add(ModerationQualificationEpoch.Initialize(epochId, host, tenant));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var scope = AuthorityScope.Tenant(database.Scope.Parent!, tenantId);
        var request = RebindExportRequest(database.Request(), scope) with { ExpectedAuthoritySourceId = epochId };
        await Assert.That((await database.Writer(context).PublishAsync(request)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        return (tenantId, request.Proposed.PublicationId);
    }

    private static ModerationPolicyPublicationRequest RebindExportRequest(
        ModerationPolicyPublicationRequest request, AuthorityScope scope)
    {
        ModerationRuleReference Rebind(ModerationRuleReference reference) =>
            ModerationRuleReference.Create(scope.Id, reference.Code, reference.Revision);
        return request with
        {
            Proposed = request.Proposed with
            {
                Jurisdiction = scope,
                Classes = request.Proposed.Classes.Select(row => row with { Reference = Rebind(row.Reference) }).ToImmutableArray(),
                Reasons = request.Proposed.Reasons.Select(row => row with
                { Reference = Rebind(row.Reference), Class = Rebind(row.Class) }).ToImmutableArray(),
                Profiles = request.Proposed.Profiles.Select(row => row with
                { Rule = Rebind(row.Rule), Reason = Rebind(row.Reason) }).ToImmutableArray(),
                SupportedProfiles = []
            }
        };
    }

    private static ConfigurationManifestCurrentStateReader CurrentExportReader(
        Database database, ExploreDbContext context)
    {
        var moderation = Exporter(database, context);
        return CurrentExportReader(database, context, moderation, new ExportTenantReadScopes(database, context, moderation));
    }

    private static ConfigurationManifestCurrentStateReader CurrentExportReader(Database database, ExploreDbContext context,
        ModerationConfigurationExportService moderation, IConfigurationManifestTenantReadScopeFactory tenantReadScopes,
        IConfigurationManifestPackageModerationReader? packageModeration = null, EfCoreUnitOfWork? unitOfWork = null)
    {
        var unit = unitOfWork ?? new EfCoreUnitOfWork(context);
        var mutation = new RelationalSettingMutationLock(context, unit);
        // Overrides must read native stored values, not invoke either effective-value resolver.
        return new(new TenantRepository(context), SystemSettingRepository.CreateReadOnly(context, mutation),
            TenantSettingRepository.CreateReadOnly(context, mutation), new TenantSettingsDocumentRepository(context),
            null!, new PaidEventPolicyRepository(context), new ConfigurationManifestOperationRepository(context),
            moderation, new LegalDocumentRepository(context), unit, tenantReadScopes,
            new ConfigurationImportLegalSourceReader(context), new PermittedPackageAuthority(),
            packageModeration ?? new TenantlessPackageModeration(database, context));
    }

    /// <summary>A republish mints a new publication id; the second read observes one.</summary>
    private sealed class RepublishedBetweenReads(IConfigurationManifestPackageModerationReader inner)
        : IConfigurationManifestPackageModerationReader
    {
        public int Reads { get; private set; }

        public async Task<ModerationConfigurationExport> ReadAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(tenantId, cancellationToken);
            return ++Reads == 1 ? read
                : new ModerationConfigurationExport(read.Instance, read.Tenants, [.. read.PublicationIds, Guid.CreateVersion7()]);
        }
    }

    /// <summary>A foreign tenant's draft references a tenant row only a context leased to that tenant may see.</summary>
    private static async Task SeedForeignLegalDraftAsync(ExploreDbContext owner, Guid tenantId)
    {
        await using var context = DetachedContext(owner, new ExportTenantContext(tenantId));
        context.LegalDocuments.Add(LegalImportDraft(tenantId));
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Opens a context on its own connection to the owner's physical database with the tenant lease
    /// bound at construction; a bound lease is immutable, so tenant selection never happens later.
    /// </summary>
    private static ExploreDbContext DetachedContext(ExploreDbContext owner, ITenantContext? tenant = null)
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>(
            (DbContextOptions<ExploreDbContext>)owner.GetService<IDbContextOptions>())
            .UseSqlite(owner.Database.GetDbConnection().ConnectionString).Options;
        var context = new ExploreDbContext(options) { TenantContext = tenant };
        try
        {
            context.BindTenantLease();
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    /// <summary>Mirrors the production Global package moderation owner on a fresh tenantless context.</summary>
    private sealed class TenantlessPackageModeration(Database database, ExploreDbContext owner)
        : IConfigurationManifestPackageModerationReader
    {
        public async Task<ModerationConfigurationExport> ReadAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            await using var context = DetachedContext(owner);
            return await ConfigurationManifestPackageModerationReader.ReadSnapshotAsync(
                context, Exporter(database, context), tenantId, cancellationToken);
        }
    }

    /// <summary>
    /// SQLite owner fixtures carry no HTTP credential. Fresh Global package authority is exercised on
    /// PostgreSQL by PostgresManifestExportAuthorityTests; these cases cover source ownership only.
    /// </summary>
    private sealed class PermittedPackageAuthority : IConfigurationManifestPackageAuthority
    {
        public Task RequireAsync(Guid tenantId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ExportTenantReadScopes(Database database, ExploreDbContext owner,
        ModerationConfigurationExportService moderation)
        : IConfigurationManifestTenantReadScopeFactory
    {
        public ValueTask<IConfigurationManifestTenantReadScope> CreateAsync(Guid tenantId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
            var context = DetachedContext(owner, new ExportTenantContext(tenantId));
            try
            {
                // Mirrors the production scope: one unit of work over the scope's own Runtime context
                // owns the serializable snapshot the scoped reader executes inside.
                var unit = new EfCoreUnitOfWork(context);
                var services = new ServiceCollection()
                    .AddSingleton<IUnitOfWork>(unit)
                    .AddSingleton(CurrentExportReader(database, context, moderation, this, unitOfWork: unit))
                    .BuildServiceProvider();
                return ValueTask.FromResult<IConfigurationManifestTenantReadScope>(new ExportTenantReadScope(context, services));
            }
            catch
            {
                context.Dispose();
                throw;
            }
        }
    }

    private sealed class ExportTenantReadScope(ExploreDbContext context, ServiceProvider services)
        : IConfigurationManifestTenantReadScope
    {
        public IServiceProvider Services => services;
        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await context.DisposeAsync();
        }
    }

    private static async Task SeedExportPaidPolicyAsync(ExploreDbContext context)
    {
        if (await new PaidEventPolicyRepository(context).GetActiveInstanceAsync(CancellationToken.None) is null)
        {
            await new PaidEventPolicyRepository(context).AddAsync(
                PaidEventPolicyVersion.CreateDefaultInstance(), CancellationToken.None);
            await context.SaveChangesAsync();
        }
        context.ChangeTracker.Clear();
    }

    private static async Task DemoteExportAdministratorAsync(Database database)
    {
        await using var context = database.Context();
        context.PlatformUserRoles.Remove(await context.PlatformUserRoles.SingleAsync(row => row.UserId == database.AdminId));
        var membership = await context.TenantUsers.SingleAsync(row => row.UserId == database.AdminId);
        var role = await context.Roles.SingleAsync(row => row.Id == (int)RoleEnum.TenantAdmin);
        context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = database.TenantId,
            Tenant = null!,
            TenantUserId = membership.Id,
            TenantUser = membership,
            RoleId = role.Id,
            Role = role,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = Now.UtcDateTime
        });
        await context.SaveChangesAsync();
    }

    private sealed record ExportTenantContext(Guid TenantId) : ITenantContext;
}
