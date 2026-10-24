using System.Data;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Legal_import_flush_persists_exact_graph_without_committing_original_transaction()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var repository = new LegalDocumentRepository(context);
        var document = LegalImportDraft(database.TenantId);
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await repository.AddAsync(document, CancellationToken.None);
            await repository.FlushConfigurationImportInCurrentTransactionAsync(transaction.TransactionId,
                ConfigurationImportTarget.ForTenant(database.TenantId), [], [document], CancellationToken.None);
            await Assert.That(context.ChangeTracker.HasChanges()).IsFalse();
            var read = await repository.GetForConfigurationExportAsync(
                LegalDocumentScope.Tenant, database.TenantId, CancellationToken.None);
            await Assert.That(read.Single().Id).IsEqualTo(document.Id);
            await transaction.RollbackAsync();
        }
        await using var reader = database.Context();
        await Assert.That(await new LegalDocumentRepository(reader).GetByIdForUpdateAsync(
            document.Id, LegalDocumentScope.Tenant, database.TenantId, CancellationToken.None)).IsNull();
    }

    [Test]
    [Arguments("foreign")]
    [Arguments("deleted")]
    [Arguments("wrong-scope")]
    [Arguments("wrong-transaction")]
    [Arguments("detached")]
    public async Task Legal_import_rejection_rolls_back_previously_flushed_sections(string failure)
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var repository = new LegalDocumentRepository(context);
        var document = LegalImportDraft(database.TenantId);
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            // A prior section has already saved in the original transaction.
            var tenant = await context.Tenants.SingleAsync(row => row.Id == database.TenantId);
            tenant.FullName = "Earlier import section";
            await context.SaveChangesAsync();
            await repository.AddAsync(document, CancellationToken.None);
            if (failure == "foreign")
                tenant.FullName = "Unowned pending section";
            if (failure == "deleted")
                context.Remove(document.Versions.Single().Sources.Single());
            if (failure == "detached")
                context.Entry(document).State = EntityState.Detached;
            var target = failure == "wrong-scope"
                ? ConfigurationImportTarget.ForInstance()
                : ConfigurationImportTarget.ForTenant(database.TenantId);
            async Task Flush() => await repository.FlushConfigurationImportInCurrentTransactionAsync(
                failure == "wrong-transaction" ? Guid.CreateVersion7() : transaction.TransactionId,
                target, [], [document], CancellationToken.None);
            await Assert.That(Flush).Throws<InvalidOperationException>();
        }
        await using var reader = database.Context();
        await Assert.That((await reader.Tenants.SingleAsync(row => row.Id == database.TenantId)).FullName)
            .IsEqualTo("Rich publication witness");
        await Assert.That(await new LegalDocumentRepository(reader).GetByIdForUpdateAsync(
            document.Id, LegalDocumentScope.Tenant, database.TenantId, CancellationToken.None)).IsNull();
    }

    [Test]
    public async Task Legal_export_reads_current_authoring_revision_without_tracking_or_authority_projection()
    {
        await using var database = await Database.CreateAsync();
        var document = LegalImportDraft(database.TenantId);
        var time = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);
        document.BindAccountableIdentity("private-target-identity", time);
        document.Approve(database.AdminId, "private-review-evidence", time);
        document.Schedule(time, time);
        document.Publish(time);
        document.CreateImportedRevision(LegalDocumentAudience.RegisteredUsers,
            [
                LegalDocumentLocalizedSource.Create("fr", "Conditions", "Nouveau sens", "Contenu actuel."),
                LegalDocumentLocalizedSource.Create("en", "Current terms", "Current meaning", "Current content.")
            ],
            LegalDocumentTemplateProvenance.Create("project-terms", "2",
                LegalDocumentTemplateSourceKind.ProjectOwned, "LicenseRef-ISLAMU", "template-review"),
            "private-import-origin", true, time);
        await using (var writer = database.Context())
        {
            await new LegalDocumentRepository(writer).AddAsync(document, CancellationToken.None);
            await writer.SaveChangesAsync();
        }
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = new LegalDocumentRepository(context);
        var read = await repository.GetForConfigurationExportAsync(
            LegalDocumentScope.Tenant, database.TenantId, CancellationToken.None);
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await Assert.That(read.Single().CurrentVersion).IsEqualTo(2);
        var portable = LegalConfigurationSnapshotProjection.Project(
            read, LegalDocumentScope.Tenant, database.TenantId);
        await Assert.That(portable.Keys.Single()).IsEqualTo("TenantTerms");
        var terms = portable["TenantTerms"];
        await Assert.That(terms.Kind).IsEqualTo("TenantTerms");
        await Assert.That(terms.LifecycleIntent).IsEqualTo("Draft");
        await Assert.That(terms.Audience).IsEqualTo("RegisteredUsers");
        await Assert.That(terms.RequiresFreshAcceptance).IsTrue();
        await Assert.That(terms.Localizations.Select(row => row.LanguageTag).ToArray())
            .IsEquivalentTo(new[] { "en", "fr" });
        await Assert.That(terms.Localizations[0].Markdown).IsEqualTo("Current content.");
        await Assert.That(terms.Localizations[1].Title).IsEqualTo("Conditions");
        await Assert.That(terms.TemplateProvenance!.TemplateVersion).IsEqualTo("2");
        await Assert.That(terms.TemplateProvenance.ReviewReference).IsEqualTo("template-review");
        await Assert.That(terms.AccountableIdentityReference).IsNull();
        await Assert.That(terms.ChangeSummary).IsNull();
        await Assert.That(terms.ProposedEffectiveAt).IsNull();
        await Assert.That(terms.JurisdictionAssumptions.Count).IsEqualTo(0);
        var crossTenant = await repository.GetForInstanceConfigurationExportAsync(
            [database.TenantId], CancellationToken.None);
        await Assert.That(crossTenant.Single().Id).IsEqualTo(document.Id);
        await Assert.That((await repository.GetForInstanceConfigurationExportAsync(
            [], CancellationToken.None)).Count).IsEqualTo(0);
        async Task ForeignRead() => await repository.GetForConfigurationExportAsync(
            LegalDocumentScope.Tenant, Guid.CreateVersion7(), CancellationToken.None);
        await Assert.That(ForeignRead).Throws<InvalidOperationException>();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
    }

    private static LegalDocument LegalImportDraft(Guid tenantId) =>
        LegalDocument.CreateImportedDraft(LegalDocumentScope.Tenant, tenantId,
            LegalDocumentKind.TenantTerms, LegalDocumentAudience.Public,
            [LegalDocumentLocalizedSource.Create("en", "Terms", "Imported terms", "Imported terms.")],
            null, "private-import-origin", false,
            new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc));
}
