using System.ComponentModel.DataAnnotations.Schema;
using Explore.Domain.Interfaces;

namespace Explore.Domain.Ai;

public class AiMessage : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ConversationId { get; set; }
    public AiConversation? Conversation { get; set; }
    public long Sequence { get; set; }
    public int RoleId { get; set; }
    public AiMessageRoleLookup? RoleLookup { get; set; }
    [NotMapped]
    public AiMessageRole Role
    {
        get => (AiMessageRole)RoleId;
        set => RoleId = (int)value;
    }
    public required string Content { get; set; }
    public string? ImageAttachmentsJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
}
