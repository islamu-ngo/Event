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
    public async Task Human_provisioning_declares_private_fields_without_reading_or_writing_them()
    {
        using var context = ExploreDbContextModelProviderTests.CreateContext("SqlServer");
        ManagedProviderProvisioningDataModel.RequireInventory(context);
        var relation = ManagedProviderProvisioningDataModel.Inventory.Single(item => item.Type == typeof(UserPii));
        var properties = context.Model.FindEntityType(typeof(UserPii))!.GetProperties()
            .Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

        await Assert.That(relation.MappedProperties.Order(StringComparer.Ordinal).SequenceEqual(properties)).IsTrue();
        await Assert.That(relation.Columns.Order(StringComparer.Ordinal)
            .SequenceEqual(new[] { "Email", "FirstName", "LastName", "UserId" })).IsTrue();
        await Assert.That(relation.Insert).IsTrue();
        await Assert.That(relation.Update.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Local_administration_declares_the_full_pii_model_without_new_column_authority()
    {
        using var context = ExploreDbContextModelProviderTests.CreateContext("SqlServer");
        var source = PostgresLocalAdministrationContract.Inventory.Single(item => item.Type == typeof(UserPii));
        PostgresLocalAdministrationContract.RequireModel(context, source);
        var properties = context.Model.FindEntityType(typeof(UserPii))!.GetProperties()
            .Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

        await Assert.That(source.MappedProperties.Order(StringComparer.Ordinal).SequenceEqual(properties)).IsTrue();
        await Assert.That(source.Properties.Order(StringComparer.Ordinal)
            .SequenceEqual(new[] { "Email", "FirstName", "LastName", "UserId" })).IsTrue();
    }

    [Test]
    public async Task Missing_private_field_declaration_still_rejects_the_local_administration_model()
    {
        using var context = ExploreDbContextModelProviderTests.CreateContext("SqlServer");
        var source = PostgresLocalAdministrationContract.Inventory.Single(item => item.Type == typeof(UserPii));
        var incomplete = source with
        {
            MappedProperties = source.MappedProperties.Where(property => property != "_birthDateRevision").ToArray()
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PostgresLocalAdministrationContract.RequireModel(context, incomplete));

        await Assert.That(exception.Message).IsEqualTo("local_administration_explicit_inventory_drift");
    }

    [Test]
    public async Task Missing_private_field_declaration_still_rejects_the_local_writer_model()
    {
        using var context = ExploreDbContextModelProviderTests.CreateContext("SqlServer");
        var source = LocalIdentityWriterInventory.Sources.Single(item => item.EntityType == typeof(UserPii));
        var incomplete = source with { MappedProperties = source.MappedProperties.Remove("_birthDateRevision") };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            LocalIdentityWriterInventory.RequireModel(context, incomplete));

        await Assert.That(exception.Message).IsEqualTo("local_identity_writer_source_model_drift");
    }

    [Test]
    public async Task Missing_private_field_declaration_still_rejects_the_human_provisioning_model()
    {
        using var context = ExploreDbContextModelProviderTests.CreateContext("SqlServer");
        var relation = ManagedProviderProvisioningDataModel.Inventory.Single(item => item.Type == typeof(UserPii));
        var incomplete = relation with
        {
            MappedProperties = relation.MappedProperties.Where(property => property != "_birthDateRevision").ToArray()
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ManagedProviderProvisioningDataModel.RequireModel(context, incomplete));

        await Assert.That(exception.Message).IsEqualTo("provisioning_data_inventory_drift");
    }
}
