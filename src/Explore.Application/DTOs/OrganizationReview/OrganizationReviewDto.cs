namespace Explore.Application.DTOs.OrganizationReview;

public sealed record OrganizationReviewDto
{
    public Guid Id { get; init; }

    // Organization
    public Guid OrganizationId { get; init; }
    public string? OrganizationFullName { get; init; }

    // User
    public Guid? UserId { get; init; }
    public string? UserFullName { get; init; }

    // Review
    public int Rating { get; init; }
    public string? Comment { get; init; }
    public DateTime CreatedAt { get; init; }
}
