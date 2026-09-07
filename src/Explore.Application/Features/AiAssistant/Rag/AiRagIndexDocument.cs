namespace Explore.Application.Features.AiAssistant.Rag;

public sealed record AiRagIndexDocument(
    Guid TenantId,
    string Kind,
    Guid ReferenceId,
    AiRagContentScope ContentScope,
    string DisplayName,
    string Summary,
    DateTimeOffset UpdatedAtUtc,
    AiRagCitation Citation);
