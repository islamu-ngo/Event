using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;
using Explore.Infrastructure.Ai;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class FakeAiChatProviderTests
{
    private readonly FakeAiChatProvider _provider = new();

    [Test]
    public async Task ListAvailableModels_ReturnsDeterministicFakeModel()
    {
        var models = await _provider.ListAvailableModelsAsync();

        await Assert.That(models.Count).IsEqualTo(1);
        await Assert.That(models[0].Id).IsEqualTo(FakeAiChatProvider.ModelId);
        await Assert.That(models[0].SupportsToolProposals).IsTrue();
    }

    [Test]
    public async Task SendAsync_ReturnsDeterministicAssistantMessage()
    {
        var result = await _provider.SendAsync(CreateRequest("Plan a community dinner"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response).IsNotNull();
        await Assert.That(result.Response!.AssistantMessage).Contains("Plan a community dinner");
        await Assert.That(result.Response.ProviderRequestId).IsEqualTo("fake-provider");
    }

    [Test]
    public async Task SendAsync_WithNoMessagesReturnsFailure()
    {
        var result = await _provider.SendAsync(new AiChatPayload(
            FakeAiChatProvider.ModelId,
            [],
            null,
            CreateOptions(toolProposalsEnabled: false)));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("empty_messages");
    }

    [Test]
    public async Task SendAsync_WhenToolProposalsEnabled_ReturnsCreateEventDraftProposal()
    {
        var result = await _provider.SendAsync(CreateRequest(
            "Create an event draft",
            toolProposalsEnabled: true,
            allowedKinds: [AiProposedActionKind.CreateEventDraft]));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.ProposedActions.Count).IsEqualTo(1);
        var proposal = result.Response.ProposedActions[0];
        await Assert.That(proposal.Kind).IsEqualTo(AiProposedActionKind.CreateEventDraft);
        await Assert.That(proposal.PayloadJson).Contains("Fake AI event draft");
        await Assert.That(AiToolContractRegistry.CreateDefault()
            .ValidatePayload(proposal.Kind, proposal.PayloadJson)
            .Succeeded).IsTrue();
    }

    [Test]
    public async Task SendAsync_WhenStructuredOutputCombinesWithToolProposals_ReturnsSafeFailure()
    {
        var result = await _provider.SendAsync(new AiChatPayload(
            FakeAiChatProvider.ModelId,
            [new AiChatMessage(AiMessageRole.User, "Create an event draft")],
            "You are a test assistant.",
            new AiChatOptions(
                8000,
                1024,
                0.2m,
                30,
                ToolProposalsEnabled: true,
                StreamingEnabled: false,
                StructuredOutputEnabled: true),
            new AiStructuredActionSchema([AiProposedActionKind.CreateEventDraft], "{}"),
            AiStructuredOutputSchemas.AssistantMessage));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("structured_output_conflict");
    }

    private static AiChatPayload CreateRequest(
        string userMessage,
        bool toolProposalsEnabled = false,
        IReadOnlyList<AiProposedActionKind>? allowedKinds = null) =>
        new(
            FakeAiChatProvider.ModelId,
            [new AiChatMessage(AiMessageRole.User, userMessage)],
            "You are a test assistant.",
            CreateOptions(toolProposalsEnabled),
            allowedKinds is null ? null : new AiStructuredActionSchema(allowedKinds, "{}"));

    private static AiChatOptions CreateOptions(bool toolProposalsEnabled) =>
        new(8000, 1024, 0.2m, 30, toolProposalsEnabled, StreamingEnabled: false);
}
