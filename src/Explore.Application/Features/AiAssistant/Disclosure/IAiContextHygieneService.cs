using System.Threading;
using System.Threading.Tasks;

namespace Explore.Application.Features.AiAssistant.Disclosure;

/// <summary>
/// Propagates consent revocation and right-to-be-forgotten requests into
/// persisted AI conversation transcripts, redacting any previously-disclosed
/// PII that is no longer authorised.
/// </summary>
public interface IAiContextHygieneService
{
    /// <summary>
    /// Redacts PII from a specific conversation's references and messages.
    /// Called when a single conversation is identified as containing stale PII.
    /// </summary>
    Task<int> RedactConversationTranscriptAsync(
        System.Guid conversationId,
        bool piiDisclosureEnabled,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds and redacts AI transcripts for a data subject after consent
    /// revocation for a specific (entity, field) tuple. Returns the number of
    /// conversations touched.
    /// </summary>
    Task<int> PropagateConsentRevocationAsync(
        System.Guid subjectUserId,
        string entityName,
        string fieldName,
        bool piiDisclosureEnabled,
        CancellationToken cancellationToken);
}
