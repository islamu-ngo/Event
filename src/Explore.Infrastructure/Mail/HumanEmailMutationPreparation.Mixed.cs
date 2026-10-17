using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain.Enums;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.SupportAccess;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Mail;

public sealed partial class HumanEmailMutationPreparation
{
    public async Task<MixedSettingsBatchMutation> PrepareMixedAsync(
        UpdateSettingBatchCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Scope is not (SettingScope.Instance or SettingScope.Tenant)
            || !MixedSettingsBatchMutation.RequiresCombinedSource(command))
            throw new UnauthorizedAccessException("Mixed preparation requires the actual mixed administrator batch.");
        await humanScope.PrepareAsync(cancellationToken);
        var protocol = await services.GetRequiredService<IHumanMixedSettingsSourceFactory>()
            .CreateAsync(command, cancellationToken);
        var originalRequest = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original mixed request is required.");
        var principal = originalRequest.User;
        var subject = protocol.Subject;
        var lifetime = protocol.Lifetime;
        Guid ambient = tenantContext.TenantId;
        if (!ReferenceEquals(subject, humanScope.Subject)
            || !ReferenceEquals(await lifetimeObserver.ObserveAsync(principal, cancellationToken), lifetime)
            || (command.Scope == SettingScope.Tenant) != protocol.TenantId.HasValue
            || protocol.TenantId.HasValue && protocol.TenantId != ambient)
            throw new UnauthorizedAccessException("Mixed preparation requires original selected human, target and credential custody.");

        void Custody()
        {
            if (!ReferenceEquals(httpContext.HttpContext, originalRequest)
                || !ReferenceEquals(originalRequest.User, principal)
                || !ReferenceEquals(humanScope.Subject, subject) || tenantContext.TenantId != ambient)
                throw new UnauthorizedAccessException("Original mixed request custody changed.");
        }

