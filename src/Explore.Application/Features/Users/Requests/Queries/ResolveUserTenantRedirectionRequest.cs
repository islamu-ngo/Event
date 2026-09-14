using System;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.User;

namespace Explore.Application.Features.Users.Requests.Queries;

public sealed record ResolveUserTenantRedirectionRequest(Guid UserId = default) : IQuery<UserTenantRedirectionDto>;
