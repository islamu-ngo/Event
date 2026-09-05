// ABOUTME: Resolves explicit email intent and coherent SMTP ownership through existing settings and secret authorities.
// ABOUTME: Disabled delivery never resolves secrets; tenant transports never inherit instance credentials.

using System.Net.Mail;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.Services;

namespace Explore.Infrastructure.Mail;

public sealed class EmailDeliveryCapabilityResolver(
    IHierarchicalSettingsResolver settings,
    ISecretResolver secrets,
    ISecretBindingRepository bindings) : IEmailDeliveryCapabilityResolver
{
    public async Task<EmailDeliveryCapability> ResolveAsync(Guid? tenantId, CancellationToken cancellationToken = default) =>
        (await ResolveTransportAsync(tenantId, cancellationToken)).Capability;

    internal async Task<(EmailDeliveryCapability Capability, SmtpConfiguration? Configuration)> ResolveTransportAsync(
        Guid? tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A tenant identifier must be nonempty.", nameof(tenantId));

        var instance = (await settings.ResolveBatchAsync(
            EmailSettingGroup.SettingKeys.Append(GovernanceSettingKeys.TenantDelegation.LockSmtp),
            new SettingContext(), cancellationToken)).ToDictionary(value => value.Key);
        var locked = !instance.TryGetValue(GovernanceSettingKeys.TenantDelegation.LockSmtp, out var delegation)
            || SettingValueSerializer.Deserialize(delegation.Value, true);
        var effective = tenantId.HasValue && !locked
            ? (await settings.ResolveBatchAsync(EmailSettingGroup.SettingKeys, new SettingContext(tenantId), cancellationToken))
                .ToDictionary(value => value.Key)
            : instance;

        var ownsTransport = effective.TryGetValue(GovernanceSettingKeys.Email.SmtpHost, out var host)
            && IsTenantOwned(host);
        var ownerId = ownsTransport ? tenantId : null;
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
            && Enum.TryParse<SmtpSecurityMode>(owner.SmtpSecurity, true, out var parsedSecurity)
            && Enum.IsDefined(parsedSecurity);
        var state = EmailDeliveryPolicy.Evaluate(enabled, hasHost, hasSender, valid, true);
        if (state != EmailDeliveryState.Available)
            return (Capability(state, enabled, ownerId), null);

        var (username, usernameMissing) = await ResolveCredentialAsync(SecretDefinitionRegistry.Keys.Smtp.Username, ownerId, cancellationToken);
        var (password, passwordMissing) = await ResolveCredentialAsync(SecretDefinitionRegistry.Keys.Smtp.Password, ownerId, cancellationToken);
        if (usernameMissing || passwordMissing)
            return (Capability(EmailDeliveryState.Misconfigured, enabled, ownerId), null);

        if (IsAuthorityFailure(username) || IsAuthorityFailure(password))
            return (Capability(EmailDeliveryState.Degraded, enabled, ownerId), null);

        if (!ScopeMatches(username, ownerId) || !ScopeMatches(password, ownerId)
            || (username.IsResolved && string.IsNullOrWhiteSpace(username.Value))
            || (password.IsResolved && string.IsNullOrWhiteSpace(password.Value))
            || string.IsNullOrWhiteSpace(username.Value) != string.IsNullOrWhiteSpace(password.Value))
            return (Capability(EmailDeliveryState.Misconfigured, enabled, ownerId), null);

        return (Capability(EmailDeliveryState.Available, enabled, ownerId), new SmtpConfiguration
        {
            Host = owner.SmtpHost!,
            Port = owner.SmtpPort,
            Username = username.Value,
            Password = password.Value,
            Security = Enum.Parse<SmtpSecurityMode>(owner.SmtpSecurity, true),
            FromAddress = owner.FromAddress!,
            FromName = owner.FromName,
            TimeoutSeconds = owner.SmtpTimeoutSeconds,
            SkipCertificateValidation = owner.SmtpSkipCertValidation
        });
    }

    private async Task<(SecretResolutionResult Result, bool MissingDeclaredValue)> ResolveCredentialAsync(
        string key, Guid? ownerId, CancellationToken ct)
    {
        var scope = ownerId.HasValue ? SecretScope.Tenant : SecretScope.Instance;
        var binding = await bindings.GetByKeyAndScopeAsync(key, scope, ownerId, ct);
        var result = ownerId is { } tenantId
            ? binding is null
                ? SecretResolutionResult.Unconfigured
                : await secrets.ResolveTenantBindingAsync(tenantId, binding.Id, ct)
            : await secrets.ResolveAsync(key, null, ct);

        return (result, binding is not null && result.Status == SecretResolutionStatus.Unconfigured);
    }

    private static bool IsTenantOwned(ResolvedSetting setting) =>
        setting.Source is SettingSource.TenantOverride or SettingSource.TenantLocked;

    private static bool IsAuthorityFailure(SecretResolutionResult result) =>
        result.Status is not (SecretResolutionStatus.Resolved or SecretResolutionStatus.Unconfigured);

    private static bool ScopeMatches(SecretResolutionResult result, Guid? ownerId) =>
        result.Status == SecretResolutionStatus.Unconfigured ||
        (result.Scope is { } scope && EmailDeliveryPolicy.CanUseCredential(ownerId, scope, result.ScopeId));

    private static EmailDeliveryCapability Capability(EmailDeliveryState state, bool enabled, Guid? ownerId) =>
        new(state, enabled, ownerId.HasValue ? SecretScope.Tenant : SecretScope.Instance, ownerId, state switch
        {
            EmailDeliveryState.Disabled => "email_delivery_disabled",
            EmailDeliveryState.Unconfigured => "email_transport_unconfigured",
            EmailDeliveryState.Misconfigured => "email_transport_misconfigured",
            EmailDeliveryState.Degraded => "email_authority_unavailable",
            _ => "email_transport_available"
        });
}
