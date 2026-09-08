namespace Event.Domain.UnitTests.InstanceOnboarding;

using System.Text.Json;
using Explore.Domain;
using Explore.Domain.Enums;

public sealed class InstanceBootstrapStateTests
{
    private static readonly DateTime CreatedAt = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SupersededAt = CreatedAt.AddMinutes(5);
    private static readonly DateTime CompletedAt = CreatedAt.AddMinutes(10);
    private static readonly Guid InitialId = Guid.Parse("01991f00-0000-7000-8000-000000000001");
    private static readonly Guid ReplacementId = Guid.Parse("01991f00-0000-7000-8000-000000000002");
    private static readonly Guid UserId = Guid.Parse("01991f00-0000-7000-8000-000000000003");

    [Test]
    public async Task ContractUsesStableTypedEnumValues()
    {
        await Assert.That((int)InstanceBootstrapStatus.Pending).IsEqualTo(1);
        await Assert.That((int)InstanceBootstrapStatus.Superseded).IsEqualTo(2);
        await Assert.That((int)InstanceBootstrapStatus.Completed).IsEqualTo(3);
        await Assert.That((int)InstanceBootstrapMode.Interactive).IsEqualTo(1);
        await Assert.That((int)InstanceBootstrapMode.ConfiguredAdministrator).IsEqualTo(2);
        await Assert.That((int)AuthenticationProviderKind.Keycloak).IsEqualTo(1);
        await Assert.That((int)AuthenticationProviderKind.Atproto).IsEqualTo(2);
    }

    [Test]
    public async Task PublicStateIsReadOnlyAndContainsNoObsoleteAliasesOrRawIdentityFields()
    {
        InstanceBootstrapState state = CompletedConfigured();
        JsonElement serialized = JsonSerializer.SerializeToElement(state);
        string[] propertyNames = serialized.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(propertyNames).IsEquivalentTo(new[]
        {
            nameof(InstanceBootstrapState.CompletedAt),
            nameof(InstanceBootstrapState.CompletedByUserId),
            nameof(InstanceBootstrapState.CompletedIdentityFingerprint),
            nameof(InstanceBootstrapState.ConfigurationFingerprint),
            nameof(InstanceBootstrapState.CreatedAt),
            nameof(InstanceBootstrapState.DeploymentMode),
            nameof(InstanceBootstrapState.Generation),
            nameof(InstanceBootstrapState.Id),
            nameof(InstanceBootstrapState.Mode),
            nameof(InstanceBootstrapState.ProviderKind),
            nameof(InstanceBootstrapState.SelectorFingerprint),
            nameof(InstanceBootstrapState.Status),
            nameof(InstanceBootstrapState.SupersededAt)
        });
        await Assert.That(() => JsonSerializer.Deserialize<InstanceBootstrapState>(serialized))
            .Throws<NotSupportedException>();
    }

