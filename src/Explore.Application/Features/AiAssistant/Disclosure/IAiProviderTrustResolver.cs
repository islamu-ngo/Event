using Explore.Domain.Enums;

namespace Explore.Application.Features.AiAssistant.Disclosure;

/// <summary>
/// Resolves a <see cref="AiProviderTrustTierEnum"/> from concrete deployment evidence
/// (endpoint URL, tenant ownership, platform defaults). Implementations must return
/// <see cref="AiProviderTrustTierEnum.Unknown"/> (the most restrictive tier) whenever
/// the evidence is ambiguous — provider trust is evidence-based, never naming-based
/// (CTO correction #6).
/// </summary>
public interface IAiProviderTrustResolver
{
    /// <summary>
    /// Returns the trust tier for the supplied resolution context. The resolver MUST
    /// downgrade to <see cref="AiProviderTrustTierEnum.Unknown"/> if any evidence
    /// field is missing or inconsistent (e.g. an external endpoint URL marked as
    /// tenant-controlled but not on a private network).
    /// </summary>
    AiProviderTrustTierEnum Resolve(AiProviderTrustResolutionContext context);
}

/// <summary>
/// Evidence bundle used by <see cref="IAiProviderTrustResolver"/>. All fields are
/// required; nulls are interpreted as "evidence missing" and force the resolver to
/// return <see cref="AiProviderTrustTierEnum.Unknown"/>.
/// </summary>
public sealed record AiProviderTrustResolutionContext(
    string? EndpointUrl,
    bool TenantControlled,
    bool PlatformDefault);
