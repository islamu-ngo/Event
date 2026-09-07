using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class TicketTransferDomainTests
{
    private static readonly DateTime UtcNow =
        new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task PolicyBoundsOfferByLifetimeCutoffAndHop()
    {
        TicketTransferPolicy policy = Policy();

        await Assert.That(policy.GetOfferExpiry(
                0,
                UtcNow.AddHours(4),
                UtcNow))
            .IsEqualTo(UtcNow.AddHours(1));
        await Assert.That(policy.GetOfferExpiry(
                0,
                UtcNow.AddMinutes(45),
                UtcNow))
            .IsEqualTo(UtcNow.AddMinutes(15));
        await Assert.That(policy.GetOfferExpiry(
                2,
                UtcNow.AddHours(4),
                UtcNow))
            .IsNull();
        await Assert.That(policy.GetOfferExpiry(
                0,
                UtcNow.AddMinutes(30),
                UtcNow))
            .IsNull();
        await Assert.That(policy.GetOfferExpiry(
                -1,
                UtcNow.AddHours(4),
                UtcNow))
            .IsNull();

        Set(
            policy,
            nameof(
                TicketTransferPolicy
                    .OfferLifetimeMinutes),
            60);
        Set(
            policy,
            nameof(
                TicketTransferPolicy
                    .CutoffMinutesBeforeEvent),
            30);
        await Assert.That(policy.GetOfferExpiry(
                0,
                UtcNow.AddMinutes(90),
                UtcNow))
            .IsEqualTo(UtcNow.AddMinutes(60));

        Set(
            policy,
            nameof(TicketTransferPolicy.IsEnabled),
            false);
        await Assert.That(policy.GetOfferExpiry(
                0,
                UtcNow.AddHours(4),
                UtcNow))
            .IsNull();
    }

    [Test]
    public async Task OfferPinsGenerationAndMatchesOnlyCanonicalDigest()
    {
        AdmissionTicket ticket = Ticket();
        TicketTransferPolicy policy = Policy();
        string capabilityDigest =
            Digest("transfer-claim");

        AdmissionTicketTransfer transfer =
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                ticket,
                policy,
                Guid.CreateVersion7(),
                capabilityDigest,
                UtcNow.AddHours(4),
                UtcNow);

        await Assert.That(transfer.IsOpen).IsTrue();
        await Assert.That(transfer.TransferHop)
            .IsEqualTo(1);
        await Assert.That(
                transfer.CredentialGeneration)
            .IsEqualTo(1);
        await Assert.That(transfer.ExpiresAt)
            .IsEqualTo(UtcNow.AddHours(1));
        await Assert.That(
                transfer.MatchesCapability(
                    capabilityDigest))
            .IsTrue();
        await Assert.That(
                transfer.MatchesCapability(
                    Digest("wrong-claim")))
            .IsFalse();
        await Assert.That(
                transfer.MatchesCapability(
                    "not-base64"))
            .IsFalse();
        await Assert.That(
                transfer.MatchesCapability(
                    " "))
            .IsFalse();
        await Assert.That(
                transfer.MatchesCapability(
                    NonCanonicalDigest(
                        capabilityDigest)))
            .IsFalse();
    }

    [Test]
    public async Task AcceptanceRotatesCredentialAndHolderExactlyOnce()
    {
        AdmissionTicket ticket = Ticket();
        AdmissionTicketTransfer transfer =
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                ticket,
                Policy(),
                Guid.CreateVersion7(),
                Digest("accept-claim"),
                UtcNow.AddHours(4),
                UtcNow);
        Guid recipientSubject = Guid.CreateVersion7();
        RegistrationParticipant recipient =
            Participant(
                ticket.TenantId,
                ticket.RegistrationOrderId,
                recipientSubject);
        string oldDigest =
            ticket.Credentials.Single().LookupDigest;
        string newDigest = Digest("new-credential");

        ticket.AcceptTransfer(
            transfer,
            recipient,
            recipientSubject,
            Guid.CreateVersion7(),
            2,
            1,
            newDigest,
            UtcNow.AddMinutes(1));

        await Assert.That(transfer.StatusId)
            .IsEqualTo(
                (int)AdmissionTicketTransferStatus
                    .Accepted);
        await Assert.That(
                transfer.CapabilityConsumedAt)
            .IsEqualTo(UtcNow.AddMinutes(1));
        await Assert.That(
                transfer.RecipientSubjectUserId)
            .IsEqualTo(recipientSubject);
        await Assert.That(transfer.ToParticipantId)
            .IsEqualTo(recipient.Id);
        await Assert.That(
                transfer.AcceptedCredentialGeneration)
            .IsEqualTo(2);
        await Assert.That(transfer.AcceptedAt)
            .IsEqualTo(UtcNow.AddMinutes(1));
        await Assert.That(
                transfer.OpenAdmissionTicketId)
            .IsEqualTo(transfer.Id);
        await Assert.That(transfer.UpdatedAt)
            .IsEqualTo(UtcNow.AddMinutes(1));
        await Assert.That(ticket.ParticipantId)
            .IsEqualTo(recipient.Id);
        await Assert.That(
                ticket.HolderSubjectUserId)
            .IsEqualTo(recipientSubject);
        await Assert.That(ticket.TransferHopCount)
            .IsEqualTo(1);
        await Assert.That(ticket.CredentialGeneration)
            .IsEqualTo(2);
        await Assert.That(ticket.ValidatesCredential(
                1,
                1,
                oldDigest))
            .IsFalse();
        await Assert.That(ticket.ValidatesCredential(
                2,
                1,
                newDigest))
            .IsTrue();
        await Assert.That(
                transfer.MatchesCapability(
                    Digest("accept-claim")))
            .IsFalse();

        Assert.Throws<InvalidOperationException>(() =>
            ticket.AcceptTransfer(
                transfer,
                recipient,
                recipientSubject,
                Guid.CreateVersion7(),
                3,
                1,
                Digest("third-credential"),
                UtcNow.AddMinutes(2)));
    }

    [Test]
    public async Task CancelAndExpireAreMonotonicTerminalTransitions()
    {
        AdmissionTicket ticket = Ticket();
        AdmissionTicketTransfer cancelled =
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                ticket,
                Policy(),
                Guid.CreateVersion7(),
                Digest("cancel-claim"),
                UtcNow.AddHours(4),
                UtcNow);

        cancelled.Cancel(UtcNow.AddMinutes(1));
        cancelled.Expire(UtcNow.AddHours(2));

        await Assert.That(cancelled.StatusId)
            .IsEqualTo(
                (int)AdmissionTicketTransferStatus
                    .Cancelled);
        await Assert.That(cancelled.CancelledAt)
            .IsEqualTo(UtcNow.AddMinutes(1));
        await Assert.That(cancelled.ExpiredAt)
            .IsNull();
        await Assert.That(cancelled.IsOpen).IsFalse();
        await Assert.That(
                cancelled.OpenAdmissionTicketId)
            .IsEqualTo(cancelled.Id);
        await Assert.That(cancelled.UpdatedAt)
            .IsEqualTo(UtcNow.AddMinutes(1));
        await Assert.That(
                cancelled.ConcurrencyStamp.Version)
            .IsEqualTo(7);

        AdmissionTicketTransfer expired =
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                ticket,
                Policy(),
                Guid.CreateVersion7(),
                Digest("expiry-claim"),
                UtcNow.AddHours(4),
                UtcNow);
        expired.Expire(UtcNow.AddMinutes(59));
        await Assert.That(expired.IsOpen).IsTrue();

        expired.Expire(UtcNow.AddHours(1));
        expired.Cancel(UtcNow.AddHours(1));

        await Assert.That(expired.StatusId)
            .IsEqualTo(
                (int)AdmissionTicketTransferStatus
                    .Expired);
        await Assert.That(expired.ExpiredAt)
            .IsEqualTo(UtcNow.AddHours(1));
        await Assert.That(expired.CancelledAt)
            .IsNull();
        await Assert.That(
                expired.OpenAdmissionTicketId)
            .IsEqualTo(expired.Id);
        await Assert.That(expired.UpdatedAt)
            .IsEqualTo(UtcNow.AddHours(1));
    }

    [Test]
    public async Task DeliveryIntentContainsPointersOnly()
    {
        AdmissionTicketTransfer transfer =
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                Ticket(),
                Policy(),
                Guid.CreateVersion7(),
                Digest("delivery-claim"),
                UtcNow.AddHours(4),
                UtcNow);
        Guid intentId = Guid.CreateVersion7();
        Guid outboxId = Guid.CreateVersion7();

        AdmissionTransferDeliveryIntent intent =
            AdmissionTransferDeliveryIntent.Create(
                intentId,
                transfer,
                outboxId,
                UtcNow);

        await Assert.That(intent.Id)
            .IsEqualTo(intentId);
        await Assert.That(intent.TenantId)
            .IsEqualTo(transfer.TenantId);
        await Assert.That(
                intent.AdmissionTicketTransferId)
            .IsEqualTo(transfer.Id);
        await Assert.That(intent.OutboxMessageId)
            .IsEqualTo(outboxId);
        await Assert.That(intent.CreatedAt)
            .IsEqualTo(UtcNow);
    }

    [Test]
    public async Task PolicyFactoryPinsLineageAndRejectsEveryInvalidBound()
    {
        EventTicketType ticketType =
            TicketType();
        Guid policyId = Guid.CreateVersion7();

        TicketTransferPolicy policy =
            TicketTransferPolicy.Create(
                policyId,
                TenantId,
                ticketType,
                true,
                2,
                60,
                30,
                UtcNow);

        await Assert.That(policy.Id)
            .IsEqualTo(policyId);
        await Assert.That(policy.TenantId)
            .IsEqualTo(TenantId);
        await Assert.That(
                policy.TicketCatalogVersionId)
            .IsEqualTo(TicketCatalogVersionId);
        await Assert.That(policy.EventTicketTypeId)
            .IsEqualTo(EventTicketTypeId);
        await Assert.That(policy.IsEnabled).IsTrue();
        await Assert.That(policy.MaximumHops)
            .IsEqualTo(2);
        await Assert.That(policy.OfferLifetimeMinutes)
            .IsEqualTo(60);
        await Assert.That(
                policy.CutoffMinutesBeforeEvent)
            .IsEqualTo(30);
        await Assert.That(policy.CreatedAt)
            .IsEqualTo(UtcNow);

        foreach ((int hops, int lifetime, int cutoff)
                 in new[]
                 {
                     (1, 5, 0),
                     (100, 43_200, 525_600),
                 })
        {
            TicketTransferPolicy boundary =
                TicketTransferPolicy.Create(
                    Guid.CreateVersion7(),
                    TenantId,
                    ticketType,
                    true,
                    hops,
                    lifetime,
                    cutoff,
                    UtcNow);
            await Assert.That(boundary.MaximumHops)
                .IsEqualTo(hops);
            await Assert.That(
                    boundary.OfferLifetimeMinutes)
                .IsEqualTo(lifetime);
            await Assert.That(
                    boundary
                        .CutoffMinutesBeforeEvent)
                .IsEqualTo(cutoff);
        }

        Assert.Throws<ArgumentException>(() =>
            TicketTransferPolicy.Create(
                Guid.NewGuid(),
                TenantId,
                ticketType,
                true,
                2,
                60,
                30,
                UtcNow));
        Assert.Throws<ArgumentException>(() =>
            TicketTransferPolicy.Create(
                Guid.CreateVersion7(),
                Guid.Empty,
                ticketType,
                true,
                2,
                60,
                30,
                UtcNow));
        Assert.Throws<ArgumentException>(() =>
            TicketTransferPolicy.Create(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                ticketType,
                true,
                2,
                60,
                30,
                UtcNow));
        foreach (int hops in new[] { 0, 101 })
        {
            Assert.Throws<
                ArgumentOutOfRangeException>(() =>
                TicketTransferPolicy.Create(
                    Guid.CreateVersion7(),
                    TenantId,
                    ticketType,
                    true,
                    hops,
                    60,
                    30,
                    UtcNow));
        }
        foreach (int lifetime in
                 new[] { 4, 43_201 })
        {
            Assert.Throws<
                ArgumentOutOfRangeException>(() =>
                TicketTransferPolicy.Create(
                    Guid.CreateVersion7(),
                    TenantId,
                    ticketType,
                    true,
                    2,
                    lifetime,
                    30,
                    UtcNow));
        }
        foreach (int cutoff in
                 new[] { -1, 525_601 })
        {
            Assert.Throws<
                ArgumentOutOfRangeException>(() =>
                TicketTransferPolicy.Create(
                    Guid.CreateVersion7(),
                    TenantId,
                    ticketType,
                    true,
                    2,
                    60,
                    cutoff,
                    UtcNow));
        }
        Assert.Throws<ArgumentException>(() =>
            TicketTransferPolicy.Create(
                Guid.CreateVersion7(),
                TenantId,
                ticketType,
                true,
                2,
                60,
                30,
                DateTime.SpecifyKind(
                    UtcNow,
                    DateTimeKind.Local)));
        Assert.Throws<ArgumentException>(() =>
            TicketTransferPolicy.Create(
                Guid.CreateVersion7(),
                TenantId,
                ticketType,
                true,
                2,
                60,
                30,
                default));
    }

    [Test]
    public async Task OfferPinsAllTicketLineageAndRejectsInvalidAuthority()
    {
        AdmissionTicket ticket = Ticket();
        TicketTransferPolicy policy = Policy();
        Guid transferId = Guid.CreateVersion7();
        Guid operationKey = Guid.CreateVersion7();
        string capabilityDigest =
            Digest("lineage-claim");

        AdmissionTicketTransfer transfer =
            AdmissionTicketTransfer.Offer(
                transferId,
                ticket,
                policy,
                operationKey,
                capabilityDigest,
                UtcNow.AddHours(4),
                UtcNow);

        await Assert.That(transfer.Id)
            .IsEqualTo(transferId);
        await Assert.That(transfer.TenantId)
            .IsEqualTo(ticket.TenantId);
        await Assert.That(transfer.EventId)
            .IsEqualTo(ticket.EventId);
        await Assert.That(
                transfer.AdmissionTicketId)
            .IsEqualTo(ticket.Id);
        await Assert.That(
                transfer.OpenAdmissionTicketId)
            .IsEqualTo(ticket.Id);
        await Assert.That(
                transfer.RegistrationOrderId)
            .IsEqualTo(
                ticket.RegistrationOrderId);
        await Assert.That(
                transfer.RegistrationOrderLineId)
            .IsEqualTo(
                ticket.RegistrationOrderLineId);
        await Assert.That(
                transfer
                    .RegistrationTicketAssignmentId)
            .IsEqualTo(
                ticket
                    .RegistrationTicketAssignmentId);
        await Assert.That(transfer.FromParticipantId)
            .IsEqualTo(ticket.ParticipantId);
        await Assert.That(transfer.OfferOperationKey)
            .IsEqualTo(operationKey);
        await Assert.That(transfer.CapabilityDigest)
            .IsEqualTo(capabilityDigest);
        await Assert.That(transfer.OfferedAt)
            .IsEqualTo(UtcNow);
        await Assert.That(transfer.CreatedAt)
            .IsEqualTo(UtcNow);

        Assert.Throws<ArgumentException>(() =>
            AdmissionTicketTransfer.Offer(
                Guid.Empty,
                ticket,
                policy,
                operationKey,
                capabilityDigest,
                UtcNow.AddHours(4),
                UtcNow));
        Assert.Throws<ArgumentException>(() =>
            AdmissionTicketTransfer.Offer(
                Guid.NewGuid(),
                ticket,
                policy,
                operationKey,
                capabilityDigest,
                UtcNow.AddHours(4),
                UtcNow));
        Assert.Throws<ArgumentException>(() =>
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                ticket,
                policy,
                Guid.Empty,
                capabilityDigest,
                UtcNow.AddHours(4),
                UtcNow));
        Assert.Throws<ArgumentException>(() =>
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                ticket,
                policy,
                Guid.NewGuid(),
                capabilityDigest,
                UtcNow.AddHours(4),
                UtcNow));
        Assert.Throws<ArgumentException>(() =>
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                ticket,
                policy,
                Guid.CreateVersion7(),
                "invalid",
                UtcNow.AddHours(4),
                UtcNow));
        Assert.Throws<ArgumentException>(() =>
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                ticket,
                policy,
                Guid.CreateVersion7(),
                " ",
                UtcNow.AddHours(4),
                UtcNow));
        Assert.Throws<ArgumentException>(() =>
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                ticket,
                policy,
                Guid.CreateVersion7(),
                Convert.ToBase64String(
                    new byte[31]),
                UtcNow.AddHours(4),
                UtcNow));
        Assert.Throws<ArgumentException>(() =>
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                ticket,
                policy,
                Guid.CreateVersion7(),
                capabilityDigest,
                DateTime.SpecifyKind(
                    UtcNow.AddHours(4),
                    DateTimeKind.Local),
                UtcNow));
        Assert.Throws<ArgumentException>(() =>
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                ticket,
                policy,
                Guid.CreateVersion7(),
                capabilityDigest,
                UtcNow.AddHours(4),
                DateTime.SpecifyKind(
                    UtcNow,
                    DateTimeKind.Local)));

        AdmissionTicket inactive = Ticket();
        Set(
            inactive,
            nameof(
                AdmissionTicket
                    .AdmissionTicketStatusId),
            (int)AdmissionTicketStatusEnum.Revoked);
        Assert.Throws<
            InvalidOperationException>(() =>
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                inactive,
                policy,
                Guid.CreateVersion7(),
                capabilityDigest,
                UtcNow.AddHours(4),
                UtcNow));

        TicketTransferPolicy wrongPolicy = Policy();
        SetTenant(
            wrongPolicy,
            Guid.CreateVersion7());
        Assert.Throws<ArgumentException>(() =>
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                ticket,
                wrongPolicy,
                Guid.CreateVersion7(),
                capabilityDigest,
                UtcNow.AddHours(4),
                UtcNow));
        wrongPolicy = Policy();
        Set(
            wrongPolicy,
            nameof(
                TicketTransferPolicy
                    .TicketCatalogVersionId),
            Guid.CreateVersion7());
        Assert.Throws<ArgumentException>(() =>
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                ticket,
                wrongPolicy,
                Guid.CreateVersion7(),
                capabilityDigest,
                UtcNow.AddHours(4),
                UtcNow));
        wrongPolicy = Policy();
        Set(
            wrongPolicy,
            nameof(
                TicketTransferPolicy
                    .EventTicketTypeId),
            Guid.CreateVersion7());
        Assert.Throws<ArgumentException>(() =>
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                ticket,
                wrongPolicy,
                Guid.CreateVersion7(),
                capabilityDigest,
                UtcNow.AddHours(4),
                UtcNow));
    }

    [Test]
    public async Task AcceptanceGuardRejectsEachStaleAuthorityDimension()
    {
        AdmissionTicket ticket = Ticket();
        AdmissionTicketTransfer transfer =
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                ticket,
                Policy(),
                Guid.CreateVersion7(),
                Digest("guard-claim"),
                UtcNow.AddHours(4),
                UtcNow);
        Guid subject = Guid.CreateVersion7();
        RegistrationParticipant recipient =
            Participant(
                ticket.TenantId,
                ticket.RegistrationOrderId,
                subject);

        transfer.EnsureCanAccept(
            ticket,
            recipient,
            subject,
            2,
            UtcNow.AddMinutes(1));
        transfer.EnsureCanAccept(
            ticket,
            recipient,
            subject,
            2,
            transfer.ExpiresAt);
        Assert.Throws<
            ArgumentNullException>(() =>
            transfer.EnsureCanAccept(
                null!,
                recipient,
                subject,
                2,
                UtcNow.AddMinutes(1)));
        Assert.Throws<
            ArgumentNullException>(() =>
            transfer.EnsureCanAccept(
                ticket,
                null!,
                subject,
                2,
                UtcNow.AddMinutes(1)));

        AssertRejectsAcceptance(
            transfer,
            ticket,
            recipient,
            subject,
            2,
            transfer.ExpiresAt.AddTicks(1));
        AdmissionTicket wrongTicket = Ticket();
        AssertRejectsAcceptance(
            transfer,
            wrongTicket,
            recipient,
            subject,
            2,
            UtcNow.AddMinutes(1));

        ticket.RotateCredential(
            Guid.CreateVersion7(),
            2,
            1,
            Digest("guard-rotated"),
            UtcNow.AddSeconds(1));
        AdmissionTicket otherTicket = Ticket();
        Assert.Throws<
            InvalidOperationException>(() =>
            transfer.Accept(
                otherTicket,
                recipient,
                subject,
                2,
                UtcNow.AddMinutes(1)));
        Set(
            ticket,
            nameof(AdmissionTicket.ParticipantId),
            Guid.CreateVersion7());
        Assert.Throws<
            InvalidOperationException>(() =>
            transfer.Accept(
                ticket,
                recipient,
                subject,
                2,
                UtcNow.AddMinutes(1)));
        Set(
            ticket,
            nameof(AdmissionTicket.ParticipantId),
            transfer.FromParticipantId);
        Assert.Throws<
            InvalidOperationException>(() =>
            transfer.Accept(
                ticket,
                recipient,
                subject,
                1,
                UtcNow.AddMinutes(1)));
        Assert.Throws<
            InvalidOperationException>(() =>
            transfer.Accept(
                ticket,
                recipient,
                Guid.CreateVersion7(),
                2,
                UtcNow.AddMinutes(1)));
        Assert.Throws<
            InvalidOperationException>(() =>
            transfer.Accept(
                ticket,
                recipient,
                subject,
                2,
                transfer.ExpiresAt.AddTicks(1)));

        transfer.Accept(
            ticket,
            recipient,
            subject,
            2,
            transfer.ExpiresAt);
        await Assert.That(
                transfer.CapabilityConsumedAt)
            .IsEqualTo(transfer.ExpiresAt);
        await Assert.That(transfer.AcceptedAt)
            .IsEqualTo(transfer.ExpiresAt);
        RegistrationParticipant wrongTenant =
            Participant(
                Guid.CreateVersion7(),
                ticket.RegistrationOrderId,
                subject);
        AssertRejectsAcceptance(
            transfer,
            ticket,
            wrongTenant,
            subject,
            2,
            UtcNow.AddMinutes(1));
        RegistrationParticipant wrongOrder =
            Participant(
                ticket.TenantId,
                Guid.CreateVersion7(),
                subject);
        AssertRejectsAcceptance(
            transfer,
            ticket,
            wrongOrder,
            subject,
            2,
            UtcNow.AddMinutes(1));
        RegistrationParticipant source =
            Participant(
                ticket.TenantId,
                ticket.RegistrationOrderId,
                subject);
        Set(
            source,
            nameof(RegistrationParticipant.Id),
            ticket.ParticipantId);
        AssertRejectsAcceptance(
            transfer,
            ticket,
            source,
            subject,
            2,
            UtcNow.AddMinutes(1));
        RegistrationParticipant wrongSubject =
            Participant(
                ticket.TenantId,
                ticket.RegistrationOrderId,
                Guid.CreateVersion7());
        AssertRejectsAcceptance(
            transfer,
            ticket,
            wrongSubject,
            subject,
            2,
            UtcNow.AddMinutes(1));
        AssertRejectsAcceptance(
            transfer,
            ticket,
            recipient,
            subject,
            1,
            UtcNow.AddMinutes(1));
        AssertRejectsAcceptance(
            transfer,
            ticket,
            recipient,
            Guid.Empty,
            2,
            UtcNow.AddMinutes(1));
    }

    [Test]
    public async Task DeliveryIntentRejectsEveryInvalidIdentityAndClock()
    {
        AdmissionTicketTransfer transfer =
            AdmissionTicketTransfer.Offer(
                Guid.CreateVersion7(),
                Ticket(),
                Policy(),
                Guid.CreateVersion7(),
                Digest("invalid-delivery-claim"),
                UtcNow.AddHours(4),
                UtcNow);

        foreach (Guid invalidId in
                 new[] { Guid.Empty, Guid.NewGuid() })
        {
            Assert.Throws<ArgumentException>(() =>
                AdmissionTransferDeliveryIntent
                    .Create(
                        invalidId,
                        transfer,
                        Guid.CreateVersion7(),
                        UtcNow));
            Assert.Throws<ArgumentException>(() =>
                AdmissionTransferDeliveryIntent
                    .Create(
                        Guid.CreateVersion7(),
                        transfer,
                        invalidId,
                        UtcNow));
        }
        Assert.Throws<ArgumentException>(() =>
            AdmissionTransferDeliveryIntent.Create(
                Guid.CreateVersion7(),
                transfer,
                Guid.CreateVersion7(),
                default));
        Assert.Throws<ArgumentException>(() =>
            AdmissionTransferDeliveryIntent.Create(
                Guid.CreateVersion7(),
                transfer,
                Guid.CreateVersion7(),
                DateTime.SpecifyKind(
                    UtcNow,
                    DateTimeKind.Local)));
        Assert.Throws<
            ArgumentNullException>(() =>
            AdmissionTransferDeliveryIntent.Create(
                Guid.CreateVersion7(),
                null!,
                Guid.CreateVersion7(),
                UtcNow));
    }

    private static TicketTransferPolicy Policy()
    {
        TicketTransferPolicy policy =
            Uninitialized<TicketTransferPolicy>();
        Set(
            policy,
            nameof(TicketTransferPolicy.IsEnabled),
            true);
        Set(
            policy,
            nameof(TicketTransferPolicy.MaximumHops),
            2);
        Set(
            policy,
            nameof(
                TicketTransferPolicy
                    .OfferLifetimeMinutes),
            60);
        Set(
            policy,
            nameof(
                TicketTransferPolicy
                    .CutoffMinutesBeforeEvent),
            30);
        Set(
            policy,
            nameof(
                TicketTransferPolicy
                    .TicketCatalogVersionId),
            TicketCatalogVersionId);
        Set(
            policy,
            nameof(
                TicketTransferPolicy
                    .EventTicketTypeId),
            EventTicketTypeId);
        SetTenant(policy, TenantId);
        return policy;
    }

    private static EventTicketType TicketType()
    {
        EventTicketType ticketType =
            Uninitialized<EventTicketType>();
        SetTenant(ticketType, TenantId);
        Set(
            ticketType,
            nameof(EventTicketType.Id),
            EventTicketTypeId);
        Set(
            ticketType,
            nameof(EventTicketType.CatalogId),
            TicketCatalogVersionId);
        return ticketType;
    }

    private static void AssertRejectsAcceptance(
        AdmissionTicketTransfer transfer,
        AdmissionTicket ticket,
        RegistrationParticipant recipient,
        Guid subject,
        int generation,
        DateTime acceptedAt) =>
        Assert.Throws<InvalidOperationException>(() =>
            transfer.EnsureCanAccept(
                ticket,
                recipient,
                subject,
                generation,
                acceptedAt));

    private static AdmissionTicket Ticket()
    {
        AdmissionTicket ticket =
            Uninitialized<AdmissionTicket>();
        Guid ticketId = Guid.CreateVersion7();
        SetTenant(ticket, TenantId);
        Set(ticket, nameof(AdmissionTicket.Id), ticketId);
        Set(
            ticket,
            nameof(AdmissionTicket.EventId),
            Guid.CreateVersion7());
        Set(
            ticket,
            nameof(
                AdmissionTicket
                    .RegistrationOrderId),
            Guid.CreateVersion7());
        Set(
            ticket,
            nameof(
                AdmissionTicket
                    .RegistrationOrderLineId),
            Guid.CreateVersion7());
        Set(
            ticket,
            nameof(
                AdmissionTicket
                    .RegistrationTicketAssignmentId),
            Guid.CreateVersion7());
        Set(
            ticket,
            nameof(AdmissionTicket.ParticipantId),
            Guid.CreateVersion7());
        Set(
            ticket,
            nameof(
                AdmissionTicket.HolderSubjectUserId),
            Guid.CreateVersion7());
        Set(
            ticket,
            nameof(
                AdmissionTicket
                    .TicketCatalogVersionId),
            TicketCatalogVersionId);
        Set(
            ticket,
            nameof(
                AdmissionTicket.EventTicketTypeId),
            EventTicketTypeId);
        Set(
            ticket,
            nameof(
                AdmissionTicket
                    .AdmissionTicketStatusId),
            (int)AdmissionTicketStatusEnum.Active);
        Set(
            ticket,
            "_credentials",
            new List<AdmissionTicketCredential>
            {
                new(
                    Guid.CreateVersion7(),
                    TenantId,
                    ticketId,
                    1,
                    1,
                    Digest("old-credential"),
                    UtcNow),
            });
        return ticket;
    }

    private static RegistrationParticipant Participant(
        Guid tenantId,
        Guid orderId,
        Guid subjectUserId)
    {
        RegistrationParticipant participant =
            Uninitialized<RegistrationParticipant>();
        SetTenant(participant, tenantId);
        Set(
            participant,
            nameof(RegistrationParticipant.Id),
            Guid.CreateVersion7());
        Set(
            participant,
            nameof(
                RegistrationParticipant
                    .RegistrationOrderId),
            orderId);
        Set(
            participant,
            nameof(
                RegistrationParticipant.LinkedUserId),
            subjectUserId);
        return participant;
    }

    private static readonly Guid TenantId =
        Guid.CreateVersion7();
    private static readonly Guid TicketCatalogVersionId =
        Guid.CreateVersion7();
    private static readonly Guid EventTicketTypeId =
        Guid.CreateVersion7();

    private static string Digest(string value) =>
        Convert.ToBase64String(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(value)));

    private static string NonCanonicalDigest(
        string canonical)
    {
        char current = canonical[^2];
        int index =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"
                .IndexOf(current);
        char replacement =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/"[
                index ^ 1];
        char[] mutated = canonical.ToCharArray();
        mutated[^2] = replacement;
        return new string(mutated);
    }

    private static T Uninitialized<T>()
        where T : class =>
        (T)RuntimeHelpers.GetUninitializedObject(
            typeof(T));

    private static void SetTenant<T>(
        T target,
        Guid tenantId)
        where T : class
    {
        FieldInfo? field = typeof(T).GetField(
            "_tenantId",
            BindingFlags.Instance
            | BindingFlags.NonPublic);
        if (field is not null)
        {
            field.SetValue(target, tenantId);
            return;
        }

        Set(target, "TenantId", tenantId);
    }

    private static void Set<T>(
        T target,
        string memberName,
        object? value)
        where T : class
    {
        FieldInfo? field = typeof(T).GetField(
            memberName,
            BindingFlags.Instance
            | BindingFlags.NonPublic);
        if (field is not null)
        {
            field.SetValue(target, value);
            return;
        }

        typeof(T).GetProperty(
                memberName,
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic)!
            .SetValue(target, value);
    }
}
