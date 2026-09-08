namespace Explore.Application.DTOs.User;

public sealed record UpdateUserNamesDto
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
}
