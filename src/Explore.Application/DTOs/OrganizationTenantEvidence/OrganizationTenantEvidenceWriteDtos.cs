namespace Explore.Application.DTOs.OrganizationTenantEvidence;

public sealed record CreateOrganizationTenantEvidenceUploadSessionDto
{
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public long ExpectedSizeBytes { get; init; }
}

public sealed record SubmitOrganizationTenantEvidenceDto
{
    public Guid DocumentStorageObjectId { get; init; }
}

public sealed record ReviewOrganizationTenantEvidenceDto
{
    public OrganizationTenantEvidenceReviewDecisionDto Decision { get; init; }
    public string? Notes { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
}

public enum OrganizationTenantEvidenceReviewDecisionDto
{
    Approve = 1,
    Reject = 2
}
