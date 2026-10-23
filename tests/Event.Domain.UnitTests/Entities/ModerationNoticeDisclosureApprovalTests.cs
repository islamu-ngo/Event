using System.Collections.Immutable;

namespace Event.Domain.UnitTests.Entities;

public sealed class ModerationNoticeDisclosureApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ContentDisclosureRetainsExactRepresentativeAssignmentWithoutChangingItsCalendar()
    {
        var representative = new ModerationNoticeContentRepresentative(Guid.CreateVersion7(), 3, Guid.CreateVersion7());
        var intent = Intent() with { ContentRepresentatives = [representative] };
        var approval = ModerationNoticeDisclosureApproval.Approve(
            intent, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.NewGuid(), 9, Now);
        await Assert.That(approval.Intent.ContentRepresentatives.Single()).IsEqualTo(representative);
        await Assert.That(approval.RetainUntil).IsEqualTo(intent.RetainUntil);
        await Assert.That(approval.IsCurrentAt(intent with
        {
            ContentRepresentatives = [representative with { AssignmentRevision = 4 }]
        }, Now)).IsFalse();
    }

    [Test]
    [Arguments("default")]
    [Arguments("assignment")]
    [Arguments("human")]
    [Arguments("revision")]
    [Arguments("duplicate-assignment")]
    [Arguments("duplicate-human")]
    [Arguments("unbounded")]
    public async Task ContentDisclosureRejectsIncompleteDuplicateOrUnboundedRepresentativeCustody(string invalid)
    {
        var proof = new ModerationNoticeContentRepresentative(Guid.CreateVersion7(), 1, Guid.CreateVersion7());
        var intent = Intent() with
        {
            ContentRepresentatives = invalid switch
            {
                "default" => default,
                "assignment" => [proof with { AssignmentId = Guid.Empty }],
                "human" => [proof with { HumanId = Guid.Empty }],
                "revision" => [proof with { AssignmentRevision = 0 }],
                "duplicate-assignment" => [proof, proof with { HumanId = Guid.CreateVersion7() }],
                "duplicate-human" => [proof, proof with { AssignmentId = Guid.CreateVersion7() }],
                _ => Enumerable.Range(0, 33).Select(_ => new ModerationNoticeContentRepresentative(
                    Guid.CreateVersion7(), 1, Guid.CreateVersion7())).ToImmutableArray()
            }
        };
        await Assert.That(() => intent.Validate()).Throws<ArgumentException>();
    }

    [Test]
    public async Task Approval_RetainsExactFutureEffectAndApprovedContentWithoutEvidenceAuthority()
    {
        var intent = Intent();
        var human = Guid.NewGuid();
        var row = ModerationNoticeDisclosureApproval.Approve(
            intent, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), human, 9, Now);
        await Assert.That(row.IsCurrentAt(intent, Now)).IsTrue();
        await Assert.That(row.ApprovedByHumanId).IsEqualTo(human);
        await Assert.That(row.WriterGeneration).IsEqualTo(9);
        await Assert.That(row.Revision).IsEqualTo(1);
        await Assert.That(row.ApprovedAt).IsEqualTo(Now);
        await Assert.That(row.IsCurrentAt(intent, intent.ValidUntil)).IsFalse();
        await Assert.That(row.IsCurrentAt(intent, Now)).IsTrue();
        await Assert.That(row.ToString()).IsEqualTo(nameof(ModerationNoticeDisclosureApproval));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task Approval_CannotQualifyChangedEffectDecisionCasePublicationOrSelection(int change)
    {
        var intent = Intent();
        var row = ModerationNoticeDisclosureApproval.Approve(
            intent, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.NewGuid(), 9, Now);
        var changed = change switch
        {
            0 => intent with { EffectId = Guid.CreateVersion7() },
            1 => intent with { ExpectedDecisionRevision = intent.ExpectedDecisionRevision + 1 },
            2 => intent with { ExpectedCaseRevision = intent.ExpectedCaseRevision + 1 },
            3 => intent with { PublicationId = Guid.CreateVersion7() },
            4 => intent with { SelectionDigest = new string('b', 64) },
            _ => intent with { RetainUntil = intent.RetainUntil.AddDays(1) }
        };
        await Assert.That(row.IsCurrentAt(changed, Now)).IsFalse();
        await Assert.That(row.IsCurrentAt(intent, Now)).IsTrue();
    }

    [Test]
    public async Task Revocation_PreservesOriginalSafeContentAndNeverRenewsApproval()
    {
        var intent = Intent();
        var row = ModerationNoticeDisclosureApproval.Approve(
            intent, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.NewGuid(), 9, Now);
        var human = Guid.NewGuid();
        row.Revoke(1, human, Now.AddMinutes(1));
        await Assert.That(row.IsCurrentAt(intent, Now.AddMinutes(1))).IsFalse();
        await Assert.That(row.Revision).IsEqualTo(2);
        await Assert.That(row.RevokedByHumanId).IsEqualTo(human);
        await Assert.That(row.RuleMeaning).IsEqualTo(intent.RuleMeaning.Value);
        await Assert.That(row.ValidUntil).IsEqualTo(intent.ValidUntil);
        await Assert.That(row.RetainUntil).IsEqualTo(intent.RetainUntil);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            row.Revoke(1, Guid.NewGuid(), Now.AddMinutes(2));
            return Task.CompletedTask;
        });
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    public async Task Approval_RejectsIncompleteOrUnboundedNativeIntent(int invalid)
    {
        var intent = Intent();
        intent = invalid switch
        {
            0 => intent with { ApprovalId = Guid.NewGuid() },
            1 => intent with { ExpectedDecisionRevision = long.MaxValue },
            2 => intent with { SelectionDigest = "not-a-native-selection" },
            3 => intent with { ValidUntil = DateTimeOffset.MaxValue },
            4 => intent with { Facts = default },
            5 => intent with { RetainUntil = DateTimeOffset.MaxValue },
            6 => intent with { RetainUntil = DateTimeOffset.MinValue },
            _ => intent with { RetainUntil = Now }
        };
        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            _ = ModerationNoticeDisclosureApproval.Approve(
                intent, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.NewGuid(), 9, Now);
            return Task.CompletedTask;
        });
    }

    private static ModerationNoticeDisclosureIntent Intent() => new(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), 4,
        Guid.CreateVersion7(), 2, Guid.CreateVersion7(), 3, Guid.CreateVersion7(),
        Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', 64),
        new("Approved rule meaning."), new("Approved reason meaning."),
        ImmutableArray.Create(new ModerationNoticeSafeText("Approved material fact.")),
        new("Protected actions remain available."), new("Request independent review."),
        new("No automatic finding."), new("Retained only for this qualified purpose."),
        Now.AddMinutes(10), Now.AddYears(1));
}
