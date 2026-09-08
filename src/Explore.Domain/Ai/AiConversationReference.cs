using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain.Ai;

public class AiConversationReference : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ConversationId { get; set; }
    public AiConversation? Conversation { get; set; }
    public int KindId { get; set; }
    public AiReferenceKindLookup? KindLookup { get; set; }
    [NotMapped]
    public AiReferenceKind Kind
    {
        get => (AiReferenceKind)KindId;
        set => KindId = (int)value;
    }
    public Guid ReferenceId { get; set; }
    public required string DisplayName { get; set; }
    public string? Summary { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
