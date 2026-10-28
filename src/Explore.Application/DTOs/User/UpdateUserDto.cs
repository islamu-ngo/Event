namespace Explore.Application.DTOs.User;

public sealed record UpdateUserDto
{
    public UpdateUserNamesDto? Names { get; init; }
}
