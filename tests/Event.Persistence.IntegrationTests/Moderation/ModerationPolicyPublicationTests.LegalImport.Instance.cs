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
    public async Task Instance_legal_read_uses_exact_authorized_tenants_without_request_tenant_impersonation()
    {
        await using var database = await Database.CreateAsync();
        var otherTenantId = Guid.CreateVersion7();
        var instance = LegalDocument.CreateImportedDraft(LegalDocumentScope.Instance, null,
            LegalDocumentKind.TermsOfService, LegalDocumentAudience.Public,
            [LegalDocumentLocalizedSource.Create("en", "Instance terms", "Instance meaning", "Instance source.")],
            null, "private-instance-origin", false,
            new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc));
        var own = LegalImportDraft(database.TenantId);
        var other = LegalImportDraft(otherTenantId);
        await using (var writer = database.Context())
        {
            var status = await writer.TenantStatuses.SingleAsync(row => row.IsActiveState);
            writer.Tenants.Add(new Tenant
            {
                Id = otherTenantId, FullName = "Other legal owner", Slug = $"legal-{otherTenantId:N}",
                TenantStatusId = status.Id, TenantStatus = status
            });
            writer.LegalDocuments.AddRange(instance, own, other);
            await writer.SaveChangesAsync();
        }
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var repository = new LegalDocumentRepository(context);
        var crossTenant = await repository.GetForInstanceConfigurationExportAsync(
            [otherTenantId], CancellationToken.None);
        await Assert.That(crossTenant.Select(row => row.Id).ToArray())
            .IsEquivalentTo(new[] { instance.Id, other.Id });
        await Assert.That((await repository.GetForConfigurationExportAsync(
            LegalDocumentScope.Tenant, database.TenantId, CancellationToken.None)).Single().Id).IsEqualTo(own.Id);
        await Assert.That(context.ChangeTracker.Entries().Any()).IsFalse();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(database.TenantId);
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
        var portable = LegalConfigurationSnapshotProjection.Project(
            crossTenant.Where(row => row.Scope == LegalDocumentScope.Instance).ToArray(),
            LegalDocumentScope.Instance, null);
        await Assert.That(portable.Keys.Single()).IsEqualTo("TermsOfService");
        await Assert.That(portable["TermsOfService"].Localizations.Single().Markdown).IsEqualTo("Instance source.");
        await Assert.That(() => LegalConfigurationSnapshotProjection.Project(
            crossTenant, LegalDocumentScope.Instance, null)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Instance_legal_flush_accepts_only_explicit_owned_tenant_set_and_original_owner_coordinates()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var repository = new LegalDocumentRepository(context);
        var document = LegalImportDraft(database.TenantId);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await repository.AddAsync(document, CancellationToken.None);
        await repository.FlushConfigurationImportInCurrentTransactionAsync(transaction.TransactionId,
            ConfigurationImportTarget.ForInstance(), [database.TenantId], [document], CancellationToken.None);
        // Reparenting a tracked child cannot turn another aggregate's row into owned work.
        var source = document.Versions.Single().Sources.Single();
        context.Entry(source).Property(row => row.LegalDocumentVersionId).CurrentValue = Guid.CreateVersion7();
        async Task Flush() => await repository.FlushConfigurationImportInCurrentTransactionAsync(
            transaction.TransactionId, ConfigurationImportTarget.ForInstance(),
            [database.TenantId], [document], CancellationToken.None);
        await Assert.That(Flush).Throws<InvalidOperationException>();
        await using var reader = database.Context();
        await Assert.That(await new LegalDocumentRepository(reader).GetByIdForUpdateAsync(
            document.Id, LegalDocumentScope.Tenant, database.TenantId, CancellationToken.None)).IsNull();
    }
}
