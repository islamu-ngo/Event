using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class TenantSettingsDocumentPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task TenantSettingsDocument_ShouldPersistJsonbPayload()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "jsonb");
        var document = NewDocument(tenant.Id, SettingsDocumentKeys.Tenant.PublicExperience, "{\"mode\":\"tenant\"}");

        context.TenantSettingsDocuments.Add(document);
        await context.SaveChangesAsync();

        var saved = await context.TenantSettingsDocuments.AsNoTracking().SingleAsync(x => x.Id == document.Id);
        using var payload = JsonDocument.Parse(saved.PayloadJson);
        await Assert.That(payload.RootElement.GetProperty("mode").GetString()).IsEqualTo("tenant");
        await Assert.That(saved.SchemaVersion).IsEqualTo(1);
    }

    [Test]
    public async Task TenantSettingsDocument_ShouldEnforceUniqueTenantDocumentKey()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "unique");

        context.TenantSettingsDocuments.Add(NewDocument(tenant.Id, SettingsDocumentKeys.Tenant.Branding, "{\"displayName\":\"One\"}"));
        await context.SaveChangesAsync();

        context.TenantSettingsDocuments.Add(NewDocument(tenant.Id, SettingsDocumentKeys.Tenant.Branding, "{\"displayName\":\"Two\"}"));

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task TenantSettingsDocument_WhenTenantContextIsSet_IsIsolatedByTenantFilter()
    {
        await fixture.ResetAsync();
        using var seedContext = fixture.CreateDbContext();
        var tenantA = await SeedTenantAsync(seedContext, "tenant-a");
        var tenantB = await SeedTenantAsync(seedContext, "tenant-b");
        var tenantADocument = NewDocument(tenantA.Id, SettingsDocumentKeys.Tenant.RenderPolicy, "{\"showOrganizationBranding\":true}");
        var tenantBDocument = NewDocument(tenantB.Id, SettingsDocumentKeys.Tenant.RenderPolicy, "{\"showOrganizationBranding\":false}");

        seedContext.TenantSettingsDocuments.AddRange(tenantADocument, tenantBDocument);
        await seedContext.SaveChangesAsync();

        using var tenantAContext = fixture.CreateDbContext();
        tenantAContext.TenantContext = new TestTenantContext(tenantA.Id);

        var visibleIds = await tenantAContext.TenantSettingsDocuments
            .AsNoTracking()
            .Where(x => x.Id == tenantADocument.Id || x.Id == tenantBDocument.Id)
            .Select(x => x.Id)
            .ToListAsync();

        await Assert.That(visibleIds).IsEquivalentTo([tenantADocument.Id]);
    }

    [Test]
    public async Task TenantSettingsDocument_ShouldUpdateConcurrencyStampOnInsertAndUpdate()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "concurrency");
        var document = NewDocument(tenant.Id, SettingsDocumentKeys.Tenant.EventDefaults, "{\"requireApproval\":true}");

        context.TenantSettingsDocuments.Add(document);
        await context.SaveChangesAsync();
        var insertedStamp = document.ConcurrencyStamp;

        document.UpdatePayload(
            schemaVersion: 2,
            defaultsVersion: "2026-06-event-defaults",
            payloadJson: "{\"requireApproval\":false}");
        await context.SaveChangesAsync();

        await Assert.That(insertedStamp).IsNotEqualTo(Guid.Empty);
        await Assert.That(document.ConcurrencyStamp).IsNotEqualTo(insertedStamp);
    }

    [Test]
    public async Task TenantSettingsDocument_ModelUsesJsonbPayloadColumn()
    {
        using var context = fixture.CreateDbContext();

        var entityType = context.Model.FindEntityType(typeof(TenantSettingsDocument));
        var payloadProperty = entityType?.FindProperty(nameof(TenantSettingsDocument.PayloadJson));

        await Assert.That(entityType).IsNotNull();
        await Assert.That(payloadProperty).IsNotNull();
        await Assert.That(payloadProperty!.GetColumnType()).IsEqualTo("jsonb");
    }

    [Test]
    public async Task TenantSettingsDocumentRepository_GetByTenantAndDocumentKey_ReturnsExactTenantDocument()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenantA = await SeedTenantAsync(context, "repo-a");
        var tenantB = await SeedTenantAsync(context, "repo-b");
        var tenantADocument = NewDocument(tenantA.Id, SettingsDocumentKeys.Tenant.Branding, "{\"displayName\":\"Tenant A\"}");
        var tenantBDocument = NewDocument(tenantB.Id, SettingsDocumentKeys.Tenant.Branding, "{\"displayName\":\"Tenant B\"}");
        context.TenantSettingsDocuments.AddRange(tenantADocument, tenantBDocument);
        await context.SaveChangesAsync();
        var repository = new TenantSettingsDocumentRepository(context);

        var result = await repository.GetByTenantAndDocumentKey(
            tenantA.Id,
            SettingsDocumentKeys.Tenant.Branding);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(tenantADocument.Id);
        await Assert.That(result.TenantId).IsEqualTo(tenantA.Id);
    }

    [Test]
    public async Task TenantSettingsDocumentRepository_WhenAmbientTenantDiffers_ReturnsOnlyExplicitTenantDocument()
    {
        await fixture.ResetAsync();
        using var seedContext = fixture.CreateDbContext();
        var tenantA = await SeedTenantAsync(seedContext, "semantic-bypass-a");
        var tenantB = await SeedTenantAsync(seedContext, "semantic-bypass-b");
        var tenantADocument = NewDocument(tenantA.Id, SettingsDocumentKeys.Tenant.Branding, "{\"displayName\":\"Tenant A\"}");
        var tenantBDocument = NewDocument(tenantB.Id, SettingsDocumentKeys.Tenant.Branding, "{\"displayName\":\"Tenant B\"}");
        seedContext.TenantSettingsDocuments.AddRange(tenantADocument, tenantBDocument);
        await seedContext.SaveChangesAsync();

        using var tenantBContext = fixture.CreateDbContext();
        tenantBContext.TenantContext = new TestTenantContext(tenantB.Id);
        var repository = new TenantSettingsDocumentRepository(tenantBContext);

        var result = await repository.GetByTenantAndDocumentKey(
            tenantA.Id,
            SettingsDocumentKeys.Tenant.Branding);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(tenantADocument.Id);
        await Assert.That(result.TenantId).IsEqualTo(tenantA.Id);
        await Assert.That(result.Id).IsNotEqualTo(tenantBDocument.Id);
    }

    [Test]
    public async Task TenantSettingsDocumentRepository_GetManyForTenant_ReturnsOnlyRequestedTenantDocuments()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenantA = await SeedTenantAsync(context, "repo-many-a");
        var tenantB = await SeedTenantAsync(context, "repo-many-b");
        var tenantABranding = NewDocument(tenantA.Id, SettingsDocumentKeys.Tenant.Branding, "{\"displayName\":\"Tenant A\"}");
        var tenantAPublicExperience = NewDocument(tenantA.Id, SettingsDocumentKeys.Tenant.PublicExperience, "{\"mode\":\"tenant\"}");
        var tenantBBranding = NewDocument(tenantB.Id, SettingsDocumentKeys.Tenant.Branding, "{\"displayName\":\"Tenant B\"}");
        context.TenantSettingsDocuments.AddRange(tenantABranding, tenantAPublicExperience, tenantBBranding);
        await context.SaveChangesAsync();
        var repository = new TenantSettingsDocumentRepository(context);

        var results = await repository.GetManyForTenant(
            tenantA.Id,
            [SettingsDocumentKeys.Tenant.Branding, SettingsDocumentKeys.Tenant.Branding]);

        await Assert.That(results.Select(document => document.Id)).IsEquivalentTo([tenantABranding.Id]);
        await Assert.That(results.All(document => document.TenantId == tenantA.Id)).IsTrue();
    }

    [Test]
    public async Task TenantSettingsDocumentRepository_ReadsAreNoTracking()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "repo-tracking");
        var document = NewDocument(tenant.Id, SettingsDocumentKeys.Tenant.Branding, "{\"displayName\":\"Tracked\"}");
        context.TenantSettingsDocuments.Add(document);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new TenantSettingsDocumentRepository(context);

        var result = await repository.GetByTenantAndDocumentKey(
            tenant.Id,
            SettingsDocumentKeys.Tenant.Branding);

        await Assert.That(result).IsNotNull();
        await Assert.That(context.ChangeTracker.Entries<TenantSettingsDocument>()).IsEmpty();
    }

    private static async Task<Tenant> SeedTenantAsync(ExploreDbContext context, string slugPrefix)
    {
        var tenant = new Tenant
        {
            FullName = $"Tenant Settings Document {slugPrefix}",
            Slug = $"tenant-settings-doc-{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = 2,
            TenantStatus = null!,
        };

        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant;
    }

    private static TenantSettingsDocument NewDocument(Guid tenantId, string documentKey, string payloadJson) =>
        TenantSettingsDocument.Create(
            tenantId,
            documentKey,
            schemaVersion: 1,
            defaultsVersion: "2026-05-defaults",
            payloadJson: payloadJson);

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
