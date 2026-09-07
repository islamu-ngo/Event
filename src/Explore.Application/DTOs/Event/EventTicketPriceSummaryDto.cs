namespace Explore.Application.DTOs.Event;

public sealed record EventTicketPriceSummaryDto
{
    public required string SummaryCode { get; init; }
    public string? CurrencyCode { get; init; }
    public int CurrencyMinorUnitDigits { get; init; }
    public long FromAmountMinor { get; init; }
}
