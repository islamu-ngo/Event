using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Portable_export_reads_complete_current_meaning_without_native_authority_or_readiness(bool supported)
    {
        await using var database = await Database.CreateAsync(implementedInventory: supported);
        var request = database.Request();
        await Assert.That((await database.PublishAsync(request)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var context = database.Context();
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context),
            new EfCoreUnitOfWork(context), database.Inventory);

        var document = await adapter.ExportAsync(
            ConfigurationImportTarget.ForTenant(database.TenantId), CancellationToken.None);

        await Assert.That(document).IsNotNull();
        await Assert.That(document!.Profiles[0].Rule.Code).IsEqualTo(request.Proposed.Profiles[0].Rule.Code);
        await Assert.That(document.Profiles[0].Rule.Revision).IsEqualTo(request.Proposed.Profiles[0].Rule.Revision);
        await Assert.That(document.SupportedProfiles.Count).IsEqualTo(request.Proposed.SupportedProfiles.Length);
        var bytes = ModerationConfigurationDocumentProjection.Serialize(document);
        var parsed = ModerationConfigurationDocumentProjection.Parse(bytes);
        await Assert.That(parsed.Profiles[0].Recipes.SequenceEqual(document.Profiles[0].Recipes)).IsTrue();
        using var exported = System.Text.Json.JsonDocument.Parse(bytes);
        var json = exported.RootElement;
        await Assert.That(json.GetProperty("profiles")[0].TryGetProperty("id", out _)).IsFalse();
        await Assert.That(json.TryGetProperty("jurisdiction", out _)).IsFalse();
        await Assert.That(json.TryGetProperty("hostBindingId", out _)).IsFalse();
        await Assert.That(json.TryGetProperty("publishedActive", out _)).IsFalse();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await database.AssertCountsAsync(1, 1, 2);
    }

    [Test]
    public async Task Portable_export_of_an_authorized_unselected_destination_does_not_invent_a_default_policy()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        await using var context = database.Context();
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context),
            new EfCoreUnitOfWork(context), database.Inventory);

        var document = await adapter.ExportAsync(
            ConfigurationImportTarget.ForTenant(database.TenantId), CancellationToken.None);

        await Assert.That(document).IsNull();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    public async Task Portable_validate_only_acquires_native_destination_without_writing()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var document = ImportDocument(database);
        await using var context = database.Context();
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context),
            new EfCoreUnitOfWork(context), database.Inventory);
        var preview = await adapter.ValidateOnlyAsync(ConfigurationImportTarget.ForTenant(database.TenantId),
            document, null, CancellationToken.None);

        await Assert.That(preview.CanApply).IsTrue();
        await Assert.That(preview.Mapping.Context.HostBindingId).IsEqualTo(database.HostId);
        await Assert.That(preview.Mapping.Context.Bounds.Jurisdiction).IsEqualTo(database.Scope);
        await Assert.That(preview.Mapping.Context.Current).IsNull();
        await Assert.That(preview.Diff.HasConflicts).IsFalse();
        foreach (var definition in document.Classes.Select(value => (Section: "classes", Reference: value.Reference))
                     .Concat(document.Reasons.Select(value => (Section: "reasons", Reference: value.Reference)))
                     .Concat(document.Profiles.Select(value => (Section: "profiles", Reference: value.Rule))))
            await Assert.That(preview.Diff.Changes.Any(change => change.Section == definition.Section
                && change.Reference == definition.Reference && change.Kind == ModerationConfigurationChangeKind.Added)).IsTrue();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    public async Task Portable_apply_joins_original_transaction_and_outer_failure_removes_every_write()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var document = ImportDocument(database);
        await using var context = database.Context();
        var unit = new EfCoreUnitOfWork(context);
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context), unit, database.Inventory);
        var target = ConfigurationImportTarget.ForTenant(database.TenantId);
        var preview = await adapter.PreviewAsync(target, document, null, CancellationToken.None);
        var originalName = (await context.Tenants.SingleAsync(row => row.Id == database.TenantId)).FullName;
        await Assert.That(async () => await unit.ExecuteSerializableAsync<int>(async token =>
        {
            var originalTransaction = context.Database.CurrentTransaction;
            var tenant = await context.Tenants.SingleAsync(row => row.Id == database.TenantId, token);
            tenant.FullName = "Outer import write";
            await context.SaveChangesAsync(token);
            var publication = await adapter.ApplyInCurrentTransactionAsync(
                Guid.CreateVersion7(), target, document, preview, token);
            await Assert.That(ReferenceEquals(originalTransaction, context.Database.CurrentTransaction)).IsTrue();
            await Assert.That(publication.PublishedActive).IsFalse();
            await Assert.That(publication.Evidence.Count).IsEqualTo(0);
            throw new PublicationWriteInterrupted();
        })).Throws<PublicationWriteInterrupted>();
        await database.AssertCountsAsync(0, 0, 1);
        await using var reader = database.Context();
        await Assert.That((await reader.Tenants.SingleAsync(row => row.Id == database.TenantId)).FullName)
            .IsEqualTo(originalName);
    }

    [Test]
    public async Task Portable_apply_commits_complete_draft_with_preview_bound_profile_identities()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var document = ImportDocument(database);
        await using var context = database.Context();
        var unit = new EfCoreUnitOfWork(context);
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context), unit, database.Inventory);
        var target = ConfigurationImportTarget.ForTenant(database.TenantId);
        var preview = await adapter.PreviewAsync(target, document, null, CancellationToken.None);
        var publication = await unit.ExecuteSerializableAsync(token => adapter.ApplyInCurrentTransactionAsync(
            Guid.CreateVersion7(), target, document, preview, token));
        await database.AssertCountsAsync(1, 1, 2);
        await Assert.That(publication.Profiles.Single().ProfileId)
            .IsEqualTo(preview.Mapping.ProfileIds[document.Profiles[0].Rule]);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That((await reader.ModerationPolicySelections.SingleAsync()).ActivePublicationId).IsNull();
        var restored = ModerationPolicyPublicationProjection.RestoreHistory(publication)!;
        await Assert.That(ModerationConfigurationDocumentProjection.FromCompiledDraft(restored)
            .Profiles[0].Recipes.SequenceEqual(document.Profiles[0].Recipes)).IsTrue();
    }

    [Test]
    public async Task Portable_apply_rejects_unflushed_foreign_work_without_publishing_it()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var document = ImportDocument(database);
        await using var context = database.Context();
        var unit = new EfCoreUnitOfWork(context);
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context), unit, database.Inventory);
        var target = ConfigurationImportTarget.ForTenant(database.TenantId);
        var preview = await adapter.PreviewAsync(target, document, null, CancellationToken.None);
        await Assert.That(async () => await unit.ExecuteSerializableAsync(async token =>
        {
            (await context.Tenants.SingleAsync(row => row.Id == database.TenantId, token)).FullName = "Unowned pending write";
            return await adapter.ApplyInCurrentTransactionAsync(Guid.CreateVersion7(), target, document, preview, token);
        })).Throws<ModerationConfigurationImportException>();
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    [Arguments("epoch")]
    [Arguments("administrator")]
    [Arguments("selection")]
    public async Task Portable_apply_reacquires_current_authority_and_selection_after_preview(string change)
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var document = ImportDocument(database);
        var target = ConfigurationImportTarget.ForTenant(database.TenantId);
        ModerationConfigurationImportPreview preview;
        await using (var read = database.Context())
            preview = await new ModerationConfigurationImportAdapter(database.Writer(read),
                new EfCoreUnitOfWork(read), database.Inventory).PreviewAsync(target, document, null, CancellationToken.None);
        if (change == "selection")
            await database.PublishAsync(database.Request());
        else
        {
            await using var changed = database.Context();
            changed.ModerationTrustDomainId = database.Scope.TrustDomainId;
            if (change == "epoch")
                (await changed.ModerationQualificationEpochs.SingleAsync()).Advance(1);
            else
                changed.PlatformUserRoles.Remove(await changed.PlatformUserRoles.SingleAsync(row => row.UserId == database.AdminId));
            await changed.SaveChangesAsync();
        }
        await using var context = database.Context();
        var unit = new EfCoreUnitOfWork(context);
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context), unit, database.Inventory);
        await Assert.That(async () => await unit.ExecuteSerializableAsync(token =>
            adapter.ApplyInCurrentTransactionAsync(Guid.CreateVersion7(), target, document, preview, token)))
            .Throws<ModerationConfigurationImportException>();
        await database.AssertCountsAsync(change == "selection" ? 1 : 0, change == "selection" ? 1 : 0,
            change == "administrator" ? 1 : 2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Portable_destination_rejects_machine_and_foreign_tenant_authority(bool machine)
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        await using var context = database.Context();
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context, machineCaller: machine),
            new EfCoreUnitOfWork(context), database.Inventory);
        await Assert.That(async () => await adapter.PreviewAsync(
            ConfigurationImportTarget.ForTenant(machine ? database.TenantId : Guid.CreateVersion7()),
            ImportDocument(database), null, CancellationToken.None)).Throws<ModerationConfigurationImportException>();
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    public async Task Portable_import_rejects_a_baseline_that_is_not_owned_adopted_history()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var first = database.Request();
        await database.PublishAsync(first);
        await using var context = database.Context();
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context),
            new EfCoreUnitOfWork(context), database.Inventory);
        await Assert.That(async () => await adapter.PreviewAsync(ConfigurationImportTarget.ForTenant(database.TenantId),
            ImportDocument(database), Guid.CreateVersion7(), CancellationToken.None))
            .Throws<ModerationConfigurationImportException>();
        await database.AssertCountsAsync(1, 1, 2);
    }

    [Test]
    public async Task Portable_merge_preserves_local_revision_and_maps_retained_profiles_from_owned_history()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var first = database.Request();
        var published = await database.PublishAsync(first);
        var incoming = ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(published.Publication!)!);
        var next = database.Next(first);
        var old = next.Proposed.Profiles[0];
        var added = old with
        {
            Id = Guid.CreateVersion7(),
            Rule = ISLAMU.Moderation.Domain.ModerationRuleReference.Create(database.Scope.Id, old.Rule.Code, 2)
        };
        next = next with
        {
            Proposed = next.Proposed with
            {
                Profiles = [old, added],
                SupportedProfiles = [added.Rule]
            }
        };
        await database.PublishAsync(next);
        await using var context = database.Context();
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context),
            new EfCoreUnitOfWork(context), database.Inventory);
        var preview = await adapter.PreviewAsync(ConfigurationImportTarget.ForTenant(database.TenantId),
            incoming, first.Proposed.PublicationId, CancellationToken.None);
        await Assert.That(preview.CanApply).IsTrue();
        await Assert.That(preview.Document!.Profiles.Count).IsEqualTo(2);
        await Assert.That(preview.Mapping.ProfileIds[new(added.Rule.Code, 2)]).IsEqualTo(added.Id);
        await Assert.That(preview.Mapping.ProfileIds[new(old.Rule.Code, 1)]).IsEqualTo(old.Id);
    }

    [Test]
    [Arguments("history")]
    [Arguments("recipe")]
    [Arguments("unsupported")]
    public async Task Portable_import_requires_native_history_exact_recipes_and_supported_inventory(string invalid)
    {
        await using var database = await Database.CreateAsync(implementedInventory: invalid != "unsupported");
        var request = database.Request();
        var written = await database.PublishAsync(request);
        var original = ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(written.Publication!)!);
        var profiles = original.Profiles.ToArray();
        if (invalid == "recipe")
            profiles[0] = profiles[0] with { Recipes = [] };
        var document = new ModerationConfigurationDocument(original.SchemaVersion, original.CatalogueRevision,
            original.Modes, invalid == "history" ? [original.Classes[0] with { Meaning = "Rewritten historical meaning" }]
                : original.Classes, original.Reasons, profiles, original.SupportedProfiles);
        await using var context = database.Context();
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context),
            new EfCoreUnitOfWork(context), database.Inventory);
        var preview = await adapter.ValidateOnlyAsync(ConfigurationImportTarget.ForTenant(database.TenantId),
            document, request.Proposed.PublicationId, CancellationToken.None);
        await Assert.That(preview.CanApply).IsFalse();
        await database.AssertCountsAsync(1, 1, 2);
    }

    [Test]
    public async Task Portable_apply_without_original_transaction_and_standalone_publish_inside_it_are_rejected()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        await using var context = database.Context();
        var writer = database.Writer(context);
        var adapter = new ModerationConfigurationImportAdapter(writer, new EfCoreUnitOfWork(context), database.Inventory);
        var target = ConfigurationImportTarget.ForTenant(database.TenantId);
        var document = ImportDocument(database);
        var preview = await adapter.PreviewAsync(target, document, null, CancellationToken.None);
        await Assert.That(async () => await adapter.ApplyInCurrentTransactionAsync(
            Guid.CreateVersion7(), target, document, preview, CancellationToken.None))
            .Throws<ModerationConfigurationImportException>();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(async () => await writer.PublishAsync(database.Request())).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Portable_apply_after_graph_flush_failure_rolls_back_the_original_import()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var document = ImportDocument(database);
        var target = ConfigurationImportTarget.ForTenant(database.TenantId);
        ModerationConfigurationImportPreview preview;
        await using (var read = database.Context())
            preview = await new ModerationConfigurationImportAdapter(database.Writer(read),
                new EfCoreUnitOfWork(read), database.Inventory).PreviewAsync(target, document, null, CancellationToken.None);
        await using var context = database.Context(new RejectSavedGraph());
        var unit = new EfCoreUnitOfWork(context);
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context), unit, database.Inventory);
        await Assert.That(async () => await unit.ExecuteSerializableAsync(token =>
            adapter.ApplyInCurrentTransactionAsync(Guid.CreateVersion7(), target, document, preview, token)))
            .Throws<PublicationWriteInterrupted>();
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    public async Task Portable_binding_rejects_changed_artifact_and_cannot_commit_an_earlier_section()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var document = ImportDocument(database);
        var changed = new ModerationConfigurationDocument(document.SchemaVersion, document.CatalogueRevision,
            document.Modes, [document.Classes[0] with { Meaning = "Different import" }],
            document.Reasons, document.Profiles, document.SupportedProfiles);
        await using var context = database.Context();
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context),
            new EfCoreUnitOfWork(context), database.Inventory);
        var target = ConfigurationImportTarget.ForTenant(database.TenantId);
        var preview = await adapter.PreviewAsync(target, document, null, CancellationToken.None);
        var tenant = await context.Tenants.SingleAsync(row => row.Id == database.TenantId);
        var originalName = tenant.FullName;
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            tenant.FullName = "Earlier section";
            await context.SaveChangesAsync();
            await Assert.That(async () => await adapter.ApplyInCurrentTransactionAsync(
                Guid.CreateVersion7(), target, changed, preview, CancellationToken.None))
                .Throws<ModerationConfigurationImportException>();
            // Even a coordinator that catches rejection cannot commit its previously flushed prefix.
            await Assert.That(async () => await transaction.CommitAsync()).Throws<InvalidOperationException>();
        }
        await using var reader = database.Context();
        await Assert.That((await reader.Tenants.SingleAsync(row => row.Id == database.TenantId)).FullName)
            .IsEqualTo(originalName);
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    public async Task Portable_conflicting_merge_withholds_the_entire_native_document()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var first = database.Request();
        var published = await database.PublishAsync(first);
        var baseline = ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(published.Publication!)!);
        var next = database.Next(first);
        var original = next.Proposed.Profiles[0];
        var local = original with
        {
            Id = Guid.CreateVersion7(),
            Rule = ISLAMU.Moderation.Domain.ModerationRuleReference.Create(database.Scope.Id, original.Rule.Code, 2),
            ContestationCalendarMonths = 12
        };
        next = next with { Proposed = next.Proposed with { Profiles = [original, local] } };
        await database.PublishAsync(next);
        var incoming = new ModerationConfigurationDocument(baseline.SchemaVersion, baseline.CatalogueRevision,
            baseline.Modes, baseline.Classes, baseline.Reasons,
            [baseline.Profiles[0], baseline.Profiles[0] with { Rule = new(original.Rule.Code, 2) }],
            baseline.SupportedProfiles);
        await using var context = database.Context();
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context),
            new EfCoreUnitOfWork(context), database.Inventory);
        var preview = await adapter.PreviewAsync(ConfigurationImportTarget.ForTenant(database.TenantId),
            incoming, first.Proposed.PublicationId, CancellationToken.None);
        await Assert.That(preview.Diff.HasConflicts).IsTrue();
        await Assert.That(preview.Document).IsNull();
        await Assert.That(preview.CanApply).IsFalse();
        await database.AssertCountsAsync(2, 1, 3);
    }

    [Test]
    public async Task Portable_draft_import_preserves_an_independently_active_selection()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var active = database.Request();
        active = active with
        {
            Proposed = active.Proposed with
            {
                RequestedState = Explore.Application.Features.Moderation.Policy.ModerationPolicyRequestedState.Active
            }
        };
        active = await database.PrepareActiveAsync(active);
        ModerationPolicyPublicationResult result;
        await using (var writer = database.Context())
            result = await database.Writer(writer, new EvidenceOwner(database, writer)).PublishAsync(active);
        var document = ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(result.Publication!)!);
        await using var context = database.Context();
        var unit = new EfCoreUnitOfWork(context);
        var adapter = new ModerationConfigurationImportAdapter(database.Writer(context), unit, database.Inventory);
        var target = ConfigurationImportTarget.ForTenant(database.TenantId);
        var preview = await adapter.PreviewAsync(target, document, active.Proposed.PublicationId, CancellationToken.None);
        var draft = await unit.ExecuteSerializableAsync(token =>
            adapter.ApplyInCurrentTransactionAsync(Guid.CreateVersion7(), target, document, preview, token));
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var selection = await reader.ModerationPolicySelections.SingleAsync();
        await Assert.That(selection.ActivePublicationId).IsEqualTo(active.Proposed.PublicationId);
        await Assert.That(selection.LatestPublicationId).IsEqualTo(draft.Id);
        await Assert.That(draft.PublishedActive).IsFalse();
    }

    [Test]
    public async Task Portable_compilation_with_fabricated_destination_identity_is_not_native_permission()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var document = ImportDocument(database);
        var target = ConfigurationImportTarget.ForTenant(database.TenantId);
        await using var context = database.Context();
        var unit = new EfCoreUnitOfWork(context);
        var writer = database.Writer(context);
        var destination = await unit.ExecuteSerializableAsync(token =>
            writer.AcquireConfigurationImportDestinationAsync(target, null, token));
        var forged = ModerationConfigurationImportAdapter.Compose(
            destination with { PrincipalId = Guid.CreateVersion7() }, document, database.Inventory);
        await Assert.That(forged.CanApply).IsTrue();
        await Assert.That(async () => await unit.ExecuteSerializableAsync(token =>
            writer.ApplyConfigurationImportInCurrentTransactionAsync(
                Guid.CreateVersion7(), target, document, forged, token)))
            .Throws<ModerationConfigurationImportException>();
        await database.AssertCountsAsync(0, 0, 1);
    }

    private static ModerationConfigurationDocument ImportDocument(Database database)
    {
        var request = database.Request();
        var compiled = Explore.Application.Features.Moderation.Policy.ModerationPolicyProposedStateCompiler.Compile(
            request.Proposed, new(database.HostId, database.EpochId, 1,
                ModerationPolicyPublicationProjection.WriterGeneration,
                ModerationPolicyPublicationProjection.AuthoringBounds(database.Scope), null), database.Inventory);
        return ModerationConfigurationDocumentProjection.FromCompiledDraft(compiled.Draft!);
    }
}
