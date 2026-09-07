using System;

namespace Explore.Application.DTOs.EventSessionLanguage;

public sealed record EventSessionLanguageDto
{
    public int Id { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public Guid EventSessionId { get; init; }
    public Guid EventId { get; set; }
    public string? EventSessionTitle { get; init; }
    public int LanguageId { get; init; }
    public string? LanguageMasterCode { get; init; } // For i18n with Tolgee
    public string? LanguageFullName { get; init; } // Fallback default
    public Guid TenantId { get; init; }
}
