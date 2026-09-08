using Explore.Blazor.Client.Helpers;

namespace Explore.Blazor.Client.Tests.Helpers;

public class RegistrationPolicyHelperTests
{
    // ========== GetAllowedScopes ==========

    [Test]
    public async Task GetAllowedScopes_NullPolicy_ReturnsAllScopes()
    {
        var result = RegistrationPolicyHelper.GetAllowedScopes(null);

        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result).Contains(RegistrationPolicyHelper.ScopeEvent);
        await Assert.That(result).Contains(RegistrationPolicyHelper.ScopeDay);
        await Assert.That(result).Contains(RegistrationPolicyHelper.ScopeSessionSelection);
    }

    [Test]
    public async Task GetAllowedScopes_WholeEventOnly_ReturnsOnlyEventScope()
    {
        var result = RegistrationPolicyHelper.GetAllowedScopes(1);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result).Contains(RegistrationPolicyHelper.ScopeEvent);
    }

    [Test]
    public async Task GetAllowedScopes_WholeDayOnly_ReturnsOnlyDayScope()
    {
        var result = RegistrationPolicyHelper.GetAllowedScopes(2);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result).Contains(RegistrationPolicyHelper.ScopeDay);
    }

    [Test]
    public async Task GetAllowedScopes_SessionSelectionOnly_ReturnsOnlySessionScope()
    {
        var result = RegistrationPolicyHelper.GetAllowedScopes(3);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result).Contains(RegistrationPolicyHelper.ScopeSessionSelection);
    }

    [Test]
    public async Task GetAllowedScopes_WholeEventOrDay_ReturnsEventAndDayScopes()
    {
        var result = RegistrationPolicyHelper.GetAllowedScopes(4);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result).Contains(RegistrationPolicyHelper.ScopeEvent);
        await Assert.That(result).Contains(RegistrationPolicyHelper.ScopeDay);
    }

    [Test]
    public async Task GetAllowedScopes_WholeEventOrSession_ReturnsEventAndSessionScopes()
    {
        var result = RegistrationPolicyHelper.GetAllowedScopes(5);

        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result).Contains(RegistrationPolicyHelper.ScopeEvent);
        await Assert.That(result).Contains(RegistrationPolicyHelper.ScopeSessionSelection);
    }

    [Test]
    public async Task GetAllowedScopes_Flexible_ReturnsAllScopes()
    {
        var result = RegistrationPolicyHelper.GetAllowedScopes(6);

        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result).Contains(RegistrationPolicyHelper.ScopeEvent);
        await Assert.That(result).Contains(RegistrationPolicyHelper.ScopeDay);
        await Assert.That(result).Contains(RegistrationPolicyHelper.ScopeSessionSelection);
    }

    [Test]
    public async Task GetAllowedScopes_UnknownPolicy_FallsBackToSessionSelection()
    {
        var result = RegistrationPolicyHelper.GetAllowedScopes(99);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result).Contains(RegistrationPolicyHelper.ScopeSessionSelection);
    }

    // ========== GetScopeLabel ==========

    [Test]
    public async Task GetScopeLabel_EventScope_ReturnsCorrectLabel()
    {
        var label = RegistrationPolicyHelper.GetScopeLabel(RegistrationPolicyHelper.ScopeEvent);
        await Assert.That(label).IsEqualTo("Register for the entire event");
    }

    [Test]
    public async Task GetScopeLabel_DayScope_ReturnsCorrectLabel()
    {
        var label = RegistrationPolicyHelper.GetScopeLabel(RegistrationPolicyHelper.ScopeDay);
        await Assert.That(label).IsEqualTo("Register for a specific day");
    }

    [Test]
    public async Task GetScopeLabel_SessionScope_ReturnsCorrectLabel()
    {
        var label = RegistrationPolicyHelper.GetScopeLabel(RegistrationPolicyHelper.ScopeSessionSelection);
        await Assert.That(label).IsEqualTo("Register for specific sessions");
    }

    [Test]
    public async Task GetScopeLabel_UnknownScope_ReturnsFallback()
    {
        var label = RegistrationPolicyHelper.GetScopeLabel(99);
        await Assert.That(label).IsEqualTo("Register");
    }

    // ========== GetScopeDescription ==========

    [Test]
    public async Task GetScopeDescription_EventScope_ReturnsCorrectDescription()
    {
        var desc = RegistrationPolicyHelper.GetScopeDescription(RegistrationPolicyHelper.ScopeEvent);
        await Assert.That(desc).IsEqualTo("You will be registered for all days and sessions");
    }

    [Test]
    public async Task GetScopeDescription_DayScope_ReturnsCorrectDescription()
    {
        var desc = RegistrationPolicyHelper.GetScopeDescription(RegistrationPolicyHelper.ScopeDay);
        await Assert.That(desc).IsEqualTo("Choose a day to attend");
    }

    [Test]
    public async Task GetScopeDescription_SessionScope_ReturnsCorrectDescription()
    {
        var desc = RegistrationPolicyHelper.GetScopeDescription(RegistrationPolicyHelper.ScopeSessionSelection);
        await Assert.That(desc).IsEqualTo("Pick individual sessions to attend");
    }

    [Test]
    public async Task GetScopeDescription_UnknownScope_ReturnsEmpty()
    {
        var desc = RegistrationPolicyHelper.GetScopeDescription(99);
        await Assert.That(desc).IsEqualTo(string.Empty);
    }

    // ========== Constants Verification ==========

    [Test]
    public async Task ScopeConstants_HaveExpectedValues()
    {
        await Assert.That(RegistrationPolicyHelper.ScopeEvent).IsEqualTo(1);
        await Assert.That(RegistrationPolicyHelper.ScopeDay).IsEqualTo(2);
        await Assert.That(RegistrationPolicyHelper.ScopeSessionSelection).IsEqualTo(3);
    }
}
