using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

public sealed class ConfigurationImportSnapshotConsistencyTests
{
    [Test]
    public async Task FidelityReadsUncommittedBrandingWithoutPublishingItToSharedCache()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        string key = SettingsDocumentKeys.Tenant.Branding;
        var catalog = ConfigurationManifestCatalog.TenantDocuments[key];
        string defaultsVersion = catalog.DefaultsVersion
            ?? throw new InvalidOperationException("Branding requires catalog defaults.");
        var context = fixture.Context;
        var document = TenantSettingsDocument.Create(
            fixture.TenantId, key, catalog.SchemaVersion, defaultsVersion,
            """{"displayName":"Committed branding"}""");
        document.Id = Guid.CreateVersion7();
        document.CreatedAt = DateTime.UtcNow;
        context.TenantSettingsDocuments.Add(document);
        if (!await context.PaidEventPolicyVersions.AnyAsync(
                policy => policy.TenantId == null && policy.IsActive))
            context.PaidEventPolicyVersions.Add(PaidEventPolicyVersion.CreateDefaultInstance());
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var resolver = fixture.Services.GetRequiredService<ITypedSettingsDocumentResolver>();
        var resolution = new SettingsResolutionContext(
            fixture.TenantId, RequestedDocuments: [key]);
        await Assert.That((await resolver.ResolveTenantDocumentAsync<BrandingSettings>(
            resolution, key))!.Payload.DisplayName).IsEqualTo("Committed branding");

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            var current = await context.TenantSettingsDocuments.SingleAsync(row => row.Id == document.Id);
            current.UpdatePayload(catalog.SchemaVersion, defaultsVersion,
                """{"displayName":"Uncommitted branding"}""");
            await context.SaveChangesAsync();
            var reader = fixture.Services.GetRequiredService<ConfigurationManifestCurrentStateReader>();
            var snapshot = await reader.ReadImportSnapshotAsync(
                ConfigurationManifestExportView.Portable, null, default);

            using JsonDocument artifact = JsonDocument.Parse(snapshot.Utf8Json);
            JsonElement payload = artifact.RootElement.GetProperty("spec").GetProperty("tenants")
                .EnumerateArray().Single().GetProperty("spec").GetProperty("documents")
                .GetProperty(key).GetProperty("payload");
            await Assert.That(payload.GetProperty("displayName").GetString())
                .IsEqualTo("Uncommitted branding");
            await Assert.That((await resolver.ResolveTenantDocumentAsync<BrandingSettings>(
                resolution, key))!.Payload.DisplayName).IsEqualTo("Committed branding");
            await using var observerScope = fixture.CreateScope();
            var observer = observerScope.ServiceProvider.GetRequiredService<Explore.Persistence.ExploreDbContext>();
            await Assert.That((await observer.TenantSettingsDocuments.AsNoTracking()
                .SingleAsync(row => row.Id == document.Id)).PayloadJson)
                .IsEqualTo("""{"displayName":"Committed branding"}""");
            await transaction.RollbackAsync();
        }

        context.ChangeTracker.Clear();
        await Assert.That((await context.TenantSettingsDocuments.AsNoTracking()
            .SingleAsync(row => row.Id == document.Id)).PayloadJson)
            .IsEqualTo("""{"displayName":"Committed branding"}""");
    }
}
