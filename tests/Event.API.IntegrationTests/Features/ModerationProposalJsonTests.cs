using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Explore.API.Serialization;
using Explore.Application.Features.Moderation.Requests.Commands;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationProposalJsonTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new ModerationAuthorityScopeJsonConverter(), new ModerationSubjectReferenceJsonConverter(),
            new ModerationRuleReferenceJsonConverter(), new ModerationProposalJsonConverter(), new JsonStringEnumConverter()
        }
    };

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualProposalRequestPreservesCaseRuleCapabilitiesAndInterval(bool longTerm)
    {
        var target = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7());
        var command = new ProposeModerationDecisionCommand
        {
            OperationId = Guid.CreateVersion7(), EffectId = Guid.CreateVersion7(), DecisionId = Guid.CreateVersion7(),
            Subject = SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), target), Target = target,
            ExpectedRevision = 4, ExpectedPolicyRevision = 6,
            Proposal = new(Guid.CreateVersion7(), ModerationRuleReference.Create(Guid.CreateVersion7(), "conduct.v1", 2),
                [ModerationCapability.Upload, ModerationCapability.Publish, ModerationCapability.Publish],
                Start, longTerm ? null : Start.AddDays(1), longTerm ? Start.AddDays(2) : null, longTerm ? 2 : 1)
        };
        var restored = JsonSerializer.Deserialize<ProposeModerationDecisionCommand>(
            JsonSerializer.Serialize(command, Options), Options);
        await Assert.That(restored).IsNotNull();
        await Assert.That((restored!.OperationId, restored.EffectId, restored.DecisionId, restored.Subject, restored.Target,
            restored.ExpectedRevision, restored.ExpectedPolicyRevision))
            .IsEqualTo((command.OperationId, command.EffectId, command.DecisionId, command.Subject, command.Target,
                command.ExpectedRevision, command.ExpectedPolicyRevision));
        await Assert.That((restored.Proposal.CaseId, restored.Proposal.Rule, restored.Proposal.StartsAt,
            restored.Proposal.EndsAt, restored.Proposal.ReviewAt, restored.Proposal.RequiredApprovals))
            .IsEqualTo((command.Proposal.CaseId, command.Proposal.Rule, command.Proposal.StartsAt,
                command.Proposal.EndsAt, command.Proposal.ReviewAt, command.Proposal.RequiredApprovals));
        await Assert.That(restored.Proposal.Capabilities.SequenceEqual(command.Proposal.Capabilities)).IsTrue();
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
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    [Arguments(14)]
    public async Task InvalidProposalCannotBypassConstructorInvariants(int change)
    {
        var document = JsonSerializer.SerializeToNode(new
        {
            caseId = Guid.CreateVersion7(),
            rule = new { ownerId = Guid.CreateVersion7(), code = "conduct", revision = 1 },
            capabilities = new[] { "Publish" }, startsAt = Start,
            endsAt = Start.AddDays(1), requiredApprovals = 1
        })!.AsObject();
        switch (change)
        {
            case 0: document.Remove("caseId"); break;
            case 1: document["caseId"] = Guid.Empty; break;
            case 2: document["rule"] = null; break;
            case 3: document["capabilities"] = new JsonArray(); break;
            case 4: document["capabilities"] = new JsonArray("Support"); break;
            case 5: document["capabilities"] = new JsonArray(99); break;
            case 6: document.Remove("startsAt"); break;
            case 7: document["endsAt"] = JsonValue.Create(Start); break;
            case 8: document["reviewAt"] = JsonValue.Create(Start.AddDays(2)); break;
            case 9: document["requiredApprovals"] = 0; break;
            case 10: document["requiredApprovals"] = 3; break;
            case 11: document["endsAt"] = null; break;
            case 12:
                document["endsAt"] = null;
                document["reviewAt"] = JsonValue.Create(Start.AddDays(1));
                break;
            case 13: document["authority"] = "approved"; break;
            default: document.Remove("requiredApprovals"); break;
        }
        await Assert.That(() => JsonSerializer.Deserialize<ModerationProposal>(document.ToJsonString(), Options))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments("null")]
    [Arguments("{}")]
    [Arguments("{\"ownerId\":\"00000000-0000-0000-0000-000000000000\",\"code\":\"conduct\",\"revision\":1}")]
    [Arguments("{\"ownerId\":\"019a0000-0000-7000-8000-000000000001\",\"code\":\"conduct\",\"revision\":0}")]
    [Arguments("{\"ownerId\":\"019a0000-0000-7000-8000-000000000001\",\"code\":\"invalid rule\",\"revision\":1}")]
    [Arguments("{\"ownerId\":\"019a0000-0000-7000-8000-000000000001\",\"code\":\"conduct\",\"revision\":1,\"revision\":2}")]
    [Arguments("{\"ownerId\":\"019a0000-0000-7000-8000-000000000001\",\"code\":\"conduct\",\"revision\":1,\"approval\":\"granted\"}")]
    public async Task InvalidRuleCannotBecomeImmutableMeaning(string json) =>
        await Assert.That(() => JsonSerializer.Deserialize<ModerationRuleReference>(json, Options)).Throws<JsonException>();

    [Test]
    public async Task DuplicateNullableProposalTermCannotReplaceTheOriginalInterval()
    {
        var json = $$"""
            {"caseId":"{{Guid.CreateVersion7()}}","rule":{"ownerId":"{{Guid.CreateVersion7()}}","code":"conduct","revision":1},
             "capabilities":["Publish"],"startsAt":"2026-10-12T12:00:00Z","endsAt":null,
             "endsAt":"2026-10-13T12:00:00Z","reviewAt":null,"requiredApprovals":1}
            """;
        await Assert.That(() => JsonSerializer.Deserialize<ModerationProposal>(json, Options)).Throws<JsonException>();
    }
}
