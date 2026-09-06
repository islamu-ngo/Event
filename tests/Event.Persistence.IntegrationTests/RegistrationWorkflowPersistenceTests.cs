// ABOUTME: Verifies the Task 7.1 registration-workflow EF model, lookup seeding, and tenant isolation.
// ABOUTME: Covers portable relational metadata and runtime behavior without inspecting private implementation details.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests;

public sealed class RegistrationWorkflowPersistenceTests
{
    [Test]
    public async Task EfModelMapsPortableTask71Contract()
    {
        await using ExploreDbContext context = CreateModelContext();
        IModel model = context.GetService<IDesignTimeModel>().Model;

        Type[] lookupTypes =
        {
            typeof(RegistrationRequirementCriticality),
            typeof(RegistrationRequirementCompletionEffect),
            typeof(RegistrationAnswerSyncMode),
            typeof(RegistrationRequirementSubjectType)
        };
        foreach (Type lookupType in lookupTypes)
        {
            IEntityType lookup = model.FindEntityType(lookupType)!;
            await Assert.That(lookup).IsNotNull();
            await Assert.That(lookup.FindPrimaryKey()!.Properties.Single().ClrType).IsEqualTo(typeof(int));
            await Assert.That(lookup.FindProperty(nameof(RegistrationRequirementCriticality.Id))!.ValueGenerated).IsEqualTo(ValueGenerated.Never);
            await Assert.That(lookup.FindProperty(nameof(RegistrationRequirementCriticality.MasterCode))!.GetMaxLength()).IsEqualTo(100);
            await Assert.That(lookup.FindProperty(nameof(RegistrationRequirementCriticality.FullName))!.GetMaxLength()).IsEqualTo(200);
            await Assert.That(lookup.FindProperty(nameof(RegistrationRequirementCriticality.Description))!.GetMaxLength()).IsEqualTo(500);
            await Assert.That(lookup.GetIndexes().Any(index => index.IsUnique && HasProperties(index, nameof(RegistrationRequirementCriticality.MasterCode)))).IsTrue();
            await Assert.That(lookup.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNull();
            await Assert.That(lookup.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNull();
            await Assert.That(lookup.GetSeedData().Count).IsEqualTo(0);
        }

        IEntityType workflow = model.FindEntityType(typeof(RegistrationWorkflow))!;
        IEntityType requirement = model.FindEntityType(typeof(RegistrationRequirement))!;
        IEntityType channel = model.FindEntityType(typeof(RegistrationChannel))!;

        foreach (IEntityType entity in new[] { workflow, requirement, channel })
        {
            await Assert.That(entity).IsNotNull();
            await Assert.That(entity.FindPrimaryKey()!.Properties.Single().ClrType).IsEqualTo(typeof(Guid));
            await Assert.That(entity.FindProperty(nameof(RegistrationWorkflow.Id))!.ValueGenerated).IsEqualTo(ValueGenerated.Never);
            await Assert.That(entity.FindProperty(nameof(RegistrationWorkflow.ConcurrencyStamp))!.IsConcurrencyToken).IsTrue();
            await Assert.That(entity.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
            await Assert.That(entity.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNotNull();
            await Assert.That(HasProviderSpecificMetadata(entity)).IsFalse();
        }

        await Assert.That(workflow.GetTableName()).IsEqualTo("registration_workflows");
        await Assert.That(workflow.FindProperty(nameof(RegistrationWorkflow.Purpose))!.GetMaxLength()).IsEqualTo(100);
        await Assert.That(workflow.GetIndexes().Any(index => index.IsUnique && HasProperties(index, nameof(RegistrationWorkflow.TenantId), nameof(RegistrationWorkflow.EventId), nameof(RegistrationWorkflow.Purpose)))).IsTrue();
        await AssertForeignKeyAsync(workflow, typeof(Explore.Domain.Event), DeleteBehavior.Restrict, nameof(RegistrationWorkflow.TenantId), nameof(RegistrationWorkflow.EventId));

        await Assert.That(requirement.GetTableName()).IsEqualTo("registration_requirements");
        await Assert.That(requirement.GetIndexes().Any(index => index.IsUnique && HasProperties(index, nameof(RegistrationRequirement.RegistrationWorkflowId), nameof(RegistrationRequirement.Ordinal)))).IsTrue();
        await AssertForeignKeyAsync(requirement, typeof(RegistrationWorkflow), DeleteBehavior.Cascade,
            nameof(RegistrationRequirement.TenantId), nameof(RegistrationRequirement.EventId), nameof(RegistrationRequirement.RegistrationWorkflowId));
        await AssertForeignKeyAsync(requirement, typeof(RegistrationRequirementCriticality), DeleteBehavior.Restrict, nameof(RegistrationRequirement.CriticalityId));
        await AssertForeignKeyAsync(requirement, typeof(RegistrationRequirementCompletionEffect), DeleteBehavior.Restrict, nameof(RegistrationRequirement.CompletionEffectId));
        await AssertForeignKeyAsync(requirement, typeof(RegistrationAnswerSyncMode), DeleteBehavior.Restrict, nameof(RegistrationRequirement.AnswerSyncModeId));
        await AssertForeignKeyAsync(requirement, typeof(RegistrationRequirementSubjectType), DeleteBehavior.Restrict, nameof(RegistrationRequirement.AppliesToSubjectTypeId));

        await Assert.That(channel.GetTableName()).IsEqualTo("registration_channels");
        await Assert.That(channel.GetIndexes().Any(index => index.IsUnique && HasProperties(index, nameof(RegistrationChannel.RegistrationRequirementId), nameof(RegistrationChannel.Ordinal)))).IsTrue();
        await AssertForeignKeyAsync(channel, typeof(RegistrationRequirement), DeleteBehavior.Cascade,
            nameof(RegistrationChannel.TenantId), nameof(RegistrationChannel.EventId), nameof(RegistrationChannel.RegistrationWorkflowId), nameof(RegistrationChannel.RegistrationRequirementId));
    }

    [Test]
    public async Task RuntimeSeederRepairsMissingRowsWithExactStableMetadata()
    {
        await using ExploreDbContext context = CreateInMemoryContext($"task71-seed-{Guid.NewGuid():N}");

        await LookupTableSeeder.SeedRegistrationWorkflowLookupsAsync(context, default);
        context.RegistrationRequirementCriticalities.Remove(await context.RegistrationRequirementCriticalities.SingleAsync(row => row.Id == 2));
        context.RegistrationRequirementCompletionEffects.Remove(await context.RegistrationRequirementCompletionEffects.SingleAsync(row => row.Id == 2));
        context.RegistrationAnswerSyncModes.Remove(await context.RegistrationAnswerSyncModes.SingleAsync(row => row.Id == 2));
        context.RegistrationRequirementSubjectTypes.Remove(await context.RegistrationRequirementSubjectTypes.SingleAsync(row => row.Id == 2));
        await context.SaveChangesAsync();

        await LookupTableSeeder.SeedRegistrationWorkflowLookupsAsync(context, default);
        await LookupTableSeeder.SeedRegistrationWorkflowLookupsAsync(context, default);

        await AssertRowsAsync(context.RegistrationRequirementCriticalities,
        [
            (1, "REQUIRED", "Required"),
            (2, "OPTIONAL", "Optional"),
            (3, "INFORMATIONAL", "Informational"),
            (4, "POST_REGISTRATION", "Post-registration")
        ]);
        await AssertRowsAsync(context.RegistrationRequirementCompletionEffects,
        [
            (1, "BLOCKS_REGISTRATION", "Blocks registration"),
            (2, "ENRICHES_REGISTRATION", "Enriches registration"),
            (3, "NO_REGISTRATION_EFFECT", "No registration effect")
        ]);
        await AssertRowsAsync(context.RegistrationAnswerSyncModes,
        [
            (1, "NONE", "None"),
            (2, "COMPLETION_ONLY", "Completion only"),
            (3, "SELECTED_FIELDS", "Selected fields"),
            (4, "FULL_CANONICAL", "Full canonical"),
            (5, "MIRROR_ONLY", "Mirror only")
        ]);
        await AssertRowsAsync(context.RegistrationRequirementSubjectTypes,
        [
            (1, "ALL_ORDERS", "All orders"),
            (2, "SPECIFIC_TICKET_TYPE", "Specific ticket type"),
            (3, "EVERY_PARTICIPANT", "Every participant"),
            (4, "LEAD_BOOKER_ONLY", "Lead booker only"),
            (5, "CHILD_PARTICIPANTS", "Child participants"),
            (6, "SPECIFIC_SESSION_SELECTION", "Specific session selection")
        ]);
    }

    [Test]
    public async Task NamedFiltersHideDeletedAndCrossTenantWorkflowRows()
    {
        string databaseName = $"task71-filters-{Guid.NewGuid():N}";
        Guid tenantA = Guid.CreateVersion7();
        Guid tenantB = Guid.CreateVersion7();

        await using (ExploreDbContext seedContext = CreateInMemoryContext(databaseName))
        {
            seedContext.AddRange(CreateWorkflowGraph(tenantA, false), CreateWorkflowGraph(tenantA, true), CreateWorkflowGraph(tenantB, false));
            await seedContext.SaveChangesAsync();
        }

        await using ExploreDbContext missingTenantContext = CreateInMemoryContext(databaseName);
        await Assert.That(await missingTenantContext.RegistrationWorkflows.CountAsync()).IsEqualTo(0);
        await Assert.That(await missingTenantContext.RegistrationRequirements.CountAsync()).IsEqualTo(0);
        await Assert.That(await missingTenantContext.RegistrationChannels.CountAsync()).IsEqualTo(0);

        await using ExploreDbContext tenantContext = CreateInMemoryContext(databaseName);
        tenantContext.TenantContext = new TestTenantContext(tenantA);
        await Assert.That(await tenantContext.RegistrationWorkflows.CountAsync()).IsEqualTo(1);
        await Assert.That(await tenantContext.RegistrationRequirements.CountAsync()).IsEqualTo(1);
        await Assert.That(await tenantContext.RegistrationChannels.CountAsync()).IsEqualTo(1);
        await Assert.That(await tenantContext.RegistrationWorkflows.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).CountAsync()).IsEqualTo(2);
        await Assert.That(await tenantContext.RegistrationRequirements.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).CountAsync()).IsEqualTo(2);
        await Assert.That(await tenantContext.RegistrationChannels.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).CountAsync()).IsEqualTo(2);
    }

    [Test]
    public async Task GeneratedInitContainsPortableTask71Tables()
    {
        await using ExploreDbContext context = CreateModelContext();
        IMigrationsAssembly assembly = context.GetService<IMigrationsAssembly>();
        KeyValuePair<string, System.Reflection.TypeInfo> item = assembly.Migrations.Single(
            migration => migration.Key.EndsWith("_Init", StringComparison.Ordinal));
        Migration migration = assembly.CreateMigration(item.Value, context.Database.ProviderName!);
        string[] taskTables =
        [
            "registration_answer_sync_modes",
            "registration_channels",
            "registration_requirement_completion_effects",
            "registration_requirement_criticalities",
            "registration_requirement_subject_types",
            "registration_requirements",
            "registration_workflows"
        ];
        CreateTableOperation[] taskOperations = migration.UpOperations
            .OfType<CreateTableOperation>()
            .Where(operation => taskTables.Contains(operation.Name, StringComparer.Ordinal))
            .ToArray();

        await Assert.That(taskOperations.Select(operation => operation.Name).Order().SequenceEqual(taskTables)).IsTrue();
        await Assert.That(taskOperations
            .SelectMany(operation => operation.GetAnnotations()
                .Concat(operation.Columns.SelectMany(column => column.GetAnnotations())))
            .Any(annotation => annotation.Name.StartsWith("Npgsql:", StringComparison.Ordinal))).IsFalse();
        await Assert.That(migration.UpOperations.OfType<SqlOperation>().Any(operation =>
            taskTables.Any(table => operation.Sql.Contains(table, StringComparison.Ordinal)))).IsFalse();
    }

    private static ExploreDbContext CreateModelContext()
    {
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=task71_model;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ExploreDbContext(options);
    }

    private readonly Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot _databaseRoot = new();

    private ExploreDbContext CreateInMemoryContext(string databaseName)
    {
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase(databaseName, _databaseRoot)
            .Options;
        return new ExploreDbContext(options);
    }

    private static RegistrationWorkflow CreateWorkflowGraph(Guid tenantId, bool isDeleted)
    {
        DateTime now = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(tenantId, Guid.CreateVersion7(), "ATTENDEE_REGISTRATION", now);
        RegistrationRequirement requirement = RegistrationRequirement.Create(
            workflow,
            1,
            RegistrationRequirementCriticalityEnum.Optional,
            true,
            RegistrationRequirementCompletionEffectEnum.EnrichesRegistration,
            RegistrationAnswerSyncModeEnum.SELECTED_FIELDS,
            RegistrationRequirementSubjectTypeEnum.AllOrders,
            null,
            now);
        RegistrationChannel channel = RegistrationChannel.Create(requirement, 1, true, null, now);
        workflow.AddRequirement(requirement);
        requirement.AddChannel(channel);
        workflow.IsDeleted = isDeleted;
        requirement.IsDeleted = isDeleted;
        channel.IsDeleted = isDeleted;
        return workflow;
    }

    private static bool HasProperties(IReadOnlyIndex index, params string[] propertyNames) =>
        index.Properties.Select(property => property.Name).SequenceEqual(propertyNames);

    private static bool HasProviderSpecificMetadata(IEntityType entity) =>
        entity.GetAnnotations()
            .Concat(entity.GetProperties().SelectMany(property => property.GetAnnotations()))
            .Concat(entity.GetKeys().SelectMany(key => key.GetAnnotations()))
            .Concat(entity.GetForeignKeys().SelectMany(foreignKey => foreignKey.GetAnnotations()))
            .Concat(entity.GetIndexes().SelectMany(index => index.GetAnnotations()))
            .Any(annotation => annotation.Name.StartsWith("Npgsql:", StringComparison.Ordinal));

    private static async Task AssertForeignKeyAsync(
        IEntityType entity,
        Type principalType,
        DeleteBehavior deleteBehavior,
        params string[] propertyNames)
    {
        IForeignKey? foreignKey = entity.GetForeignKeys().SingleOrDefault(candidate =>
            candidate.PrincipalEntityType.ClrType == principalType &&
            candidate.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
        await Assert.That(foreignKey).IsNotNull();
        await Assert.That(foreignKey!.DeleteBehavior).IsEqualTo(deleteBehavior);
        await Assert.That(foreignKey.Properties.All(property => !property.IsNullable)).IsTrue();
    }

    private static async Task AssertRowsAsync<TLookup>(
        DbSet<TLookup> set,
        (int Id, string MasterCode, string FullName)[] expected)
        where TLookup : class
    {
        var rows = await set
            .OrderBy(row => EF.Property<int>(row, "Id"))
            .Select(row => new
            {
                Id = EF.Property<int>(row, "Id"),
                MasterCode = EF.Property<string>(row, "MasterCode"),
                FullName = EF.Property<string>(row, "FullName")
            })
            .ToArrayAsync();
        await Assert.That(rows.Select(row => (row.Id, row.MasterCode, row.FullName)).SequenceEqual(expected)).IsTrue();
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class RegistrationWorkflowPostgreSqlPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Category("Runtime")]
    public async Task PostgreSqlAppliesMigrationSeedFiltersAndRelationalConstraints()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        EventScope tenantA = await SeedEventAsync(context, "task71-a");
        EventScope tenantB = await SeedEventAsync(context, "task71-b");
        DateTime now = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

        RegistrationWorkflow workflow = CreateWorkflowGraph(tenantA.TenantId, tenantA.EventId, "ATTENDEE_REGISTRATION", now);
        context.RegistrationWorkflows.Add(workflow);
        await context.SaveChangesAsync();

        await Assert.That(await context.RegistrationRequirementCriticalities.CountAsync()).IsEqualTo(4);
        await Assert.That(await context.RegistrationRequirementCompletionEffects.CountAsync()).IsEqualTo(3);
        await Assert.That(await context.RegistrationAnswerSyncModes.CountAsync()).IsEqualTo(5);
        await Assert.That(await context.RegistrationRequirementSubjectTypes.CountAsync()).IsEqualTo(6);

        await using (ExploreDbContext tenantAContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.TenantId)))
        {
            await Assert.That(await tenantAContext.RegistrationWorkflows.CountAsync()).IsEqualTo(1);
            await Assert.That(await tenantAContext.RegistrationRequirements.CountAsync()).IsEqualTo(1);
            await Assert.That(await tenantAContext.RegistrationChannels.CountAsync()).IsEqualTo(1);
        }

