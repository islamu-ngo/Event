using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Moderation;
using Explore.Application.Features.Moderation.Policy;
using Explore.Domain;
using Explore.Persistence;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Instance_import_acquires_empty_installed_target_without_installing_defaults()
    {
        await using var database = await Database.CreateAsync();
        var target = await SeedInstanceImportTenantAsync(database);
        await using var context = database.Context();
        var destination = await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(token =>
            database.Writer(context).AcquireInstanceConfigurationImportDestinationAsync(target.Id, null, token));
        await Assert.That(destination.TenantId).IsEqualTo(target.Id);
        await Assert.That(destination.Epoch.TenantId).IsEqualTo(target.Id);
        await Assert.That(destination.Current).IsNull();
        await Assert.That(destination.Selection).IsNull();
        await Assert.That(destination.Baseline).IsNull();
        await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
        await Assert.That(await context.ModerationPolicyPublications.IgnoreQueryFilters(["ModerationAuthority"])
            .CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Instance_import_of_all_reference_profiles_preserves_every_canonical_recipe()
    {
        await using var database = await Database.CreateAsync();
        var target = await SeedInstanceImportTenantAsync(database);
        var proposed = database.Request(Enum.GetValues<ModerationReferenceProfileId>()).Proposed with { SupportedProfiles = [] };
        var compilation = ModerationPolicyProposedStateCompiler.Compile(proposed,
            new(database.HostId, database.EpochId, 1, ModerationPolicyPublicationProjection.WriterGeneration,
                ModerationPolicyPublicationProjection.AuthoringBounds(database.Scope), null), database.Inventory);
        var document = ModerationConfigurationDocumentProjection.FromCompiledDraft(compilation.Draft!);
        await using var context = database.Context();
        var unit = new EfCoreUnitOfWork(context);
        var writer = database.Writer(context);
        var destination = await unit.ExecuteSerializableAsync(token =>
            writer.AcquireInstanceConfigurationImportDestinationAsync(target.Id, null, token));
        var preview = ModerationConfigurationImportAdapter.Compose(destination, document, database.Inventory);
        await Assert.That(preview.CanApply).IsTrue();
        var publication = await unit.ExecuteSerializableAsync(token =>
            writer.ApplyInstanceConfigurationImportInCurrentTransactionAsync(
                Guid.CreateVersion7(), target.Id, document, preview, token));
        var restored = ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(publication)!);
        await Assert.That(restored.Profiles.Count).IsEqualTo(27);
        await Assert.That(restored.Profiles.Select(profile => profile.Bundle).Distinct().Count()).IsEqualTo(10);
        foreach (var profile in restored.Profiles)
            await Assert.That(profile.Recipes.SequenceEqual(
                document.Profiles.Single(row => row.Rule == profile.Rule).Recipes)).IsTrue();
        await Assert.That(publication.Profiles.All(profile => profile.RuleRevision == 1)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Instance_import_requires_both_installed_source_and_target_epochs(bool missingSource)
    {
        await using var database = await Database.CreateAsync();
        var target = await SeedInstanceImportTenantAsync(database, installEpoch: missingSource);
        if (missingSource)
        {
            await using var setup = database.Context();
            setup.ModerationQualificationEpochs.Remove(await setup.ModerationQualificationEpochs.SingleAsync());
            await setup.SaveChangesAsync();
        }
        await using var context = database.Context();
        await Assert.That(async () => await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(token =>
            database.Writer(context).AcquireInstanceConfigurationImportDestinationAsync(target.Id, null, token)))
            .Throws<ModerationConfigurationImportException>();
    }

    [Test]
    public async Task Instance_import_retains_independent_active_pointer_when_appending_a_draft()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var target = await SeedInstanceImportTenantAsync(database);
        await using (var setup = database.Context())
        {
            setup.TenantContext = new ExportTenantContext(target.Id);
            var epoch = await setup.ModerationQualificationEpochs.SingleAsync();
            var request = RebindExportRequest(database.Request(), AuthorityScope.Tenant(database.Scope.Parent!, target.Id))
                with
            { ExpectedAuthoritySourceId = epoch.Id };
            request = request with
            {
                Proposed = request.Proposed with
                {
                    SupportedProfiles = request.Proposed.Profiles
                .Select(profile => profile.Rule).ToImmutableArray(),
                    RequestedState = ModerationPolicyRequestedState.Active
                }
            };
            var owner = new EvidenceOwner(database, setup);
            var writer = database.Writer(setup, owner);
            var candidate = await writer.PrepareAsync(request, Now.AddHours(2));
            await Assert.That(candidate.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
            request = request with { OperationId = Guid.CreateVersion7(), ExpectedQualificationRevision = 2 };
            await Assert.That((await writer.PublishAsync(request)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        }
        await using var context = database.Context();
        var unit = new EfCoreUnitOfWork(context);
        var importer = database.Writer(context);
        var destination = await unit.ExecuteSerializableAsync(token =>
            importer.AcquireInstanceConfigurationImportDestinationAsync(target.Id, null, token));
        var document = ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(destination.Current!)!);
        var preview = ModerationConfigurationImportAdapter.Compose(destination, document, database.Inventory);
        var draft = await unit.ExecuteSerializableAsync(token =>
            importer.ApplyInstanceConfigurationImportInCurrentTransactionAsync(
                Guid.CreateVersion7(), target.Id, document, preview, token));
        var after = await unit.ExecuteSerializableAsync(token =>
            importer.AcquireInstanceConfigurationImportDestinationAsync(target.Id, destination.Current!.Id, token));
        await Assert.That(after.Selection!.ActivePublicationId).IsEqualTo(destination.Current!.Id);
        await Assert.That(after.Selection.LatestPublicationId).IsEqualTo(draft.Id);
        await Assert.That(draft.PublishedActive).IsFalse();
    }

    [Test]
    public async Task Instance_import_checks_full_ancestry_even_when_baseline_is_latest()
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        await using var context = database.Context();
        var unit = new EfCoreUnitOfWork(context);
        var writer = database.Writer(context);
        var destination = await unit.ExecuteSerializableAsync(token =>
            writer.AcquireInstanceConfigurationImportDestinationAsync(foreign.TenantId, null, token));
        var document = ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(destination.Current!)!);
        var preview = ModerationConfigurationImportAdapter.Compose(destination, document, database.Inventory);
        var latest = await unit.ExecuteSerializableAsync(token =>
            writer.ApplyInstanceConfigurationImportInCurrentTransactionAsync(
                Guid.CreateVersion7(), foreign.TenantId, document, preview, token));
        await using (var changed = database.Context())
            await changed.Set<ModerationPolicyPublicationPlan>().IgnoreQueryFilters(["ModerationAuthority"])
                .Where(row => row.PublicationId == foreign.PublicationId).ExecuteDeleteAsync();
        await Assert.That(async () => await unit.ExecuteSerializableAsync(token =>
            writer.AcquireInstanceConfigurationImportDestinationAsync(foreign.TenantId, latest.Id, token)))
            .Throws<ModerationConfigurationImportException>();
    }

    [Test]
    [Arguments("profile")]
    [Arguments("publication")]
    [Arguments("policy")]
    [Arguments("operation")]
    public async Task Instance_import_rejects_reserved_identity_aliases_from_another_destination(string alias)
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        var target = await SeedInstanceImportTenantAsync(database);
        await using var context = database.Context();
        var unit = new EfCoreUnitOfWork(context);
        var writer = database.Writer(context);
        var source = await unit.ExecuteSerializableAsync(token =>
            writer.AcquireInstanceConfigurationImportDestinationAsync(foreign.TenantId, null, token));
        var destination = await unit.ExecuteSerializableAsync(token =>
            writer.AcquireInstanceConfigurationImportDestinationAsync(target.Id, null, token));
        var document = ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(source.Current!)!);
        var reserved = ModerationConfigurationImportAdapter.Compose(destination, document, database.Inventory).Reserve();
        var collision = new ModerationConfigurationImportReservation(null,
            alias == "policy" ? source.Selection!.Id : reserved.PolicyId,
            alias == "publication" ? source.Current!.Id : reserved.PublicationId,
            reserved.Binding, reserved.Profiles.Select(profile => alias == "profile"
                ? profile with { ProfileId = source.Current!.Profiles.Single().ProfileId } : profile).ToArray());
        var preview = ModerationConfigurationImportAdapter.ComposeReserved(destination, document, database.Inventory, collision);
        await Assert.That(preview.CanApply).IsTrue();
        await Assert.That(async () => await unit.ExecuteSerializableAsync(token =>
            writer.ApplyInstanceConfigurationImportInCurrentTransactionAsync(
                alias == "operation" ? source.Current!.OperationId : Guid.CreateVersion7(),
                target.Id, document, preview, token))).Throws<ModerationConfigurationImportException>();
        await using var reader = database.Context();
        await Assert.That(await reader.ModerationPolicyPublications.IgnoreQueryFilters(["ModerationAuthority"])
            .CountAsync(row => row.TenantId == target.Id)).IsEqualTo(0);
    }

    [Test]
    [Arguments("recipe")]
    [Arguments("meaning")]
    [Arguments("inventory")]
    public async Task Instance_import_recompiles_canonical_meaning_recipes_and_installed_inventory(string invalid)
    {
        await using var database = await Database.CreateAsync();
        var foreign = await PublishExportTenantAsync(database);
        await using var context = database.Context();
        var unit = new EfCoreUnitOfWork(context);
        var writer = database.Writer(context);
        var destination = await unit.ExecuteSerializableAsync(token =>
            writer.AcquireInstanceConfigurationImportDestinationAsync(foreign.TenantId, null, token));
        var original = ModerationConfigurationDocumentProjection.FromCompiledDraft(
            ModerationPolicyPublicationProjection.RestoreHistory(destination.Current!)!);
        var document = new ModerationConfigurationDocument(original.SchemaVersion, original.CatalogueRevision,
            original.Modes, invalid == "meaning" ? [original.Classes[0] with { Meaning = "Rewritten retained meaning" }]
                : original.Classes, original.Reasons,
            invalid == "recipe" ? [original.Profiles[0] with { Recipes = [] }] : original.Profiles,
            invalid == "inventory" ? [original.Profiles[0].Rule] : original.SupportedProfiles);
        var preview = ModerationConfigurationImportAdapter.Compose(destination, document, database.Inventory);
        await Assert.That(preview.CanApply).IsFalse();
        await Assert.That(async () => await unit.ExecuteSerializableAsync(token =>
            writer.ApplyInstanceConfigurationImportInCurrentTransactionAsync(
                Guid.CreateVersion7(), foreign.TenantId, document, preview, token)))
            .Throws<ModerationConfigurationImportException>();
    }

    private static async Task<Tenant> SeedInstanceImportTenantAsync(Database database, bool installEpoch = true)
    {
        await using var context = database.Context();
        var status = await context.TenantStatuses.SingleAsync(row => row.IsActiveState);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            Slug = $"instance-import-{Guid.CreateVersion7():N}",
            FullName = "Instance import destination",
            TenantStatusId = status.Id,
            TenantStatus = status
        };
        context.Tenants.Add(tenant);
        if (installEpoch)
            context.ModerationQualificationEpochs.Add(ModerationQualificationEpoch.Initialize(
                Guid.CreateVersion7(), await context.ModerationHostBindings.SingleAsync(), tenant));
        await context.SaveChangesAsync();
        return tenant;
    }
}
