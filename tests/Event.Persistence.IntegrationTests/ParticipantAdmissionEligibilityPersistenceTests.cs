using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;
using DomainEvent = Explore.Domain.Event;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<PostgreSqlContainerFixture>(
    Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ParticipantAdmissionEligibilityPersistenceTests(
    PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime UtcNow =
        new(
            2026,
            8,
            27,
            12,
            0,
            0,
            DateTimeKind.Utc);

    [Test]
    public async Task EligibilityAuthorityIsTenantFilteredAndAssignmentUnique()
    {
        await using ExploreDbContext context =
            fixture.CreateDbContext();
        IModel model =
            context.GetService<IDesignTimeModel>().Model;
        IEntityType? eligibility = model.FindEntityType(
            typeof(ParticipantAdmissionEligibility));

        await Assert.That(eligibility).IsNotNull();
        await Assert.That(
                eligibility!.FindDeclaredQueryFilter(
                    QueryFilterNames.Tenant))
            .IsNotNull();
        await Assert.That(
                eligibility.GetIndexes().Any(index =>
                    index.IsUnique
                    && HasProperties(
                        index.Properties,
                        nameof(ParticipantAdmissionEligibility.TenantId),
                        nameof(ParticipantAdmissionEligibility.RegistrationTicketAssignmentId))))
            .IsTrue();
        await Assert.That(
                eligibility.GetForeignKeys().Any(key =>
                    key.PrincipalEntityType.ClrType ==
                    typeof(RegistrationTicketAssignment)
                    && HasProperties(
                        key.Properties,
                        nameof(ParticipantAdmissionEligibility.TenantId),
                        nameof(ParticipantAdmissionEligibility.RegistrationOrderId),
                        nameof(ParticipantAdmissionEligibility.RegistrationTicketAssignmentId),
                        nameof(ParticipantAdmissionEligibility.RegistrationOrderLineId))))
            .IsTrue();
        await Assert.That(
                eligibility.GetForeignKeys().Any(key =>
                    key.PrincipalEntityType.ClrType ==
                    typeof(RegistrationParticipant)
                    && HasProperties(
                        key.Properties,
                        nameof(ParticipantAdmissionEligibility.TenantId),
                        nameof(ParticipantAdmissionEligibility.RegistrationOrderId),
                        nameof(ParticipantAdmissionEligibility.ParticipantId))))
            .IsTrue();
    }

    [Test]
    public async Task AuthorityPersistsOnlyReferencesAndBoundedStateNotParticipantPii()
    {
        await using ExploreDbContext context =
            fixture.CreateDbContext();
        IEntityType? eligibility =
            context.GetService<IDesignTimeModel>().Model
                .FindEntityType(
                    typeof(ParticipantAdmissionEligibility));

        await Assert.That(eligibility).IsNotNull();
        string[] forbidden =
        [
            "email",
            "phone",
            "name",
            "address",
            "answer",
            "consenttext",
        ];
        await Assert.That(
                eligibility!.GetProperties().Any(property =>
                    forbidden.Any(fragment =>
                        property.Name.Contains(
                            fragment,
                            StringComparison.OrdinalIgnoreCase))))
            .IsFalse();
        await Assert.That(
                eligibility.FindProperty(
                    nameof(ParticipantAdmissionEligibility.RequirementsCompletedAt)))
            .IsNotNull();
        await Assert.That(
                eligibility.FindProperty(
                    nameof(ParticipantAdmissionEligibility.SubjectConsentRecordId)))
            .IsNotNull();
        await Assert.That(
                eligibility.FindProperty(
                    nameof(ParticipantAdmissionEligibility.SubjectUserId)))
            .IsNotNull();
    }

    [Test]
    public async Task ProvisionalPurchaserDataCannotBecomeAdultSubjectCompletion()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        Guid lineId = Guid.CreateVersion7();
        RegistrationParticipant adult =
            RegistrationParticipant.Create(
                tenantId,
                orderId,
                linkedUserId: null,
                ParticipantTypeEnum.Adult,
                guardian: null);
        RegistrationTicketAssignment assignment =
            RegistrationTicketAssignment.Create(
                tenantId,
                orderId,
                lineId,
                1,
                adult.Id,
                AssignmentStatusEnum.Assigned,
                assignmentDeadline: null,
                UtcNow);
        ParticipantAdmissionEligibility eligibility =
            ParticipantAdmissionEligibility.Create(
                tenantId,
                Guid.CreateVersion7(),
                assignment,
                adult,
                consentRequired: true,
                approvalRequired: false,
                UtcNow);

        await Assert.That(() =>
                eligibility.RecordSubjectCompletion(
                    adult,
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    UtcNow.AddMinutes(1),
                    Guid.CreateVersion7()))
            .Throws<ArgumentException>();
        await Assert.That(
                eligibility.DescribeReadiness(
                    orderConfirmed: true,
                    paymentSatisfied: true)
                    .Code)
            .IsEqualTo(
                ParticipantAdmissionReadinessCode
                    .SubjectOwnershipPending);
    }

    [Test]
    public async Task PaymentConsentApprovalAndRevocationConvergeThroughOneDecision()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        Guid subjectUserId = Guid.CreateVersion7();
        RegistrationParticipant adult =
            RegistrationParticipant.Create(
                tenantId,
                orderId,
                subjectUserId,
                ParticipantTypeEnum.Adult,
                guardian: null);
        RegistrationTicketAssignment assignment =
            RegistrationTicketAssignment.Create(
                tenantId,
                orderId,
                Guid.CreateVersion7(),
                1,
                adult.Id,
                AssignmentStatusEnum.Assigned,
                assignmentDeadline: null,
                UtcNow);
        ParticipantAdmissionEligibility eligibility =
            ParticipantAdmissionEligibility.Create(
                tenantId,
                Guid.CreateVersion7(),
                assignment,
                adult,
                consentRequired: true,
                approvalRequired: true,
                UtcNow);
        eligibility.RecordSubjectCompletion(
            adult,
            subjectUserId,
            Guid.CreateVersion7(),
            UtcNow.AddMinutes(1),
            Guid.CreateVersion7());
        eligibility.Approve(
            Guid.CreateVersion7(),
            UtcNow.AddMinutes(2),
            Guid.CreateVersion7());

        await Assert.That(
                eligibility.DescribeReadiness(
                    orderConfirmed: true,
                    paymentSatisfied: false)
                    .Code)
            .IsEqualTo(
                ParticipantAdmissionReadinessCode.PaymentPending);
        await Assert.That(
                eligibility.DescribeReadiness(
                    orderConfirmed: true,
                    paymentSatisfied: true)
                    .IsReady)
            .IsTrue();

        eligibility.Revoke(
            Guid.CreateVersion7(),
            UtcNow.AddMinutes(3),
            Guid.CreateVersion7());

        await Assert.That(
                eligibility.DescribeReadiness(
                    orderConfirmed: true,
                    paymentSatisfied: true)
                    .Code)
            .IsEqualTo(
                ParticipantAdmissionReadinessCode.Revoked);
    }

    [Test]
    public async Task ConcurrentApprovalAndRevocationConvergeToRevokedUnderAssignmentFence()
    {
        EligibilitySeed seed = await SeedEligibilityAsync();
        using var timeout =
            new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ready = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int readyCount = 0;

        async Task<bool> TransitionAsync(bool revoke)
        {
            await using ExploreDbContext context =
                fixture.CreateTenantFilteredDbContext(
                    new TestTenantContext(seed.TenantId));
            var repository =
                new ParticipantAdmissionEligibilityRepository(
                    context);
            if (Interlocked.Increment(ref readyCount) == 2)
            {
                ready.TrySetResult();
            }
            await release.Task.WaitAsync(timeout.Token);
            try
            {
                return await new EfCoreUnitOfWork(context)
                    .ExecuteInTransactionAsync(
                        async token =>
                        {
                            ParticipantAdmissionEligibility
                                eligibility =
                                await repository
                                    .LoadForUpdateAsync(
                                        seed.TenantId,
                                        seed.AssignmentId,
                                        token)
                                ?? throw new InvalidOperationException(
                                    "Eligibility was not seeded.");
                            if (revoke)
                            {
                                eligibility.Revoke(
                                    Guid.CreateVersion7(),
                                    UtcNow.AddMinutes(3),
                                    Guid.CreateVersion7());
                            }
                            else
                            {
                                eligibility.Approve(
                                    Guid.CreateVersion7(),
                                    UtcNow.AddMinutes(2),
                                    Guid.CreateVersion7());
                            }
                            await repository.ApplyDecisionAsync(
                                eligibility,
                                token);
                            return true;
                        },
                        timeout.Token);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        Task<bool> approval = TransitionAsync(revoke: false);
        Task<bool> revocation = TransitionAsync(revoke: true);
        await ready.Task.WaitAsync(timeout.Token);
        release.TrySetResult();
        bool[] results =
            await Task.WhenAll(approval, revocation);

        await using ExploreDbContext verification =
            fixture.CreateTenantFilteredDbContext(
                new TestTenantContext(seed.TenantId));
        var repository =
            new ParticipantAdmissionEligibilityRepository(
                verification);
        ParticipantAdmissionEligibility persisted =
            await repository.GetAsync(
                seed.TenantId,
                seed.AssignmentId,
                timeout.Token)
            ?? throw new InvalidOperationException(
                "Eligibility disappeared.");
        await using ExploreDbContext otherTenant =
            fixture.CreateTenantFilteredDbContext(
                new TestTenantContext(Guid.CreateVersion7()));
        ParticipantAdmissionEligibility? crossTenant =
            await new ParticipantAdmissionEligibilityRepository(
                    otherTenant)
                .GetAsync(
                    seed.TenantId,
                    seed.AssignmentId,
                    timeout.Token);

        await Assert.That(results.Count(result => result))
            .IsGreaterThanOrEqualTo(1);
        await Assert.That(persisted.RevokedAt).IsNotNull();
        await Assert.That(persisted.ConsentRequired).IsTrue();
        await Assert.That(persisted.ApprovalRequired).IsTrue();
        await Assert.That(
                persisted.DescribeReadiness(
                    orderConfirmed: true,
                    paymentSatisfied: true)
                    .Code)
            .IsEqualTo(
                ParticipantAdmissionReadinessCode.Revoked);
        await Assert.That(crossTenant).IsNull();
    }

    private async Task<EligibilitySeed> SeedEligibilityAsync()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context =
            fixture.CreateDbContext();
        TenantStatus activeStatus =
            await context.TenantStatuses.SingleAsync(
                status =>
                    status.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant
        {
            FullName = "Readiness authority tenant",
            Slug = $"readiness-{Guid.CreateVersion7():N}",
            TenantStatusId = activeStatus.Id,
            TenantStatus = activeStatus,
        };
        var user = new User
        {
            Pii = new UserPii
            {
                Email =
                    $"readiness-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Readiness",
                LastName = "Subject",
            },
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        var actor = new Actor
        {
            Pii = new ActorPii
            {
                DisplayName = "Readiness organizer",
            },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id,
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();
        var eventEntity =
            new DomainEvent(EventStatusEnum.Draft)
            {
                Id = Guid.CreateVersion7(),
                Title = "Readiness event",
                Subtitle = string.Empty,
                Description = string.Empty,
                FirstSessionDate =
                    DateOnly.FromDateTime(UtcNow.AddDays(1)),
                LastSessionDate =
                    DateOnly.FromDateTime(UtcNow.AddDays(1)),
                EventTypeId = 1,
                AudienceGenderId = 1,
                AudienceAgeId = 1,
                ActorId = actor.Id,
                Actor = null!,
                OrganizerActorId = actor.Id,
                TenantId = tenant.Id,
                Tenant = tenant,
                VisibilityTypeId = 1,
                VisibilityType = null!,
                EventStatus = null!,
                EventFormatId = 1,
                EventFormat = null!,
                EventProvenanceTypeId =
                    (int)EventProvenanceTypeEnum
                        .OrganizerCreated,
            };
        EventTicketCatalogVersion catalog =
            EventTicketCatalogVersion.Create(
                tenant.Id,
                eventEntity.Id,
                "USD",
                1);
        EventTicketType ticketType =
            EventTicketType.Create(
                Guid.CreateVersion7(),
                tenant.Id,
                catalog.Id,
                "Readiness ticket",
                "USD",
                TicketPricingModeEnum.Free,
                fixedPrice: null,
                minimumPrice: null,
                suggestedPrice: null,
                ParticipantDataCollectionModeEnum.PerTicketRequired,
                capacityPoolId: null,
                minimumAge: null,
                maximumAge: null,
                requiresGuardian: false,
                requiresApproval: true,
                perOrderLimit: null,
                perAccountLimit: null,
                perVerifiedContactLimit: null,
                perBookingPartyLimit: null);
        catalog.AddTicketType(ticketType, capacityPool: null);
        catalog.AddEntitlement(
            ticketType,
            TicketTypeEntitlement.CreateForEvent(
                ticketType.Id,
                tenant.Id,
                eventEntity.Id,
                includedQuantity: 1));
        catalog.Publish();
        RegistrationOrder order =
            RegistrationOrder.Create(
                tenant.Id,
                eventEntity.Id,
                user.Id,
                actor.Id,
                BookingPartyTypeEnum.Individual,
                catalog.Id,
                RegistrationParticipationSnapshot.Create(
                    Guid.CreateVersion7(),
                    1,
                    1,
                    1,
                    null),
                registrationWorkflowVersionId: null,
                guestAccessTokenHash: null,
                "USD",
                UtcNow,
                expiresAt: null);
        RegistrationOrderLine line =
            RegistrationOrderLine.Create(
                catalog,
                ticketType,
                order.Id,
                quantity: 1,
                chosenUnitPriceAmount: null,
                platformFeePolicy: null);
        order.AddLine(line);
        context.AddRange(eventEntity, catalog, order);
        await context.SaveChangesAsync();
        RegistrationParticipant participant =
            RegistrationParticipant.Create(
                tenant.Id,
                order.Id,
                user.Id,
                ParticipantTypeEnum.Adult,
                guardian: null);
        RegistrationTicketAssignment assignment =
            RegistrationTicketAssignment.Create(
                tenant.Id,
                order.Id,
                line.Id,
                1,
                participant.Id,
                AssignmentStatusEnum.Assigned,
                assignmentDeadline: null,
                UtcNow);
        context.AddRange(participant, assignment);
        await context.SaveChangesAsync();
        var repository =
            new ParticipantAdmissionEligibilityRepository(
                context);
        await new EfCoreUnitOfWork(context)
            .ExecuteInTransactionAsync(
                async token =>
                {
                    await repository.EnsureForAssignmentsAsync(
                        tenant.Id,
                        eventEntity.Id,
                        order.Id,
                        [assignment.Id],
                        UtcNow,
                        token);
                    return true;
                },
                CancellationToken.None);
        return new EligibilitySeed(
            tenant.Id,
            assignment.Id);
    }

    private static bool HasProperties(
        IReadOnlyList<IReadOnlyProperty> actual,
        params string[] expected) =>
        actual.Select(property => property.Name)
            .SequenceEqual(expected);

    private sealed record EligibilitySeed(
        Guid TenantId,
        Guid AssignmentId);

    private sealed class TestTenantContext(
        Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
    }
}
