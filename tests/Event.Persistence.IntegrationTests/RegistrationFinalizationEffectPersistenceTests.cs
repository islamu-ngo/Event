// ABOUTME: Proves optional requirement progress and finalization-effect fencing against real PostgreSQL persistence.
// ABOUTME: Runs concurrent workers to show one durable effect is claimed and completed exactly once.

using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests;

public sealed class RegistrationFinalizationEffectPersistenceTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Category("Runtime")]
    public async Task EveryParticipantRequiresFulfillmentForEachCurrentParticipant()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("phase85subjects")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await database.StartAsync();
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        (RegistrationOrder order, RegistrationWorkflow workflow) = CreateOrder();
        RegistrationRequirement required = RegistrationRequirement.Create(
            workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.EveryParticipant, null, UtcNow);
        RegistrationParticipant first = RegistrationParticipant.Create(
            order.TenantId, order.Id, null, ParticipantTypeEnum.Adult, null);
        RegistrationParticipant second = RegistrationParticipant.Create(
            order.TenantId, order.Id, null, ParticipantTypeEnum.Adult, null);

        await using ExploreDbContext context = CreateContext(options, true);
        await context.Database.EnsureCreatedAsync();
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        context.RegistrationOrders.Add(order);
        context.RegistrationWorkflows.Add(workflow);
        context.RegistrationRequirements.Add(required);
        context.RegistrationParticipants.AddRange(first, second);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO islamu_event.registration_requirement_fulfillments
                (id, tenant_id, event_id, registration_order_id, registration_workflow_id,
                 registration_requirement_id, subject_type_id, subject_id,
                 source_registration_submission_id, is_skipped, recorded_at, created_at)
            VALUES
                ({Guid.CreateVersion7()}, {order.TenantId}, {order.EventId}, {order.Id}, {workflow.Id},
                 {required.Id}, {(int)RegistrationAnswerSubjectTypeEnum.Participant}, {first.Id},
                 {Guid.CreateVersion7()}, false, {UtcNow}, {UtcNow})
            """);

        var repository = new RegistrationFinalizationRepository(context);
        bool oneOfTwoReady = await repository.AreMandatoryRequirementsFulfilledAsync(
            order.TenantId, order.Id, CancellationToken.None);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO islamu_event.registration_requirement_fulfillments
                (id, tenant_id, event_id, registration_order_id, registration_workflow_id,
                 registration_requirement_id, subject_type_id, subject_id,
                 source_registration_submission_id, is_skipped, recorded_at, created_at)
            VALUES
                ({Guid.CreateVersion7()}, {order.TenantId}, {order.EventId}, {order.Id}, {workflow.Id},
                 {required.Id}, {(int)RegistrationAnswerSubjectTypeEnum.Participant}, {second.Id},
                 {Guid.CreateVersion7()}, false, {UtcNow}, {UtcNow})
            """);
        bool bothReady = await repository.AreMandatoryRequirementsFulfilledAsync(
            order.TenantId, order.Id, CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync("SET session_replication_role = origin");

        await Assert.That(oneOfTwoReady).IsFalse();
        await Assert.That(bothReady).IsTrue();
    }

    [Test]
    [Category("Runtime")]
    public async Task RequiredApplicabilityMatrixUsesOrderPurchaserChildTicketAndSessionSubjects()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("phase85matrix")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await database.StartAsync();
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        (RegistrationOrder order, RegistrationWorkflow workflow) = CreateOrder();
        Guid ticketTypeId = Guid.CreateVersion7();
        Guid sessionSelectionId = Guid.CreateVersion7();
        RegistrationRequirement[] requirements =
        [
            CreateRequired(workflow, 1, RegistrationRequirementSubjectTypeEnum.AllOrders, null),
            CreateRequired(workflow, 2, RegistrationRequirementSubjectTypeEnum.SpecificTicketType, ticketTypeId),
            CreateRequired(workflow, 3, RegistrationRequirementSubjectTypeEnum.EveryParticipant, null),
            CreateRequired(workflow, 4, RegistrationRequirementSubjectTypeEnum.LeadBookerOnly, null),
            CreateRequired(workflow, 5, RegistrationRequirementSubjectTypeEnum.ChildParticipants, null),
            CreateRequired(workflow, 6, RegistrationRequirementSubjectTypeEnum.SpecificSessionSelection, sessionSelectionId)
        ];
        RegistrationParticipant adult = RegistrationParticipant.Create(
            order.TenantId, order.Id, null, ParticipantTypeEnum.Adult, null);
        RegistrationParticipant child = RegistrationParticipant.Create(
            order.TenantId, order.Id, null, ParticipantTypeEnum.Child, adult);
        Guid lineId = Guid.CreateVersion7();
        RegistrationTicketAssignment firstAssignment = RegistrationTicketAssignment.Create(
            Guid.CreateVersion7(), order.TenantId, order.Id, lineId, 1, null,
            AssignmentStatusEnum.Unassigned, null, UtcNow);
        RegistrationTicketAssignment secondAssignment = RegistrationTicketAssignment.Create(
            Guid.CreateVersion7(), order.TenantId, order.Id, lineId, 2, null,
            AssignmentStatusEnum.Unassigned, null, UtcNow);

        await using ExploreDbContext context = CreateContext(options, true);
        await context.Database.EnsureCreatedAsync();
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        context.RegistrationOrders.Add(order);
        context.RegistrationWorkflows.Add(workflow);
        context.RegistrationRequirements.AddRange(requirements);
        context.RegistrationParticipants.AddRange(adult, child);
        await context.SaveChangesAsync();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO islamu_event.registration_order_lines
                (id, concurrency_stamp, created_at, currency_code_snapshot, line_subtotal_snapshot,
                 pre_discount_line_subtotal_minor_snapshot, promotion_discount_amount_minor_snapshot,
                 post_discount_line_subtotal_minor_snapshot,
                 quantity, registration_order_id, tenant_id, ticket_catalog_version_id,
                 ticket_pricing_mode_snapshot, ticket_type_id, ticket_type_name_snapshot, unit_price_amount_snapshot)
            VALUES
                ({lineId}, {Guid.CreateVersion7()}, {UtcNow}, {"EUR"}, {0L}, {0L}, {0L}, {0L}, {2}, {order.Id},
                 {order.TenantId}, {order.TicketCatalogVersionId}, {1}, {ticketTypeId}, {"Matrix"}, {0L})
            """);
        context.RegistrationTicketAssignments.AddRange(firstAssignment, secondAssignment);
        await context.SaveChangesAsync();
        await InsertFulfillmentAsync(context, order, requirements[0], RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id);
        await InsertFulfillmentAsync(context, order, requirements[1], RegistrationAnswerSubjectTypeEnum.TicketAssignment, firstAssignment.Id);
        await InsertFulfillmentAsync(context, order, requirements[2], RegistrationAnswerSubjectTypeEnum.Participant, adult.Id);
        await InsertFulfillmentAsync(context, order, requirements[2], RegistrationAnswerSubjectTypeEnum.Participant, child.Id);
        await InsertFulfillmentAsync(context, order, requirements[3], RegistrationAnswerSubjectTypeEnum.Purchaser, order.Id);
        await InsertFulfillmentAsync(context, order, requirements[4], RegistrationAnswerSubjectTypeEnum.Participant, child.Id);
        await InsertFulfillmentAsync(context, order, requirements[5], RegistrationAnswerSubjectTypeEnum.SessionSelection, sessionSelectionId);

        var repository = new RegistrationFinalizationRepository(context);
        bool oneOfTwoTicketsReady = await repository.AreMandatoryRequirementsFulfilledAsync(
            order.TenantId, order.Id, CancellationToken.None);
        await InsertFulfillmentAsync(context, order, requirements[1], RegistrationAnswerSubjectTypeEnum.TicketAssignment, secondAssignment.Id);
        bool fullMatrixReady = await repository.AreMandatoryRequirementsFulfilledAsync(
            order.TenantId, order.Id, CancellationToken.None);
        await context.Database.ExecuteSqlRawAsync("SET session_replication_role = origin");

        await Assert.That(oneOfTwoTicketsReady).IsFalse();
        await Assert.That(fullMatrixReady).IsTrue();
    }

    [Test]
    [Category("Runtime")]
    public async Task ConcurrentOptionalSkipAtomicallyConsumesAttemptAndRecordsOneFulfillment()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("phase86skipatomic")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await database.StartAsync();
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        (RegistrationOrder order, RegistrationWorkflow workflow) = CreateOrder();
        RegistrationRequirement optional = RegistrationRequirement.Create(
            workflow, 1, RegistrationRequirementCriticalityEnum.Optional, true,
            RegistrationRequirementCompletionEffectEnum.EnrichesRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, UtcNow);
        RegistrationChannel channel = RegistrationChannel.Create(optional, 1, true, null, UtcNow);
        optional.AddChannel(channel);
        workflow.AddRequirement(optional);
        RegistrationForm form = RegistrationForm.Create(
            order.TenantId, order.EventId, "registration", "atomic_skip", "Atomic skip", UtcNow);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, UtcNow);
        form.AddVersion(version);
        RegistrationAttempt attempt = RegistrationAttempt.Create(
            order.TenantId, order.EventId, order.Id, workflow.Id, optional.Id, channel.Id,
            form.Id, version.Id, CapabilityTokenHash.Create(Convert.ToBase64String(new byte[32])),
            null, null, UtcNow, UtcNow.AddMinutes(10));

        await using (ExploreDbContext setup = CreateContext(options, true))
        {
            await setup.Database.EnsureCreatedAsync();
            await setup.Database.OpenConnectionAsync();
            await setup.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
            setup.RegistrationOrders.Add(order);
            setup.RegistrationWorkflows.Add(workflow);
            setup.RegistrationForms.Add(form);
            setup.RegistrationAttempts.Add(attempt);
            await setup.SaveChangesAsync();
            await setup.Database.ExecuteSqlRawAsync("SET session_replication_role = origin");
        }

        await using ExploreDbContext firstContext = CreateContext(options, true);
        await using ExploreDbContext secondContext = CreateContext(options, true);
        await firstContext.Database.OpenConnectionAsync();
        await secondContext.Database.OpenConnectionAsync();
        await firstContext.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        await secondContext.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        RegistrationAttempt firstAttempt = await firstContext.RegistrationAttempts.AsNoTracking().SingleAsync();
        RegistrationAttempt secondAttempt = await secondContext.RegistrationAttempts.AsNoTracking().SingleAsync();
        Guid expectedStamp = firstAttempt.ConcurrencyStamp;
        DateTime skippedAt = UtcNow.AddMinutes(1);
        firstAttempt.Consume(skippedAt);
        secondAttempt.Consume(skippedAt);
        RegistrationRequirementFulfillment firstSkip = RegistrationRequirementFulfillment.CreateSkipped(
            order, optional, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id, skippedAt);
        RegistrationRequirementFulfillment secondSkip = RegistrationRequirementFulfillment.CreateSkipped(
            order, optional, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id, skippedAt);
        var firstRepository = new RegistrationFinalizationRepository(firstContext);
        var secondRepository = new RegistrationFinalizationRepository(secondContext);

        bool[] results = await Task.WhenAll(
            firstRepository.TryRecordSkippedFulfillmentsAndConsumeAttemptAsync(
                firstAttempt, expectedStamp, [firstSkip], skippedAt, CancellationToken.None),
            secondRepository.TryRecordSkippedFulfillmentsAndConsumeAttemptAsync(
                secondAttempt, expectedStamp, [secondSkip], skippedAt, CancellationToken.None));

        await Assert.That(results.Count(result => result)).IsEqualTo(1);
        await using ExploreDbContext verification = CreateContext(options, true);
        RegistrationAttempt persistedAttempt = await verification.RegistrationAttempts.AsNoTracking().SingleAsync();
        await Assert.That(persistedAttempt.StatusId).IsEqualTo((int)RegistrationAttemptStatusEnum.Consumed);
        await Assert.That(await verification.RegistrationRequirementFulfillments.CountAsync()).IsEqualTo(1);
        await Assert.That(await verification.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Category("Runtime")]
    public async Task OptionalSkipQueuesOneEffectAndConcurrentWorkersCompleteItOnce()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("phase85")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await database.StartAsync();
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        (RegistrationOrder order, RegistrationWorkflow workflow) = CreateOrder();
        RegistrationRequirement optional = RegistrationRequirement.Create(
            workflow, 1, RegistrationRequirementCriticalityEnum.Optional, true,
            RegistrationRequirementCompletionEffectEnum.EnrichesRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, UtcNow);

        await using (ExploreDbContext setup = CreateContext(options, true))
        {
            await setup.Database.EnsureCreatedAsync();
            await setup.Database.OpenConnectionAsync();
            await setup.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
            setup.RegistrationOrders.Add(order);
            setup.RegistrationWorkflows.Add(workflow);
            setup.RegistrationRequirements.Add(optional);
            await setup.SaveChangesAsync();
            RegistrationRequirementFulfillment skip = RegistrationRequirementFulfillment.CreateSkipped(
                order, optional, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id, UtcNow);
            bool ready = await new RegistrationFinalizationRepository(setup)
                .RecordFulfillmentAsync(skip, UtcNow, CancellationToken.None);
            await setup.Database.ExecuteSqlRawAsync("SET session_replication_role = origin");
            await Assert.That(ready).IsTrue();
            await Assert.That(await setup.RegistrationRequirementFulfillments.CountAsync()).IsEqualTo(1);
            await Assert.That(await setup.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(1);
        }

        await using ExploreDbContext firstContext = CreateContext(options, false);
        await using ExploreDbContext secondContext = CreateContext(options, false);
        var first = new RegistrationFinalizationRepository(firstContext);
        var second = new RegistrationFinalizationRepository(secondContext);
        var claims = await Task.WhenAll(
            first.ClaimDueAsync("worker-a", 1, UtcNow.AddSeconds(1), TimeSpan.FromMinutes(1), CancellationToken.None),
            second.ClaimDueAsync("worker-b", 1, UtcNow.AddSeconds(1), TimeSpan.FromMinutes(1), CancellationToken.None));
        RegistrationFinalizationClaim abandonedClaim = claims.SelectMany(value => value).Single();
        IReadOnlyList<RegistrationFinalizationClaim> prematureRecovery = await first.ClaimDueAsync(
            "worker-c", 1, UtcNow.AddSeconds(30), TimeSpan.FromMinutes(1), CancellationToken.None);
        var recoveredClaims = await Task.WhenAll(
            first.ClaimDueAsync("worker-c", 1, UtcNow.AddMinutes(1).AddSeconds(1), TimeSpan.FromMinutes(1), CancellationToken.None),
            second.ClaimDueAsync("worker-d", 1, UtcNow.AddMinutes(1).AddSeconds(1), TimeSpan.FromMinutes(1), CancellationToken.None));
        RegistrationFinalizationClaim recoveredClaim = recoveredClaims.SelectMany(value => value).Single();
        bool staleCompletion = await first.CompleteAsync(
            abandonedClaim, UtcNow.AddMinutes(1).AddSeconds(2), CancellationToken.None);
        bool completed = await second.CompleteAsync(
            recoveredClaim, UtcNow.AddMinutes(1).AddSeconds(2), CancellationToken.None);

        await Assert.That(prematureRecovery).IsEmpty();
        await Assert.That(staleCompletion).IsFalse();
        await Assert.That(completed).IsTrue();
        await using ExploreDbContext verification = CreateContext(options, true);
        RegistrationFinalizationEffect persisted = await verification.RegistrationFinalizationEffects.AsNoTracking().SingleAsync();
        await Assert.That(persisted.Status).IsEqualTo(OutboxMessageStatus.Completed);
        await Assert.That(persisted.AttemptCount).IsEqualTo(2);
        await Assert.That(persisted.ProcessingFence).IsEqualTo(2);
    }

    [Test]
    [Category("Runtime")]
    public async Task ProviderCallbackAndUserReturnFulfillmentOrdersConvergeToOneFinalizationEffect()
    {
        await AssertConvergesAsync("phase95callbackfirst", callbackFirst: true);
        await AssertConvergesAsync("phase95userfirst", callbackFirst: false);
    }

    private static async Task AssertConvergesAsync(string databaseName, bool callbackFirst)
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase(databaseName)
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await database.StartAsync();
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        (RegistrationOrder order, RegistrationWorkflow workflow) = CreateOrder();
        RegistrationRequirement required = CreateRequired(
            workflow, 1, RegistrationRequirementSubjectTypeEnum.AllOrders, null);

        await using (ExploreDbContext setup = CreateContext(options, true))
        {
            await setup.Database.EnsureCreatedAsync();
            await setup.Database.OpenConnectionAsync();
            await setup.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
            setup.RegistrationOrders.Add(order);
            setup.RegistrationWorkflows.Add(workflow);
            setup.RegistrationRequirements.Add(required);
            await setup.SaveChangesAsync();
            await setup.Database.ExecuteSqlRawAsync("SET session_replication_role = origin");
        }

        await using ExploreDbContext context = CreateContext(options, true);
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET session_replication_role = replica");
        var repository = new RegistrationFinalizationRepository(context);
        RegistrationSubmission callbackSubmission = CreateProviderSubmission(order, required, UtcNow);
        RegistrationSubmission userReturnSubmission = CreateNativeSubmission(order, required, UtcNow.AddSeconds(1));
        RegistrationRequirementFulfillment callback = RegistrationRequirementFulfillment.CreateFulfilled(
            order, required, callbackSubmission, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id, UtcNow);
        RegistrationRequirementFulfillment userReturn = RegistrationRequirementFulfillment.CreateFulfilled(
            order, required, userReturnSubmission, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id, UtcNow.AddSeconds(1));

        bool firstReady = await repository.RecordFulfillmentAsync(
            callbackFirst ? callback : userReturn, UtcNow, CancellationToken.None);
        bool secondReady = await repository.RecordFulfillmentAsync(
            callbackFirst ? userReturn : callback, UtcNow.AddSeconds(1), CancellationToken.None);

        await Assert.That(firstReady).IsTrue();
        await Assert.That(secondReady).IsTrue();
        await Assert.That(await context.RegistrationRequirementFulfillments.CountAsync()).IsEqualTo(1);
        await Assert.That(await context.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(1);
        RegistrationFinalizationEffect effect = await context.RegistrationFinalizationEffects.SingleAsync();
        await Assert.That(effect.RegistrationOrderId).IsEqualTo(order.Id);
        await Assert.That(effect.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await context.Database.ExecuteSqlRawAsync("SET session_replication_role = origin");
    }

    private static RegistrationSubmission CreateNativeSubmission(
        RegistrationOrder order,
        RegistrationRequirement requirement,
        DateTime receivedAt)
    {
        RegistrationAttempt attempt = RegistrationAttempt.Create(
            order.TenantId, order.EventId, order.Id, requirement.RegistrationWorkflowId, requirement.Id,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            CapabilityTokenHash.Create(Hash("native-capability")), null, null, UtcNow, UtcNow.AddMinutes(10));
        return attempt.SubmitNative(
            RegistrationEvidenceHash.Create(Hash("native-evidence")), receivedAt,
            RegistrationTransportIdempotencyHash.Create(Hash("native-transport")));
    }

    private static RegistrationSubmission CreateProviderSubmission(
        RegistrationOrder order,
        RegistrationRequirement requirement,
        DateTime receivedAt)
    {
        RegistrationAttempt attempt = RegistrationAttempt.Create(
            order.TenantId, order.EventId, order.Id, requirement.RegistrationWorkflowId, requirement.Id,
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            CapabilityTokenHash.Create(Hash("provider-capability")), Guid.CreateVersion7(),
            RegistrationEvidenceHash.Create(Hash("provider-mapping")), UtcNow, UtcNow.AddMinutes(10));
        return attempt.SubmitProvider(
            RegistrationEvidenceHash.Create(Hash("provider-evidence")), receivedAt,
            RegistrationTransportIdempotencyHash.Create(Hash("provider-transport")),
            "provider-submission", "revision-1", null, null);
    }

    private static string Hash(string value) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private static ExploreDbContext CreateContext(DbContextOptions<ExploreDbContext> options, bool bypassTenantFilter)
    {
        var context = new ExploreDbContext(options);
        if (bypassTenantFilter)
        {
            context.EnableTenantFilterBypass("Phase 8.5 real PostgreSQL setup or verification.");
        }
        return context;
    }

    private static RegistrationRequirement CreateRequired(
        RegistrationWorkflow workflow,
        int ordinal,
        RegistrationRequirementSubjectTypeEnum subjectType,
        Guid? subjectId) => RegistrationRequirement.Create(
        workflow, ordinal, RegistrationRequirementCriticalityEnum.Required, false,
        RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
        RegistrationAnswerSyncModeEnum.FULL_CANONICAL, subjectType, subjectId, UtcNow);

    private static async Task InsertFulfillmentAsync(
        ExploreDbContext context,
        RegistrationOrder order,
        RegistrationRequirement requirement,
        RegistrationAnswerSubjectTypeEnum subjectType,
        Guid subjectId)
    {
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO islamu_event.registration_requirement_fulfillments
                (id, tenant_id, event_id, registration_order_id, registration_workflow_id,
                 registration_requirement_id, subject_type_id, subject_id,
                 source_registration_submission_id, is_skipped, recorded_at, created_at)
            VALUES
                ({Guid.CreateVersion7()}, {order.TenantId}, {order.EventId}, {order.Id}, {requirement.RegistrationWorkflowId},
                 {requirement.Id}, {(int)subjectType}, {subjectId}, {Guid.CreateVersion7()}, false, {UtcNow}, {UtcNow})
            """);
    }

    private static (RegistrationOrder Order, RegistrationWorkflow Workflow) CreateOrder()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(tenantId, eventId, "REGISTRATION", UtcNow);
        RegistrationOrder order = RegistrationOrder.Create(
            tenantId, eventId, Guid.CreateVersion7(), null, BookingPartyTypeEnum.Individual,
            Guid.CreateVersion7(), RegistrationParticipationSnapshot.Create(
                Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            workflow.Id, null, "EUR", UtcNow, UtcNow.AddMinutes(15));
        return (order, workflow);
    }
}
