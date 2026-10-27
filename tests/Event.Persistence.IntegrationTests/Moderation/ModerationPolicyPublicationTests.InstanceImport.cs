using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Instance_import_writes_target_owned_draft_without_changing_request_or_transaction()
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        await using var context = database.Context();
        var writer = database.Writer(context);
        var unit = new EfCoreUnitOfWork(context);
        var destination = await unit.ExecuteSerializableAsync(token =>
            writer.AcquireInstanceConfigurationImportDestinationAsync(foreign.TenantId, null, token));
        var document = ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(destination.Current!)!);
        var preview = ModerationConfigurationImportAdapter.Compose(destination, document, database.Inventory);
        var tenantContext = context.TenantContext;
        var connection = context.Database.GetDbConnection();
        var publication = await unit.ExecuteSerializableAsync(async token =>
        {
            var transaction = context.Database.CurrentTransaction;
            var result = await writer.ApplyInstanceConfigurationImportInCurrentTransactionAsync(
                Guid.CreateVersion7(), foreign.TenantId, document, preview, token);
            await Assert.That(ReferenceEquals(transaction, context.Database.CurrentTransaction)).IsTrue();
            await Assert.That(ReferenceEquals(connection, context.Database.GetDbConnection())).IsTrue();
            await Assert.That(ReferenceEquals(tenantContext, context.TenantContext)).IsTrue();
            await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
            await Assert.That(context.IsTenantFilterBypassed).IsFalse();
            await Assert.That(context.ModerationTrustDomainId).IsNull();
            return result;
        });
        await Assert.That(publication.TenantId).IsEqualTo(foreign.TenantId);
        await Assert.That(publication.Jurisdiction.Id).IsEqualTo(foreign.TenantId);
        await Assert.That(publication.PrincipalId).IsEqualTo(database.AdminId);
        await Assert.That(publication.AuthoritySourceId).IsEqualTo(destination.Epoch.Id);
        await Assert.That(publication.AuthorityRevision).IsEqualTo(2);
        await Assert.That(publication.QualificationRevision).IsEqualTo(3);
        await Assert.That(publication.PreviousPublicationId).IsEqualTo(foreign.PublicationId);
        await Assert.That(publication.AdoptedRevision).IsEqualTo(2);
        await Assert.That(publication.PublishedActive).IsFalse();
        await Assert.That(publication.Evidence.Count).IsEqualTo(0);
        await Assert.That(publication.Profiles.Single().ProfileId)
            .IsEqualTo(destination.Current!.Profiles.Single().ProfileId);
        await Assert.That(ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(publication)!).Profiles[0].Recipes
            .SequenceEqual(document.Profiles[0].Recipes)).IsTrue();
        var current = await unit.ExecuteSerializableAsync(token =>
            writer.AcquireInstanceConfigurationImportDestinationAsync(foreign.TenantId, foreign.PublicationId, token));
        await Assert.That(current.Selection!.LatestPublicationId).IsEqualTo(publication.Id);
        await Assert.That(current.Selection.ActivePublicationId).IsNull();
        await Assert.That(current.Baseline!.Id).IsEqualTo(foreign.PublicationId);
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1);
    }

    [Test]
    public async Task Instance_import_does_not_relax_the_request_tenant_destination_contract()
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(async () => await database.Writer(context).AcquireConfigurationImportDestinationAsync(
            ConfigurationImportTarget.ForTenant(foreign.TenantId), null))
            .Throws<ModerationConfigurationImportException>();
    }

    [Test]
    [Arguments("machine")]
    [Arguments("tenant-admin")]
    [Arguments("deleted-human")]
    [Arguments("source-inactive")]
    [Arguments("target-inactive")]
    [Arguments("missing-target")]
    [Arguments("bypass")]
    public async Task Instance_import_requires_current_native_root_and_both_active_tenants(string failure)
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        if (failure == "tenant-admin") await DemoteExportAdministratorAsync(database);
        await using var context = database.Context();
        if (failure == "deleted-human")
        {
            (await context.Users.SingleAsync(row => row.Id == database.AdminId)).IsDeleted = true;
            await context.SaveChangesAsync();
        }
        if (failure is "source-inactive" or "target-inactive")
        {
            var id = failure == "source-inactive" ? database.TenantId : foreign.TenantId;
            var status = await context.TenantStatuses.FirstAsync(row => !row.IsActiveState);
            (await context.Tenants.SingleAsync(row => row.Id == id)).TenantStatusId = status.Id;
            await context.SaveChangesAsync();
        }
        context.ChangeTracker.Clear();
        if (failure == "bypass") context.EnableTenantFilterBypass("Instance import rejection witness");
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(async () => await database.Writer(context, machineCaller: failure == "machine")
            .AcquireInstanceConfigurationImportDestinationAsync(
                failure == "missing-target" ? Guid.CreateVersion7() : foreign.TenantId, null))
            .Throws<ModerationConfigurationImportException>();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
    }

    [Test]
    public async Task Instance_import_requires_original_serializable_transaction_and_clean_tracker()
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        await using var context = database.Context();
        var writer = database.Writer(context);
        await Assert.That(async () => await writer.AcquireInstanceConfigurationImportDestinationAsync(
            foreign.TenantId, null)).Throws<ModerationConfigurationImportException>();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        (await context.Tenants.SingleAsync(row => row.Id == database.TenantId)).FullName = "Unowned pending section";
        await Assert.That(async () => await writer.AcquireInstanceConfigurationImportDestinationAsync(
            foreign.TenantId, null)).Throws<ModerationConfigurationImportException>();
    }

    [Test]
    [Arguments("epoch")]
    [Arguments("root")]
    [Arguments("history")]
    [Arguments("artifact")]
    [Arguments("principal")]
    public async Task Instance_apply_reacquires_authority_history_and_complete_preview_binding(string change)
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        await using var context = database.Context();
        var writer = database.Writer(context);
        var unit = new EfCoreUnitOfWork(context);
        var destination = await unit.ExecuteSerializableAsync(token =>
            writer.AcquireInstanceConfigurationImportDestinationAsync(foreign.TenantId, null, token));
        var document = ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(destination.Current!)!);
        var preview = ModerationConfigurationImportAdapter.Compose(
            change == "principal" ? destination with { PrincipalId = Guid.CreateVersion7() } : destination,
            document, database.Inventory);
        if (change == "root") await DemoteExportAdministratorAsync(database);
        if (change is "epoch" or "history")
        {
            await using var changed = database.Context();
            changed.TenantContext = new ExportTenantContext(foreign.TenantId);
            changed.ModerationTrustDomainId = database.Scope.TrustDomainId;
            if (change == "epoch")
            {
                var epoch = await changed.ModerationQualificationEpochs.SingleAsync();
                epoch.Advance(epoch.Revision);
                await changed.SaveChangesAsync();
            }
            else
                await changed.Set<ModerationPolicyPublicationPlan>().ExecuteDeleteAsync();
        }
        if (change == "artifact")
            document = new(document.SchemaVersion, document.CatalogueRevision, document.Modes,
                [document.Classes[0] with { Meaning = "Different approved meaning" }],
                document.Reasons, document.Profiles, document.SupportedProfiles);
        await Assert.That(async () => await unit.ExecuteSerializableAsync(token =>
            writer.ApplyInstanceConfigurationImportInCurrentTransactionAsync(
                Guid.CreateVersion7(), foreign.TenantId, document, preview, token)))
            .Throws<ModerationConfigurationImportException>();
        await using var reader = database.Context();
        await Assert.That(await reader.ModerationPolicyPublications.IgnoreQueryFilters(["ModerationAuthority"])
            .CountAsync(row => row.TenantId == foreign.TenantId)).IsEqualTo(1);
    }

    [Test]
    public async Task Failed_second_instance_target_rolls_back_first_draft_and_previously_flushed_section()
    {
        await using var database = await Database.CreateAsync();
        var first = await PublishExportTenantAsync(database);
        var second = await PublishExportTenantAsync(database);
        await using var context = database.Context();
        var writer = database.Writer(context);
        var unit = new EfCoreUnitOfWork(context);
        var destinations = await unit.ExecuteSerializableAsync(async token => new[]
        {
            await writer.AcquireInstanceConfigurationImportDestinationAsync(first.TenantId, null, token),
            await writer.AcquireInstanceConfigurationImportDestinationAsync(second.TenantId, null, token)
        });
        var documents = destinations.Select(destination => ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(destination.Current!)!)).ToArray();
        var previews = destinations.Select((destination, index) =>
            ModerationConfigurationImportAdapter.Compose(destination, documents[index], database.Inventory)).ToArray();
        var tenant = await context.Tenants.SingleAsync(row => row.Id == database.TenantId);
        var originalName = tenant.FullName;
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            tenant.FullName = "Earlier flushed import section";
            await context.SaveChangesAsync();
            await writer.ApplyInstanceConfigurationImportInCurrentTransactionAsync(
                Guid.CreateVersion7(), first.TenantId, documents[0], previews[0]);
            await Assert.That(await context.ModerationPolicyPublications.IgnoreQueryFilters(["ModerationAuthority"])
                .CountAsync(row => row.TenantId == first.TenantId)).IsEqualTo(2);
            await Assert.That(async () => await writer.ApplyInstanceConfigurationImportInCurrentTransactionAsync(
                Guid.Empty, second.TenantId, documents[1], previews[1]))
                .Throws<ModerationConfigurationImportException>();
            await Assert.That(async () => await transaction.CommitAsync()).Throws<InvalidOperationException>();
        }
        await using var reader = database.Context();
        await Assert.That((await reader.Tenants.SingleAsync(row => row.Id == database.TenantId)).FullName)
            .IsEqualTo(originalName);
        await Assert.That(await reader.ModerationPolicyPublications.IgnoreQueryFilters(["ModerationAuthority"])
            .CountAsync(row => row.TenantId == first.TenantId || row.TenantId == second.TenantId)).IsEqualTo(2);
        var epochs = await reader.ModerationQualificationEpochs.IgnoreQueryFilters(["ModerationQualification"])
            .Where(row => row.TenantId == first.TenantId || row.TenantId == second.TenantId).ToArrayAsync();
        foreach (var epoch in epochs) await Assert.That(epoch.Revision).IsEqualTo(2);
    }

    [Test]
    public async Task Instance_import_flush_failure_rolls_back_target_graph_and_earlier_section()
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        ModerationConfigurationImportDestination destination;
        await using (var previewContext = database.Context())
            destination = await new EfCoreUnitOfWork(previewContext).ExecuteSerializableAsync(token =>
                database.Writer(previewContext).AcquireInstanceConfigurationImportDestinationAsync(foreign.TenantId, null, token));
        var document = ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(destination.Current!)!);
        var preview = ModerationConfigurationImportAdapter.Compose(destination, document, database.Inventory);
        await using var context = database.Context(new RejectSavedGraph());
        var originalName = (await context.Tenants.SingleAsync(row => row.Id == database.TenantId)).FullName;
        await Assert.That(async () => await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
        {
            (await context.Tenants.SingleAsync(row => row.Id == database.TenantId, token)).FullName = "Earlier section";
            await context.SaveChangesAsync(token);
            return await database.Writer(context).ApplyInstanceConfigurationImportInCurrentTransactionAsync(
                Guid.CreateVersion7(), foreign.TenantId, document, preview, token);
        })).Throws<PublicationWriteInterrupted>();
        await using var reader = database.Context();
        await Assert.That((await reader.Tenants.SingleAsync(row => row.Id == database.TenantId)).FullName).IsEqualTo(originalName);
        await Assert.That(await reader.ModerationPolicyPublications.IgnoreQueryFilters(["ModerationAuthority"])
            .CountAsync(row => row.TenantId == foreign.TenantId)).IsEqualTo(1);
    }

    [Test]
    public async Task Instance_import_rejects_baseline_from_another_target()
    {
        await using var database = await Database.CreateAsync();
        var first = await PublishExportTenantAsync(database);
        var second = await PublishExportTenantAsync(database);
        await using var context = database.Context();
        await Assert.That(async () => await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(token =>
            database.Writer(context).AcquireInstanceConfigurationImportDestinationAsync(
                first.TenantId, second.PublicationId, token))).Throws<ModerationConfigurationImportException>();
    }

    [Test]
    public async Task Instance_import_rejects_request_tenant_drift_after_native_graph_flush()
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        ModerationConfigurationImportDestination destination;
        await using (var previewContext = database.Context())
            destination = await new EfCoreUnitOfWork(previewContext).ExecuteSerializableAsync(token =>
                database.Writer(previewContext).AcquireInstanceConfigurationImportDestinationAsync(foreign.TenantId, null, token));
        var document = ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(destination.Current!)!);
        var preview = ModerationConfigurationImportAdapter.Compose(destination, document, database.Inventory);
        await using var context = database.Context(new InstanceImportRequestDrift(foreign.TenantId));
        await Assert.That(async () => await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(token =>
            database.Writer(context).ApplyInstanceConfigurationImportInCurrentTransactionAsync(
                Guid.CreateVersion7(), foreign.TenantId, document, preview, token)))
            .Throws<ModerationConfigurationImportException>();
        await using var reader = database.Context();
        await Assert.That(await reader.ModerationPolicyPublications.IgnoreQueryFilters(["ModerationAuthority"])
            .CountAsync(row => row.TenantId == foreign.TenantId)).IsEqualTo(1);
    }

    private sealed class InstanceImportRequestDrift(Guid targetTenantId) : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ExploreDbContext context
                && context.ChangeTracker.Entries<ModerationPolicyPublication>().Any())
                context.TenantContext = new ExportTenantContext(targetTenantId);
            return ValueTask.FromResult(result);
        }
    }
}
