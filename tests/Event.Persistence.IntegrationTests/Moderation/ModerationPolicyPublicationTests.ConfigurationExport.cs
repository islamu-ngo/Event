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
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Full_current_state_export_carries_instance_and_foreign_tenant_definitions_in_actual_artifact()
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        var request = RebindExportRequest(database.Request(), database.Scope.Parent!);
        request = request with { ExpectedQualificationRevision = 1 };
        await Assert.That((await database.PublishAsync(request)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var context = database.Context();
        await SeedExportPaidPolicyAsync(context);
        var result = await CurrentExportReader(database, context).ReadAsync(
            ConfigurationManifestExportView.Overrides, CancellationToken.None);
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
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        await database.PublishAsync(database.Request());
        await DemoteExportAdministratorAsync(database);
        await using (var foreignContext = database.Context())
        {
            foreignContext.TenantContext = new ExportTenantContext(foreign.TenantId);
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
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        context.ModerationQualificationEpochs.Remove(await context.ModerationQualificationEpochs.SingleAsync());
        context.ModerationHostBindings.Remove(await context.ModerationHostBindings.SingleAsync());
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await SeedExportPaidPolicyAsync(context);
        var result = await CurrentExportReader(database, context).ReadAsync(
            ConfigurationManifestExportView.Overrides, CancellationToken.None);
        using var json = JsonDocument.Parse(result.Utf8Json);

        await Assert.That(json.RootElement.GetProperty("spec").GetProperty("instance")
            .TryGetProperty("moderation", out _)).IsFalse();
        foreach (var tenant in json.RootElement.GetProperty("spec").GetProperty("tenants").EnumerateArray())
            await Assert.That(tenant.GetProperty("spec").TryGetProperty("moderation", out _)).IsFalse();
    }

    [Test]
    public async Task Instance_definition_export_reads_other_tenant_without_changing_request_authority()
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        await using var context = database.Context();
        var priorTrustDomain = context.ModerationTrustDomainId;
        var exported = await Exporter(database, context).ReadAsync(
            ConfigurationImportTarget.ForInstance(), CancellationToken.None);

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
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        await database.PublishAsync(database.Request());
        await DemoteExportAdministratorAsync(database);
        await using var context = database.Context();
        var exported = await Exporter(database, context).ReadAsync(
            ConfigurationImportTarget.ForTenant(database.TenantId), CancellationToken.None);

        await Assert.That(exported.Instance).IsNull();
        await Assert.That(exported.Tenants.Count).IsEqualTo(1);
        await Assert.That(exported.Tenants.ContainsKey(database.TenantId)).IsTrue();
        await Assert.That(exported.Tenants.ContainsKey(foreign.TenantId)).IsFalse();
        await Assert.That(async () => await Exporter(database, context).ReadAsync(
            ConfigurationImportTarget.ForInstance(), CancellationToken.None))
            .Throws<ModerationConfigurationImportException>();
        await Assert.That(async () => await Exporter(database, context).ReadAsync(
            ConfigurationImportTarget.ForTenant(foreign.TenantId), CancellationToken.None))
            .Throws<ModerationConfigurationImportException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Definition_export_omits_unselected_scope_without_installing_native_defaults(bool instance)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var exported = await Exporter(database, context).ReadAsync(instance
            ? ConfigurationImportTarget.ForInstance()
            : ConfigurationImportTarget.ForTenant(database.TenantId), CancellationToken.None);

        await Assert.That(exported.Instance).IsNull();
        await Assert.That(exported.Tenants.Count).IsEqualTo(0);
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    public async Task Definition_export_retains_all_profiles_and_thirteen_recipe_dimensions_without_private_coordinates()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request(Enum.GetValues<ModerationReferenceProfileId>());
        request = request with { Proposed = request.Proposed with { SupportedProfiles = [] } };
        await Assert.That((await database.PublishAsync(request)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var context = database.Context();
        var exported = await Exporter(database, context).ReadAsync(
            ConfigurationImportTarget.ForTenant(database.TenantId), CancellationToken.None);
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
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        await using (var changed = database.Context())
        {
            changed.TenantContext = new ExportTenantContext(foreign.TenantId);
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
        await Assert.That(async () => await Exporter(database, context).ReadAsync(
            ConfigurationImportTarget.ForInstance(), CancellationToken.None))
            .Throws<ModerationConfigurationImportException>();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
        await Assert.That(context.ModerationTrustDomainId).IsNull();
    }

    [Test]
    public async Task Definition_export_rejects_machine_callers_even_with_native_human_role()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var exporter = new ModerationConfigurationExportService(
            database.Writer(context, machineCaller: true), new EfCoreUnitOfWork(context));
        await Assert.That(async () => await exporter.ReadAsync(
            ConfigurationImportTarget.ForInstance(), CancellationToken.None))
            .Throws<ModerationConfigurationImportException>();
    }

    [Test]
    public async Task Native_definition_export_requires_serializable_original_transaction_and_clean_tracker()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var repository = database.Writer(context);
        await Assert.That(async () => await repository.AcquireConfigurationExportAsync(
            ConfigurationImportTarget.ForInstance())).Throws<ModerationConfigurationImportException>();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        (await context.Tenants.SingleAsync(row => row.Id == database.TenantId)).FullName = "Unflushed foreign work";
        await Assert.That(async () => await repository.AcquireConfigurationExportAsync(
            ConfigurationImportTarget.ForInstance())).Throws<ModerationConfigurationImportException>();
    }

    private static ModerationConfigurationExportService Exporter(Database database, ExploreDbContext context) =>
        new(database.Writer(context), new EfCoreUnitOfWork(context));

    private static async Task<(Guid TenantId, Guid PublicationId)> PublishExportTenantAsync(Database database)
    {
        var tenantId = Guid.CreateVersion7();
        var epochId = Guid.CreateVersion7();
        await using var context = database.Context();
        context.TenantContext = new ExportTenantContext(tenantId);
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
        return CurrentExportReader(context, moderation, new ExportTenantReadScopes(context, moderation));
    }

    private static ConfigurationManifestCurrentStateReader CurrentExportReader(ExploreDbContext context,
        ModerationConfigurationExportService moderation, IConfigurationManifestTenantReadScopeFactory tenantReadScopes)
    {
        var unit = new EfCoreUnitOfWork(context);
        var mutation = new RelationalSettingMutationLock(context, unit);
        // Overrides must read native stored values, not invoke either effective-value resolver.
        return new(new TenantRepository(context), SystemSettingRepository.CreateReadOnly(context, mutation),
            TenantSettingRepository.CreateReadOnly(context, mutation), new TenantSettingsDocumentRepository(context),
            null!, null!, new PaidEventPolicyRepository(context), new ConfigurationManifestOperationRepository(context),
            moderation, new LegalDocumentRepository(context), unit, tenantReadScopes,
            new ConfigurationImportLegalSourceReader(context));
    }

    private sealed class ExportTenantReadScopes(ExploreDbContext owner, ModerationConfigurationExportService moderation)
        : IConfigurationManifestTenantReadScopeFactory
    {
        public ValueTask<IConfigurationManifestTenantReadScope> CreateAsync(Guid tenantId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
            var options = new DbContextOptionsBuilder<ExploreDbContext>(
                (DbContextOptions<ExploreDbContext>)owner.GetService<IDbContextOptions>())
                .UseSqlite(owner.Database.GetDbConnection().ConnectionString).Options;
            var context = new ExploreDbContext(options) { TenantContext = new ExportTenantContext(tenantId) };
            try
            {
                context.BindTenantLease();
                var services = new ServiceCollection()
                    .AddSingleton(CurrentExportReader(context, moderation, this)).BuildServiceProvider();
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
