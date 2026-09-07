using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Event.Domain.UnitTests.Entities;

public sealed class AdmissionTicketAuthorityForgeryTests
{
    private static readonly DateTime IssuedAt = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task IssueRejectsDetachedParticipantAndAssignmentWithMatchingPublicLineageIds()
    {
        AdmissionTicketTestAuthority authority = AdmissionTicketTestAuthority.Create(IssuedAt);
        RegistrationParticipant detachedParticipant = RegistrationParticipant.Create(
            authority.Order.TenantId,
            authority.Order.Id,
            linkedUserId: null,
            ParticipantTypeEnum.Adult,
            guardian: null);
        RegistrationTicketAssignment detachedAssignment = RegistrationTicketAssignment.CreateAssigned(
            Guid.CreateVersion7(),
            authority.OrderLine.Id,
            ordinal: 1,
            detachedParticipant,
            IssuedAt);

        await Assert.That(() => Issue(authority, detachedParticipant, detachedAssignment))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task EveryAuthorityEntityRejectsRetenantingAfterInitialAssignment()
    {
        AdmissionTicketTestAuthority authority = AdmissionTicketTestAuthority.Create(IssuedAt);
        AdmissionTicket ticket = Issue(authority, authority.Participant, authority.Assignment);
        ITenantEntity[] entities =
        [
            authority.Order,
            authority.OrderLine,
            authority.Participant,
            authority.Assignment,
            authority.Catalog,
            authority.TicketType,
            ticket
        ];
        Guid replacementTenantId = Guid.CreateVersion7();

        foreach (ITenantEntity entity in entities)
        {
            await Assert.That(() => entity.TenantId = replacementTenantId)
                .Throws<InvalidOperationException>();
            await Assert.That(entity.TenantId).IsEqualTo(authority.Order.TenantId);
        }
    }

    [Test]
    public async Task TenantIdentityAllowsEfCompatibleFirstAssignmentAndSameValueButRejectsReplacement()
    {
        Type[] tenantEntityTypes =
        [
            typeof(RegistrationOrder),
            typeof(RegistrationOrderLine),
            typeof(RegistrationParticipant),
            typeof(RegistrationTicketAssignment),
            typeof(EventTicketCatalogVersion),
            typeof(EventTicketType),
            typeof(AdmissionTicket),
            typeof(AdmissionTicketCredential)
        ];
        Guid tenantId = Guid.CreateVersion7();

        foreach (Type entityType in tenantEntityTypes)
        {
            object instance = Activator.CreateInstance(entityType, nonPublic: true)!;
            ITenantEntity tenantEntity = (ITenantEntity)instance;

            tenantEntity.TenantId = tenantId;
            tenantEntity.TenantId = tenantId;

            await Assert.That(tenantEntity.TenantId).IsEqualTo(tenantId);
            await Assert.That(() => tenantEntity.TenantId = Guid.CreateVersion7())
                .Throws<InvalidOperationException>();
        }
    }

    [Test]
    public async Task ConfirmedOrderCannotAppendParticipantOrAssignmentMembership()
    {
        AdmissionTicketTestAuthority authority = AdmissionTicketTestAuthority.Create(IssuedAt);
        RegistrationParticipant lateParticipant = RegistrationParticipant.Create(
            authority.Order.TenantId,
            authority.Order.Id,
            linkedUserId: null,
            ParticipantTypeEnum.Adult,
            guardian: null);
        RegistrationTicketAssignment lateAssignment = RegistrationTicketAssignment.CreateAssigned(
            Guid.CreateVersion7(),
            authority.OrderLine.Id,
            ordinal: 2,
            lateParticipant,
            IssuedAt.AddMinutes(1));
        await Assert.That(() => authority.Order.AddParticipant(lateParticipant))
            .Throws<InvalidOperationException>();
        await Assert.That(() => authority.Order.AddAssignment(
                authority.OrderLine,
                lateAssignment,
                lateParticipant))
            .Throws<InvalidOperationException>();
    }

    private static AdmissionTicket Issue(
        AdmissionTicketTestAuthority authority,
        RegistrationParticipant participant,
        RegistrationTicketAssignment assignment) => AdmissionTicket.Issue(
        authority.Order,
        authority.OrderLine,
        assignment,
        participant,
        authority.Catalog,
        authority.TicketType,
        Guid.CreateVersion7(),
        "TKT-FORGERY",
        Guid.CreateVersion7(),
        1,
        1,
        Convert.ToBase64String(new byte[32]),
        IssuedAt);

}
