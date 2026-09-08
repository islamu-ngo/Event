// ABOUTME: Defines the tenant public visitor posture independently of operator authentication.
// ABOUTME: Directory-only restricts new native allocations, never existing participant recovery.

namespace Explore.Domain.Enums;

public enum VisitorAccessMode
{
    FullRegistrationAndAuth = 0,
    AnonymousOnly = 1,
    DirectoryListingOnly = 2
}
