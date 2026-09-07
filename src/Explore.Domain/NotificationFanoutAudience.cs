namespace Explore.Domain;

public readonly record struct NotificationFanoutAudienceCursor(
    DateTime FirstEligibleRegistrationCreatedAt,
    Guid UserId);

public sealed record NotificationFanoutAudienceMember
{
    public NotificationFanoutAudienceMember()
    {
    }

    public NotificationFanoutAudienceMember(
        Guid userId,
        DateTime firstEligibleRegistrationCreatedAt)
    {
        UserId = userId;
        FirstEligibleRegistrationCreatedAt = firstEligibleRegistrationCreatedAt;
    }

    public Guid UserId { get; init; }
    public DateTime FirstEligibleRegistrationCreatedAt { get; init; }
}
