namespace Explore.Application.DTOs.EventReporting;

public sealed record OspreySignalCallbackRequestDto
{
    public Guid TenantId { get; init; }
    public Guid ReportId { get; init; }
    public Guid EventId { get; init; }
    public Guid? CaseId { get; init; }
    public string? ProviderTargetScope { get; init; }
    public string? ProviderTargetId { get; init; }
    public string? ProviderSignalId { get; init; }
    public string? CorrelationId { get; init; }
    public IReadOnlyList<OspreySignalCallbackItemDto> Signals { get; init; } = [];
}
