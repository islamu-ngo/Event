namespace Explore.Application.Features.LegalDocuments.Requests.Queries;

using Explore.Application.DTOs.LegalDocuments;
using MediatR;

public sealed record GetPublicLegalDocumentQuery(
    string KindCode,
    string LanguageTag) : IRequest<PublicLegalDocumentQueryResult>;

public sealed record PublicLegalDocumentQueryResult
{
    private PublicLegalDocumentQueryResult(
        PublicLegalDocumentDto? document,
        string? failureCode,
        bool isNotFound)
    {
        Document = document;
        FailureCode = failureCode;
        IsNotFound = isNotFound;
    }

    public bool IsAvailable => Document is not null;
    public PublicLegalDocumentDto? Document { get; }
    public string? FailureCode { get; }
    public bool IsNotFound { get; }

    public static PublicLegalDocumentQueryResult Available(
        PublicLegalDocumentDto document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(document, failureCode: null, isNotFound: false);
    }

    public static PublicLegalDocumentQueryResult NotFound() =>
        new(
            document: null,
            "legal_document_not_found",
            isNotFound: true);

    public static PublicLegalDocumentQueryResult Unavailable() =>
        new(
            document: null,
            "legal_document_rendering_unavailable",
            isNotFound: false);
}
