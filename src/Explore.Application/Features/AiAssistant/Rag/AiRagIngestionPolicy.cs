namespace Explore.Application.Features.AiAssistant.Rag;

public static class AiRagIngestionPolicy
{
    public const int MaxDisplayNameLength = 200;
    public const int MaxSummaryLength = 500;
    private const string EventKind = "event";

    public static AiRagIngestionValidationResult Validate(AiRagIndexDocument document)
    {
        if (document.TenantId == Guid.Empty)
        {
            return Failure("rag_tenant_required", "AI RAG documents must be tenant-bound before indexing.");
        }

        if (!string.Equals(document.Kind.Trim(), EventKind, StringComparison.OrdinalIgnoreCase))
        {
            return Failure("rag_kind_not_supported", "AI RAG indexing currently supports event summaries only.");
        }

        if (document.ReferenceId == Guid.Empty)
        {
            return Failure("rag_reference_required", "AI RAG documents must include a reference identifier.");
        }

        if (!Enum.IsDefined(document.ContentScope))
        {
            return Failure("rag_scope_not_allowed", "AI RAG documents must use an approved public-summary scope.");
        }

        if (string.IsNullOrWhiteSpace(document.DisplayName) || document.DisplayName.Trim().Length > MaxDisplayNameLength)
        {
            return Failure("rag_display_name_invalid", "AI RAG documents must include a bounded display name.");
        }

        if (string.IsNullOrWhiteSpace(document.Summary) || document.Summary.Trim().Length > MaxSummaryLength)
        {
            return Failure("rag_summary_invalid", "AI RAG documents must include a bounded public summary.");
        }

        if (string.IsNullOrWhiteSpace(document.Citation.Label) ||
            string.IsNullOrWhiteSpace(document.Citation.RouteName) ||
            string.IsNullOrWhiteSpace(document.Citation.ResourcePath))
        {
            return Failure("rag_citation_required", "AI RAG documents must include safe citation metadata.");
        }

        return AiRagIngestionValidationResult.Success();
    }

    private static AiRagIngestionValidationResult Failure(string failureCode, string failureMessage)
        => AiRagIngestionValidationResult.Failure(failureCode, failureMessage);
}
