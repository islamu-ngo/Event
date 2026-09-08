using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services.Shell;

namespace Explore.Blazor.Client.Services.Ai;

public sealed class AiAssistantConversationState
{
    public IReadOnlyList<HalResourceOfAiConversationSummaryDto> Conversations { get; private set; } = [];

    public bool CanCreateConversation { get; private set; }

    public HalResourceOfAiConversationDto? SelectedConversation { get; private set; }

    public IReadOnlyList<HalResourceOfAiReferenceSearchResultDto> ReferenceResults { get; private set; } = [];

    public IReadOnlyList<HalResourceOfAiReferenceSearchResultDto> SelectedReferences { get; private set; } = [];

    public bool IsLoading { get; private set; }

    public string? ErrorMessage { get; private set; }

    public WorkspaceKey ReturnWorkspace { get; private set; } = WorkspaceKey.Events;

    public event Action? OnChange;

    public void SetLoading(bool isLoading)
    {
        if (IsLoading == isLoading)
        {
            return;
        }

        IsLoading = isLoading;
        NotifyChanged();
    }

    public void SetError(string? errorMessage)
    {
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage.Trim();
        NotifyChanged();
    }

    public void SetConversations(IReadOnlyList<HalResourceOfAiConversationSummaryDto> conversations)
    {
        Conversations = conversations;
        CanCreateConversation = false;
        NotifyChanged();
    }

    public void SetConversationCollection(HalCollectionResourceOfAiConversationSummaryDto? collection)
    {
        Conversations = collection?._embedded?.Items?.ToList() ?? [];
        CanCreateConversation = HasLink(collection?._links, "create");
        NotifyChanged();
    }

    public void SelectConversation(HalResourceOfAiConversationDto? conversation)
    {
        SelectedConversation = conversation;
        NotifyChanged();
    }

    public void SetReferenceResults(IReadOnlyList<HalResourceOfAiReferenceSearchResultDto> references)
    {
        ReferenceResults = references;
        NotifyChanged();
    }

    public void SetSelectedReferences(IReadOnlyList<HalResourceOfAiReferenceSearchResultDto> references)
    {
        SelectedReferences = references;
        NotifyChanged();
    }

    public void SetReturnWorkspace(WorkspaceKey workspace)
    {
        if (workspace != WorkspaceKey.Ai)
        {
            ReturnWorkspace = workspace;
        }
    }

    public static bool CanConfirm(ProposedActions2 proposedAction) => HasLink(proposedAction._links, "confirm-action");

    public static bool CanReject(ProposedActions2 proposedAction) => HasLink(proposedAction._links, "reject-action");

    public static bool CanSendMessage(HalResourceOfAiConversationDto? conversation) =>
        HasLink(conversation?._links, "send-message");

    public static bool HasEventLink(HalResourceOfAiReferenceSearchResultDto reference) => HasLink(reference._links, "event");

    public void RemoveConversation(Guid conversationId)
    {
        Conversations = Conversations.Where(c => c.Id != conversationId).ToList();
        if (SelectedConversation?.Id == conversationId)
        {
            SelectedConversation = null;
        }
        NotifyChanged();
    }

    private static bool HasLink<TLink>(IDictionary<string, TLink>? links, string rel) =>
        links?.ContainsKey(rel) == true;

    private void NotifyChanged() => OnChange?.Invoke();
}
