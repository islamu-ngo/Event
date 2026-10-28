using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Features.Moderation.Requests.Queries;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Application.UnitTests.Moderation;

/// <summary>Pure authoring mapping witnesses; these do not claim any native authority acquisition.</summary>
public sealed class ModerationCurrentPolicyViewTests
{
    [Test]
    public async Task RetainedMappingPreservesDisabledDefinitionMeaningAndAllConsequenceDimensions()
    {
        var draft = Draft();
        var retained = ModerationCurrentPolicyAuthoringState.FromHistory(draft);
        await Assert.That(retained.HistoricalExpectedRevision).IsEqualTo(0);
        await Assert.That(retained.SelectedProfiles.IsEmpty).IsTrue();
        await Assert.That(retained.Classes[0].Meaning).IsEqualTo("Entitlement integrity class.");
        await Assert.That(retained.Reasons[0].Meaning).IsEqualTo("Proved entitlement inconsistency.");
        await Assert.That(retained.Profiles.Length).IsEqualTo(1);
        await Assert.That(retained.Profiles[0].Definition).IsEqualTo(draft.Proposed.Profiles[0]);
        await Assert.That(retained.Profiles[0].Consequences.SequenceEqual(draft.Profiles[0].Consequences)).IsTrue();
        await Assert.That(retained.Profiles[0].Consequences[0].Credentials)
            .IsEqualTo(ModerationCredentialMode.InvalidateProvedBadEntitlementsOnly);
    }

    [Test]
    public async Task RetainedViewContainsNoConsumerCheckpointsOrAuthorityReceipt()
    {
        var retained = ModerationCurrentPolicyAuthoringState.FromHistory(Draft());
        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(retained,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var keys = json.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();
        await Assert.That(keys.SetEquals(["publicationId", "historicalExpectedRevision", "catalogueRevision",
            "inventoryRevision", "inventoryFingerprint", "modes", "classes", "reasons", "profiles", "selectedProfiles"])).IsTrue();
        var profileKeys = json.RootElement.GetProperty("profiles")[0].EnumerateObject()
            .Select(property => property.Name).ToHashSet();
        await Assert.That(profileKeys.SetEquals(["definition", "consequences"])).IsTrue();
    }

    [Test]
    [Arguments(ModerationCurrentPolicyReadOutcome.Unauthenticated)]
    [Arguments(ModerationCurrentPolicyReadOutcome.Denied)]
    [Arguments(ModerationCurrentPolicyReadOutcome.Unavailable)]
    [Arguments(ModerationCurrentPolicyReadOutcome.Invalid)]
    public async Task PendingFailuresCannotRetainAuthoringDataOrAProjection(ModerationCurrentPolicyReadOutcome outcome)
    {
        await using var pending = new ModerationCurrentPolicyReadResult(outcome);
        await Assert.That(pending.Projection).IsNull();
        await Assert.That(ModerationCurrentPolicyReadResult.Failure(outcome).IsSuccess).IsFalse();
        using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(pending,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await Assert.That(json.RootElement.EnumerateObject().Select(property => property.Name)
            .SequenceEqual(["outcome"])).IsTrue();
    }

    private static ModerationPolicyCompiledDraft Draft()
    {
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var scope = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        var reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG19);
        var response = reference.Responses[0];
        ModerationRuleReference Rule(string code) => ModerationRuleReference.Create(scope.Id, code, 1);
        var profile = new ModerationPolicyProfileDefinition(Guid.CreateVersion7(), Rule("integrity.profile"),
            Rule("integrity.reason"), reference.Id, reference.Revision, response.Grade, [ModerationScopeKind.Tenant],
            [reference.SubjectKinds.Order().First()], reference.Effects.ToImmutableArray(),
            reference.Bundle, reference.ProtectionPurpose, reference.Strategies.Order().ToImmutableArray(),
            new(response.Timing.Kind, response.Timing.Duration), response.RequiredApprovals, 6,
            ModerationReferenceRecognition.Never);
        var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        var proposed = new ModerationPolicyProposedState(Guid.CreateVersion7(), 0, Guid.CreateVersion7(), scope,
            ModerationReferenceCatalogue.Revision, inventory.Revision, inventory.Fingerprint,
            ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
            [new(Rule("integrity.class"), "Entitlement integrity class.", reference.Ground, reference.Basis)],
            [new(Rule("integrity.reason"), "Proved entitlement inconsistency.", Rule("integrity.class"))],
            [profile], []);
        return ModerationPolicyProposedStateCompiler.Compile(proposed,
            new(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, ModerationPolicyPublicationProjection.WriterGeneration,
                ModerationPolicyPublicationProjection.AuthoringBounds(scope), null), inventory).Draft
            ?? throw new InvalidOperationException("The pure disabled authoring definition must compile.");
    }
}
