namespace Explore.Application.DTOs.LegalDocuments;

public sealed record PublicLegalDocumentDto
{
    public required string KindCode { get; init; }
    public required string ScopeCode { get; init; }
    public required string OwnerRoleCode { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string LanguageTag { get; init; }
    public required string RenderedHtml { get; init; }
    public required int Version { get; init; }
    public required DateTime EffectiveAt { get; init; }
    public required string ContentDigest { get; init; }
    public required bool IsLocaleFallback { get; init; }
}
