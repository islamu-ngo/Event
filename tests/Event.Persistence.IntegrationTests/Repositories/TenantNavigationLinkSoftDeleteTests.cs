// ABOUTME: Integration test verifying soft-delete EF query filter for TenantNavigationLink entity.
// ABOUTME: Confirms that deleting a nav link sets IsDeleted=true and excludes it from normal queries.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class TenantNavigationLinkSoftDeleteTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public TenantNavigationLinkSoftDeleteTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Test]
    public async Task Delete_ShouldSoftDelete_AndExcludeFromNormalQueries()
    {
        // Arrange — create tenant + nav link
        using var context = _fixture.CreateDbContext();

        var activeStatus = await context.TenantStatuses.FindAsync(2);
        var tenant = new Tenant
        {
            FullName = "Nav Soft Delete Tenant",
            Slug = "nav-softdel-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = activeStatus?.Id ?? 2,
            TenantStatus = activeStatus!
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var link = new TenantNavigationLink
        {
            Label = "Soft Delete Test Link",
            Url = "https://example.com",
            Order = 0,
            OpenInNewTab = false,
            IsActive = true,
            TenantId = tenant.Id,
            CreatedAt = DateTime.UtcNow
        };
        context.TenantNavigationLinks.Add(link);
        await context.SaveChangesAsync();

        var linkId = link.Id;

        // Act — remove triggers ISoftDeletable interceptor in SaveChangesAsync
        context.Remove(link);
        await context.SaveChangesAsync();

        // Assert — normal query excludes soft-deleted link
        using var verifyContext = _fixture.CreateDbContext();
        var normalResult = await verifyContext.TenantNavigationLinks
            .Where(l => l.Id == linkId)
            .FirstOrDefaultAsync();
        await Assert.That(normalResult).IsNull();

        // Assert — IgnoreQueryFilters finds it with IsDeleted=true
        var unfilteredResult = await verifyContext.TenantNavigationLinks
            .IgnoreQueryFilters()
            .Where(l => l.Id == linkId)
            .FirstOrDefaultAsync();
        await Assert.That(unfilteredResult).IsNotNull();
        await Assert.That(unfilteredResult!.IsDeleted).IsTrue();
        await Assert.That(unfilteredResult.DeletedAt).IsNotNull();
    }

    [Test]
    public async Task NonDeletedLink_ShouldBeVisibleInNormalQuery()
    {
        // Arrange
        using var context = _fixture.CreateDbContext();

        var activeStatus = await context.TenantStatuses.FindAsync(2);
        var tenant = new Tenant
        {
            FullName = "Nav Visible Tenant",
            Slug = "nav-visible-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = activeStatus?.Id ?? 2,
            TenantStatus = activeStatus!
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var link = new TenantNavigationLink
        {
            Label = "Visible Link",
            Url = "https://visible.example.com",
            Order = 0,
            OpenInNewTab = false,
            IsActive = true,
            TenantId = tenant.Id,
            CreatedAt = DateTime.UtcNow
        };
        context.TenantNavigationLinks.Add(link);
        await context.SaveChangesAsync();

        // Act + Assert — non-deleted link visible in normal query
        using var verifyContext = _fixture.CreateDbContext();
        var result = await verifyContext.TenantNavigationLinks
            .Where(l => l.Id == link.Id)
            .FirstOrDefaultAsync();
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.IsDeleted).IsFalse();
    }
}
