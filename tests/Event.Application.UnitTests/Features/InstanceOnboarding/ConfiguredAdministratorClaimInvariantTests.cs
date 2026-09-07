using Explore.Application.Authentication;
using Explore.Application.Responses;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Features.InstanceOnboarding;

public sealed class ConfiguredAdministratorClaimInvariantTests
{
    [Test]
    public async Task ExactProviderAccountGenerationAndFingerprintCompleteWithExactEvidence()
    {
        var scenario = new OnboardingCompletionScenario();

        BaseCommandResponse<Guid> response = await scenario.ClaimAsync();

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(scenario.Bootstrap.ProviderKind).IsEqualTo(AuthenticationProviderKind.Keycloak);
        await Assert.That(scenario.Bootstrap.Generation).IsEqualTo(7);
        await Assert.That(scenario.Bootstrap.CompletedIdentityFingerprint)
            .IsEqualTo(OnboardingCompletionScenario.Fingerprint);
        await Assert.That(scenario.Bootstrap.CompletedByUserId).IsEqualTo(scenario.UserId);
    }

    [Test]
    public async Task LocalIdentityProviderCanClaimConfiguredAdministratorExactlyOnce()
    {
        var scenario = new OnboardingCompletionScenario(
            providerKind: AuthenticationProviderKind.Local);

        BaseCommandResponse<Guid> response = await scenario.ClaimAsync();

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(scenario.Bootstrap.ProviderKind)
            .IsEqualTo(AuthenticationProviderKind.Local);
        await Assert.That(scenario.Bootstrap.CompletedByUserId)
            .IsEqualTo(scenario.UserId);
    }

    [Test]
    public async Task LocalIdentityBootstrapRejectsKeycloakAccountWithoutWrites()
    {
        var scenario = new OnboardingCompletionScenario(
            providerKind: AuthenticationProviderKind.Local);
        var keycloakAccount = new ProviderAccountKey(
            AuthenticationProviderKind.Keycloak,
            "subject-123");

        BaseCommandResponse<Guid> response = await scenario.ClaimAsync(
            account: keycloakAccount);

        await AssertBoundedMismatch(response, scenario);
    }

    [Test]
    public async Task IndirectOrWrongProviderIdentityProducesNoWrites()
    {
        var scenario = new OnboardingCompletionScenario();
        var wrongAccount = new ProviderAccountKey(AuthenticationProviderKind.Keycloak, "other-subject");

        BaseCommandResponse<Guid> response = await scenario.ClaimAsync(account: wrongAccount);

        await AssertBoundedMismatch(response, scenario);
    }

    [Test]
    public async Task BindingForDifferentProviderProducesNoWrites()
    {
        var scenario = new OnboardingCompletionScenario();
        scenario.BindingAccount = new ProviderAccountKey(AuthenticationProviderKind.Atproto, "did:plc:other");

        BaseCommandResponse<Guid> response = await scenario.ClaimAsync();

        await AssertBoundedMismatch(response, scenario);
    }

    [Test]
    public async Task DifferentGenerationProducesNoWrites()
    {
        var scenario = new OnboardingCompletionScenario { BindingGeneration = 8 };

        BaseCommandResponse<Guid> response = await scenario.ClaimAsync();

        await AssertBoundedMismatch(response, scenario);
    }

    [Test]
    public async Task DifferentFingerprintProducesNoWrites()
    {
        var scenario = new OnboardingCompletionScenario
        {
            BindingFingerprint = OnboardingCompletionScenario.OtherFingerprint
        };

        BaseCommandResponse<Guid> response = await scenario.ClaimAsync();

        await AssertBoundedMismatch(response, scenario);
    }

    [Test]
    public async Task ExactCompletedReplayReturnsOriginalBootstrapWithoutWritesAndReconcilesEffects()
    {
        var scenario = new OnboardingCompletionScenario();
        scenario.CompleteBootstrap();
        Guid bootstrapId = scenario.Bootstrap.Id;

        BaseCommandResponse<Guid> response = await scenario.ClaimAsync();

        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(response.Id).IsEqualTo(bootstrapId);
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        await Assert.That(scenario.PostCommitEffects)
            .IsEquivalentTo(["secret-lock", "admin-cache", "deployment-cache", "jwt-reload", "audit"]);
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task ConflictingCompletedReplayIsBoundedAndProducesNoWrites(int conflict)
    {
        var scenario = new OnboardingCompletionScenario();
        scenario.CompleteBootstrap();
        Guid replayUser = scenario.UserId;
        ProviderAccountKey replayAccount = scenario.Account;
        switch (conflict)
        {
            case 1:
                replayAccount = new ProviderAccountKey(AuthenticationProviderKind.Atproto, "did:plc:other");
                scenario.BindingAccount = replayAccount;
                break;
            case 2:
                scenario.BindingGeneration = 8;
                break;
            case 3:
                scenario.BindingFingerprint = OnboardingCompletionScenario.OtherFingerprint;
                break;
            case 4:
                replayUser = Guid.Parse("018e4e5c-7f00-7000-8000-000000000333");
                break;
        }

        BaseCommandResponse<Guid> response = await scenario.ClaimAsync(replayUser, replayAccount);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("configured_administrator_claim_conflict");
        await Assert.That(response.Id).IsEqualTo(Guid.Empty);
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        await Assert.That(scenario.PostCommitEffects).IsEmpty();
    }

    [Test]
    public async Task UnavailableVerifiedBindingReturnsBoundedFailureWithoutWrites()
    {
        var scenario = new OnboardingCompletionScenario { ProviderAvailable = false };

        BaseCommandResponse<Guid> response = await scenario.ClaimAsync();

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("configured_administrator_claim_mismatch");
        await Assert.That(response.Id).IsEqualTo(Guid.Empty);
        await Assert.That(response.Errors).IsNull();
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        await Assert.That(scenario.PostCommitEffects).IsEmpty();
    }

    private static async Task AssertBoundedMismatch(
        BaseCommandResponse<Guid> response,
        OnboardingCompletionScenario scenario)
    {
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsEqualTo("configured_administrator_claim_mismatch");
        await Assert.That(response.Id).IsEqualTo(Guid.Empty);
        await Assert.That(response.Errors).IsNull();
        await Assert.That(scenario.CommittedWrites).IsEmpty();
        await Assert.That(scenario.PostCommitEffects).IsEmpty();
    }
}
