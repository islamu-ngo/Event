using System;

namespace Explore.Application.DTOs.EventSession;

public sealed record EventSessionAuthorizationContextDto
{
    public Guid Id { get; init; }
    public Guid EventId { get; init; }
    public Guid TenantId { get; init; }
}
