using Explore.Application.Contracts.Persistence;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Disclosure;

/// <summary>
/// Redacts PII from persisted AI conversation transcripts when consent is
/// revoked or a right-to-be-forgotten request is processed. Ensures that
/// previously-disclosed PII in <see cref="AiConversationReference.Summary"/>
/// and <see cref="AiMessage.Content"/> is scrubbed.
/// </summary>
public sealed partial class AiContextHygieneService : IAiContextHygieneService
{
    private readonly IAiConversationRepository _conversationRepository;
    private readonly IAiContextRedactor _redactor;
    private readonly IUnitOfWork _unitOfWork;

    public AiContextHygieneService(
        IAiConversationRepository conversationRepository,
        IAiContextRedactor redactor,
        IUnitOfWork unitOfWork)
    {
        _conversationRepository = conversationRepository;
        _redactor = redactor;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc/>
    public Task<int> RedactConversationTranscriptAsync(
        Guid conversationId,
        bool piiDisclosureEnabled,
        CancellationToken requestToken) =>
        _unitOfWork.ExecuteSerializableAsync(async cancellationToken =>
        {
            var conversation = await _conversationRepository
                .GetByIdForPrivacyUpdateAsync(conversationId, cancellationToken)
                .ConfigureAwait(false);
            if (conversation is null)
            {
                return 0;
            }

            var redactedCount = 0;
            foreach (var reference in conversation.References)
            {
                if (string.IsNullOrEmpty(reference.Summary))
                {
                    continue;
                }
                var redacted = _redactor.RedactEmbeddedPii(reference.Summary);
                if (!string.Equals(redacted, reference.Summary, StringComparison.Ordinal))
                {
                    reference.Summary = redacted;
                    redactedCount++;
                }
            }

            foreach (var message in conversation.Messages)
            {
                if (string.IsNullOrEmpty(message.Content))
                {
                    continue;
                }
                var redacted = _redactor.RedactEmbeddedPii(message.Content);
                if (!string.Equals(redacted, message.Content, StringComparison.Ordinal))
                {
                    message.Content = redacted;
                    redactedCount++;
                }
            }

            if (redactedCount > 0)
            {
                await _conversationRepository.Update(conversation).ConfigureAwait(false);
            }
            return redactedCount;
        }, requestToken);

    /// <inheritdoc/>
    public async Task<int> PropagateConsentRevocationAsync(
        Guid subjectUserId,
        string entityName,
        string fieldName,
        bool piiDisclosureEnabled,
        CancellationToken cancellationToken)
    {
        var recentConversations = await _conversationRepository
            .ListRecentForUserAsync(subjectUserId, 200, cancellationToken)
            .ConfigureAwait(false);

        var touched = 0;
        foreach (var conversation in recentConversations)
        {
            var redacted = await RedactConversationTranscriptAsync(
                conversation.Id,
                piiDisclosureEnabled,
                cancellationToken).ConfigureAwait(false);

            if (redacted > 0)
            {
                touched++;
            }
        }

        return touched;
    }
}
