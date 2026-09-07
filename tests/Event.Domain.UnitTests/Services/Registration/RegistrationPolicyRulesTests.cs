namespace Event.Domain.UnitTests.Services.Registration;

using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

public class RegistrationPolicyRulesTests
{
    // WholeEventOnly: only Event scope allowed
    [Test]
    [Arguments((int)RegistrationScopeEnum.Event, true)]
    [Arguments((int)RegistrationScopeEnum.Day, false)]
    [Arguments((int)RegistrationScopeEnum.SessionSelection, false)]
    public async Task WholeEventOnly_AllowsOnlyEventScope(int scopeId, bool expected)
    {
        var result = RegistrationPolicyRules.IsScopeAllowed(
            (int)EventRegistrationPolicyEnum.WholeEventOnly, scopeId);

        await Assert.That(result).IsEqualTo(expected);
    }

    // WholeDayOnly: only Day scope allowed
    [Test]
    [Arguments((int)RegistrationScopeEnum.Event, false)]
    [Arguments((int)RegistrationScopeEnum.Day, true)]
    [Arguments((int)RegistrationScopeEnum.SessionSelection, false)]
    public async Task WholeDayOnly_AllowsOnlyDayScope(int scopeId, bool expected)
    {
        var result = RegistrationPolicyRules.IsScopeAllowed(
            (int)EventRegistrationPolicyEnum.WholeDayOnly, scopeId);

        await Assert.That(result).IsEqualTo(expected);
    }

    // SessionSelectionOnly: only SessionSelection scope allowed
    [Test]
    [Arguments((int)RegistrationScopeEnum.Event, false)]
    [Arguments((int)RegistrationScopeEnum.Day, false)]
    [Arguments((int)RegistrationScopeEnum.SessionSelection, true)]
    public async Task SessionSelectionOnly_AllowsOnlySessionSelectionScope(int scopeId, bool expected)
    {
        var result = RegistrationPolicyRules.IsScopeAllowed(
            (int)EventRegistrationPolicyEnum.SessionSelectionOnly, scopeId);

        await Assert.That(result).IsEqualTo(expected);
    }

    // WholeEventOrDay: Event + Day allowed
    [Test]
    [Arguments((int)RegistrationScopeEnum.Event, true)]
    [Arguments((int)RegistrationScopeEnum.Day, true)]
    [Arguments((int)RegistrationScopeEnum.SessionSelection, false)]
    public async Task WholeEventOrDay_AllowsEventAndDayScopes(int scopeId, bool expected)
    {
        var result = RegistrationPolicyRules.IsScopeAllowed(
            (int)EventRegistrationPolicyEnum.WholeEventOrDay, scopeId);

        await Assert.That(result).IsEqualTo(expected);
    }

    // WholeEventOrSession: Event + SessionSelection allowed
    [Test]
    [Arguments((int)RegistrationScopeEnum.Event, true)]
    [Arguments((int)RegistrationScopeEnum.Day, false)]
    [Arguments((int)RegistrationScopeEnum.SessionSelection, true)]
    public async Task WholeEventOrSession_AllowsEventAndSessionSelectionScopes(int scopeId, bool expected)
    {
        var result = RegistrationPolicyRules.IsScopeAllowed(
            (int)EventRegistrationPolicyEnum.WholeEventOrSession, scopeId);

        await Assert.That(result).IsEqualTo(expected);
    }

    // Flexible: all scopes allowed
    [Test]
    [Arguments((int)RegistrationScopeEnum.Event)]
    [Arguments((int)RegistrationScopeEnum.Day)]
    [Arguments((int)RegistrationScopeEnum.SessionSelection)]
    public async Task Flexible_AllowsAllScopes(int scopeId)
    {
        var result = RegistrationPolicyRules.IsScopeAllowed(
            (int)EventRegistrationPolicyEnum.Flexible, scopeId);

        await Assert.That(result).IsTrue();
    }

    // Null policyId: treated as Flexible (backward compat for events without policy)
    [Test]
    [Arguments((int)RegistrationScopeEnum.Event)]
    [Arguments((int)RegistrationScopeEnum.Day)]
    [Arguments((int)RegistrationScopeEnum.SessionSelection)]
    public async Task NullPolicyId_TreatedAsFlexible_AllowsAllScopes(int scopeId)
    {
        var result = RegistrationPolicyRules.IsScopeAllowed(null, scopeId);

        await Assert.That(result).IsTrue();
    }

    // Unknown policy value: returns false for safety
    [Test]
    [Arguments((int)RegistrationScopeEnum.Event)]
    [Arguments((int)RegistrationScopeEnum.Day)]
    [Arguments((int)RegistrationScopeEnum.SessionSelection)]
    public async Task UnknownPolicyValue_ReturnsFalse(int scopeId)
    {
        var result = RegistrationPolicyRules.IsScopeAllowed(999, scopeId);

        await Assert.That(result).IsFalse();
    }

    [Test]
    [Category("EventLocationPrivacy")]
    [Arguments((int)RegistrationModeEnum.Open, (int)ApprovalStatusEnum.Approved)]
    [Arguments((int)RegistrationModeEnum.ApprovalRequired, (int)ApprovalStatusEnum.Pending)]
    [Arguments((int)RegistrationModeEnum.InviteOnly, null)]
    [Arguments((int)RegistrationModeEnum.Closed, null)]
    [Arguments(null, null)]
    public async Task ResolveInitialApprovalStatusCharacterizesEveryRegistrationMode(
        int? registrationModeId,
        int? expectedApprovalStatusId)
    {
        var result = RegistrationPolicyRules.ResolveInitialApprovalStatus([registrationModeId]);

        await Assert.That((int?)result).IsEqualTo(expectedApprovalStatusId);
    }

    [Test]
    [Category("EventLocationPrivacy")]
    public async Task ResolveInitialApprovalStatusUsesMostRestrictiveCoveredSessionAndDeniesEmptyCoverage()
    {
        var mixedResult = RegistrationPolicyRules.ResolveInitialApprovalStatus(
        [
            (int)RegistrationModeEnum.Open,
            (int)RegistrationModeEnum.ApprovalRequired
        ]);
        var emptyResult = RegistrationPolicyRules.ResolveInitialApprovalStatus([]);

        await Assert.That(mixedResult).IsEqualTo(ApprovalStatusEnum.Pending);
        await Assert.That(emptyResult).IsNull();
    }
}