        var original = await protocol.ObserveAsync(cancellationToken);
        IMixedSettingsBatchStore? capturedStore = null;
        IHumanMixedSettingsSource? capturedSource = null;
        string principalImage = JsonSerializer.Serialize(original.Principal);
        string kind = protocol.TenantId.HasValue ? ResourceKinds.TenantSetting : ResourceKinds.InstanceSetting;
        var attributes = AuthorizationFactAttributeProjection.ToAttributes(protocol.TenantId is { } tenant
            ? new TenantScopedAuthorizationFacts(tenant) : InstanceScopedAuthorizationFacts.Instance);
        void Check(HumanEmailSourceFacts facts)
        {
            Custody();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, facts.Principal);
            if (facts.Principal.SelectedTenantId != protocol.TenantId
                || !ReferenceEquals(facts.Support.Forwarding, protocol.Forwarding)
                || !ReferenceEquals(protocol.Forwarding.Subject, subject)
                || principalImage != JsonSerializer.Serialize(facts.Principal))
                throw new UnauthorizedAccessException("Original mixed principal sources changed.");
            if (!original.Support.Sessions.SequenceEqual(facts.Support.Sessions))
                throw new UnauthorizedAccessException("The original mixed support session was replaced.");
            var expectedSupport = original.Support.Settings.ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
            if (capturedStore is not null && capturedSource is not null)
            {
                using var image = JsonDocument.Parse(capturedStore.ExpectedSourceData(capturedSource));
                var rows = image.RootElement.GetProperty(nameof(SystemSetting));
                foreach (string key in original.Support.Settings.Select(row => row.SettingKey)
                    .Concat(facts.Support.Settings.Select(row => row.SettingKey)).Distinct(StringComparer.Ordinal))
                {
                    var row = rows.EnumerateArray().SingleOrDefault(candidate =>
                        candidate.GetProperty(nameof(SystemSetting.SettingKey)).GetString() == key);
                    if (row.ValueKind != JsonValueKind.Undefined)
                    {
                        var allowed = row.GetProperty(nameof(SystemSetting.AllowedValues));
                        expectedSupport[key] = new(
                            row.GetProperty(nameof(SystemSetting.Id)).GetGuid(), key,
                            row.GetProperty(nameof(SystemSetting.Value)).GetString()!,
                            row.GetProperty(nameof(SystemSetting.SettingValueTypeId)).GetInt32(),
                            row.GetProperty(nameof(SystemSetting.IsLocked)).GetBoolean(),
                            row.GetProperty(nameof(SystemSetting.Description)).GetString(),
                            row.GetProperty(nameof(SystemSetting.Category)).GetString(),
                            allowed.ValueKind switch
                            {
                                JsonValueKind.Null => null,
                                JsonValueKind.String => allowed.GetString(),
                                _ => allowed.GetRawText()
                            });
                    }
                }
            }
            if (!expectedSupport.Values.OrderBy(row => row.SettingKey, StringComparer.Ordinal)
                .SequenceEqual(facts.Support.Settings.OrderBy(row => row.SettingKey, StringComparer.Ordinal)))
                throw new UnauthorizedAccessException("Original mixed support policy sources changed outside the compiled effects.");
            bool administrator;
            if (protocol.TenantId is { } target)
            {
                var memberships = facts.Principal.TenantMemberships.Where(member => member.TenantId == target
                    && !member.IsDeleted && member.StatusId == (int)TenantUserStatusEnum.Active).ToArray();
                administrator = memberships.Length == 1
                    && facts.Principal.Tenants.Any(row => row.Id == target && row.IsActiveState)
                    && facts.Principal.TenantGrants.Any(grant => grant.TenantId == target
                        && grant.TenantUserId == memberships[0].Id && grant.RevokedAt is null
                        && grant.RoleId == (int)RoleEnum.TenantAdmin && grant.RoleScopeId == (int)RoleScopeEnum.Tenant
                        && facts.Principal.Roles.Any(role => role.Id == grant.RoleId
                            && role.RoleScopeId == (int)RoleScopeEnum.Tenant));
            }
            else
                administrator = InstancePolicyInput(facts.Principal);
            if (!administrator)
                throw new UnauthorizedAccessException("The original mixed batch administrator predicate is not satisfied.");
        }
        var current = original;
        void Time(DateTimeOffset now)
        {
            Custody();
            if (!lifetime.IsValidAt(now)
                || HumanSupportAccessProjection.BoundaryDenialReason(original.Support, now, kind,
                    AuthorizationActions.Update, attributes) is not null
                || HumanSupportAccessProjection.BoundaryDenialReason(current.Support, now, kind,
                    AuthorizationActions.Update, attributes) is not null)
                throw new UnauthorizedAccessException("The original mixed credential or support horizon ended.");
        }
        Check(original);
        Time(clock.GetUtcNow());
        var source = new MixedSource(this, protocol, Custody, async token =>
        {
            current = await protocol.RequireOwnedAsync(token);
            Check(current);
        }, async token =>
        {
            current = await protocol.AcquireAsync(token);
            Check(current);
        }, Time);
        var store = services.GetRequiredService<IMixedSettingsBatchStore>();
        capturedSource = source;
        capturedStore = store;
        await store.ObserveAsync(command, source, owner, cancellationToken);
        Custody();
        Time(clock.GetUtcNow());
        return new MixedSettingsBatchMutation(this, command, source, store, owner);
    }

    public void RequireMixedSource(IHumanMixedSettingsSource source)
    {
        if (source is not MixedSource original || !ReferenceEquals(original.Issuer, this))
            throw new UnauthorizedAccessException("The original scoped mixed source issuer is required.");
        original.RequireCustody();
    }

    public async Task<T> ExecuteMixedPreparedAsync<T>(MixedSettingsBatchMutation mutation,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        RequireMixedSource(mutation.Source);
        mutation.Claim(this);
        var source = (MixedSource)mutation.Source;
        try
        {
            return await mutation.Owner.ExecuteAsync(async token =>
            {
                await source.Acquire(token);
                await mutation.Store.AcquireAsync(mutation, token);
                source.RequireTime(clock.GetUtcNow());
                mutation.Activate(this);
                finalizer.Register(async finalToken =>
                {
                    await mutation.Store.RequireCompletedAsync(mutation, finalToken);
                    await source.RequireSourcesAsync(finalToken);
                }, source.RequireTime);
                try { return await operation(token); }
                catch (Exception exception) when (ContainsRollbackMarker(exception))
                {
                    throw new InvalidOperationException("A mixed callback cannot certify physical rollback.");
                }
            }, cancellationToken);
        }
        finally
        {
            mutation.Store.Close(mutation);
            mutation.Close(this);
        }
    }

    private sealed class MixedSource(HumanEmailMutationPreparation issuer, IHumanMixedSettingsSourceProtocol protocol,
        Action custody, Func<CancellationToken, Task> validate, Func<CancellationToken, Task> acquire,
        Action<DateTimeOffset> time) : IHumanMixedSettingsSource
    {
        internal HumanEmailMutationPreparation Issuer => issuer;
        public HumanCommitSubject Subject => protocol.Subject;
        public Guid? TenantId => protocol.TenantId;
        public string OriginalData => protocol.OriginalData;
        public void RequireCustody() => custody();
        public Task RequireSourcesAsync(CancellationToken cancellationToken) => validate(cancellationToken);
        internal Task Acquire(CancellationToken cancellationToken) => acquire(cancellationToken);
        public void RequireTime(DateTimeOffset now) => time(now);
    }
}
