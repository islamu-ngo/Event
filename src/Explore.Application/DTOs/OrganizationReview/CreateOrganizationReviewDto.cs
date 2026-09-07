namespace Explore.Application.DTOs.OrganizationReview;

public sealed record CreateOrganizationReviewDto
{
    public Guid OrganizationId { get; init; }
    public Guid ProgramId { get; init; }
    public string ReviewerName { get; init; } = string.Empty;
    public int Rating { get; init; }
    public string Comment { get; init; } = string.Empty;
}
