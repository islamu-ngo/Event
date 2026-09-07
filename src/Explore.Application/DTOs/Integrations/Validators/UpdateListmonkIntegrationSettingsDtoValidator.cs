namespace Explore.Application.DTOs.Integrations.Validators;

using System.Net;
using FluentValidation;

public sealed class UpdateListmonkIntegrationSettingsDtoValidator
    : AbstractValidator<UpdateListmonkIntegrationSettingsDto>
{
    public UpdateListmonkIntegrationSettingsDtoValidator(ListmonkIntegrationSettingsDto current)
    {
        RuleFor(x => x)
            .Must(x => x.Connection is not null || x.Behavior is not null)
            .WithMessage("At least one Listmonk settings group is required.");

        RuleFor(x => x.Connection!.DefaultListId)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Listmonk default list ID cannot be negative.")
            .When(x => x.Connection is not null);

        RuleFor(x => x.Connection!.InstanceUrl)
            .Must(BeSafeHttpUrl)
            .When(x => !string.IsNullOrWhiteSpace(x.Connection?.InstanceUrl))
            .WithMessage("Listmonk instance URL must be an absolute public HTTP(S) URL.");

        RuleFor(x => x)
            .Must(x =>
            {
                var enabled = x.Behavior?.Enabled ?? current.Enabled;
                var sync = x.Behavior?.SyncOnRegistration ?? current.SyncOnRegistration;
                var instanceUrl = x.Connection?.InstanceUrl ?? current.InstanceUrl;
                var defaultListId = x.Connection?.DefaultListId ?? current.DefaultListId;
                return (!enabled && !sync) || (!string.IsNullOrWhiteSpace(instanceUrl) && defaultListId > 0);
            })
            .WithMessage("Listmonk instance URL and default list ID are required when the integration is enabled.");

        RuleFor(x => x)
            .Must(x => !(x.Behavior?.SyncOnRegistration ?? current.SyncOnRegistration)
                || (x.Behavior?.Enabled ?? current.Enabled))
            .WithMessage("Listmonk registration sync requires the integration to be enabled.");
    }

    private static bool BeSafeHttpUrl(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        if (uri.IsLoopback ||
            string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !IPAddress.TryParse(uri.Host, out var address) || IsPublicAddress(address);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return false;

        if (address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.Broadcast) ||
            address.Equals(IPAddress.None))
        {
            return false;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return !address.IsIPv6LinkLocal && !address.IsIPv6SiteLocal;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            10 => false,
            127 => false,
            169 when bytes[1] == 254 => false,
            172 when bytes[1] >= 16 && bytes[1] <= 31 => false,
            192 when bytes[1] == 168 => false,
            _ => true
        };
    }
}
