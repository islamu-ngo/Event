using Explore.Application.Contracts.Moderation;
using Explore.Application.Features.Moderation.Responses;
using Explore.Application.Features.Moderation.Responses.Commands;
using Explore.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationPreFindingAdministrationCommandTests
{
    [Test]
    public async Task CustodyIntentAcceptsExplicitFiniteTermsWithoutApprovingThem()
    {
        await Assert.That((await new ProposeModerationPreFindingCustodyCommandValidator()
            .ValidateAsync(Proposal())).IsValid).IsTrue();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task CustodyIntentRejectsIndefiniteMissingMandateOrInvalidIdentity(int change)
    {
        var proposal = Proposal();
        proposal = change switch
        {
            0 => proposal with { Terms = proposal.Terms with { MaximumRetention = TimeSpan.MaxValue } },
            1 => proposal with { Terms = proposal.Terms with { PrivacyLegalMandate = string.Empty } },
            2 => proposal with { PrivacyOwnerHumanId = Guid.NewGuid() },
            _ => proposal with { ValidUntil = DateTimeOffset.MaxValue }
        };
        await Assert.That((await new ProposeModerationPreFindingCustodyCommandValidator()
            .ValidateAsync(proposal)).IsValid).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NoticeIntentAcceptsCoherentHumanOrContentRecipientCoordinates(bool content)
    {
        var command = Notice() with
        {
            Recipient = new(content ? Guid.CreateVersion7() : Guid.Empty, content ? 1 : 0, Guid.CreateVersion7())
        };
        await Assert.That((await new IssueModerationPreFindingNoticeCommandValidator()
            .ValidateAsync(command)).IsValid).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NoticeIntentRejectsIncoherentRecipientAssignment(bool present)
    {
        var command = Notice() with
        {
            Recipient = new(present ? Guid.CreateVersion7() : Guid.Empty, present ? 0 : 1, Guid.CreateVersion7())
        };
        await Assert.That((await new IssueModerationPreFindingNoticeCommandValidator()
            .ValidateAsync(command)).IsValid).IsFalse();
    }

    [Test]
    public async Task AcceptanceCannotNameAnAlreadyAcceptedRevision()
    {
        var proposal = Proposal();
        var command = new AcceptModerationPreFindingCustodyCommand
        {
            OperationId = proposal.OperationId,
            CaseId = proposal.CaseId,
            ExpectedCaseRevision = 1,
            CustodyId = Guid.CreateVersion7(),
            ExpectedCustodyRevision = 1
        };
        var validator = new AcceptModerationPreFindingCustodyCommandValidator();
        await Assert.That((await validator.ValidateAsync(command)).IsValid).IsTrue();
        await Assert.That((await validator.ValidateAsync(command with { ExpectedCustodyRevision = 2 })).IsValid).IsFalse();
    }

    [Test]
    [Arguments(1L, true)]
    [Arguments(2L, true)]
    [Arguments(3L, false)]
    public async Task WithdrawalIntentCannotRenewTerminalCustody(long revision, bool valid)
    {
        var proposal = Proposal();
        var command = new WithdrawModerationPreFindingCustodyCommand
        {
            OperationId = proposal.OperationId,
            CaseId = proposal.CaseId,
            ExpectedCaseRevision = 1,
            CustodyId = Guid.CreateVersion7(),
            ExpectedCustodyRevision = revision
        };
        await Assert.That((await new WithdrawModerationPreFindingCustodyCommandValidator()
            .ValidateAsync(command)).IsValid).IsEqualTo(valid);
    }

    private static ProposeModerationPreFindingCustodyCommand Proposal() => new()
    {
        OperationId = Guid.CreateVersion7(),
        CaseId = Guid.CreateVersion7(),
        ExpectedCaseRevision = 1,
        PrivacyOwnerHumanId = Guid.CreateVersion7(),
        Terms = new(TimeSpan.FromHours(1), 32768, "Protected response opportunity", "Explicit case-purpose mandate"),
        ValidUntil = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero)
    };

    private static IssueModerationPreFindingNoticeCommand Notice() => new()
    {
        OperationId = Guid.CreateVersion7(),
        CaseId = Guid.CreateVersion7(),
        ExpectedCaseRevision = 1,
        CustodyId = Guid.CreateVersion7(),
        Recipient = new(Guid.CreateVersion7(), 1, Guid.CreateVersion7()),
        Text = new ModerationPreFindingText(new("Subject-safe allegation"), new("Applicable rule"), new("Respond privately")),
        RetainUntil = new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero)
    };
}
