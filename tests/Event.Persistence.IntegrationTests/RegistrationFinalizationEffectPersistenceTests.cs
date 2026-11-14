using Event.Testing.PostgreSql;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Schema;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Security.Cryptography;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class RegistrationFinalizationEffectPersistenceTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime UtcNow = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Category("Runtime")]
    public async Task EveryParticipantRequiresFulfillmentForEachCurrentParticipant()
    {
        string connectionString = await fixture.CreateIsolatedDatabaseAsync("phase85subjects_");
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using ExploreDbContext context = CreateContext(options, true);
        await AssertParticipantFulfillmentAsync(context);
    }

    internal static async Task AssertParticipantFulfillmentAsync(ExploreDbContext context)
    {
        (RegistrationOrder order, RegistrationWorkflow workflow) = CreateOrder(out FinalizationPrincipals principals);
        RegistrationRequirement required = RegistrationRequirement.Create(
            workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_SYNC,
            RegistrationRequirementSubjectTypeEnum.EveryParticipant, null, UtcNow);
        RegistrationParticipant first = RegistrationParticipant.Create(
            order.TenantId, order.Id, null, ParticipantTypeEnum.Adult, null);
        RegistrationParticipant second = RegistrationParticipant.Create(
            order.TenantId, order.Id, null, ParticipantTypeEnum.Adult, null);

        await InitializeAsync(context, principals);
        context.RegistrationOrders.Add(order);
        context.RegistrationWorkflows.Add(workflow);
        context.RegistrationRequirements.Add(required);
        context.RegistrationParticipants.AddRange(first, second);
        await context.SaveChangesAsync();
        await InsertFulfillmentAsync(context, order, required, RegistrationAnswerSubjectTypeEnum.Participant, first.Id);

        var repository = new RegistrationFinalizationRepository(context);
        bool oneOfTwoReady = await repository.AreMandatoryRequirementsFulfilledAsync(
            order.TenantId, order.Id, CancellationToken.None);
        await InsertFulfillmentAsync(context, order, required, RegistrationAnswerSubjectTypeEnum.Participant, second.Id);
        bool bothReady = await repository.AreMandatoryRequirementsFulfilledAsync(
            order.TenantId, order.Id, CancellationToken.None);

        await Assert.That(oneOfTwoReady).IsFalse();
        await Assert.That(bothReady).IsTrue();
    }

    [Test]
    [Category("Runtime")]
    public async Task RequiredApplicabilityMatrixUsesOrderPurchaserChildTicketAndSessionSubjects()
    {
        string connectionString = await fixture.CreateIsolatedDatabaseAsync("phase85matrix_");
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using ExploreDbContext context = CreateContext(options, true);
        await AssertApplicabilityMatrixAsync(context);
    }

    internal static async Task AssertApplicabilityMatrixAsync(ExploreDbContext context)
    {
        (RegistrationOrder order, RegistrationWorkflow workflow) = CreateOrder(out FinalizationPrincipals principals);
        var catalog = principals.Catalog;
        var ticketType = EventTicketType.Create(Guid.CreateVersion7(), order.TenantId, catalog.Id, "Matrix", "EUR",
            TicketPricingModeEnum.Free, null, null, null, ParticipantDataCollectionModeEnum.None,
            null, null, null, false, false, null, null, null, null);
        catalog.AddTicketType(ticketType, null);
        catalog.AddEntitlement(ticketType, TicketTypeEntitlement.CreateForEvent(ticketType.Id, order.TenantId, order.EventId, 1));
        catalog.Publish();
        Guid ticketTypeId = ticketType.Id;
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
        RegistrationOrderLine line = RegistrationOrderLine.Create(catalog, ticketType, order.Id, 2, null, null);
        order.AddLine(line);
        Guid lineId = line.Id;
        RegistrationTicketAssignment firstAssignment = RegistrationTicketAssignment.Create(
            Guid.CreateVersion7(), order.TenantId, order.Id, lineId, 1, null,
            AssignmentStatusEnum.Unassigned, null, UtcNow);
        RegistrationTicketAssignment secondAssignment = RegistrationTicketAssignment.Create(
            Guid.CreateVersion7(), order.TenantId, order.Id, lineId, 2, null,
            AssignmentStatusEnum.Unassigned, null, UtcNow);

        await InitializeAsync(context, principals);
        context.RegistrationOrders.Add(order);
        context.RegistrationWorkflows.Add(workflow);
        context.RegistrationRequirements.AddRange(requirements);
        context.RegistrationParticipants.AddRange(adult, child);
        await context.SaveChangesAsync();
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

        await Assert.That(oneOfTwoTicketsReady).IsFalse();
        await Assert.That(fullMatrixReady).IsTrue();
    }

    [Test]
    [Category("Runtime")]
    public async Task ConcurrentOptionalSkipAtomicallyConsumesAttemptAndRecordsOneFulfillment()
    {
        string connectionString = await fixture.CreateIsolatedDatabaseAsync("phase86skipatomic_");
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        (RegistrationOrder order, RegistrationWorkflow workflow) = CreateOrder(out FinalizationPrincipals principals);
        RegistrationRequirement optional = RegistrationRequirement.Create(
            workflow, 1, RegistrationRequirementCriticalityEnum.Optional, true,
            RegistrationRequirementCompletionEffectEnum.EnrichesRegistration,
            RegistrationAnswerSyncModeEnum.FULL_SYNC,
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
            await InitializeAsync(setup, principals);
            setup.RegistrationOrders.Add(order);
            setup.RegistrationWorkflows.Add(workflow);
            setup.RegistrationForms.Add(form);
            setup.RegistrationAttempts.Add(attempt);
            await setup.SaveChangesAsync();
        }

        await using ExploreDbContext firstContext = CreateContext(options, true);
        await using ExploreDbContext secondContext = CreateContext(options, true);
        await firstContext.Database.OpenConnectionAsync();
        await secondContext.Database.OpenConnectionAsync();
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
        string connectionString = await fixture.CreateIsolatedDatabaseAsync("phase85_");
        string suffix = Guid.CreateVersion7().ToString("N");
        var enrollment = new PostgresNativeQueueEnrollment("postgres", $"final_global_{suffix}",
            $"final_runtime_{suffix}", $"final_instance_{suffix}", $"final_tenant_{suffix}");
        string globalPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var purposeLogins = new Dictionary<PrimaryDatabaseRole, (string Username, string Password)>
        {
            [PrimaryDatabaseRole.Global] = (enrollment.GlobalLogin, globalPassword),
            [PrimaryDatabaseRole.Runtime] = (enrollment.RuntimeLogin, Convert.ToHexString(RandomNumberGenerator.GetBytes(24))),
            [PrimaryDatabaseRole.EmailPolicyInstanceMutation] = (enrollment.EmailInstanceMutationLogin, Convert.ToHexString(RandomNumberGenerator.GetBytes(24))),
            [PrimaryDatabaseRole.EmailPolicyTenantMutation] = (enrollment.EmailTenantMutationLogin, Convert.ToHexString(RandomNumberGenerator.GetBytes(24)))
        };
        await PostgresTestRoleBootstrap.ProvisionAsync(connectionString, enrollment, purposeLogins);
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        (RegistrationOrder order, RegistrationWorkflow workflow) = CreateOrder(out FinalizationPrincipals principals);
        RegistrationRequirement optional = RegistrationRequirement.Create(
            workflow, 1, RegistrationRequirementCriticalityEnum.Optional, true,
            RegistrationRequirementCompletionEffectEnum.EnrichesRegistration,
            RegistrationAnswerSyncModeEnum.FULL_SYNC,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, UtcNow);

        await using (ExploreDbContext setup = CreateContext(options, true))
        {
            await InitializeAsync(setup, principals);
            setup.RegistrationOrders.Add(order);
            setup.RegistrationWorkflows.Add(workflow);
            setup.RegistrationRequirements.Add(optional);
            await setup.SaveChangesAsync();
            RegistrationRequirementFulfillment skip = RegistrationRequirementFulfillment.CreateSkipped(
                order, optional, RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id, UtcNow);
            bool ready = await new RegistrationFinalizationRepository(setup)
                .RecordFulfillmentAsync(skip, UtcNow, CancellationToken.None);
            await Assert.That(ready).IsTrue();
            await Assert.That(await setup.RegistrationRequirementFulfillments.CountAsync()).IsEqualTo(1);
            await Assert.That(await setup.RegistrationFinalizationEffects.CountAsync()).IsEqualTo(1);
            await PostgresRegistrationFinalizationQueueContract.ApplyAsync(setup, enrollment);
        }

        var databaseConnection = new NpgsqlConnectionStringBuilder(connectionString);
        var global = new GlobalExploreDbContextFactory(new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Global,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = databaseConnection.Host,
            Port = databaseConnection.Port,
            Database = databaseConnection.Database,
            Schema = RelationalModelNamespace.DefaultSchema,
            Username = enrollment.GlobalLogin,
            Password = globalPassword,
            TlsMode = PrimaryDatabaseTlsMode.Disabled
        });
        await using ExploreDbContext firstContext = CreateContext(options, false);
        await using ExploreDbContext secondContext = CreateContext(options, false);
        firstContext.TenantContext = new FinalizationTenantContext(order.TenantId);
        secondContext.TenantContext = new FinalizationTenantContext(order.TenantId);
        firstContext.BindTenantLease();
        secondContext.BindTenantLease();
        var first = new RegistrationFinalizationRepository(firstContext);
        var second = new RegistrationFinalizationRepository(secondContext);
        var claims = await Task.WhenAll(
            ClaimAsync(global, "worker-a", UtcNow.AddSeconds(1)),
            ClaimAsync(global, "worker-b", UtcNow.AddSeconds(1)));
        RegistrationFinalizationClaim abandonedClaim = claims.SelectMany(value => value).Single();
        IReadOnlyList<RegistrationFinalizationClaim> prematureRecovery = await ClaimAsync(
            global, "worker-c", UtcNow.AddSeconds(30));
        var recoveredClaims = await Task.WhenAll(
            ClaimAsync(global, "worker-c", UtcNow.AddMinutes(1).AddSeconds(1)),
            ClaimAsync(global, "worker-d", UtcNow.AddMinutes(1).AddSeconds(1)));
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

    private async Task AssertConvergesAsync(string databaseName, bool callbackFirst)
    {
        string connectionString = await fixture.CreateIsolatedDatabaseAsync($"{databaseName}_");
        DbContextOptions<ExploreDbContext> options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await AssertConvergesAsync(options, callbackFirst);
    }

    internal static async Task AssertConvergesAsync(DbContextOptions<ExploreDbContext> options, bool callbackFirst)
    {
        (RegistrationOrder order, RegistrationWorkflow workflow) = CreateOrder(out FinalizationPrincipals principals);
        RegistrationRequirement required = CreateRequired(
            workflow, 1, RegistrationRequirementSubjectTypeEnum.AllOrders, null);

        await using (ExploreDbContext setup = CreateContext(options, true))
        {
            await InitializeAsync(setup, principals);
            setup.RegistrationOrders.Add(order);
            setup.RegistrationWorkflows.Add(workflow);
            setup.RegistrationRequirements.Add(required);
            await setup.SaveChangesAsync();
        }

        await using ExploreDbContext context = CreateContext(options, true);
        await context.Database.OpenConnectionAsync();
        var repository = new RegistrationFinalizationRepository(context);
        RegistrationSubmission callbackSubmission = await CreateSubmissionAsync(context, order, required, UtcNow, provider: true);
        RegistrationSubmission userReturnSubmission = await CreateSubmissionAsync(context, order, required, UtcNow.AddSeconds(1));
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
    }

    private static async Task<RegistrationSubmission> CreateSubmissionAsync(
        ExploreDbContext context,
        RegistrationOrder order,
        RegistrationRequirement requirement,
        DateTime receivedAt,
        bool provider = false)
    {
        string suffix = Guid.CreateVersion7().ToString("N");
        var form = RegistrationForm.Create(order.TenantId, order.EventId, "platform.registration",
            $"fulfillment-{suffix}", "Fulfillment", UtcNow);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, UtcNow);
        form.AddVersion(version);
        context.Add(form);
        RegistrationProviderBinding? binding = null;
        RegistrationEvidenceHash? mappingHash = null;
        if (provider)
        {
            var connection = RegistrationProviderConnection.Create(order.TenantId, "Fulfillment provider",
                RegistrationProviderKindEnum.ExternalForm, RegistrationProviderDeploymentKindEnum.HostedSaas,
                "FORMBRICKS", "HOSTED_SAAS", "v1", "formbricks-policy-v1", "evidence-v1",
                "https://provider.example/management", "https://provider.example", $"workspace-{suffix}",
                null, null, UtcNow);
            binding = RegistrationProviderBinding.Create(order.TenantId, connection.Id, form.Id, version.Id,
                RegistrationProviderPresentationModeEnum.Redirect, RegistrationProviderCollectionModeEnum.ProviderHosted,
                RegistrationProviderCompletionModeEnum.Callback, RegistrationProviderTrustLevelEnum.SelectedFields, null, UtcNow);
            mappingHash = RegistrationEvidenceHash.Create(Hash($"mapping-{suffix}"));
            binding.Publish(mappingHash, UtcNow);
            context.AddRange(connection, binding);
        }
        int ordinal = await context.RegistrationChannels.CountAsync(value => value.RegistrationRequirementId == requirement.Id) + 1;
        var channel = RegistrationChannel.Create(requirement, ordinal, !provider, binding?.Id, UtcNow);
        context.Add(channel);
        await context.SaveChangesAsync();
        RegistrationAttempt attempt = RegistrationAttempt.Create(
            order.TenantId, order.EventId, order.Id, requirement.RegistrationWorkflowId, requirement.Id,
            channel.Id, form.Id, version.Id, CapabilityTokenHash.Create(Hash($"capability-{suffix}")),
            binding?.Id, mappingHash, UtcNow, UtcNow.AddMinutes(10));
        var evidence = RegistrationEvidenceHash.Create(Hash($"evidence-{suffix}"));
        var transport = RegistrationTransportIdempotencyHash.Create(Hash($"transport-{suffix}"));
        var submission = provider
            ? attempt.SubmitProvider(evidence, receivedAt, transport, "provider-submission", "revision-1", null, null)
            : attempt.SubmitNative(evidence, receivedAt, transport);
        context.AddRange(attempt, submission);
        await context.SaveChangesAsync();
        return submission;
    }

    private static string Hash(string value) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));

    private static Task<IReadOnlyList<RegistrationFinalizationClaim>> ClaimAsync(
        IGlobalExploreDbContextFactory factory, string owner, DateTime claimedAt)
        => new RegistrationFinalizationQueueRepository(factory).ClaimDueAsync(
            owner, 1, claimedAt, TimeSpan.FromMinutes(1), CancellationToken.None);

    private sealed record FinalizationTenantContext(Guid TenantId) : Explore.Application.Contracts.Infrastructure.ITenantContext;

    private static ExploreDbContext CreateContext(DbContextOptions<ExploreDbContext> options, bool bypassTenantFilter)
    {
        var context = new ExploreDbContext(options);
        if (bypassTenantFilter)
        {
            context.EnableTenantFilterBypass("Phase 8.5 real PostgreSQL setup or verification.");
            context.BindTenantLease();
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
        RegistrationAnswerSyncModeEnum.FULL_SYNC, subjectType, subjectId, UtcNow);

    private static async Task InsertFulfillmentAsync(
        ExploreDbContext context,
        RegistrationOrder order,
        RegistrationRequirement requirement,
        RegistrationAnswerSubjectTypeEnum subjectType,
        Guid subjectId)
    {
        RegistrationSubmission submission = await CreateSubmissionAsync(context, order, requirement, UtcNow);
        context.Add(RegistrationRequirementFulfillment.CreateFulfilled(order, requirement, submission, subjectType, subjectId, UtcNow));
        await context.SaveChangesAsync();
    }

    private static (RegistrationOrder Order, RegistrationWorkflow Workflow) CreateOrder(out FinalizationPrincipals principals)
    {
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Finalization",
            Slug = $"finalization-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User { Id = Guid.CreateVersion7(), Pii = new UserPii { Email = $"{Guid.CreateVersion7():N}@example.test", FirstName = "Finalization", LastName = "Registrant" } };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            Pii = new ActorPii { DisplayName = "Organizer" },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id
        };
        var eventEntity = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = "Finalization",
            TenantId = tenant.Id,
            Tenant = null!,
            ActorId = actor.Id,
            Actor = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        Guid tenantId = tenant.Id;
        Guid eventId = eventEntity.Id;
        var catalog = EventTicketCatalogVersion.Create(tenantId, eventId, "EUR", 1);
        principals = new(tenant, user, actor, eventEntity, catalog);
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(tenantId, eventId, "REGISTRATION", UtcNow);
        RegistrationOrder order = RegistrationOrder.Create(
            tenantId, eventId, user.Id, null, BookingPartyTypeEnum.Individual,
            catalog.Id, RegistrationParticipationSnapshot.Create(
                Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            workflow.Id, null, "EUR", UtcNow, UtcNow.AddMinutes(15));
        return (order, workflow);
    }

    private static async Task InitializeAsync(ExploreDbContext context, FinalizationPrincipals principals)
    {
        context.BindTenantLease();
        await context.Database.EnsureCreatedAsync();
        await LookupTableSeeder.SeedAsync(context);
        context.AddRange(principals.Tenant, principals.User, principals.Actor, principals.Event, principals.Catalog);
        await context.SaveChangesAsync();
    }

    private sealed record FinalizationPrincipals(Tenant Tenant, User User, Actor Actor,
        Explore.Domain.Event Event, EventTicketCatalogVersion Catalog);
}
