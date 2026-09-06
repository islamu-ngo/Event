// ABOUTME: Verifies lookup-relationship uniqueness is declared by the EF Core model.
// ABOUTME: Keeps generated migrations correct without coupling tests to a migration class.

using Explore.Domain;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Event.Persistence.IntegrationTests.Migrations;

public sealed class LookupRelationshipUniquenessMigrationTests
{
    [Test]
    public async Task Model_DeclaresTenantQualifiedRelationshipIndexesAsUnique()
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=lookup_relationship_model;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention();
        await using var context = new ExploreDbContext(builder.Options);
        IModel model = context.GetService<IDesignTimeModel>().Model;

        await AssertUniqueIndexAsync<TagTypeTags>(model, ["TenantId", "TagId", "TagTypeId"]);
        await AssertUniqueIndexAsync<CategoryTypeCategories>(model, ["TenantId", "CategoryId", "CategoryTypeId"]);
    }

    private static async Task AssertUniqueIndexAsync<TEntity>(IModel model, string[] properties)
    {
        IEntityType entity = model.FindEntityType(typeof(TEntity))!;
        await Assert.That(entity.GetIndexes().Any(index =>
            index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(properties))).IsTrue();
    }
}
