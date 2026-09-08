namespace Explore.Application.Contracts.Services;

public interface IOrganizerPaymentCommerceConfiguration
{
    string ProviderCode { get; }

    string ConnectPlatformId { get; }
}

public sealed class OrganizerPaymentCommerceOptions : IOrganizerPaymentCommerceConfiguration
{
    public const string SectionName = "Payments:OrganizerDirect";

    public string ProviderCode { get; set; } = string.Empty;

    public string ConnectPlatformId { get; set; } = string.Empty;
}
