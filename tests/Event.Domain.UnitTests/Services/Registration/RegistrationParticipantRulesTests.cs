using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Event.Domain.UnitTests.Services.Registration;

public sealed class RegistrationParticipantRulesTests
{
    private static readonly DateTime Now = new(2026, 7, 31, 10, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task None_RequiresNoTicketAssignments()
    {
        await Assert.That(RegistrationOrderRules.CanConfirmParticipantAssignments(
            ParticipantDataCollectionModeEnum.None, 2, [], null, Now)).IsTrue();
        await Assert.That(() => RegistrationOrderRules.CanConfirmParticipantAssignments(
            ParticipantDataCollectionModeEnum.None, 2, [Assigned(1)], null, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationOrderRules.CanConfirmParticipantAssignments(
            ParticipantDataCollectionModeEnum.None, 2, [], Now.AddDays(1), Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task LeadBookerOnly_RequiresNoTicketAssignments()
    {
        await Assert.That(RegistrationOrderRules.CanConfirmParticipantAssignments(
            ParticipantDataCollectionModeEnum.LeadBookerOnly, 2, [], null, Now)).IsTrue();
        await Assert.That(() => RegistrationOrderRules.CanConfirmParticipantAssignments(
            ParticipantDataCollectionModeEnum.LeadBookerOnly, 2, [Assigned(1)], null, Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task PerTicketOptional_AllowsMissingAssignments()
    {
        await Assert.That(RegistrationOrderRules.CanConfirmParticipantAssignments(
            ParticipantDataCollectionModeEnum.PerTicketOptional, 2, [], null, Now)).IsTrue();
        await Assert.That(RegistrationOrderRules.CanConfirmParticipantAssignments(
            ParticipantDataCollectionModeEnum.PerTicketOptional, 2, [Assigned(1)], null, Now)).IsTrue();
    }

    [Test]
    public async Task PerTicketRequired_BlocksUntilEveryTicketUnitIsAssigned()
    {
        await Assert.That(RegistrationOrderRules.CanConfirmParticipantAssignments(
            ParticipantDataCollectionModeEnum.PerTicketRequired, 2, [Assigned(1)], null, Now)).IsFalse();
        await Assert.That(RegistrationOrderRules.CanConfirmParticipantAssignments(
            ParticipantDataCollectionModeEnum.PerTicketRequired, 2, [Assigned(1), Assigned(2)], null, Now)).IsTrue();
    }

    [Test]
    public async Task DeferredAssignment_RequiresFutureDeadlineAndOutstandingRows()
    {
        DateTime deadline = Now.AddDays(7);
        RegistrationTicketAssignment[] outstanding = [Deferred(1, deadline), Deferred(2, deadline)];

        await Assert.That(RegistrationOrderRules.CanConfirmParticipantAssignments(
            ParticipantDataCollectionModeEnum.DeferredAssignment, 2, outstanding, deadline, Now)).IsTrue();
        await Assert.That(RegistrationOrderRules.CanConfirmParticipantAssignments(
            ParticipantDataCollectionModeEnum.DeferredAssignment, 2, outstanding, null, Now)).IsFalse();
        await Assert.That(RegistrationOrderRules.CanConfirmParticipantAssignments(
            ParticipantDataCollectionModeEnum.DeferredAssignment, 2, outstanding, Now, Now)).IsFalse();
        await Assert.That(RegistrationOrderRules.CanConfirmParticipantAssignments(
            ParticipantDataCollectionModeEnum.DeferredAssignment, 2, [Deferred(1, deadline)], deadline, Now)).IsFalse();
    }

    [Test]
    public async Task ChildAndDependent_AreEligibleOnlyWithAdultGuardian()
    {
        RegistrationParticipant adult = Participant(ParticipantTypeEnum.Adult);
        RegistrationParticipant child = Participant(ParticipantTypeEnum.Child, adult);
        RegistrationParticipant dependent = Participant(ParticipantTypeEnum.Dependent, adult);

        await Assert.That(RegistrationOrderRules.IsParticipantEligibleForTicket(adult)).IsTrue();
        await Assert.That(RegistrationOrderRules.IsParticipantEligibleForTicket(child)).IsTrue();
        await Assert.That(RegistrationOrderRules.IsParticipantEligibleForTicket(dependent)).IsTrue();
        await Assert.That(() => Participant(ParticipantTypeEnum.Child)).Throws<ArgumentException>();
        await Assert.That(() => Participant(ParticipantTypeEnum.Dependent)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Entities_RejectMalformedIdentityOrdinalAndGuardianGraphs()
    {
        RegistrationParticipant adult = Participant(ParticipantTypeEnum.Adult);
        RegistrationParticipant otherOrderAdult = RegistrationParticipant.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), null, ParticipantTypeEnum.Adult, null);

        await Assert.That(() => RegistrationParticipant.Create(
            Guid.Empty, Guid.CreateVersion7(), null, ParticipantTypeEnum.Adult, null)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationParticipant.Create(
            adult.TenantId, adult.RegistrationOrderId, null, ParticipantTypeEnum.Child, otherOrderAdult)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationTicketAssignment.Create(
            adult.TenantId, adult.RegistrationOrderId, Guid.Empty, 1, null, AssignmentStatusEnum.Unassigned, null, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationTicketAssignment.Create(
            adult.TenantId, adult.RegistrationOrderId, Guid.CreateVersion7(), 0, null, AssignmentStatusEnum.Unassigned, null, Now)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RegistrationTicketAssignment.Create(
            adult.TenantId, adult.RegistrationOrderId, Guid.CreateVersion7(), 1, null, AssignmentStatusEnum.Assigned, null, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationTicketAssignment.Create(
            adult.TenantId, adult.RegistrationOrderId, Guid.CreateVersion7(), 1, null, AssignmentStatusEnum.Deferred, Now, Now)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationOrderRules.CanConfirmParticipantAssignments(
            ParticipantDataCollectionModeEnum.PerTicketOptional, 2, [Assigned(3)], null, Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task ParticipantPii_RemainsSeparateAndTenantBound()
    {
        RegistrationParticipant participant = Participant(ParticipantTypeEnum.Guest);
        RegistrationParticipantPii pii = RegistrationParticipantPii.Create(
            participant.Id, participant.TenantId, "  Example Person  ", " PERSON@EXAMPLE.TEST ", null);

        participant.SetPii(pii);

        await Assert.That(participant.Pii).IsSameReferenceAs(pii);
        await Assert.That(pii.DisplayName).IsEqualTo("Example Person");
        await Assert.That(pii.NormalizedEmail).IsEqualTo("PERSON@EXAMPLE.TEST");
        await Assert.That(() => participant.SetPii(RegistrationParticipantPii.Create(
            participant.Id, Guid.CreateVersion7(), null, null, null))).Throws<ArgumentException>();
    }

    [Test]
    public async Task ParticipantAndAssignmentMutations_PreserveOrderIdentityAndNormalizeState()
    {
        RegistrationParticipant adult = Participant(ParticipantTypeEnum.Adult);
        RegistrationParticipant child = Participant(ParticipantTypeEnum.Child, adult);
        RegistrationParticipantPii pii = RegistrationParticipantPii.Create(child.Id, child.TenantId, " Initial ", null, null);
        child.SetPii(pii);
        RegistrationTicketAssignment assignment = RegistrationTicketAssignment.Create(
            child.TenantId, child.RegistrationOrderId, Guid.CreateVersion7(), 1, null,
            AssignmentStatusEnum.Deferred, Now.AddDays(1), Now);
        Guid assignmentStamp = Guid.CreateVersion7();

        pii.Update(" Updated ", " person@example.test ", null);
        assignment.Assign(child, assignmentStamp);

        await Assert.That(pii.DisplayName).IsEqualTo("Updated");
        await Assert.That(pii.NormalizedEmail).IsEqualTo("PERSON@EXAMPLE.TEST");
        await Assert.That(assignment.ParticipantId).IsEqualTo(child.Id);
        await Assert.That(assignment.AssignmentStatusId).IsEqualTo((int)AssignmentStatusEnum.Assigned);
        await Assert.That(assignment.AssignmentDeadline).IsNull();
        await Assert.That(assignment.ConcurrencyStamp).IsEqualTo(assignmentStamp);
        await Assert.That(() => assignment.Assign(
            RegistrationParticipant.Create(child.TenantId, Guid.CreateVersion7(), null, ParticipantTypeEnum.Adult, null),
            Guid.CreateVersion7())).Throws<ArgumentException>();
    }

    [Test]
    public async Task LookupEnums_UseStableIntegerIdentifiers()
    {
        await Assert.That((int)ParticipantTypeEnum.Adult).IsEqualTo(1);
        await Assert.That((int)ParticipantTypeEnum.Child).IsEqualTo(2);
        await Assert.That((int)ParticipantTypeEnum.Dependent).IsEqualTo(3);
        await Assert.That((int)ParticipantTypeEnum.Employee).IsEqualTo(4);
        await Assert.That((int)ParticipantTypeEnum.Guest).IsEqualTo(5);
        await Assert.That((int)ParticipantTypeEnum.Unnamed).IsEqualTo(6);
        await Assert.That((int)AssignmentStatusEnum.Unassigned).IsEqualTo(1);
        await Assert.That((int)AssignmentStatusEnum.Assigned).IsEqualTo(2);
        await Assert.That((int)AssignmentStatusEnum.Deferred).IsEqualTo(3);
    }

    private static RegistrationParticipant Participant(
        ParticipantTypeEnum type,
        RegistrationParticipant? guardian = null) => RegistrationParticipant.Create(
        guardian?.TenantId ?? Guid.CreateVersion7(),
        guardian?.RegistrationOrderId ?? Guid.CreateVersion7(),
        null,
        type,
        guardian);

    private static RegistrationTicketAssignment Assigned(int ordinal) => RegistrationTicketAssignment.Create(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), ordinal, Guid.CreateVersion7(), AssignmentStatusEnum.Assigned, null, Now);

    private static RegistrationTicketAssignment Deferred(int ordinal, DateTime deadline) => RegistrationTicketAssignment.Create(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), ordinal, null, AssignmentStatusEnum.Deferred, deadline, Now);
}
