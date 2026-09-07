namespace Event.Application.UnitTests.Features.AiAssistant;

using System.Reflection;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.AiAssistant.Requests.Queries;

public sealed class AiAssistantAuthorizationMetadataTests
{
    [Test]
    public async Task ConversationRequests_ShouldDeclareAiConversationAuthorizationActions()
    {
        await AssertAuthorization<CreateAiConversationCommand>(AuthorizationActions.AiConversations.Create);
        await AssertAuthorization<SendAiMessageCommand>(AuthorizationActions.AiConversations.SendMessage);
        await AssertAuthorization<ConfirmAiProposedActionCommand>(AuthorizationActions.AiConversations.ConfirmAction);
        await AssertAuthorization<RejectAiProposedActionCommand>(AuthorizationActions.AiConversations.RejectAction);
        await AssertAuthorization<CancelAiRunCommand>(AuthorizationActions.AiConversations.CancelRun);
        await AssertAuthorization<ProposeAiToolActionCommand>(AuthorizationActions.AiConversations.ProposeAction);
        await AssertAuthorization<GetAiConversationListQuery>(AuthorizationActions.AiConversations.View);
        await AssertAuthorization<GetAiConversationDetailQuery>(AuthorizationActions.AiConversations.View);
        await AssertAuthorization<GetAiRunStatusQuery>(AuthorizationActions.AiConversations.View);
    }

    [Test]
    public async Task ConversationResourceRequests_ShouldExposeConversationIdForAuthorizationProvider()
    {
        var conversationId = Guid.CreateVersion7();

        ISecureRequest send = new SendAiMessageCommand
        {
            ConversationId = conversationId,
            Message = new SendAiMessageRequestDto { Content = "hello", IdempotencyKey = "key" }
        };
        ISecureRequest confirm = new ConfirmAiProposedActionCommand { ProposedActionId = conversationId };
        ISecureRequest reject = new RejectAiProposedActionCommand { ProposedActionId = conversationId };
        ISecureRequest cancel = new CancelAiRunCommand { ConversationId = conversationId, RunId = Guid.CreateVersion7() };
        ISecureRequest propose = new ProposeAiToolActionCommand { ConversationId = conversationId, ToolName = "CreateEventDraft", PayloadJson = "{}" };
        ISecureRequest detail = new GetAiConversationDetailQuery { ConversationId = conversationId };
        ISecureRequest runStatus = new GetAiRunStatusQuery { ConversationId = conversationId, RunId = Guid.CreateVersion7() };

        await Assert.That(send.ResourceId).IsEqualTo(conversationId.ToString());
        await Assert.That(confirm.ResourceId).IsEqualTo(conversationId.ToString());
        await Assert.That(reject.ResourceId).IsEqualTo(conversationId.ToString());
        await Assert.That(cancel.ResourceId).IsEqualTo(conversationId.ToString());
        await Assert.That(propose.ResourceId).IsEqualTo(conversationId.ToString());
        await Assert.That(detail.ResourceId).IsEqualTo(conversationId.ToString());
        await Assert.That(runStatus.ResourceId).IsEqualTo(conversationId.ToString());
    }

    private static async Task AssertAuthorization<TRequest>(string action)
    {
        var attribute = typeof(TRequest).GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Resource).IsEqualTo(ResourceKinds.AiConversation);
        await Assert.That(attribute.Action).IsEqualTo(action);
    }
}
