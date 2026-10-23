using Explore.Domain;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class UserPiiWriterManifestTests
{
    [Test]
    public async Task Worker_can_insert_the_complete_pii_row_without_reading_or_updating_it()
    {
        using var context = ExploreDbContextModelProviderTests.CreateContext("SqlServer");
        var source = ManagedTenantWorkerCreationInventory.Sources.Single(item => item.Entity == typeof(UserPii));
        var properties = context.Model.FindEntityType(typeof(UserPii))!.GetProperties()
            .Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

        await Assert.That(source.Insert.Order(StringComparer.Ordinal).SequenceEqual(properties)).IsTrue();
        await Assert.That(source.Read.IsEmpty).IsTrue();
        await Assert.That(source.Update.IsEmpty).IsTrue();
    }

    [Test]
    public async Task Local_identity_can_insert_private_defaults_without_acquiring_birth_date_read_or_update()
    {
        using var context = ExploreDbContextModelProviderTests.CreateContext("SqlServer");
        var source = LocalIdentityWriterInventory.Sources.Single(item => item.EntityType == typeof(UserPii));
        LocalIdentityWriterInventory.RequireModel(context, source);

        await Assert.That(source.Insert.Order(StringComparer.Ordinal)
            .SequenceEqual(source.MappedProperties.Order(StringComparer.Ordinal))).IsTrue();
        await Assert.That(source.Select.Order(StringComparer.Ordinal)
            .SequenceEqual(new[] { "Email", "FirstName", "LastName", "UserId" })).IsTrue();
        await Assert.That(source.Update.Order(StringComparer.Ordinal)
            .SequenceEqual(new[] { "Email", "FirstName", "LastName" })).IsTrue();
        await Assert.That(source.AllowDelete).IsFalse();
    }

    [Test]
    public async Task Missing_private_field_declaration_still_rejects_the_local_writer_model()
    {
        using var context = ExploreDbContextModelProviderTests.CreateContext("SqlServer");
        var source = LocalIdentityWriterInventory.Sources.Single(item => item.EntityType == typeof(UserPii));
        var incomplete = source with { MappedProperties = source.MappedProperties.Remove("_birthDateRevision") };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LocalIdentityWriterInventory.RequireModel(context, incomplete));

        await Assert.That(exception.Message).IsEqualTo("local_identity_writer_source_model_drift");
    }
}
