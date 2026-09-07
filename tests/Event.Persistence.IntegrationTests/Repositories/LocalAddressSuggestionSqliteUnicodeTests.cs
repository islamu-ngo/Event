// ABOUTME: Asserts EF Core metadata collation mapping for portable ordinal ASCII properties under SQLite.
// ABOUTME: Replaces redundant full-database corpus execution with fast metadata model verification.

using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class LocalAddressSuggestionSqliteUnicodeTests
{
    [Test]
    public async Task SqliteModelMapsLocationAddressKeysWithBinaryCollation()
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>()
            .UseSqlite("DataSource=:memory:")
            .UseSnakeCaseNamingConvention();

        await using var context = new ExploreDbContext(builder.Options);
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType? locationType = model.FindEntityType(typeof(Location));
        await Assert.That(locationType).IsNotNull();

        IProperty? displaySortKeyProperty = locationType!.FindProperty(nameof(Location.DisplaySortKey));
        await Assert.That(displaySortKeyProperty).IsNotNull();
        await Assert.That(displaySortKeyProperty!.GetCollation()).IsEqualTo("BINARY");
        await Assert.That(displaySortKeyProperty.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value)
            .IsEqualTo(true);

        IEntityType? locationPiiType = model.FindEntityType(typeof(LocationPii));
        await Assert.That(locationPiiType).IsNotNull();

        IProperty? addressSubstringKeyProperty = locationPiiType!.FindProperty(nameof(LocationPii.AddressSubstringKey));
        await Assert.That(addressSubstringKeyProperty).IsNotNull();
        await Assert.That(addressSubstringKeyProperty!.GetCollation()).IsEqualTo("BINARY");
        await Assert.That(addressSubstringKeyProperty.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value)
            .IsEqualTo(true);
    }

    [Test]
    public async Task PostgreSqlModelMapsLocationAddressKeysWithOrdinalCCollation()
    {
        var builder = new DbContextOptionsBuilder<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=dummy")
            .UseSnakeCaseNamingConvention();

        await using var context = new ExploreDbContext(builder.Options);
        IModel model = context.GetService<IDesignTimeModel>().Model;

        IEntityType? locationType = model.FindEntityType(typeof(Location));
        await Assert.That(locationType).IsNotNull();

        IProperty? displaySortKeyProperty = locationType!.FindProperty(nameof(Location.DisplaySortKey));
        await Assert.That(displaySortKeyProperty).IsNotNull();
        await Assert.That(displaySortKeyProperty!.GetCollation()).IsEqualTo("C");
        await Assert.That(displaySortKeyProperty.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value)
            .IsEqualTo(true);

        IEntityType? locationPiiType = model.FindEntityType(typeof(LocationPii));
        await Assert.That(locationPiiType).IsNotNull();

        IProperty? addressSubstringKeyProperty = locationPiiType!.FindProperty(nameof(LocationPii.AddressSubstringKey));
        await Assert.That(addressSubstringKeyProperty).IsNotNull();
        await Assert.That(addressSubstringKeyProperty!.GetCollation()).IsEqualTo("C");
        await Assert.That(addressSubstringKeyProperty.FindAnnotation(PortableOrdinalAsciiPropertyExtensions.AnnotationName)?.Value)
            .IsEqualTo(true);
    }
}
