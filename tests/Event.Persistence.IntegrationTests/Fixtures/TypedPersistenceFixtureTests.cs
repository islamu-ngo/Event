using System.Security.Cryptography;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Seed;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Fixtures;

public sealed class TypedPersistenceFixtureTests
{
    private static readonly DateTime Now = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task CallbackAndUserReturnConvergeWithPersistedEvidence(bool callbackFirst)
    {
        await using var connection = new SqliteConnection(SqliteTestDatabaseFactory.CreateIsolatedMemoryConnectionString());
        await connection.OpenAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention()
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance).Options;
        await RegistrationFinalizationEffectPersistenceTests.AssertConvergesAsync(options, callbackFirst);
    }

    [Test]
    public async Task ParticipantFulfillmentUsesCompleteRelationalGraph()
    {
        await using var connection = new SqliteConnection(SqliteTestDatabaseFactory.CreateIsolatedMemoryConnectionString());
        await connection.OpenAsync();
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
        context.EnableTenantFilterBypass("Typed finalization fixture verification.");
        await RegistrationFinalizationEffectPersistenceTests.AssertParticipantFulfillmentAsync(context);
    }

    [Test]
    public async Task ApplicabilityMatrixUsesCompleteRelationalGraph()
    {
        await using var connection = new SqliteConnection(SqliteTestDatabaseFactory.CreateIsolatedMemoryConnectionString());
        await connection.OpenAsync();
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
        context.EnableTenantFilterBypass("Typed finalization fixture verification.");
        await RegistrationFinalizationEffectPersistenceTests.AssertApplicabilityMatrixAsync(context);
    }

    [Test]
    public async Task FulfillmentRequiresPersistedSubmissionWithMatchingTenant()
    {
        await using var connection = new SqliteConnection(SqliteTestDatabaseFactory.CreateIsolatedMemoryConnectionString());
        await connection.OpenAsync();
        await using var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseSqlite(connection).UseSnakeCaseNamingConvention().Options);
        context.EnableTenantFilterBypass("Typed registration fixture relationship verification.");
        context.BindTenantLease();
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");
        await LookupTableSeeder.SeedAsync(context);

        var tenant = new Tenant
        {
            FullName = "Fulfillment fixture", Slug = $"fulfillment-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        };
        var user = new User
        {
            Pii = new UserPii { Email = "registrant@example.test", FirstName = "Fixture", LastName = "Registrant" }
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Fixture organizer" },
            ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!, UserId = user.Id
        };
        context.Add(actor);
        await context.SaveChangesAsync();
        var eventEntity = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(), Title = "Fulfillment fixture", ActorId = actor.Id, Actor = null!,
            TenantId = tenant.Id, Tenant = null!, EventStatus = null!, EventFormatId = 1,
            EventFormat = null!, EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = 1, VisibilityType = null!, ConcurrencyStamp = Guid.CreateVersion7()
        };
        var catalog = EventTicketCatalogVersion.Create(tenant.Id, eventEntity.Id, "EUR", 1);
        var workflow = RegistrationWorkflow.Create(tenant.Id, eventEntity.Id, "FULFILLMENT", Now);
        var requirement = RegistrationRequirement.Create(workflow, 1,
            RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_SYNC, RegistrationRequirementSubjectTypeEnum.AllOrders, null, Now);
        var channel = RegistrationChannel.Create(requirement, 1, true, null, Now);
        requirement.AddChannel(channel);
        workflow.AddRequirement(requirement);
        var form = RegistrationForm.Create(tenant.Id, eventEntity.Id, "platform.registration", "fulfillment", "Fulfillment", Now);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, Now);
        form.AddVersion(version);
        var order = RegistrationOrder.Create(tenant.Id, eventEntity.Id, user.Id, null,
            BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2,
                GuestRecoveryPolicyEnum.VerifiedEmailRequired), workflow.Id, null, "EUR", Now, Now.AddHours(1));
        context.AddRange(eventEntity, catalog, workflow, form, order);
        await context.SaveChangesAsync();
        var attempt = RegistrationAttempt.Create(tenant.Id, eventEntity.Id, order.Id, workflow.Id,
            requirement.Id, channel.Id, form.Id, version.Id,
            CapabilityTokenHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            null, null, Now, Now.AddMinutes(10));
        var submission = attempt.SubmitNative(
            RegistrationEvidenceHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), Now, null);
        context.Add(attempt);
        await context.SaveChangesAsync();
        var fulfillment = RegistrationRequirementFulfillment.CreateFulfilled(order, requirement, submission,
            RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id, Now);
        context.Add(fulfillment);

        var missingSubmission = await Assert.That(async () => await context.SaveChangesAsync()).Throws<DbUpdateException>();
        await Assert.That(((SqliteException)missingSubmission!.InnerException!).SqliteExtendedErrorCode).IsEqualTo(787);

        context.Entry(fulfillment).State = EntityState.Detached;
        context.Add(submission);
        await context.SaveChangesAsync();
        var otherTenant = new Tenant
        {
            FullName = "Other tenant", Slug = $"other-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        };
        context.Add(otherTenant);
        await context.SaveChangesAsync();
        fulfillment.TenantId = otherTenant.Id;
        context.Add(fulfillment);
        var wrongTenant = await Assert.That(async () => await context.SaveChangesAsync()).Throws<DbUpdateException>();
        await Assert.That(((SqliteException)wrongTenant!.InnerException!).SqliteExtendedErrorCode).IsEqualTo(787);
        context.Entry(fulfillment).State = EntityState.Detached;
        fulfillment.TenantId = tenant.Id;
        context.Add(fulfillment);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var persisted = await context.RegistrationRequirementFulfillments.AsNoTracking().SingleAsync();
        await Assert.That(persisted.SourceRegistrationSubmissionId).IsEqualTo(submission.Id);
        await Assert.That(persisted.IsSkipped).IsFalse();
    }
}
