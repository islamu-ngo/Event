namespace Explore.Application.Features.AiAssistant.Disclosure;

/// <summary>
/// The AI Context Disclosure Gateway — the single choke point through which entity
/// data must pass before reaching an AI prompt, MCP resource, or tool descriptor.
/// Implementations consult <see cref="AiContextDisclosureRegistry"/>, the provider
/// trust resolver, the viewer scope, consent grants, and the Phase-4 PII gate, then
/// emit an <see cref="AiContextSanitizedEnvelope"/> that only contains fields the
/// caller is authorized to see at the current trust tier.
/// </summary>
public interface IAiContextGateway
{
    /// <summary>
    /// Sanitizes a single entity's fields. Returns a fail-closed envelope on any
    /// unexpected error (unregistered entity, ambiguous trust tier, missing consent).
    /// </summary>
    AiContextSanitizedEnvelope Sanitize(AiContextSanitizationInput request);

    /// <summary>
    /// Sanitizes multiple entities in one call (e.g. an event + its sessions + its
    /// speakers). Each envelope is independent — a failure on one entity does NOT
    /// fail the others. The order of returned envelopes matches the input order.
    /// </summary>
    IReadOnlyList<AiContextSanitizedEnvelope> SanitizeMany(IReadOnlyList<AiContextSanitizationInput> requests);
}
