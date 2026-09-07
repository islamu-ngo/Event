// ABOUTME: Executes HAL-gated SMTP delivery actions through the native generated clients.
// ABOUTME: Requires current typed confirmation authority and never retries destructive delivery updates.

using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services;

public interface IEmailDeliveryAdminService
{
    Task<HalResourceOfInstanceSmtpSettingsDto> GetInstanceAsync(CancellationToken cancellationToken = default);
    Task<HalResourceOfSettingGroupResponseDto> GetTenantAsync(CancellationToken cancellationToken = default);
    Task<HalResourceOfEmailDeliveryDisablePreviewDto> PreviewAsync(bool instanceScope, IDictionary<string, HalLink>? links, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> EnableAsync(bool instanceScope, IDictionary<string, HalLink>? links, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> DisableAsync(bool instanceScope, HalResourceOfEmailDeliveryDisablePreviewDto preview, string acknowledgement, CancellationToken cancellationToken = default);
}

public sealed class EmailDeliveryAdminService(
    IInstanceMessagingSettingsClient instanceClient,
    ISettingsClient tenantClient,
    TimeProvider clock) : IEmailDeliveryAdminService
{
    public const string DeliveryEnabledKey = "email.delivery_enabled";
    public const string Acknowledgement = "DISABLE EMAIL DELIVERY";

    public Task<HalResourceOfInstanceSmtpSettingsDto> GetInstanceAsync(CancellationToken cancellationToken = default) =>
        instanceClient.GetInstanceSmtpSettingsAsync(cancellationToken: cancellationToken);

    public Task<HalResourceOfSettingGroupResponseDto> GetTenantAsync(CancellationToken cancellationToken = default) =>
        tenantClient.GetTenantScopedSettingsAsync("Email", cancellationToken: cancellationToken);

    public Task<HalResourceOfEmailDeliveryDisablePreviewDto> PreviewAsync(
        bool instanceScope, IDictionary<string, HalLink>? links, CancellationToken cancellationToken = default)
    {
        RequireAction(links, "disable-preview", "POST");
        return instanceScope
            ? instanceClient.PreviewInstanceSmtpDisableAsync(cancellationToken: cancellationToken)
            : tenantClient.PreviewTenantSmtpDisableAsync(cancellationToken: cancellationToken);
    }

    public Task<BaseCommandResponseOfGuid> EnableAsync(
        bool instanceScope, IDictionary<string, HalLink>? links, CancellationToken cancellationToken = default)
    {
        RequireAction(links, instanceScope ? "edit" : "enable", instanceScope ? "PATCH" : "PUT");
        return instanceScope
            ? instanceClient.UpdateInstanceSmtpSettingsAsync(new PatchInstanceSmtpSettingsDto
            {
                DeliveryEnabled = new OptionalUpdateOfboolean { HasValue = true, Value = true }
            }, cancellationToken: cancellationToken)
            : tenantClient.UpdateTenantSettingAsync(DeliveryEnabledKey,
                new UpdateSettingValueDto { Value = "true" }, cancellationToken: cancellationToken);
    }

    public Task<BaseCommandResponseOfGuid> DisableAsync(bool instanceScope,
        HalResourceOfEmailDeliveryDisablePreviewDto preview, string acknowledgement,
        CancellationToken cancellationToken = default)
    {
        RequireAction(preview._links, "disable", "POST");
        if (!CanConfirm(preview, clock.GetUtcNow()) || acknowledgement != Acknowledgement
            || instanceScope != (preview.TenantId is null))
            throw new InvalidOperationException("A current disable confirmation is required.");

        var request = new EmailDeliveryDisableRequest
        {
            ExpectedRevision = preview.ExpectedRevision,
            ConfirmationToken = preview.ConfirmationToken!,
            Acknowledgement = acknowledgement
        };
        return instanceScope
            ? instanceClient.DisableInstanceSmtpAsync(request, cancellationToken: cancellationToken)
            : tenantClient.DisableTenantSmtpAsync(request, cancellationToken: cancellationToken);
    }

    public static bool HasAction(IDictionary<string, HalLink>? links, string relation, string method) =>
        links?.TryGetValue(relation, out var link) == true
        && !string.IsNullOrWhiteSpace(link.Href)
        && string.Equals(link.Method, method, StringComparison.OrdinalIgnoreCase);

    public static bool CanConfirm(HalResourceOfEmailDeliveryDisablePreviewDto preview, DateTimeOffset now) =>
        preview.IsLocked == false && preview.CanDisable == true
        && preview.AffectedScopes.Count > 0
        && preview.ExpiresAtUtc > now && !string.IsNullOrWhiteSpace(preview.ConfirmationToken)
        && HasAction(preview._links, "disable", "POST");

    private static void RequireAction(IDictionary<string, HalLink>? links, string relation, string method)
    {
        if (!HasAction(links, relation, method))
            throw new InvalidOperationException("The server did not advertise this email delivery action.");
    }
}
