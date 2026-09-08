using System.Text.Json;
using Explore.API.Mcp;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant.Disclosure;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.AiAssistant.Requests.Queries;
using Explore.Application.Responses;
using MediatR;
using NSubstitute;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class McpAiAssistantAdapterTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IAiContextRedactor _redactor = Substitute.For<IAiContextRedactor>();

    [Test]
    public async Task ProposeAiToolActionAsync_DelegatesToMediatRAndReturnsSafeResult()
    {
        var conversationId = Guid.CreateVersion7();
        var proposedActionId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<ProposeAiToolActionCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                proposedActionId,
                "AI tool action proposed. Confirm before execution."));

        var tool = new AiAssistantMcpTools(_mediator);

        var json = await tool.ProposeAiToolActionAsync(
            conversationId,
            "CreateEventDraft",
            "{ \"title\": \"MCP draft\" }",
            "Draft an event",
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        await Assert.That(document.RootElement.GetProperty("Success").GetBoolean()).IsTrue();
        await Assert.That(document.RootElement.GetProperty("Id").GetGuid()).IsEqualTo(proposedActionId);
        await Assert.That(document.RootElement.GetProperty("Message").GetString()).Contains("Confirm");

        await _mediator.Received(1).Send(
            Arg.Is<ProposeAiToolActionCommand>(command =>
                command.ConversationId == conversationId &&
                command.ToolName == "CreateEventDraft" &&
                command.PayloadJson.Contains("MCP draft") &&
                command.Summary == "Draft an event"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ListConversationsAsync_ReturnsSafeConversationSummaries()
    {
        var conversationId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<GetAiConversationListQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                new AiConversationSummaryDto
                {
                    Id = conversationId,
                    Status = "Active",
                    Title = "Event planning",
                    Provider = "fake",
                    ModelId = "fake-model",
                    LastMessageSequence = 3,
                    CreatedAt = new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc)
                }
            ]);

        var resources = new AiAssistantMcpResources(_mediator, _redactor);

        var json = await resources.ListConversationsAsync(CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var conversations = document.RootElement.GetProperty("Conversations");
        await Assert.That(conversations.GetArrayLength()).IsEqualTo(1);
        await Assert.That(conversations[0].GetProperty("Id").GetGuid()).IsEqualTo(conversationId);
        await Assert.That(conversations[0].GetProperty("Title").GetString()).IsEqualTo("Event planning");
        await Assert.That(json).DoesNotContain("PayloadJson");

        await _mediator.Received(1).Send(
            Arg.Is<GetAiConversationListQuery>(query => query.Limit == 10),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetConversationAsync_OmitsRawProposedActionPayloads()
    {
        var conversationId = Guid.CreateVersion7();
        var proposedActionId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<GetAiConversationDetailQuery>(), Arg.Any<CancellationToken>())
            .Returns(new AiConversationDto
            {
                Id = conversationId,
                Status = "Active",
                Title = "Detail",
                LastMessageSequence = 1,
                CreatedAt = new DateTime(2026, 6, 3, 12, 0, 0, DateTimeKind.Utc),
                Messages = [
                    new AiMessageDto
                    {
                        Id = Guid.CreateVersion7(),
                        Sequence = 1,
                        Role = "User",
                        Content = "Sensitive prompt text",
                        CreatedAt = new DateTime(2026, 6, 3, 12, 1, 0, DateTimeKind.Utc)
                    }
                ],
                ProposedActions = [
                    new AiProposedActionDto
                    {
                        Id = proposedActionId,
                        Kind = "CreateEventDraft",
                        Status = "Proposed",
                        PayloadJson = "{ \"title\": \"Secret draft payload\" }",
                        CreatedAt = new DateTime(2026, 6, 3, 12, 2, 0, DateTimeKind.Utc)
                    }
                ]
            });

        var resources = new AiAssistantMcpResources(_mediator, _redactor);

        var json = await resources.GetConversationAsync(conversationId, CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        await Assert.That(document.RootElement.GetProperty("Found").GetBoolean()).IsTrue();
        await Assert.That(document.RootElement.GetProperty("ProposedActions")[0].GetProperty("Id").GetGuid()).IsEqualTo(proposedActionId);
        await Assert.That(document.RootElement.GetProperty("Messages")[0].GetProperty("HasContent").GetBoolean()).IsTrue();
        await Assert.That(json).DoesNotContain("PayloadJson");
        await Assert.That(json).DoesNotContain("Secret draft payload");
        await Assert.That(json).DoesNotContain("Sensitive prompt text");
    }

    [Test]
    public async Task CreateEventDraftWithConfirmationPrompt_RequiresProposalAndConfirmation()
    {
        var prompt = new AiAssistantMcpPrompts().CreateEventDraftWithConfirmation();

        await Assert.That(prompt).Contains("list_ai_tool_contracts");
        await Assert.That(prompt).Contains("propose_ai_tool_action");
        await Assert.That(prompt).Contains("wait for");
        var normalized = prompt.ToLowerInvariant();
        await Assert.That(normalized).DoesNotContain("apikey");
        await Assert.That(normalized).DoesNotContain("provider endpoint");
        await Assert.That(normalized).DoesNotContain("tenant id");
    }
}
