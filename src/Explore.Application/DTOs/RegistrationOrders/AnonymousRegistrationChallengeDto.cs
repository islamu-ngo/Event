
namespace Explore.Application.DTOs.RegistrationOrders;

public sealed record AnonymousRegistrationChallengeDto
{
    public AnonymousRegistrationChallengeDto(string protectedChallenge, DateTimeOffset expiresAt, int difficulty, int version)
    {
        ProtectedChallenge = protectedChallenge;
        ExpiresAt = expiresAt;
        Difficulty = difficulty;
        Version = version;
    }

    public string ProtectedChallenge { get; }
    public DateTimeOffset ExpiresAt { get; }
    public int Difficulty { get; }
    public int Version { get; }

    public override string ToString() => "AnonymousRegistrationChallengeDto { Redacted = true }";
}
