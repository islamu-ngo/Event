// ABOUTME: Regression coverage for custom-property option update lifecycle semantics.
// ABOUTME: Uses EF Core in-memory storage to verify repositories preserve option identity instead of hard-replacing rows.

using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class CustomPropertyOptionLifecycleRepositoryTests
{
    [Test]
    public async Task SharedUpdateWithOptionsPreservesMatchedOptionIdAndRetiresOmittedOptions()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var context = CreateContext(databaseRoot);
        var repository = new CustomPropertyDefinitionRepository(context);
        var definition = CreateSharedDefinition();
        var preserved = CreateSharedOption(definition.Id, "format", isDefault: true);
        var omitted = CreateSharedOption(definition.Id, "legacy", isDefault: false);
        await context.CustomPropertyDefinitions.AddAsync(definition);
        await context.CustomPropertyOptions.AddRangeAsync(preserved, omitted);
        definition.DefaultOptionId = preserved.Id;
        await context.SaveChangesAsync();

        var incoming = CreateSharedOption(definition.Id, "format", isDefault: true);
        incoming.DisplayName = "Updated Format";
        await repository.UpdateWithOptions(definition, [incoming], incoming.Id, CancellationToken.None);

        var options = await context.CustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.CustomPropertyDefinitionId == definition.Id)
            .ToListAsync();
        var persisted = options.Single(x => x.Key == "format");
        var retired = options.Single(x => x.Key == "legacy");

        await Assert.That(persisted.Id).IsEqualTo(preserved.Id);
        await Assert.That(persisted.DisplayName).IsEqualTo("Updated Format");
        await Assert.That(definition.DefaultOptionId).IsEqualTo(preserved.Id);
        await Assert.That(retired.IsActive).IsFalse();
        await Assert.That(retired.IsDefault).IsFalse();
        await Assert.That(retired.IsDeleted).IsFalse();
    }

    [Test]
    public async Task EventUpdateWithOptionsPreservesMatchedOptionIdAndRetiresOmittedOptions()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var context = CreateContext(databaseRoot);
        var repository = new EventCustomPropertyRepository(context);
        var definition = CreateEventDefinition();
        var preserved = CreateEventOption(definition.Id, "format", isDefault: true);
        var omitted = CreateEventOption(definition.Id, "legacy", isDefault: false);
        await context.EventCustomPropertyDefinitions.AddAsync(definition);
        await context.EventCustomPropertyOptions.AddRangeAsync(preserved, omitted);
        definition.DefaultOptionId = preserved.Id;
        await context.SaveChangesAsync();

        var incoming = CreateEventOption(definition.Id, "format", isDefault: true);
        incoming.DisplayName = "Updated Format";
        await repository.UpdateWithOptions(definition, [incoming], incoming.Id, CancellationToken.None);

        var options = await context.EventCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventCustomPropertyDefinitionId == definition.Id)
            .ToListAsync();
        var persisted = options.Single(x => x.Key == "format");
        var retired = options.Single(x => x.Key == "legacy");

        await Assert.That(persisted.Id).IsEqualTo(preserved.Id);
        await Assert.That(persisted.DisplayName).IsEqualTo("Updated Format");
        await Assert.That(definition.DefaultOptionId).IsEqualTo(preserved.Id);
        await Assert.That(retired.IsActive).IsFalse();
        await Assert.That(retired.IsDefault).IsFalse();
        await Assert.That(retired.IsDeleted).IsFalse();
    }

    [Test]
    public async Task EventSessionUpdateWithOptionsPreservesMatchedOptionIdAndRetiresOmittedOptions()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var context = CreateContext(databaseRoot);
        var repository = new EventSessionCustomPropertyRepository(context);
        var definition = CreateEventSessionDefinition();
        var preserved = CreateEventSessionOption(definition.Id, "format", isDefault: true);
        var omitted = CreateEventSessionOption(definition.Id, "legacy", isDefault: false);
        await context.EventSessionCustomPropertyDefinitions.AddAsync(definition);
        await context.EventSessionCustomPropertyOptions.AddRangeAsync(preserved, omitted);
        definition.DefaultOptionId = preserved.Id;
        await context.SaveChangesAsync();

        var incoming = CreateEventSessionOption(definition.Id, "format", isDefault: true);
        incoming.DisplayName = "Updated Format";
        await repository.UpdateWithOptions(definition, [incoming], incoming.Id, CancellationToken.None);

        var options = await context.EventSessionCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .Where(x => x.EventSessionCustomPropertyDefinitionId == definition.Id)
            .ToListAsync();
        var persisted = options.Single(x => x.Key == "format");
        var retired = options.Single(x => x.Key == "legacy");

        await Assert.That(persisted.Id).IsEqualTo(preserved.Id);
        await Assert.That(persisted.DisplayName).IsEqualTo("Updated Format");
        await Assert.That(definition.DefaultOptionId).IsEqualTo(preserved.Id);
        await Assert.That(retired.IsActive).IsFalse();
        await Assert.That(retired.IsDefault).IsFalse();
        await Assert.That(retired.IsDeleted).IsFalse();
    }

    [Test]
    public async Task SharedDeleteDefinitionSoftDeletesDefinitionOptionsAndValues()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var context = CreateContext(databaseRoot);
        var repository = new CustomPropertyDefinitionRepository(context);
        var definition = CreateSharedDefinition();
        var option = CreateSharedOption(definition.Id, "format", isDefault: true);
        var value = CreateSharedValue(definition.Id, option.Id, definition.TenantId);
        await context.CustomPropertyDefinitions.AddAsync(definition);
        await context.CustomPropertyOptions.AddAsync(option);
        await context.CustomPropertyValues.AddAsync(value);
        definition.DefaultOptionId = option.Id;
        await context.SaveChangesAsync();

        var deleted = await repository.DeleteDefinition(definition.Id, CancellationToken.None);

        await Assert.That(deleted).IsTrue();
        var persistedDefinition = await context.CustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .SingleAsync(x => x.Id == definition.Id);
        var persistedOption = await context.CustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .SingleAsync(x => x.Id == option.Id);
        var persistedValue = await context.CustomPropertyValues
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .SingleAsync(x => x.Id == value.Id);

        await Assert.That(persistedDefinition.IsDeleted).IsTrue();
        await Assert.That(persistedDefinition.IsActive).IsFalse();
        await Assert.That(persistedDefinition.DefaultOptionId).IsNull();
        await Assert.That(persistedOption.IsDeleted).IsTrue();
        await Assert.That(persistedOption.IsActive).IsFalse();
        await Assert.That(persistedOption.IsDefault).IsFalse();
        await Assert.That(persistedValue.IsDeleted).IsTrue();
    }

    [Test]
    public async Task EventDeleteDefinitionSoftDeletesDefinitionOptionsAndValues()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var context = CreateContext(databaseRoot);
        var repository = new EventCustomPropertyRepository(context);
        var definition = CreateEventDefinition();
        var option = CreateEventOption(definition.Id, "format", isDefault: true);
        var value = CreateEventValue(definition.Id, option.Id, definition.EventId, definition.TenantId);
        await context.EventCustomPropertyDefinitions.AddAsync(definition);
        await context.EventCustomPropertyOptions.AddAsync(option);
        await context.EventCustomPropertyValues.AddAsync(value);
        definition.DefaultOptionId = option.Id;
        await context.SaveChangesAsync();

        var deleted = await repository.DeleteDefinition(definition.Id, CancellationToken.None);

        await Assert.That(deleted).IsTrue();
        var persistedDefinition = await context.EventCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .SingleAsync(x => x.Id == definition.Id);
        var persistedOption = await context.EventCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .SingleAsync(x => x.Id == option.Id);
        var persistedValue = await context.EventCustomPropertyValues
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .SingleAsync(x => x.Id == value.Id);

        await Assert.That(persistedDefinition.IsDeleted).IsTrue();
        await Assert.That(persistedDefinition.IsActive).IsFalse();
        await Assert.That(persistedDefinition.DefaultOptionId).IsNull();
        await Assert.That(persistedOption.IsDeleted).IsTrue();
        await Assert.That(persistedOption.IsActive).IsFalse();
        await Assert.That(persistedOption.IsDefault).IsFalse();
        await Assert.That(persistedValue.IsDeleted).IsTrue();
    }

    [Test]
    public async Task EventSessionDeleteDefinitionSoftDeletesDefinitionOptionsAndValues()
    {
        var databaseRoot = new InMemoryDatabaseRoot();
        await using var context = CreateContext(databaseRoot);
        var repository = new EventSessionCustomPropertyRepository(context);
        var definition = CreateEventSessionDefinition();
        var option = CreateEventSessionOption(definition.Id, "format", isDefault: true);
        var value = CreateEventSessionValue(definition.Id, option.Id, definition.EventSessionId, definition.TenantId);
        await context.EventSessionCustomPropertyDefinitions.AddAsync(definition);
        await context.EventSessionCustomPropertyOptions.AddAsync(option);
        await context.EventSessionCustomPropertyValues.AddAsync(value);
        definition.DefaultOptionId = option.Id;
        await context.SaveChangesAsync();

        var deleted = await repository.DeleteDefinition(definition.Id, CancellationToken.None);

        await Assert.That(deleted).IsTrue();
        var persistedDefinition = await context.EventSessionCustomPropertyDefinitions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .SingleAsync(x => x.Id == definition.Id);
        var persistedOption = await context.EventSessionCustomPropertyOptions
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .SingleAsync(x => x.Id == option.Id);
        var persistedValue = await context.EventSessionCustomPropertyValues
            .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
            .SingleAsync(x => x.Id == value.Id);

        await Assert.That(persistedDefinition.IsDeleted).IsTrue();
        await Assert.That(persistedDefinition.IsActive).IsFalse();
        await Assert.That(persistedDefinition.DefaultOptionId).IsNull();
        await Assert.That(persistedOption.IsDeleted).IsTrue();
        await Assert.That(persistedOption.IsActive).IsFalse();
        await Assert.That(persistedOption.IsDefault).IsFalse();
        await Assert.That(persistedValue.IsDeleted).IsTrue();
    }

    private static ExploreDbContext CreateContext(InMemoryDatabaseRoot databaseRoot)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase(Guid.NewGuid().ToString("N"), databaseRoot)
            .Options;

        var context = new ExploreDbContext(options);
        context.EnableTenantFilterBypass("Custom property option lifecycle repository tests use a system context.");
        context.Database.EnsureCreated();
        return context;
    }

    private static CustomPropertyDefinition CreateSharedDefinition()
    {
        return new CustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            EntityTypeName = EntityTypeName.Organization,
            Namespace = "event",
            Key = "format",
            DisplayName = "Format",
            PropertyType = PropertyType.Option,
            IsActive = true,
            ExposureLevel = ExposureLevel.Internal,
        };
    }

    private static EventCustomPropertyDefinition CreateEventDefinition()
    {
        return new EventCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            Namespace = "event",
            Key = "format",
            DisplayName = "Format",
            PropertyType = PropertyType.Option,
            IsActive = true,
            ExposureLevel = ExposureLevel.Internal,
        };
    }

    private static EventSessionCustomPropertyDefinition CreateEventSessionDefinition()
    {
        return new EventSessionCustomPropertyDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            EventSessionId = Guid.NewGuid(),
            Namespace = "session",
            Key = "format",
            DisplayName = "Format",
            PropertyType = PropertyType.Option,
            IsActive = true,
            ExposureLevel = ExposureLevel.Internal,
        };
    }

    private static CustomPropertyOption CreateSharedOption(Guid definitionId, string key, bool isDefault)
    {
        return new CustomPropertyOption
        {
            Id = Guid.NewGuid(),
            CustomPropertyDefinitionId = definitionId,
            Namespace = "event",
            Key = key,
            DisplayName = key,
            Value = key,
            IsDefault = isDefault,
            IsActive = true,
        };
    }

    private static EventCustomPropertyOption CreateEventOption(Guid definitionId, string key, bool isDefault)
    {
        return new EventCustomPropertyOption
        {
            Id = Guid.NewGuid(),
            EventCustomPropertyDefinitionId = definitionId,
            Namespace = "event",
            Key = key,
            DisplayName = key,
            Value = key,
            IsDefault = isDefault,
            IsActive = true,
        };
    }

    private static EventSessionCustomPropertyOption CreateEventSessionOption(Guid definitionId, string key, bool isDefault)
    {
        return new EventSessionCustomPropertyOption
        {
            Id = Guid.NewGuid(),
            EventSessionCustomPropertyDefinitionId = definitionId,
            Namespace = "session",
            Key = key,
            DisplayName = key,
            Value = key,
            IsDefault = isDefault,
            IsActive = true,
        };
    }

    private static CustomPropertyValue CreateSharedValue(Guid definitionId, Guid optionId, Guid tenantId)
    {
        return new CustomPropertyValue
        {
            Id = Guid.NewGuid(),
            CustomPropertyDefinitionId = definitionId,
            EntityId = Guid.NewGuid(),
            TenantId = tenantId,
            Ordinal = 0,
            OptionId = optionId,
        };
    }

    private static EventCustomPropertyValue CreateEventValue(
        Guid definitionId,
        Guid optionId,
        Guid eventId,
        Guid tenantId)
    {
        return new EventCustomPropertyValue
        {
            Id = Guid.NewGuid(),
            EventCustomPropertyDefinitionId = definitionId,
            EventId = eventId,
            TenantId = tenantId,
            Ordinal = 0,
            OptionId = optionId,
        };
    }

    private static EventSessionCustomPropertyValue CreateEventSessionValue(
        Guid definitionId,
        Guid optionId,
        Guid eventSessionId,
        Guid tenantId)
    {
        return new EventSessionCustomPropertyValue
        {
            Id = Guid.NewGuid(),
            EventSessionCustomPropertyDefinitionId = definitionId,
            EventSessionId = eventSessionId,
            TenantId = tenantId,
            Ordinal = 0,
            OptionId = optionId,
        };
    }
}
