namespace Event.Distributed.IntegrationTests;

[Category("Harness")]
public sealed class BrowserArtifactPolicyTests
{
    [Test]
    [Arguments("pw:api", null)]
    [Arguments(null, "1")]
    [Arguments("*", null)]
    public async Task RawDebugEnvironmentIsRejected(string? debug, string? playwrightDebug)
    {
        await Assert.That(() => BrowserArtifactPolicy.AdmitDiagnostics(debug, playwrightDebug))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task AbsentDebugEnvironmentDoesNotRequireBrowserStartup()
    {
        BrowserArtifactPolicy.AdmitDiagnostics(null, null);
        BrowserArtifactPolicy.AdmitDiagnostics(string.Empty, string.Empty);
        await Assert.That(BrowserArtifactPolicy.Outcome(BrowserJourney.LoginLogout, true))
            .IsEqualTo("browser_login_logout_passed");
    }

    [Test]
    [Arguments("trace")]
    [Arguments("har")]
    [Arguments("storage-state")]
    [Arguments("screenshot")]
    [Arguments("video")]
    [Arguments("console")]
    [Arguments("response-body")]
    [Arguments("failure.txt")]
    [Arguments("../outcome")]
    [Arguments("Outcome")]
    public async Task SensitiveOrUnknownArtifactIsRejected(string artifact)
    {
        await Assert.That(() => BrowserArtifactPolicy.Admit(artifact))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task FailureCannotCarryCredentialOrResponseContent()
    {
        string canary = Guid.NewGuid().ToString("N");
        Exception source = new InvalidOperationException(
            $"Authorization: Bearer {canary}; password={canary}; user={canary}@example.test");

        Exception safe = BrowserArtifactPolicy.Sanitize(BrowserJourney.LoginLogout, source);

        await Assert.That(safe.Message).IsEqualTo("browser_login_logout_failed");
        await Assert.That(safe.InnerException).IsNull();
        await Assert.That(safe.Data.Count).IsEqualTo(0);
        await Assert.That(safe.ToString().Contains(canary, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task UnknownJourneyFailsClosedWithoutEchoingInput()
    {
        await Assert.That(() => BrowserArtifactPolicy.Outcome((BrowserJourney)int.MaxValue, true))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SafeOutcomeHasOnlyKnownJourneyAndResult()
    {
        BrowserArtifactPolicy.Admit("outcome");
        await Assert.That(BrowserArtifactPolicy.Outcome(BrowserJourney.FreeRegistration, true))
            .IsEqualTo("browser_free_registration_passed");
        await Assert.That(BrowserArtifactPolicy.Outcome(BrowserJourney.FreeRegistration, false))
            .IsEqualTo("browser_free_registration_failed");
    }
}
