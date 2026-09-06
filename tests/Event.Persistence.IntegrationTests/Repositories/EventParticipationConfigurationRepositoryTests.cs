// ABOUTME: Model-first persistence tests for event participation configuration and normalized lookup repair.
// ABOUTME: Uses EF's current model and InMemory provider so checks do not depend on generated migrations.

using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[Category("EventParticipationConfiguration")]
public sealed class EventParticipationConfigurationRepositoryTests
{
    [Test]
    public async Task EfModel_MapsSharedKeyConfigurationAndExactlyThreeNormalizedLookups()
    {
        await using var context = CreateRelationalModelContext();
        var model = context.GetService<IDesignTimeModel>().Model;
        var configurationType = model.FindEntityType(typeof(EventParticipationConfiguration))!;

        await Assert.That(configurationType.GetTableName()).IsEqualTo("event_participation_configurations");
        await Assert.That(configurationType.FindPrimaryKey()!.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(EventParticipationConfiguration.Id)])).IsTrue();
        await Assert.That(configurationType.FindProperty(nameof(EventParticipationConfiguration.Id))!.ValueGenerated)
            .IsEqualTo(ValueGenerated.Never);
        await Assert.That(configurationType.FindProperty(nameof(EventParticipationConfiguration.GuestRecoveryPolicy))!.ClrType)
            .IsEqualTo(typeof(GuestRecoveryPolicyEnum?));
        await Assert.That(configurationType.FindProperty(nameof(EventParticipationConfiguration.ConcurrencyStamp))!
            .IsConcurrencyToken).IsTrue();
        await Assert.That(configurationType.FindDeclaredQueryFilter(QueryFilterNames.Tenant)).IsNotNull();
        await Assert.That(configurationType.FindDeclaredQueryFilter(QueryFilterNames.SoftDelete)).IsNotNull();

        var eventForeignKey = configurationType.GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(Explore.Domain.Event));
        await Assert.That(eventForeignKey.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(EventParticipationConfiguration.TenantId), nameof(EventParticipationConfiguration.Id)]))
            .IsTrue();
        await Assert.That(eventForeignKey.PrincipalKey.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(Explore.Domain.Event.TenantId), nameof(Explore.Domain.Event.Id)]))
            .IsTrue();
        await Assert.That(eventForeignKey.IsUnique).IsTrue();
        await Assert.That(eventForeignKey.DeleteBehavior).IsEqualTo(DeleteBehavior.Cascade);

        await AssertLookupModelAsync<ParticipationHandlingMode>(model, "participation_handling_modes");
        await AssertLookupModelAsync<AdvanceRegistrationObligation>(model, "advance_registration_obligations");
        await AssertLookupModelAsync<IdentityAccessMode>(model, "identity_access_modes");
        await Assert.That(model.GetEntityTypes().Any(entity => entity.ClrType?.Name == "GuestRecoveryPolicy"))
            .IsFalse();
    }

    [Test]
    public async Task RuntimeSeeder_RepairsAllParticipationLookupRowsIdempotently()
    {
        await using var context = CreateInMemoryContext("participation-lookup-repair");

        await LookupTableSeeder.SeedEventParticipationLookupsAsync(context, CancellationToken.None);
        context.ParticipationHandlingModes.Remove(await context.ParticipationHandlingModes.SingleAsync(
            mode => mode.Id == (int)ParticipationHandlingModeEnum.PlatformManaged));
        context.AdvanceRegistrationObligations.Remove(await context.AdvanceRegistrationObligations.SingleAsync(
            obligation => obligation.Id == (int)AdvanceRegistrationObligationEnum.Required));
        context.IdentityAccessModes.Remove(await context.IdentityAccessModes.SingleAsync(
            mode => mode.Id == (int)IdentityAccessModeEnum.CapabilityTokenAllowed));
        await context.SaveChangesAsync();

        await LookupTableSeeder.SeedEventParticipationLookupsAsync(context, CancellationToken.None);
        await LookupTableSeeder.SeedEventParticipationLookupsAsync(context, CancellationToken.None);

        await AssertRowsAsync(context.ParticipationHandlingModes,
        [
            (1, "INFORMATION_ONLY"),
            (2, "WALK_IN"),
            (3, "EXTERNAL_MANAGED"),
            (4, "PLATFORM_MANAGED")
        ]);
        await AssertRowsAsync(context.AdvanceRegistrationObligations,
        [
            (1, "NOT_APPLICABLE"),
            (2, "OPTIONAL"),
            (3, "REQUIRED")
        ]);
        await AssertRowsAsync(context.IdentityAccessModes,
        [
            (1, "ACCOUNT_REQUIRED"),
            (2, "GUEST_ALLOWED"),
            (3, "CAPABILITY_TOKEN_ALLOWED")
        ]);
    }

    [Test]
    public async Task Repository_ReadsAndUpdatesOnlyTheCurrentTenantConfiguration()
    {
        await using var context = CreateInMemoryContext("participation-repository");
        await LookupTableSeeder.SeedEventParticipationLookupsAsync(context, CancellationToken.None);
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();
        var eventA = Guid.CreateVersion7();
        var eventB = Guid.CreateVersion7();
        var configurationA = CreateConfiguration(eventA, tenantA);
        var configurationB = CreateConfiguration(eventB, tenantB);
        context.EnableTenantFilterBypass("Seeds configurations for repository tenant-isolation verification.");
        context.EventParticipationConfigurations.AddRange(configurationA, configurationB);
        await context.SaveChangesAsync();
        context.ClearTenantFilterBypass();
        context.TenantContext = new TestTenantContext(tenantA);
        var repository = new EventParticipationConfigurationRepository(context);

        EventParticipationConfiguration? found = await repository.GetByEventAndTenantAsync(
            eventA,
            tenantA,
            CancellationToken.None);
        EventParticipationConfiguration? wrongTenant = await repository.GetByEventAndTenantAsync(
            eventA,
            tenantB,
            CancellationToken.None);

        await Assert.That(found).IsNotNull();
        await Assert.That(found!.ParticipationHandlingMode!.MasterCode).IsEqualTo("INFORMATION_ONLY");
        await Assert.That(found.AdvanceRegistrationObligation!.MasterCode).IsEqualTo("NOT_APPLICABLE");
        await Assert.That(wrongTenant).IsNull();

        Guid originalConcurrencyStamp = found.ConcurrencyStamp;
        found.Reconfigure(
            (int)ParticipationHandlingModeEnum.ExternalManaged,
            (int)AdvanceRegistrationObligationEnum.Required,
            identityAccessModeId: null,
            guestRecoveryPolicy: null);
        await repository.UpdateAsync(found, CancellationToken.None);

        await Assert.That(found.ConcurrencyStamp).IsNotEqualTo(originalConcurrencyStamp);
        context.ChangeTracker.Clear();
        EventParticipationConfiguration? persisted = await repository.GetByEventAndTenantAsync(
            eventA,
            tenantA,
            CancellationToken.None);
        await Assert.That(persisted!.ParticipationHandlingModeId)
            .IsEqualTo((int)ParticipationHandlingModeEnum.ExternalManaged);
        await Assert.That(persisted.AdvanceRegistrationObligationId)
            .IsEqualTo((int)AdvanceRegistrationObligationEnum.Required);
    }

    [Test]
    public async Task EventRepository_StandardDetailsIncludeTypedParticipationConfiguration()
    {
        await using var context = CreateInMemoryContext("participation-standard-details");
        await LookupTableSeeder.SeedEventParticipationLookupsAsync(context, CancellationToken.None);
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        context.TenantContext = new TestTenantContext(tenantId);
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = "participation-standard-details@example.test",
                FirstName = "Participation",
                LastName = "Tester"
            }
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id,
            User = user,
            Pii = new ActorPii { DisplayName = "Participation test actor" }
        };
        context.Users.Add(user);
        context.ActorTypes.Add(new ActorType { Id = 1, MasterCode = "USER", FullName = "User" });
        context.EventStatuses.Add(new EventStatus { Id = 1, MasterCode = "PUBLISHED", FullName = "Published" });
        context.EventFormats.Add(new EventFormat { Id = 1, MasterCode = "DIGITAL", FullName = "Digital" });
        context.VisibilityTypes.Add(new VisibilityType { Id = 1, MasterCode = "PUBLIC", FullName = "Public" });
        context.EventProvenanceTypes.Add(new EventProvenanceType
        {
            Id = 1,
            MasterCode = "LOCAL",
            FullName = "Local"
        });
        var eventEntity = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = eventId,
            Title = "Participation details",
            ActorId = actor.Id,
            Actor = actor,
            EventProvenanceTypeId = 1,
            TenantId = tenantId,
            Tenant = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            TotalViews = 0
        };
        context.Actors.Add(actor);
        context.Events.Add(eventEntity);
        context.EventParticipationConfigurations.Add(CreateConfiguration(eventId, tenantId));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Explore.Domain.Event? result = await new EventRepository(context).GetEventWithDetails(eventId);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.ParticipationConfiguration).IsNotNull();
        await Assert.That(result.ParticipationConfiguration!.ParticipationHandlingMode!.MasterCode)
            .IsEqualTo("INFORMATION_ONLY");
        await Assert.That(result.ParticipationConfiguration.AdvanceRegistrationObligation!.MasterCode)
            .IsEqualTo("NOT_APPLICABLE");
    }

    private static async Task AssertLookupModelAsync<TLookup>(
        IModel model,
        string tableName)
        where TLookup : class
    {
        var entityType = model.FindEntityType(typeof(TLookup))!;

        await Assert.That(entityType.GetTableName()).IsEqualTo(tableName);
        await Assert.That(entityType.FindProperty("Id")!.ValueGenerated).IsEqualTo(ValueGenerated.Never);
        await Assert.That(entityType.GetIndexes().Any(index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(["MasterCode"]))).IsTrue();
        await Assert.That(entityType.GetSeedData().Count).IsEqualTo(0);
    }

    private static async Task AssertRowsAsync<TLookup>(
        DbSet<TLookup> rows,
        (int Id, string MasterCode)[] expected)
        where TLookup : class
    {
        var actual = await rows.OrderBy(row => EF.Property<int>(row, "Id"))
            .Select(row => new
            {
                Id = EF.Property<int>(row, "Id"),
                MasterCode = EF.Property<string>(row, "MasterCode")
            })
            .ToArrayAsync();

        await Assert.That(actual.Select(row => (row.Id, row.MasterCode)).SequenceEqual(expected)).IsTrue();
    }

    private static EventParticipationConfiguration CreateConfiguration(Guid eventId, Guid tenantId) =>
        EventParticipationConfiguration.Create(
            eventId,
            tenantId,
            (int)ParticipationHandlingModeEnum.InformationOnly,
            (int)AdvanceRegistrationObligationEnum.NotApplicable,
            identityAccessModeId: null,
            guestRecoveryPolicy: null,
            now: DateTime.UtcNow);

    private static ParticipationTestDbContext CreateInMemoryContext(string name) =>
        new(TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase($"{name}-{Guid.NewGuid():N}")
            .Options);

    private static ParticipationTestDbContext CreateRelationalModelContext() =>
        new(TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=event_participation_model;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention()
            .Options);

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed class ParticipationTestDbContext(DbContextOptions<ExploreDbContext> options)
        : ExploreDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Actor>()
                .Ignore(actor => actor.MergesFrom)
                .Ignore(actor => actor.MergesInto);
            modelBuilder.Ignore<ActorMerge>();
        }
    }
}
