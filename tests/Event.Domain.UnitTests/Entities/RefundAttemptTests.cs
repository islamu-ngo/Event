using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class RefundAttemptTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid OrderId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000002");
    private static readonly Guid PaymentId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000003");
    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task AllocatePartial_UsesDeterministicLargestRemainderAndPreservesComposition()
    {
        RefundAllocation allocation = RefundAllocation.AllocatePartial(
            requestedTotalMinor: 563,
            capturedOrganizerMinor: 1_000,
            capturedPlatformFeeMinor: 75,
            capturedContributionMinor: 125);

        await Assert.That(allocation.OrganizerAmountMinor).IsEqualTo(500);
        await Assert.That(allocation.PlatformFeeMinor).IsEqualTo(38);
        await Assert.That(allocation.PlatformContributionMinor).IsEqualTo(63);
        await Assert.That(allocation.TotalMinor).IsEqualTo(563);
    }

    [Test]
    [Arguments(0, 1, 0, 0)]
    [Arguments(1, -1, 0, 2)]
    [Arguments(1, 1, -1, 0)]
    [Arguments(1, 1, 0, -1)]
    [Arguments(1, 1, 2, 0)]
    public async Task AllocatePartial_RejectsInvalidMoneyInputs(
        long requested, long organizer, long fee, long contribution)
    {
        await Assert.That(() => RefundAllocation.AllocatePartial(requested, organizer, fee, contribution))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task AllocatePartial_AllowsExactBoundaryAndZeroOrganizer()
    {
        RefundAllocation contributionOnly = RefundAllocation.AllocatePartial(1, 0, 0, 1);
        RefundAllocation organizerOnly = RefundAllocation.AllocatePartial(1, 1, 1, 0);

        await Assert.That(contributionOnly.OrganizerAmountMinor).IsEqualTo(0);
        await Assert.That(contributionOnly.PlatformFeeMinor).IsEqualTo(0);
        await Assert.That(contributionOnly.PlatformContributionMinor).IsEqualTo(1);
        await Assert.That(contributionOnly.TotalMinor).IsEqualTo(1);
        await Assert.That(organizerOnly.OrganizerAmountMinor).IsEqualTo(1);
        await Assert.That(organizerOnly.PlatformFeeMinor).IsEqualTo(1);
        await Assert.That(organizerOnly.PlatformContributionMinor).IsEqualTo(0);
        await Assert.That(organizerOnly.TotalMinor).IsEqualTo(1);
    }

    [Test]
    public async Task AllocatePartial_TiedRemainderDeterministicallyFavorsOrganizer()
    {
        RefundAllocation allocation = RefundAllocation.AllocatePartial(1, 1, 0, 1);

        await Assert.That(allocation.OrganizerAmountMinor).IsEqualTo(1);
        await Assert.That(allocation.PlatformContributionMinor).IsEqualTo(0);
        await Assert.That(allocation.TotalMinor).IsEqualTo(1);
    }

    [Test]
    public async Task AllocateReservationDelta_SequentialRefundsConsumeEachComponentExactlyOnce()
    {
        RefundAllocation firstFee = RefundAllocation.AllocateReservationDelta(0, 1, 2, 1, 0, 0, 0, 0);
        RefundAllocation secondFee = RefundAllocation.AllocateReservationDelta(
            1, 1, 2, 1, 0, firstFee.OrganizerAmountMinor, firstFee.PlatformFeeMinor, 0);
        RefundAllocation firstContribution = RefundAllocation.AllocateReservationDelta(0, 1, 1, 0, 1, 0, 0, 0);
        RefundAllocation secondContribution = RefundAllocation.AllocateReservationDelta(
            1, 1, 1, 0, 1, firstContribution.OrganizerAmountMinor, 0,
            firstContribution.PlatformContributionMinor);

        await Assert.That(firstFee.PlatformFeeMinor + secondFee.PlatformFeeMinor).IsEqualTo(1);
        await Assert.That(firstFee.OrganizerAmountMinor + secondFee.OrganizerAmountMinor).IsEqualTo(2);
        await Assert.That(firstContribution.PlatformContributionMinor + secondContribution.PlatformContributionMinor)
            .IsEqualTo(1);
        await Assert.That(firstContribution.OrganizerAmountMinor + secondContribution.OrganizerAmountMinor).IsEqualTo(1);
    }

    [Test]
    public async Task AllocateReservationDelta_FragmentedReleaseNeverRequiresANegativeFeeShare()
    {
        var allocations = new List<RefundAllocation>();
        for (int index = 0; index < 5; index++)
        {
            allocations.Add(RefundAllocation.AllocateReservationDelta(
                index,
                1,
                10,
                5,
                0,
                allocations.Sum(value => value.OrganizerAmountMinor),
                allocations.Sum(value => value.PlatformFeeMinor),
                0));
        }

        RefundAllocation[] active = [allocations[0], allocations[2], allocations[4]];
        RefundAllocation replacement = RefundAllocation.AllocateReservationDelta(
            3,
            1,
            10,
            5,
            0,
            active.Sum(value => value.OrganizerAmountMinor),
            active.Sum(value => value.PlatformFeeMinor),
            0);

        await Assert.That(allocations.Select(value => value.PlatformFeeMinor).ToArray())
            .IsEquivalentTo(new long[] { 1, 0, 1, 0, 1 });
        await Assert.That(replacement.TotalMinor).IsEqualTo(1);
        await Assert.That(replacement.PlatformFeeMinor).IsEqualTo(0);
    }

    [Test]
    public async Task AllocateReservationDeltaRejectsCorruptPersistedComponentTotals()
    {
        await Assert.That(() => RefundAllocation.AllocateReservationDelta(-1, 1, 10, 5, 0, 0, 0, 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RefundAllocation.AllocateReservationDelta(1, 1, 10, 5, 0, -1, 0, 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RefundAllocation.AllocateReservationDelta(1, 1, 10, 5, 0, 1, -1, 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RefundAllocation.AllocateReservationDelta(1, 1, 10, 5, 1, 1, 0, -1))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RefundAllocation.AllocateReservationDelta(11, 1, 10, 5, 1, 11, 0, 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RefundAllocation.AllocateReservationDelta(1, 1, 10, 5, 0, 1, 6, 0))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RefundAllocation.AllocateReservationDelta(2, 1, 10, 5, 1, 0, 0, 2))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RefundAllocation.AllocateReservationDelta(2, 1, 10, 5, 0, 1, 0, 0))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task AllocateReservationDeltaConvergesAfterEitherBuyerComponentIsFragmented()
    {
        var allocations = new List<RefundAllocation>();
        for (int index = 0; index < 6; index++)
        {
            allocations.Add(RefundAllocation.AllocateReservationDelta(
                index,
                1,
                5,
                0,
                5,
                allocations.Sum(value => value.OrganizerAmountMinor),
                0,
                allocations.Sum(value => value.PlatformContributionMinor)));
        }

        RefundAllocation[] organizerHeavy = [allocations[0], allocations[2], allocations[4]];
        RefundAllocation organizerCorrection = RefundAllocation.AllocateReservationDelta(
            3, 1, 5, 0, 5,
            organizerHeavy.Sum(value => value.OrganizerAmountMinor), 0,
            organizerHeavy.Sum(value => value.PlatformContributionMinor));
        RefundAllocation[] contributionHeavy = [allocations[1], allocations[3], allocations[5]];
        RefundAllocation contributionCorrection = RefundAllocation.AllocateReservationDelta(
            3, 1, 5, 0, 5,
            contributionHeavy.Sum(value => value.OrganizerAmountMinor), 0,
            contributionHeavy.Sum(value => value.PlatformContributionMinor));

        await Assert.That(organizerCorrection.OrganizerAmountMinor).IsEqualTo(0L);
        await Assert.That(organizerCorrection.PlatformContributionMinor).IsEqualTo(1L);
        await Assert.That(contributionCorrection.OrganizerAmountMinor).IsEqualTo(1L);
        await Assert.That(contributionCorrection.PlatformContributionMinor).IsEqualTo(0L);
    }

    [Test]
    public async Task AllocateLinesRejectsMissingOrInvalidAcceptedLineage()
    {
        PaidOrderAcceptanceSnapshot acceptance = Acceptance([1, 1]);
        RefundAllocation allocation = RefundAllocation.AllocatePartial(1, 2, 0, 0);
        Guid refundId = Guid.CreateVersion7();

        ArgumentNullException missingAllocation = Assert.Throws<ArgumentNullException>(
            () => RefundLineAllocation.Allocate(TenantId, refundId, null!, acceptance.Lines));
        ArgumentNullException missingLines = Assert.Throws<ArgumentNullException>(
            () => RefundLineAllocation.Allocate(TenantId, refundId, allocation, null!));
        await Assert.That(missingAllocation.ParamName).IsEqualTo("allocation");
        await Assert.That(missingLines.ParamName).IsEqualTo("acceptedLines");
        ArgumentException empty = Assert.Throws<ArgumentException>(
            () => RefundLineAllocation.Allocate(TenantId, refundId, allocation, []));
        await Assert.That(empty.Message).Contains("valid accepted tenant lineage");
        await Assert.That(() => RefundLineAllocation.Allocate(Guid.Empty, refundId, allocation, acceptance.Lines))
            .Throws<ArgumentException>();
        await Assert.That(() => RefundLineAllocation.Allocate(TenantId, Guid.Empty, allocation, acceptance.Lines))
            .Throws<ArgumentException>();

        PaidOrderAcceptanceLine line = acceptance.Lines.First();
        line.TenantId = Guid.CreateVersion7();
        ArgumentException wrongTenant = Assert.Throws<ArgumentException>(
            () => RefundLineAllocation.Allocate(TenantId, refundId, allocation, acceptance.Lines));
        await Assert.That(wrongTenant.Message).Contains("valid accepted tenant lineage");
    }

    [Test]
    public async Task AllocateLinesSortsByOrdinalAndUsesOrdinalForTiedRemainders()
    {
        PaidOrderAcceptanceSnapshot acceptance = Acceptance([1, 1]);
        RefundAllocation allocation = RefundAllocation.AllocatePartial(1, 2, 0, 0);

        IReadOnlyList<RefundLineAllocation> lines = RefundLineAllocation.Allocate(
            TenantId,
            Guid.CreateVersion7(),
            allocation,
            acceptance.Lines.Reverse().ToArray());

        await Assert.That(lines.Select(line => line.Ordinal).SequenceEqual([0, 1])).IsTrue();
        await Assert.That(lines.Select(line => line.OrganizerAmountMinor).SequenceEqual([1L, 0L])).IsTrue();
    }

    [Test]
    public async Task AllocateLinesRejectsZeroAcceptedWeight()
    {
        Guid acceptanceId = Guid.CreateVersion7();
        PaidOrderAcceptanceLine line = PaidOrderAcceptanceLine.Create(
            TenantId,
            acceptanceId,
            PaidOrderAcceptanceLineFact.Create(Guid.CreateVersion7(), "Zero", 1, 0, 0, 0),
            0);

        ArgumentException exception = Assert.Throws<ArgumentException>(() => RefundLineAllocation.Allocate(
            TenantId,
            Guid.CreateVersion7(),
            RefundAllocation.AllocatePartial(1, 1, 0, 0),
            [line]));
        await Assert.That(exception.Message).Contains("positive accepted value");
    }

    [Test]
    public async Task AllocateRemainingLinesRejectsPersistedUsageBeyondAcceptedCapacity()
    {
        PaidOrderAcceptanceSnapshot acceptance = Acceptance([1, 2], platformFeeMinor: 1);
        RefundAttempt first = RefundAttempt.Create(
            Guid.CreateVersion7(), TenantId, PaymentId, acceptance, "acct_original",
            "pi_123", "refund-corrupt-first", 3, Now);
        RefundAttempt second = RefundAttempt.Create(
            Guid.CreateVersion7(), TenantId, PaymentId, acceptance, "acct_original",
            "pi_123", "refund-corrupt-second", 3, Now);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            RefundLineAllocation.AllocateFromRemaining(
                TenantId,
                Guid.CreateVersion7(),
                RefundAllocation.AllocatePartial(1, 3, 1, 0),
                acceptance,
                [first, second]));

        await Assert.That(exception.Message).IsEqualTo("Persisted refund lines exceed accepted line capacity.");
    }

    [Test]
    public async Task AllocateRemainingLinesRequiresEveryAuthorityInput()
    {
        PaidOrderAcceptanceSnapshot acceptance = Acceptance([1, 2], platformFeeMinor: 1);
        RefundAllocation allocation = RefundAllocation.AllocatePartial(1, 3, 1, 0);
        Guid refundId = Guid.CreateVersion7();

        ArgumentNullException missingAllocation = Assert.Throws<ArgumentNullException>(() =>
            RefundLineAllocation.AllocateFromRemaining(TenantId, refundId, null!, acceptance, []));
        ArgumentNullException missingAcceptance = Assert.Throws<ArgumentNullException>(() =>
            RefundLineAllocation.AllocateFromRemaining(TenantId, refundId, allocation, null!, []));
        ArgumentNullException missingAttempts = Assert.Throws<ArgumentNullException>(() =>
            RefundLineAllocation.AllocateFromRemaining(TenantId, refundId, allocation, acceptance, null!));

        await Assert.That(missingAllocation.ParamName).IsEqualTo("allocation");
        await Assert.That(missingAcceptance.ParamName).IsEqualTo("acceptance");
        await Assert.That(missingAttempts.ParamName).IsEqualTo("existingAttempts");
    }

    [Test]
    [Arguments(2, 1, 0, 0)]
    [Arguments(1, 0, 0, 0)]
    public async Task AllocatePartial_RejectsAmountsWithoutCapturedCapacity(
        long requested, long organizer, long fee, long contribution)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => RefundAllocation.AllocatePartial(requested, organizer, fee, contribution));

        await Assert.That(exception.Message).IsEqualTo("Refund amount exceeds the captured amount.");
    }

    [Test]
    public async Task Reserve_AmbiguousAttemptsConsumeCapacityAndReleasedTerminalsDoNot()
    {
        RefundAttempt pending = Attempt(RefundAttemptStatusEnum.Pending, 600);
        RefundAttempt unknown = Attempt(RefundAttemptStatusEnum.Unknown, 300);
        RefundAttempt failed = Attempt(RefundAttemptStatusEnum.Failed, 900);

        await Assert.That(() => RefundReservationRules.EnsureReservable(1_000, [pending, unknown, failed], [], 101))
            .Throws<InvalidOperationException>();
        RefundReservationRules.EnsureReservable(1_000, [pending, unknown, failed], [], 100);
    }

    [Test]
    public async Task Reserve_OpenDisputeBlocksOrdinaryRefund()
    {
        PaymentDispute dispute = PaymentDispute.Observe(
            Guid.CreateVersion7(), TenantId, PaymentId, "dp_123", PaymentDisputeStage.Formal,
            PaymentDisputeStatus.Open, 1_125, "EUR", Now);

        await Assert.That(() => RefundReservationRules.EnsureReservable(1_125, [], [dispute], 100))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task Reserve_RequiresBothCollections()
    {
        ArgumentNullException attemptsException = Assert.Throws<ArgumentNullException>(
            () => RefundReservationRules.EnsureReservable(100, null!, [], 1));
        ArgumentNullException disputesException = Assert.Throws<ArgumentNullException>(
            () => RefundReservationRules.EnsureReservable(100, [], null!, 1));

        await Assert.That(attemptsException.ParamName).IsEqualTo("existingAttempts");
        await Assert.That(disputesException.ParamName).IsEqualTo("disputes");
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(-1, 1)]
    [Arguments(1, 0)]
    [Arguments(1, -1)]
    public async Task Reserve_RejectsNonPositiveCapturedOrRequestedMoney(long captured, long requested)
    {
        await Assert.That(() => RefundReservationRules.EnsureReservable(captured, [], [], requested))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Reserve_CountsSucceededCapacityAndExcludesReleasedTerminals()
    {
        RefundAttempt active = Attempt(RefundAttemptStatusEnum.Pending, 900);
        RefundAttempt succeeded = Attempt(RefundAttemptStatusEnum.Succeeded, 900);
        RefundAttempt failed = Attempt(RefundAttemptStatusEnum.Failed, 900);
        RefundAttempt cancelled = Attempt(RefundAttemptStatusEnum.Cancelled, 900);

        RefundReservationRules.EnsureReservable(1_000, [failed, cancelled], [], 1_000);
        await Assert.That(() => RefundReservationRules.EnsureReservable(1_000, [succeeded], [], 101))
            .Throws<InvalidOperationException>();
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => RefundReservationRules.EnsureReservable(1_000, [active, failed], [], 101));

        await Assert.That(exception.Message).IsEqualTo("Refund reservation exceeds captured payment capacity.");
    }

    [Test]
    public async Task Reserve_OpenDisputeUsesStableFailureReason()
    {
        PaymentDispute dispute = PaymentDispute.Observe(
            Guid.CreateVersion7(), TenantId, PaymentId, "dp_open", PaymentDisputeStage.Inquiry,
            PaymentDisputeStatus.Open, 100, "EUR", Now);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => RefundReservationRules.EnsureReservable(100, [], [dispute], 100));

        await Assert.That(exception.Message).IsEqualTo("An open dispute blocks ordinary refunds.");
    }

    [Test]
    public async Task Dispute_TerminalEvidenceWinsTimestampTieAndCannotReopen()
    {
        DateTime deadline = Now.AddDays(1);
        PaymentDispute dispute = PaymentDispute.Observe(
            Guid.CreateVersion7(), TenantId, PaymentId, "dp_tie", PaymentDisputeStage.Inquiry,
            PaymentDisputeStatus.Open, 100, "EUR", Now, deadline);

        bool closed = dispute.ApplyProviderEvidence(
            PaymentDisputeStage.Formal, PaymentDisputeStatus.Won, 100, "EUR", Now);
        bool reopened = dispute.ApplyProviderEvidence(
            PaymentDisputeStage.Formal, PaymentDisputeStatus.Open, 100, "EUR", Now.AddSeconds(1), deadline);

        await Assert.That(closed).IsTrue();
        await Assert.That(reopened).IsFalse();
        await Assert.That(dispute.Stage).IsEqualTo(PaymentDisputeStage.Formal);
        await Assert.That(dispute.Status).IsEqualTo(PaymentDisputeStatus.Won);
        await Assert.That(dispute.ResponseDueAt).IsNull();
        await Assert.That(dispute.LastObservedAt).IsEqualTo(Now);
    }

    [Test]
    public async Task Attempt_PinsOriginalConnectedAccountAndOnlyProviderProofMarksSucceeded()
    {
        RefundAttempt attempt = Attempt(RefundAttemptStatusEnum.Requested, 500);

        attempt.MarkDispatchPending(Now.AddSeconds(1), "req_1");
        attempt.MarkUnknown(Now.AddSeconds(2), "req_2");
        attempt.MarkPending("re_123", Now.AddSeconds(3), "req_3");
        attempt.MarkSucceeded("re_123", Now.AddSeconds(4), "req_4");

        await Assert.That(attempt.ExternalAccountId).IsEqualTo("acct_original");
        await Assert.That(attempt.Status).IsEqualTo(RefundAttemptStatusEnum.Succeeded);
        await Assert.That(attempt.SucceededAt).IsEqualTo(Now.AddSeconds(4));
        await Assert.That(() => attempt.MarkFailed("re_123", Now.AddSeconds(5), "req_5"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task BuyerRefundSuccessCannotReleaseCapacityThroughLateFailureEvidence()
    {
        RefundAttempt attempt = Attempt(RefundAttemptStatusEnum.Requested, 500);
        attempt.MarkBuyerRefundSucceeded("re_123", Now.AddSeconds(1), "req_buyer");
        attempt.MarkProviderBlocked(Now.AddSeconds(2), "req_fee", "refund_provider_fee_rejected");

        await Assert.That(() => attempt.MarkFailed("re_123", Now.AddSeconds(3), "req_late"))
            .Throws<InvalidOperationException>();
        await Assert.That(() => attempt.MarkCancelled("re_123", Now.AddSeconds(3), "req_late"))
            .Throws<InvalidOperationException>();
        await Assert.That(attempt.ReservesCapacity).IsTrue();
        await Assert.That(attempt.BuyerRefundSucceededAt).IsEqualTo(Now.AddSeconds(1));
    }

    [Test]
    public async Task Create_PinsAcceptedPolicyAndAllocatesEveryMinorUnitAcrossAcceptedLines()
    {
        PaidOrderAcceptanceSnapshot acceptance = Acceptance();

        RefundAttempt attempt = RefundAttempt.Create(
            Guid.CreateVersion7(), TenantId, PaymentId, acceptance, "acct_original",
            "pi_123", "refund-idem-123", requestedTotalMinor: 500, Now);

        await Assert.That(attempt.PaidOrderAcceptanceSnapshotId).IsEqualTo(acceptance.Id);
        await Assert.That(attempt.RefundPolicyVersion).IsEqualTo(acceptance.RefundPolicyVersion);
        await Assert.That(attempt.RefundPolicyText).IsEqualTo(acceptance.RefundPolicyText);
        await Assert.That(attempt.Lines.Sum(line => line.OrganizerAmountMinor)).IsEqualTo(attempt.Allocation.OrganizerAmountMinor);
        await Assert.That(attempt.Lines.Sum(line => line.PlatformFeeMinor)).IsEqualTo(attempt.Allocation.PlatformFeeMinor);
        await Assert.That(attempt.Lines.Sum(line => line.PlatformContributionMinor)).IsEqualTo(attempt.Allocation.PlatformContributionMinor);
        await Assert.That(attempt.Lines.Sum(line => line.TotalMinor)).IsEqualTo(attempt.Allocation.TotalMinor);
        await Assert.That(attempt.Lines.Select(line => line.OrderLineId).ToArray())
            .IsEquivalentTo(acceptance.Lines.Select(line => line.OrderLineId).ToArray());
    }

    [Test]
    public async Task ReallocateFromRemainingLineCapacityNeverCreatesNegativeLargestRemainderShares()
    {
        PaidOrderAcceptanceSnapshot acceptance = Acceptance([1_500, 1_500, 900, 500, 500, 200]);
        RefundAttempt first = RefundAttempt.Create(
            Guid.CreateVersion7(), TenantId, PaymentId, acceptance, "acct_original",
            "pi_123", "refund-line-first", 25, Now);
        first.ReallocateForReservation([], acceptance);
        RefundAttempt second = RefundAttempt.Create(
            Guid.CreateVersion7(), TenantId, PaymentId, acceptance, "acct_original",
            "pi_123", "refund-line-second", 1, Now);

        second.ReallocateForReservation([first], acceptance);

        await Assert.That(second.Lines.All(line => line.OrganizerAmountMinor >= 0)).IsTrue();
        await Assert.That(first.Lines.Select(line => line.OrganizerAmountMinor)
            .SequenceEqual([7L, 7L, 4L, 3L, 3L, 1L])).IsTrue();
        await Assert.That(second.Lines.Select(line => line.OrganizerAmountMinor)
            .SequenceEqual([1L, 0L, 0L, 0L, 0L, 0L])).IsTrue();
        await Assert.That(second.Lines.Sum(line => line.TotalMinor)).IsEqualTo(1);
        await Assert.That(first.Lines.Sum(line => line.TotalMinor) + second.Lines.Sum(line => line.TotalMinor))
            .IsEqualTo(26);
    }

    [Test]
    public async Task ReallocateLineFeeNeverExceedsTheSameAttemptOrganizerShare()
    {
        PaidOrderAcceptanceSnapshot acceptance = Acceptance([1, 2], platformFeeMinor: 1);
        RefundAttempt first = RefundAttempt.Create(
            Guid.CreateVersion7(), TenantId, PaymentId, acceptance, "acct_original",
            "pi_123", "refund-line-fee-first", 1, Now);
        first.ReallocateForReservation([], acceptance);
        RefundAttempt second = RefundAttempt.Create(
            Guid.CreateVersion7(), TenantId, PaymentId, acceptance, "acct_original",
            "pi_123", "refund-line-fee-second", 1, Now);

        second.ReallocateForReservation([first], acceptance);

        await Assert.That(first.Lines.Concat(second.Lines)
            .All(line => line.PlatformFeeMinor <= line.OrganizerAmountMinor)).IsTrue();
        await Assert.That(second.Lines.Sum(line => line.PlatformFeeMinor))
            .IsEqualTo(second.Allocation.PlatformFeeMinor);
        await Assert.That(second.Lines.Select(line => line.OrganizerAmountMinor).SequenceEqual([1L, 0L])).IsTrue();
        await Assert.That(second.Lines.Select(line => line.PlatformFeeMinor).SequenceEqual([1L, 0L])).IsTrue();
    }

    private static RefundAttempt Attempt(RefundAttemptStatusEnum status, long totalMinor)
    {
        RefundAttempt attempt = RefundAttempt.Create(
            Guid.CreateVersion7(), TenantId, PaymentId, Acceptance(), "acct_original",
            "pi_123", $"refund-{Guid.CreateVersion7():N}", totalMinor, Now);

        if (status == RefundAttemptStatusEnum.Requested)
        {
            return attempt;
        }

        attempt.MarkDispatchPending(Now.AddSeconds(1), null);
        return status switch
        {
            RefundAttemptStatusEnum.DispatchPending => attempt,
            RefundAttemptStatusEnum.Pending => MarkPending(attempt),
            RefundAttemptStatusEnum.RequiresAction => MarkRequiresAction(attempt),
            RefundAttemptStatusEnum.Unknown => MarkUnknown(attempt),
            RefundAttemptStatusEnum.Succeeded => MarkSucceeded(attempt),
            RefundAttemptStatusEnum.Failed => MarkFailed(attempt),
            RefundAttemptStatusEnum.Cancelled => MarkCancelled(attempt),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
    }

    private static RefundAttempt MarkPending(RefundAttempt attempt) { attempt.MarkPending("re_123", Now.AddSeconds(2), null); return attempt; }
    private static RefundAttempt MarkRequiresAction(RefundAttempt attempt) { attempt.MarkRequiresAction("re_123", Now.AddSeconds(2), null); return attempt; }
    private static RefundAttempt MarkUnknown(RefundAttempt attempt) { attempt.MarkUnknown(Now.AddSeconds(2), null); return attempt; }
    private static RefundAttempt MarkSucceeded(RefundAttempt attempt) { attempt.MarkSucceeded("re_123", Now.AddSeconds(2), null); return attempt; }
    private static RefundAttempt MarkFailed(RefundAttempt attempt) { attempt.MarkFailed("re_123", Now.AddSeconds(2), null); return attempt; }
    private static RefundAttempt MarkCancelled(RefundAttempt attempt) { attempt.MarkCancelled("re_123", Now.AddSeconds(2), null); return attempt; }

    private static PaidOrderAcceptanceSnapshot Acceptance() => Acceptance([333, 667], 75, 125);

    private static PaidOrderAcceptanceSnapshot Acceptance(
        IReadOnlyList<long> lineTotals,
        long platformFeeMinor = 0,
        long contributionMinor = 0)
    {
        long organizerMinor = lineTotals.Sum();
        return PaidOrderAcceptanceSnapshot.Create(
            Guid.CreateVersion7(),
            TenantId,
            TenantId,
            OrderId,
            Guid.CreateVersion7(),
            "composition-1",
            "disclosure-1",
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateIdentifier,
            PaidOrderAcceptanceSnapshot.CurrentAcceptanceTemplateText,
            Guid.CreateVersion7(),
            "Example Organizer",
            PaidCheckoutTenantDirectoryOperatorDisclosure.Create(
                Guid.CreateVersion7(), Guid.CreateVersion7(), "Community Events", "Community Events ASBL",
                "registered_organization", "BE", null, "contact@example.test", "https://example.test/legal",
                "https://example.test/terms", "https://example.test/privacy"),
            PaidCheckoutOperatorDisclosure.Create(
                Guid.CreateVersion7(), "Example Operator", false, "https://events.example.test", "BE",
                "https://events.example.test", "https://events.example.test/legal", "https://events.example.test/terms",
                "https://events.example.test/privacy", "complaints@example.test", "Trust and Safety", "Payments Operations",
                "Dispute Operations", "Payment Reconciliation", "approved"),
            PaidOrderDeliverySnapshot.Create(
                DateTimeOffset.Parse("2026-09-10T17:00:00Z"),
                DateTimeOffset.Parse("2026-09-10T20:00:00Z"),
                "Europe/Brussels"),
            "EUR",
            organizerMinor,
            platformFeeMinor,
            contributionMinor,
            organizerMinor + contributionMinor,
            Guid.CreateVersion7(),
            7,
            "Refunds follow accepted policy v7.",
            "en-GB",
            "support@example.test",
            PaidCheckoutProviderDisclosure.Create(
                "stripe", "OrganizerDirect", "direct-charge", "EXAMPLE EVENT", "test", "instance-operator"),
            lineTotals.Select((total, index) => PaidOrderAcceptanceLineFact.Create(
                Guid.CreateVersion7(), $"Line {index + 1}", 1, total, 0, total)).ToArray(),
            Now, organizerPaymentProviderConnectionId: Guid.CreateVersion7(),
            connectPlatformId: "platform-live-eu", externalAccountId: "acct_123", merchantCountryCode: "BE");
    }
}
