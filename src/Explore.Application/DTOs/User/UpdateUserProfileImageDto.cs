using System;

namespace Explore.Application.DTOs.User;

public sealed record UpdateUserProfileImageDto
{
    public Guid ProfilePictureId { get; init; }
}
