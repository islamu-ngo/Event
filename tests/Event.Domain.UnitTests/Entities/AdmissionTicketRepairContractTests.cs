using System.Reflection;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class AdmissionTicketRepairContractTests
{
    private static readonly DateTime IssuedAt = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task IssueBoundaryConsumesRepositoryNativeAuthorityInsteadOfCallerLineageIds()
    {
        MethodInfo issue = typeof(AdmissionTicket).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == nameof(AdmissionTicket.Issue));
        Type[] parameters = issue.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        await Assert.That(parameters).IsEquivalentTo(new[]
        {
            typeof(RegistrationOrder),
            typeof(RegistrationOrderLine),
            typeof(RegistrationTicketAssignment),
            typeof(RegistrationParticipant),
            typeof(EventTicketCatalogVersion),
            typeof(EventTicketType),
            typeof(Guid),
            typeof(string),
            typeof(Guid),
            typeof(int),
            typeof(int),
            typeof(string),
            typeof(DateTime)
        });
        await Assert.That(parameters.Count(type => type == typeof(Guid))).IsEqualTo(2);
    }

    [Test]
    public async Task IssueRejectsUnconfirmedAndMismatchedAuthorityGraphs()
    {
        AdmissionTicketTestAuthority unconfirmed = AdmissionTicketTestAuthority.Create(IssuedAt, confirmed: false);
        await Assert.That(() => Issue(unconfirmed)).Throws<InvalidOperationException>();

        AdmissionTicketTestAuthority first = AdmissionTicketTestAuthority.Create(IssuedAt);
        AdmissionTicketTestAuthority second = AdmissionTicketTestAuthority.Create(IssuedAt);
        Func<AdmissionTicket>[] mismatchedGraphs =
        [
            () => Issue(first, orderLine: second.OrderLine),
            () => Issue(first, assignment: second.Assignment),
            () => Issue(first, participant: second.Participant),
            () => Issue(first, catalog: second.Catalog),
            () => Issue(first, ticketType: second.TicketType)
        ];

        foreach (Func<AdmissionTicket> issue in mismatchedGraphs)
        {
            await Assert.That(issue).Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task IssuePersistsTheValidatedImmutableLineageMap()
    {
        AdmissionTicketTestAuthority authority = AdmissionTicketTestAuthority.Create(IssuedAt);
        AdmissionTicket ticket = Issue(authority);

        await Assert.That(ticket.TenantId).IsEqualTo(authority.Order.TenantId);
        await Assert.That(ticket.EventId).IsEqualTo(authority.Order.EventId);
        await Assert.That(ticket.RegistrationOrderId).IsEqualTo(authority.Order.Id);
        await Assert.That(ticket.RegistrationOrderLineId).IsEqualTo(authority.OrderLine.Id);
        await Assert.That(ticket.RegistrationTicketAssignmentId).IsEqualTo(authority.Assignment.Id);
        await Assert.That(ticket.ParticipantId).IsEqualTo(authority.Participant.Id);
        await Assert.That(ticket.TicketCatalogVersionId).IsEqualTo(authority.Catalog.Id);
        await Assert.That(ticket.EventTicketTypeId).IsEqualTo(authority.TicketType.Id);
    }

    [Test]
    public async Task RelevantRefundFactRequiresConcreteAssignmentWithoutMutatingTicket()
    {
        AdmissionTicket ticket = Issue(AdmissionTicketTestAuthority.Create(IssuedAt));

        await Assert.That(() => AdmissionRefundLineAllocation.Create(
                registrationTicketAssignmentId: null,
                Guid.CreateVersion7(),
                isAdmissionRelevant: true,
                acceptedAmountMinor: 1_000,
                refundedAmountMinor: 1_000))
            .Throws<ArgumentException>();
        await Assert.That(ticket.AdmissionTicketStatusId).IsEqualTo((int)AdmissionTicketStatusEnum.Active);
        await Assert.That(ticket.Credentials.Count(credential =>
            credential.AdmissionTicketCredentialStatusId == (int)AdmissionTicketCredentialStatusEnum.Active)).IsEqualTo(1);
    }

    [Test]
    public async Task FullRelevantRefundMustMatchAssignmentAndOrderLine()
    {
        AdmissionTicketTestAuthority authority = AdmissionTicketTestAuthority.Create(IssuedAt);
        AdmissionTicket ticket = Issue(authority);

        ticket.ApplyRefundAllocations(
            [AdmissionRefundLineAllocation.Create(
                authority.Assignment.Id,
                Guid.CreateVersion7(),
                isAdmissionRelevant: true,
                acceptedAmountMinor: 1_000,
                refundedAmountMinor: 1_000)],
            IssuedAt.AddMinutes(1));

        await Assert.That(ticket.AdmissionTicketStatusId).IsEqualTo((int)AdmissionTicketStatusEnum.Active);
        await Assert.That(ticket.ValidatesCredential(1, 1, Digest(1))).IsTrue();
    }

    [Test]
    public async Task CredentialHistoryAllowsVersionElevenAndRetainsOneActiveChild()
    {
        AdmissionTicket ticket = Issue(AdmissionTicketTestAuthority.Create(IssuedAt));

        for (int version = 2; version <= 11; version++)
        {
            ticket.RotateCredential(
                Guid.CreateVersion7(),
                version,
                lookupKeyVersion: 1,
                Digest(version),
                IssuedAt.AddMinutes(version));
        }

        await Assert.That(ticket.Credentials.Count).IsEqualTo(11);
        await Assert.That(ticket.Credentials.Count(credential =>
            credential.AdmissionTicketCredentialStatusId == (int)AdmissionTicketCredentialStatusEnum.Active)).IsEqualTo(1);
        await Assert.That(ticket.ValidatesCredential(11, 1, Digest(11))).IsTrue();
    }

    [Test]
    public async Task AggregateAndCredentialIdsRequireRfc4122UuidV7()
    {
        AdmissionTicketTestAuthority authority = AdmissionTicketTestAuthority.Create(IssuedAt);
        Guid nonRfcVariantVersion7 = Guid.Parse("018e4e5c-7f00-7000-0000-000000000001");
        await Assert.That(() => Issue(authority, ticketId: Guid.NewGuid())).Throws<ArgumentException>();
        await Assert.That(() => Issue(authority, credentialId: Guid.NewGuid())).Throws<ArgumentException>();
        await Assert.That(() => Issue(authority, ticketId: nonRfcVariantVersion7)).Throws<ArgumentException>();

        AdmissionTicket ticket = Issue(authority);
        await Assert.That(() => ticket.RotateCredential(
                Guid.NewGuid(),
                2,
                1,
                Digest(2),
                IssuedAt.AddMinutes(1)))
            .Throws<ArgumentException>();

        ticket.RotateCredential(Guid.CreateVersion7(), 2, 1, Digest(2), IssuedAt.AddMinutes(1));
        await Assert.That(ticket.Id.Version).IsEqualTo(7);
        await Assert.That(ticket.Credentials.All(credential => credential.Id.Version == 7)).IsTrue();
    }

    private static AdmissionTicket Issue(
        AdmissionTicketTestAuthority authority,
        RegistrationOrderLine? orderLine = null,
        RegistrationTicketAssignment? assignment = null,
        RegistrationParticipant? participant = null,
        EventTicketCatalogVersion? catalog = null,
        EventTicketType? ticketType = null,
        Guid? ticketId = null,
        Guid? credentialId = null) => AdmissionTicket.Issue(
        authority.Order,
        orderLine ?? authority.OrderLine,
        assignment ?? authority.Assignment,
        participant ?? authority.Participant,
        catalog ?? authority.Catalog,
        ticketType ?? authority.TicketType,
        ticketId ?? Guid.CreateVersion7(),
        "TKT-REPAIR",
        credentialId ?? Guid.CreateVersion7(),
        1,
        1,
        Digest(1),
        IssuedAt);

    private static string Digest(int marker)
    {
        byte[] digest = new byte[32];
        digest[0] = checked((byte)marker);
        return Convert.ToBase64String(digest);
    }
}
