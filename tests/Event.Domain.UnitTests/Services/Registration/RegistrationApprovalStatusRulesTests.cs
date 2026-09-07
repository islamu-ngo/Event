using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Event.Domain.UnitTests.Services.Registration;

[Category("EventLocationPrivacy")]
public sealed class RegistrationApprovalStatusRulesTests
{
    [Test]
    public async Task ApprovalStatusIdentifiersRemainStable()
    {
        await Assert.That((int)ApprovalStatusEnum.Pending).IsEqualTo(1);
        await Assert.That((int)ApprovalStatusEnum.Approved).IsEqualTo(2);
        await Assert.That((int)ApprovalStatusEnum.Rejected).IsEqualTo(3);
        await Assert.That((int)ApprovalStatusEnum.Waitlisted).IsEqualTo(4);
        await Assert.That((int)ApprovalStatusEnum.Cancelled).IsEqualTo(5);
        await Assert.That((int)ApprovalStatusEnum.Revoked).IsEqualTo(6);
    }

    [Test]
    [Arguments((int)ApprovalStatusEnum.Pending, true)]
    [Arguments((int)ApprovalStatusEnum.Approved, true)]
    [Arguments((int)ApprovalStatusEnum.Waitlisted, true)]
    [Arguments((int)ApprovalStatusEnum.Rejected, false)]
    [Arguments((int)ApprovalStatusEnum.Cancelled, false)]
    [Arguments((int)ApprovalStatusEnum.Revoked, false)]
    [Arguments(null, false)]
    public async Task LiveLocationDisclosureClassificationFailsClosed(int? approvalStatusId, bool expected)
    {
        var result = RegistrationApprovalStatusRules.IsLiveForLocationDisclosure(approvalStatusId);

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments((int)ApprovalStatusEnum.Pending)]
    [Arguments((int)ApprovalStatusEnum.Approved)]
    [Arguments((int)ApprovalStatusEnum.Waitlisted)]
    public async Task DeletedRegistrationNeverRemainsLive(int approvalStatusId)
    {
        var result = RegistrationApprovalStatusRules.IsLiveForLocationDisclosure(
            approvalStatusId,
            isDeleted: true);

        await Assert.That(result).IsFalse();
    }

    [Test]
    [Arguments((int)ApprovalStatusEnum.Rejected)]
    [Arguments((int)ApprovalStatusEnum.Cancelled)]
    [Arguments((int)ApprovalStatusEnum.Revoked)]
    public async Task DeniedStatusesAreTerminal(int approvalStatusId)
    {
        await Assert.That(RegistrationApprovalStatusRules.IsTerminal(approvalStatusId)).IsTrue();
        await Assert.That(RegistrationApprovalStatusRules.CanTransition(
            approvalStatusId,
            (int)ApprovalStatusEnum.Approved)).IsFalse();
        await Assert.That(RegistrationApprovalStatusRules.CanTransition(
            approvalStatusId,
            approvalStatusId)).IsTrue();
    }

    [Test]
    [Arguments((int)ApprovalStatusEnum.Pending, true)]
    [Arguments((int)ApprovalStatusEnum.Approved, true)]
    [Arguments((int)ApprovalStatusEnum.Waitlisted, false)]
    [Arguments((int)ApprovalStatusEnum.Cancelled, false)]
    [Arguments((int)ApprovalStatusEnum.Revoked, false)]
    public async Task OnlyPendingAndApprovedBearCapacity(int approvalStatusId, bool expected)
    {
        await Assert.That(RegistrationApprovalStatusRules.IsCapacityBearing(approvalStatusId))
            .IsEqualTo(expected);
    }

    [Test]
    [MethodDataSource(nameof(ParentStatusCases))]
    public async Task ParentStatusUsesLeastPrivilegeLiveChildOrTerminalFallback(
        (int?[] ChildApprovalStatusIds, int TerminalFallbackId, int ExpectedApprovalStatusId) testCase)
    {
        var result = RegistrationApprovalStatusRules.ResolveParentApprovalStatus(
            testCase.ChildApprovalStatusIds,
            testCase.TerminalFallbackId);

        await Assert.That(result).IsEqualTo(testCase.ExpectedApprovalStatusId);
    }

    public static IEnumerable<(int?[] ChildApprovalStatusIds, int TerminalFallbackId, int ExpectedApprovalStatusId)>
        ParentStatusCases()
    {
        yield return (
            [(int)ApprovalStatusEnum.Approved, (int)ApprovalStatusEnum.Pending],
            (int)ApprovalStatusEnum.Revoked,
            (int)ApprovalStatusEnum.Pending);
        yield return (
            [(int)ApprovalStatusEnum.Approved, (int)ApprovalStatusEnum.Waitlisted],
            (int)ApprovalStatusEnum.Revoked,
            (int)ApprovalStatusEnum.Waitlisted);
        yield return (
            [(int)ApprovalStatusEnum.Approved, (int)ApprovalStatusEnum.Rejected],
            (int)ApprovalStatusEnum.Revoked,
            (int)ApprovalStatusEnum.Approved);
        yield return (
            [(int)ApprovalStatusEnum.Cancelled, (int)ApprovalStatusEnum.Rejected],
            (int)ApprovalStatusEnum.Revoked,
            (int)ApprovalStatusEnum.Revoked);
    }

    [Test]
    public async Task RegistrationIdentityRequiresExactUserEventTenantAndParentParity()
    {
        var intentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        await Assert.That(RegistrationApprovalStatusRules.PreservesRegistrationIdentity(
            intentId,
            intentId,
            userId,
            userId,
            eventId,
            eventId,
            tenantId,
            tenantId)).IsTrue();
        await Assert.That(RegistrationApprovalStatusRules.PreservesRegistrationIdentity(
            intentId,
            Guid.NewGuid(),
            userId,
            userId,
            eventId,
            eventId,
            tenantId,
            tenantId)).IsFalse();
        await Assert.That(RegistrationApprovalStatusRules.PreservesRegistrationIdentity(
            intentId,
            intentId,
            userId,
            Guid.NewGuid(),
            eventId,
            eventId,
            tenantId,
            tenantId)).IsFalse();
        await Assert.That(RegistrationApprovalStatusRules.PreservesRegistrationIdentity(
            intentId,
            intentId,
            userId,
            userId,
            eventId,
            Guid.NewGuid(),
            tenantId,
            tenantId)).IsFalse();
        await Assert.That(RegistrationApprovalStatusRules.PreservesRegistrationIdentity(
            intentId,
            intentId,
            userId,
            userId,
            eventId,
            eventId,
            tenantId,
            Guid.NewGuid())).IsFalse();
    }
}
