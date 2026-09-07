using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class AdmissionTicketLifecycleTests
{
    private static readonly DateTime IssuedAt = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task SuspensionCanReactivateButEveryTerminalStateIsMonotonic()
    {
        AdmissionTicket ticket = Issue(Digest(1));

        ticket.TransitionTo(AdmissionTicketStatusEnum.Suspended, IssuedAt.AddMinutes(1));
        await Assert.That(ticket.ValidatesCredential(1, 1, Digest(1))).IsFalse();
        await Assert.That(ticket.Credentials.Count(credential =>
            credential.AdmissionTicketCredentialStatusId == (int)AdmissionTicketCredentialStatusEnum.Active)).IsEqualTo(1);

        ticket.TransitionTo(AdmissionTicketStatusEnum.Active, IssuedAt.AddMinutes(2));
        await Assert.That(ticket.ValidatesCredential(1, 1, Digest(1))).IsTrue();

        ticket.TransitionTo(AdmissionTicketStatusEnum.Transferred, IssuedAt.AddMinutes(3));
        await Assert.That(() => ticket.TransitionTo(AdmissionTicketStatusEnum.Suspended, IssuedAt.AddMinutes(4)))
            .Throws<InvalidOperationException>();
        await Assert.That(ticket.AdmissionTicketStatusId).IsEqualTo((int)AdmissionTicketStatusEnum.Transferred);
        await Assert.That(ticket.Credentials.Single().AdmissionTicketCredentialStatusId)
            .IsEqualTo((int)AdmissionTicketCredentialStatusEnum.Revoked);
    }

    [Test]
    public async Task InvalidRotationPreservesCurrentAuthorityAndVersionElevenSucceeds()
    {
        AdmissionTicket ticket = Issue(Digest(1));

        await Assert.That(() => ticket.RotateCredential(
                Guid.CreateVersion7(),
                credentialVersion: 3,
                lookupKeyVersion: 1,
                Digest(2),
                IssuedAt.AddMinutes(1)))
            .Throws<ArgumentException>();
        await Assert.That(ticket.Credentials.Count).IsEqualTo(1);
        await Assert.That(ticket.ValidatesCredential(1, 1, Digest(1))).IsTrue();

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
    public async Task RealLibraryQa_IssueRotateRejectStaleAcceptCurrentAndRevokeOnRelevantRefund()
    {
        AdmissionTicketTestAuthority authority = AdmissionTicketTestAuthority.Create(IssuedAt);
        string initialDigest = Digest(1);
        string currentDigest = Digest(2);
        AdmissionTicket ticket = Issue(authority, initialDigest, "TKT-QA");

        ticket.RotateCredential(Guid.CreateVersion7(), 2, 2, currentDigest, IssuedAt.AddMinutes(1));
        bool staleAccepted = ticket.ValidatesCredential(1, 1, initialDigest);
        bool currentAccepted = ticket.ValidatesCredential(2, 2, currentDigest);
        ticket.ApplyRefundAllocations(
            [AdmissionRefundLineAllocation.Create(
                authority.Assignment.Id,
                authority.OrderLine.Id,
                isAdmissionRelevant: true,
                acceptedAmountMinor: 1_000,
                refundedAmountMinor: 1_000)],
            IssuedAt.AddMinutes(2));
        bool refundedAccepted = ticket.ValidatesCredential(2, 2, currentDigest);

        await TestContext.Current.OutputWriter.WriteLineAsync(
            $"ADMISSION_QA issued=Active rotated=2 staleAccepted={staleAccepted} currentAccepted={currentAccepted} " +
            $"refundStatus={(AdmissionTicketStatusEnum)ticket.AdmissionTicketStatusId} refundedAccepted={refundedAccepted}");

        await Assert.That(staleAccepted).IsFalse();
        await Assert.That(currentAccepted).IsTrue();
        await Assert.That(ticket.AdmissionTicketStatusId).IsEqualTo((int)AdmissionTicketStatusEnum.Revoked);
        await Assert.That(refundedAccepted).IsFalse();
    }

    private static AdmissionTicket Issue(string digest) => Issue(
        AdmissionTicketTestAuthority.Create(IssuedAt),
        digest,
        "TKT-TEST");

    private static AdmissionTicket Issue(
        AdmissionTicketTestAuthority authority,
        string digest,
        string displayReference) => AdmissionTicket.Issue(
        authority.Order,
        authority.OrderLine,
        authority.Assignment,
        authority.Participant,
        authority.Catalog,
        authority.TicketType,
        Guid.CreateVersion7(),
        displayReference,
        Guid.CreateVersion7(),
        1,
        1,
        digest,
        IssuedAt);

    private static string Digest(int marker)
    {
        byte[] digest = new byte[32];
        digest[0] = checked((byte)marker);
        return Convert.ToBase64String(digest);
    }
}
