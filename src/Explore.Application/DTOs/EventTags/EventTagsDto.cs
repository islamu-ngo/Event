using System;

namespace Explore.Application.DTOs.EventTags;

public sealed record EventTagsDto
{
    public Guid Id { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public Guid EventId { get; init; }
    public string? EventTitle { get; init; }
    public Guid TagId { get; init; }
    public string? TagFullName { get; init; }
    public string? TagMasterCode { get; init; } // For i18n with Tolgee
    public Guid TenantId { get; init; }
}