    [Test]
    public async Task InteractiveFactoryCreatesPositivePendingGenerationWithoutProviderEvidence()
    {
        InstanceBootstrapState state = InstanceBootstrapState.CreateInteractivePending(
            InitialId, DeploymentMode.SingleTenant, CreatedAt);

        await Assert.That(state.Id).IsEqualTo(InitialId);
        await Assert.That(state.Id.Version).IsEqualTo(7);
        await Assert.That(state.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(state.Mode).IsEqualTo(InstanceBootstrapMode.Interactive);
        await Assert.That(state.ProviderKind).IsNull();
        await Assert.That(state.DeploymentMode).IsEqualTo(DeploymentMode.SingleTenant);
        await Assert.That(state.Generation).IsGreaterThan(0);
        await Assert.That(state.ConfigurationFingerprint).IsNull();
        await Assert.That(state.SelectorFingerprint).IsNull();
        await Assert.That(state.CompletedIdentityFingerprint).IsNull();
        await Assert.That(state.CreatedAt).IsEqualTo(CreatedAt);
        await Assert.That(state.SupersededAt).IsNull();
        await Assert.That(state.CompletedAt).IsNull();
        await Assert.That(state.CompletedByUserId).IsNull();
    }

    [Test]
    public async Task ConfiguredFactoryPersistsTypedProviderDeploymentAndFingerprints()
    {
        InstanceBootstrapState state = ConfiguredPending();

        await Assert.That(state.Id.Version).IsEqualTo(7);
        await Assert.That(state.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(state.Mode).IsEqualTo(InstanceBootstrapMode.ConfiguredAdministrator);
        await Assert.That(state.ProviderKind).IsEqualTo(AuthenticationProviderKind.Keycloak);
        await Assert.That(state.DeploymentMode).IsEqualTo(DeploymentMode.MultiTenant);
        await Assert.That(state.Generation).IsEqualTo(7);
        await Assert.That(state.ConfigurationFingerprint).IsEqualTo(Fingerprint('a'));
        await Assert.That(state.SelectorFingerprint).IsEqualTo(Fingerprint('b'));
        await Assert.That(state.CompletedIdentityFingerprint).IsNull();
        await Assert.That(state.CreatedAt).IsEqualTo(CreatedAt);
    }

    [Test]
    public async Task FactoriesRejectNonUuidV7NonPositiveGenerationAndNonUtcTime()
    {
        await Assert.That(() => InstanceBootstrapState.CreateInteractivePending(
            Guid.NewGuid(), DeploymentMode.SingleTenant, CreatedAt)).Throws<ArgumentException>();
        await Assert.That(() => InstanceBootstrapState.CreateInteractivePending(
            InitialId, DeploymentMode.SingleTenant, DateTime.SpecifyKind(CreatedAt, DateTimeKind.Local)))
            .Throws<ArgumentException>();
        await Assert.That(() => InstanceBootstrapState.CreateConfiguredAdministratorPending(
            InitialId, AuthenticationProviderKind.Keycloak, DeploymentMode.MultiTenant,
            0, Fingerprint('a'), Fingerprint('b'), CreatedAt)).Throws<ArgumentException>();
        await Assert.That(() => InstanceBootstrapState.CreateConfiguredAdministratorPending(
            InitialId, AuthenticationProviderKind.Keycloak, DeploymentMode.MultiTenant,
            -1, Fingerprint('a'), Fingerprint('b'), CreatedAt)).Throws<ArgumentException>();
    }

    [Test]
    public async Task ConfiguredFactoryRequiresProviderAndBothFingerprintValues()
    {
        await Assert.That(() => InstanceBootstrapState.CreateConfiguredAdministratorPending(
            InitialId, default, DeploymentMode.MultiTenant, 7,
            Fingerprint('a'), Fingerprint('b'), CreatedAt)).Throws<ArgumentException>();
        await Assert.That(() => InstanceBootstrapState.CreateConfiguredAdministratorPending(
            InitialId, AuthenticationProviderKind.Keycloak, DeploymentMode.MultiTenant, 7,
            null!, Fingerprint('b'), CreatedAt)).Throws<ArgumentException>();
        await Assert.That(() => InstanceBootstrapState.CreateConfiguredAdministratorPending(
            InitialId, AuthenticationProviderKind.Keycloak, DeploymentMode.MultiTenant, 7,
            Fingerprint('a'), null!, CreatedAt)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(63, 'a')]
    [Arguments(65, 'a')]
    [Arguments(64, 'A')]
    [Arguments(64, 'g')]
    public async Task FingerprintsMustBeExactly64LowercaseHexCharacters(int length, char value)
    {
        string malformed = new(value, length);
        await Assert.That(() => InstanceBootstrapState.CreateConfiguredAdministratorPending(
            InitialId, AuthenticationProviderKind.Keycloak, DeploymentMode.MultiTenant, 7,
            malformed, Fingerprint('b'), CreatedAt)).Throws<ArgumentException>();
        await Assert.That(() => InstanceBootstrapState.CreateConfiguredAdministratorPending(
            InitialId, AuthenticationProviderKind.Keycloak, DeploymentMode.MultiTenant, 7,
            Fingerprint('a'), malformed, CreatedAt)).Throws<ArgumentException>();
    }

    [Test]
    public async Task SupersedeCreatesHigherCorrectedPendingGeneration()
    {
        InstanceBootstrapState original = ConfiguredPending();
        InstanceBootstrapState replacement = original.Supersede(
            ReplacementId, AuthenticationProviderKind.Atproto, DeploymentMode.SingleTenant,
            8, Fingerprint('c'), Fingerprint('d'), SupersededAt);

        await Assert.That(original.Status).IsEqualTo(InstanceBootstrapStatus.Superseded);
        await Assert.That(original.SupersededAt).IsEqualTo(SupersededAt);
        await Assert.That(replacement.Id).IsEqualTo(ReplacementId);
        await Assert.That(replacement.Id.Version).IsEqualTo(7);
        await Assert.That(replacement.Status).IsEqualTo(InstanceBootstrapStatus.Pending);
        await Assert.That(replacement.ProviderKind).IsEqualTo(AuthenticationProviderKind.Atproto);
        await Assert.That(replacement.DeploymentMode).IsEqualTo(DeploymentMode.SingleTenant);
        await Assert.That(replacement.Generation).IsEqualTo(8);
        await Assert.That(replacement.ConfigurationFingerprint).IsEqualTo(Fingerprint('c'));
        await Assert.That(replacement.SelectorFingerprint).IsEqualTo(Fingerprint('d'));
        await Assert.That(replacement.CreatedAt).IsEqualTo(SupersededAt);
    }

    [Test]
    public async Task SupersedeFullyValidatesBeforeMutationAndRejectsDriftOrRegression()
    {
        foreach ((long generation, string configuration, string selector) attempt in new[]
        {
            (6L, Fingerprint('c'), Fingerprint('d')),
            (7L, Fingerprint('c'), Fingerprint('b')),
            (7L, Fingerprint('a'), Fingerprint('d'))
        })
        {
            InstanceBootstrapState state = ConfiguredPending();
            BootstrapSnapshot before = Snapshot(state);
            await Assert.That(() => state.Supersede(
                ReplacementId, AuthenticationProviderKind.Keycloak, DeploymentMode.MultiTenant,
                attempt.generation, attempt.configuration, attempt.selector, SupersededAt))
                .Throws<InvalidOperationException>();
            await Assert.That(Snapshot(state)).IsEqualTo(before);
        }
    }

    [Test]
    public async Task SupersedeRejectsEveryMalformedReplacementBeforeMutation()
    {
        InstanceBootstrapState invalidId = ConfiguredPending();
        await AssertRejectedWithoutMutation(invalidId, () => invalidId.Supersede(
            Guid.NewGuid(), AuthenticationProviderKind.Atproto, DeploymentMode.SingleTenant,
            8, Fingerprint('c'), Fingerprint('d'), SupersededAt));

        InstanceBootstrapState invalidProvider = ConfiguredPending();
        await AssertRejectedWithoutMutation(invalidProvider, () => invalidProvider.Supersede(
            ReplacementId, default, DeploymentMode.SingleTenant,
            8, Fingerprint('c'), Fingerprint('d'), SupersededAt));

        InstanceBootstrapState invalidConfiguration = ConfiguredPending();
        await AssertRejectedWithoutMutation(invalidConfiguration, () => invalidConfiguration.Supersede(
            ReplacementId, AuthenticationProviderKind.Atproto, DeploymentMode.SingleTenant,
            8, "issuer:raw-authority", Fingerprint('d'), SupersededAt));

        InstanceBootstrapState invalidSelector = ConfiguredPending();
        await AssertRejectedWithoutMutation(invalidSelector, () => invalidSelector.Supersede(
            ReplacementId, AuthenticationProviderKind.Atproto, DeploymentMode.SingleTenant,
            8, Fingerprint('c'), "subject:raw-account", SupersededAt));

        InstanceBootstrapState invalidTimestamp = ConfiguredPending();
        await AssertRejectedWithoutMutation(invalidTimestamp, () => invalidTimestamp.Supersede(
            ReplacementId, AuthenticationProviderKind.Atproto, DeploymentMode.SingleTenant,
            8, Fingerprint('c'), Fingerprint('d'), CreatedAt.AddTicks(-1)));
    }

    [Test]
    public async Task ConfiguredCompletionMatchesProviderGenerationAndSelectorIdentity()
    {
        InstanceBootstrapState state = ConfiguredPending();
        bool completed = state.CompleteConfiguredAdministrator(
            AuthenticationProviderKind.Keycloak, 7, Fingerprint('b'), UserId, CompletedAt);

        await Assert.That(completed).IsTrue();
        await Assert.That(state.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
        await Assert.That(state.CompletedIdentityFingerprint).IsEqualTo(state.SelectorFingerprint);
        await Assert.That(state.CompletedByUserId).IsEqualTo(UserId);
        await Assert.That(state.CompletedAt).IsEqualTo(CompletedAt);
        await Assert.That(state.ProviderKind).IsEqualTo(AuthenticationProviderKind.Keycloak);
        await Assert.That(state.Generation).IsEqualTo(7);
    }

    [Test]
    public async Task ExactConfiguredCompletionReplayReturnsFalseAndPreservesEvidence()
    {
        InstanceBootstrapState state = CompletedConfigured();
        BootstrapSnapshot before = Snapshot(state);
        bool replay = state.CompleteConfiguredAdministrator(
            AuthenticationProviderKind.Keycloak, 7, Fingerprint('b'), UserId, CompletedAt.AddHours(1));

        await Assert.That(replay).IsFalse();
        await Assert.That(Snapshot(state)).IsEqualTo(before);
    }

    [Test]
    public async Task ConfiguredCompletionMismatchesThrowWithoutChangingEvidence()
    {
        foreach ((AuthenticationProviderKind provider, long generation, string identity, Guid userId) attempt in new[]
        {
            (AuthenticationProviderKind.Atproto, 7L, Fingerprint('b'), UserId),
            (AuthenticationProviderKind.Keycloak, 8L, Fingerprint('b'), UserId),
            (AuthenticationProviderKind.Keycloak, 7L, Fingerprint('c'), UserId),
            (AuthenticationProviderKind.Keycloak, 7L, Fingerprint('b'), ReplacementId)
        })
        {
            InstanceBootstrapState state = CompletedConfigured();
            BootstrapSnapshot before = Snapshot(state);
            await Assert.That(() => state.CompleteConfiguredAdministrator(
                attempt.provider, attempt.generation, attempt.identity, attempt.userId, CompletedAt))
                .Throws<InvalidOperationException>();
            await Assert.That(Snapshot(state)).IsEqualTo(before);
        }
    }

    [Test]
    public async Task InteractiveCompletionIsIdempotentAndCarriesNoIdentityFingerprint()
    {
        InstanceBootstrapState state = InstanceBootstrapState.CreateInteractivePending(
            InitialId, DeploymentMode.SingleTenant, CreatedAt);

        await Assert.That(state.CompleteInteractive(UserId, CompletedAt)).IsTrue();
        BootstrapSnapshot completed = Snapshot(state);
        await Assert.That(state.CompleteInteractive(UserId, CompletedAt)).IsFalse();
        await Assert.That(Snapshot(state)).IsEqualTo(completed);
        await Assert.That(state.Status).IsEqualTo(InstanceBootstrapStatus.Completed);
        await Assert.That(state.CompletedIdentityFingerprint).IsNull();
    }

    [Test]
    public async Task CompletionValidatesFingerprintUuidV7AndUtcNonRegressingTimestampBeforeMutation()
    {
        InstanceBootstrapState invalidIdentity = ConfiguredPending();
        await AssertRejectedWithoutMutation(invalidIdentity, () => invalidIdentity.CompleteConfiguredAdministrator(
            AuthenticationProviderKind.Keycloak, 7, "did:plc:raw-identity", UserId, CompletedAt));

        InstanceBootstrapState invalidUser = ConfiguredPending();
        await AssertRejectedWithoutMutation(invalidUser, () => invalidUser.CompleteConfiguredAdministrator(
            AuthenticationProviderKind.Keycloak, 7, Fingerprint('b'), Guid.NewGuid(), CompletedAt));

        InstanceBootstrapState invalidTimestamp = ConfiguredPending();
        await AssertRejectedWithoutMutation(invalidTimestamp, () => invalidTimestamp.CompleteConfiguredAdministrator(
            AuthenticationProviderKind.Keycloak, 7, Fingerprint('b'), UserId, CreatedAt.AddTicks(-1)));

        InstanceBootstrapState nonUtcTimestamp = ConfiguredPending();
        await AssertRejectedWithoutMutation(nonUtcTimestamp, () => nonUtcTimestamp.CompleteConfiguredAdministrator(
            AuthenticationProviderKind.Keycloak, 7, Fingerprint('b'), UserId,
            DateTime.SpecifyKind(CompletedAt, DateTimeKind.Local)));
    }

    [Test]
    public async Task PendingConfiguredCompletionRejectsProviderGenerationAndIdentityMismatchBeforeMutation()
    {
        InstanceBootstrapState providerMismatch = ConfiguredPending();
        await AssertRejectedWithoutMutation(providerMismatch, () => providerMismatch.CompleteConfiguredAdministrator(
            AuthenticationProviderKind.Atproto, 7, Fingerprint('b'), UserId, CompletedAt),
            expectArgumentException: false);

        InstanceBootstrapState generationDrift = ConfiguredPending();
        await AssertRejectedWithoutMutation(generationDrift, () => generationDrift.CompleteConfiguredAdministrator(
            AuthenticationProviderKind.Keycloak, 8, Fingerprint('b'), UserId, CompletedAt),
            expectArgumentException: false);

        InstanceBootstrapState identityMismatch = ConfiguredPending();
        await AssertRejectedWithoutMutation(identityMismatch, () => identityMismatch.CompleteConfiguredAdministrator(
            AuthenticationProviderKind.Keycloak, 7, Fingerprint('c'), UserId, CompletedAt),
            expectArgumentException: false);
    }

    [Test]
    public async Task SupersededAndCompletedGenerationsAreTerminal()
    {
        InstanceBootstrapState superseded = ConfiguredPending();
        _ = superseded.Supersede(
            ReplacementId, AuthenticationProviderKind.Atproto, DeploymentMode.SingleTenant,
            8, Fingerprint('c'), Fingerprint('d'), SupersededAt);
        BootstrapSnapshot supersededBefore = Snapshot(superseded);
        await Assert.That(() => superseded.CompleteConfiguredAdministrator(
            AuthenticationProviderKind.Keycloak, 7, Fingerprint('b'), UserId, CompletedAt))
            .Throws<InvalidOperationException>();
        await Assert.That(Snapshot(superseded)).IsEqualTo(supersededBefore);

        InstanceBootstrapState completed = CompletedConfigured();
        BootstrapSnapshot completedBefore = Snapshot(completed);
        await Assert.That(() => completed.Supersede(
            ReplacementId, AuthenticationProviderKind.Atproto, DeploymentMode.SingleTenant,
            8, Fingerprint('c'), Fingerprint('d'), CompletedAt.AddMinutes(1)))
            .Throws<InvalidOperationException>();
        await Assert.That(Snapshot(completed)).IsEqualTo(completedBefore);
    }

    [Test]
    public async Task DeploymentModeTransitionIsCompletedOnlyAndPreservesEvidence()
    {
        InstanceBootstrapState pending = ConfiguredPending();
        BootstrapSnapshot pendingBefore = Snapshot(pending);
        await Assert.That(() => pending.TransitionDeploymentMode(DeploymentMode.SingleTenant))
            .Throws<InvalidOperationException>();
        await Assert.That(Snapshot(pending)).IsEqualTo(pendingBefore);

        InstanceBootstrapState completed = CompletedConfigured();
        BootstrapSnapshot completedBefore = Snapshot(completed);
        await Assert.That(completed.TransitionDeploymentMode(DeploymentMode.MultiTenant)).IsFalse();
        await Assert.That(Snapshot(completed)).IsEqualTo(completedBefore);
        await Assert.That(completed.TransitionDeploymentMode(DeploymentMode.SingleTenant)).IsTrue();
        await Assert.That(completed.DeploymentMode).IsEqualTo(DeploymentMode.SingleTenant);
        await Assert.That(completed.Status).IsEqualTo(completedBefore.Status);
        await Assert.That(completed.CompletedIdentityFingerprint)
            .IsEqualTo(completedBefore.CompletedIdentityFingerprint);
        await Assert.That(completed.CompletedByUserId).IsEqualTo(completedBefore.CompletedByUserId);
        await Assert.That(completed.CompletedAt).IsEqualTo(completedBefore.CompletedAt);
    }

    private static async Task AssertRejectedWithoutMutation(
        InstanceBootstrapState state,
        Func<object?> transition,
        bool expectArgumentException = true)
    {
        BootstrapSnapshot before = Snapshot(state);
        if (expectArgumentException)
        {
            await Assert.That(transition).Throws<ArgumentException>();
        }
        else
        {
            await Assert.That(transition).Throws<InvalidOperationException>();
        }

        await Assert.That(Snapshot(state)).IsEqualTo(before);
    }

    private static InstanceBootstrapState ConfiguredPending() =>
        InstanceBootstrapState.CreateConfiguredAdministratorPending(
            InitialId, AuthenticationProviderKind.Keycloak, DeploymentMode.MultiTenant,
            7, Fingerprint('a'), Fingerprint('b'), CreatedAt);

    private static InstanceBootstrapState CompletedConfigured()
    {
        InstanceBootstrapState state = ConfiguredPending();
        _ = state.CompleteConfiguredAdministrator(
            AuthenticationProviderKind.Keycloak, 7, Fingerprint('b'), UserId, CompletedAt);
        return state;
    }

    private static BootstrapSnapshot Snapshot(InstanceBootstrapState state) => new(
        state.Id, state.Status, state.Mode, state.ProviderKind, state.DeploymentMode, state.Generation,
        state.ConfigurationFingerprint, state.SelectorFingerprint, state.CompletedIdentityFingerprint,
        state.CreatedAt, state.SupersededAt, state.CompletedAt, state.CompletedByUserId);

    private static string Fingerprint(char value) => new(value, 64);

    private sealed record BootstrapSnapshot(
        Guid Id,
        InstanceBootstrapStatus Status,
        InstanceBootstrapMode Mode,
        AuthenticationProviderKind? ProviderKind,
        DeploymentMode DeploymentMode,
        long Generation,
        string? ConfigurationFingerprint,
        string? SelectorFingerprint,
        string? CompletedIdentityFingerprint,
        DateTime CreatedAt,
        DateTime? SupersededAt,
        DateTime? CompletedAt,
        Guid? CompletedByUserId);
}