        await using (ExploreDbContext tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.TenantId)))
        {
            await Assert.That(await tenantBContext.RegistrationWorkflows.CountAsync()).IsEqualTo(0);
            await Assert.That(await tenantBContext.RegistrationRequirements.CountAsync()).IsEqualTo(0);
            await Assert.That(await tenantBContext.RegistrationChannels.CountAsync()).IsEqualTo(0);
        }

        context.RegistrationWorkflows.Add(RegistrationWorkflow.Create(
            tenantA.TenantId, tenantA.EventId, workflow.Purpose, now));
        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        RegistrationRequirement duplicateRequirement = RegistrationRequirement.Create(
            workflow,
            1,
            RegistrationRequirementCriticalityEnum.Optional,
            true,
            RegistrationRequirementCompletionEffectEnum.EnrichesRegistration,
            RegistrationAnswerSyncModeEnum.SELECTED_FIELDS,
            RegistrationRequirementSubjectTypeEnum.AllOrders,
            null,
            now);
        context.RegistrationRequirements.Add(duplicateRequirement);
        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        RegistrationRequirement requirement = workflow.Requirements.Single();
        context.RegistrationChannels.Add(RegistrationChannel.Create(requirement, 1, true, null, now));
        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
        context.ChangeTracker.Clear();

        context.RegistrationWorkflows.Add(RegistrationWorkflow.Create(
            tenantA.TenantId, tenantB.EventId, "CROSS_TENANT", now));
        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    private static RegistrationWorkflow CreateWorkflowGraph(
        Guid tenantId,
        Guid eventId,
        string purpose,
        DateTime now)
    {
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(tenantId, eventId, purpose, now);
        RegistrationRequirement requirement = RegistrationRequirement.Create(
            workflow,
            1,
            RegistrationRequirementCriticalityEnum.Optional,
            true,
            RegistrationRequirementCompletionEffectEnum.EnrichesRegistration,
            RegistrationAnswerSyncModeEnum.SELECTED_FIELDS,
            RegistrationRequirementSubjectTypeEnum.AllOrders,
            null,
            now);
        requirement.AddChannel(RegistrationChannel.Create(requirement, 1, true, null, now));
        workflow.AddRequirement(requirement);
        return workflow;
    }

    private static async Task<EventScope> SeedEventAsync(ExploreDbContext context, string slugPrefix)
    {
        Tenant tenant = new()
        {
            FullName = $"Task 7.1 {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid():N}",
            TenantStatusId = 2,
            TenantStatus = null!
        };
        User user = new()
        {
            Pii = new UserPii
            {
                Email = $"{slugPrefix}-{Guid.NewGuid():N}@example.com",
                FirstName = "Task",
                LastName = "Workflow"
            }
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();

        Actor actor = new()
        {
            Pii = new ActorPii { DisplayName = $"Task 7.1 {slugPrefix}" },
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        Explore.Domain.Event @event = new(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = $"Task 7.1 {slugPrefix}",
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            EventProvenanceTypeId = 1,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.Events.Add(@event);
        await context.SaveChangesAsync();
        return new(tenant.Id, @event.Id);
    }

    private sealed record EventScope(Guid TenantId, Guid EventId);
    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
