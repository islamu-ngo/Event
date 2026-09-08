// Uses a 1:1 shared primary-key relationship with Actor for hard-deleteable PII.

namespace Explore.Domain;

public class ActorPii
{
    public Guid ActorId { get; set; }
    public Actor? Actor { get; set; }

    public required string DisplayName { get; set; }
    public string? ProfilePictureUri { get; set; }
}
