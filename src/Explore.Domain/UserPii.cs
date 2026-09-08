// Uses a 1:1 shared primary-key relationship with User for hard-deleteable PII.

namespace Explore.Domain;

public class UserPii
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public required string Email { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
}
