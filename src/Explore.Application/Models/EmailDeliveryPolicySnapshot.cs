// ABOUTME: Evaluates non-secret SMTP intent and transport ownership from resolved instance and tenant settings.
// ABOUTME: Shares the same deterministic policy between capability discovery and transactional delivery fencing.

using System.Net.Mail;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services;

namespace Explore.Application.Models;

public sealed record EmailDeliveryPolicySnapshot(
    EmailDeliveryState State,
    bool Enabled,
    Guid? TransportTenantId)
{
    public static EmailDeliveryPolicySnapshot Evaluate(
        IReadOnlyDictionary<string, ResolvedSetting> instance,
        IReadOnlyDictionary<string, ResolvedSetting> effective,
        Guid? tenantId)
    {
        var ownsTransport = effective.TryGetValue(GovernanceSettingKeys.Email.SmtpHost, out var host)
            && IsTenantOwned(host);
        var owner = new EmailSettingGroup();
        owner.Populate(ownsTransport ? effective : instance);
        var requested = new EmailSettingGroup();
        requested.Populate(effective);
        var enabled = owner.DeliveryEnabled && requested.DeliveryEnabled;
        var hasHost = !string.IsNullOrWhiteSpace(owner.SmtpHost);
        var hasSender = !string.IsNullOrWhiteSpace(owner.FromAddress);
        var coherentSender = !ownsTransport ||
            (effective.TryGetValue(GovernanceSettingKeys.Email.FromAddress, out var sender) && IsTenantOwned(sender));
        var valid = coherentSender && owner.SmtpPort is > 0 and <= 65535 && owner.SmtpTimeoutSeconds > 0
            && MailAddress.TryCreate(owner.FromAddress, out _)
            && Enum.TryParse<SmtpSecurityMode>(owner.SmtpSecurity, true, out var security)
            && Enum.IsDefined(security);

        return new EmailDeliveryPolicySnapshot(
            State: EmailDeliveryPolicy.Evaluate(enabled, hasHost, hasSender, valid, authorityAvailable: true),
            Enabled: enabled,
            TransportTenantId: ownsTransport ? tenantId : null);
    }

    private static bool IsTenantOwned(ResolvedSetting setting) =>
        setting.Source is SettingSource.TenantOverride or SettingSource.TenantLocked;
}
