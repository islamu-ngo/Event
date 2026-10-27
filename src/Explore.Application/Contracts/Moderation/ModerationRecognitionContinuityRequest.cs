using Explore.Application.Authentication;

namespace Explore.Application.Contracts.Moderation;

/// <summary>
/// Trusted adapter input, not a body or claims parser. Null tenant means native instance
/// enrollment, never an inferred tenant/community jurisdiction. No email or profile evidence.
/// </summary>
public sealed record ModerationRecognitionContinuityRequest(
    ProviderAccountKey Account, Guid FreshSubjectId, Guid? EnrollmentTenantId)
{
    /// <summary>The caller's exact existing or preallocated native verified login, checked before commit.</summary>
    public Guid? VerifiedBindingId { get; init; }
    public override string ToString() => nameof(ModerationRecognitionContinuityRequest);
}
