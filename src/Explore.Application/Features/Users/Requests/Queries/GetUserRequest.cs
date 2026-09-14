using System;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.User;

namespace Explore.Application.Features.Users.Requests.Queries;

public sealed record GetUserRequest(Guid UserId = default) : IQuery<UserDto>;
