using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Services;
using Explore.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Explore.API.Authentication;

public sealed class ConfiguredApiKeyResourceAuthorityReader(
    IOptionsMonitor<ApiKeyAuthenticationOptions> options, TimeProvider clock,
    IHttpContextAccessor http) : IConfiguredApiKeyResourceAuthorityReader
{
    public ValueTask<ApiKeyPrincipalContext?> ReadAsync(
        string keyId, DateTimeOffset evaluationUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(keyId) || evaluationUtc.Offset != TimeSpan.Zero)
            return ValueTask.FromResult<ApiKeyPrincipalContext?>(null);

        var client = ReadClient(options.Get(ApiAuthenticationSchemeNames.ApiKey), keyId, evaluationUtc);
        return ValueTask.FromResult(client is null ? null : Principal(client));
    }

    public async ValueTask<IConfiguredApiKeyResourceAuthorityLease?> AcquireAsync(
        string keyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var timestamp = clock.GetTimestamp();
        var observedAt = clock.GetUtcNow();
        if (string.IsNullOrWhiteSpace(keyId) || observedAt.Offset != TimeSpan.Zero) return null;
        var source = options.Get(ApiAuthenticationSchemeNames.ApiKey);
        var client = ReadClient(source, keyId, observedAt);
        if (client is null) return null;
        return await RetainAsync(new SourceLease(options, source, client, Principal(client), clock,
            timestamp, observedAt, client.SecretHash, client.ExpiresAtUtc), cancellationToken);
    }

    public async ValueTask<IConfiguredApiKeyResourceAuthorityLease?> AcquireAuthenticatedAsync(
        string keyId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = http.HttpContext;
        var origin = context?.Features.Get<ConfiguredApiKeyAuthenticationOrigin>();
        var caller = context?.User.TryGetApiKeyPrincipalContext();
        if (origin is null || caller is null || string.IsNullOrWhiteSpace(keyId)
            || !string.Equals(keyId, origin.Principal.KeyId, StringComparison.Ordinal)
            || !SamePrincipal(caller, origin.Principal))
            return null;
        return await RetainAsync(new SourceLease(options, origin.Source, origin.Descriptor,
            origin.Principal, origin.Clock, origin.Timestamp, origin.ObservedAtUtc,
            origin.VerifiedHash, origin.ExpiresAtUtc), cancellationToken);
    }

    private static async ValueTask<IConfiguredApiKeyResourceAuthorityLease?> RetainAsync(
        SourceLease lease, CancellationToken cancellationToken)
    {
        try
        {
            if (await lease.RevalidateAsync(cancellationToken)) return lease;
        }
        catch
        {
            await lease.DisposeAsync();
            throw;
        }
        await lease.DisposeAsync();
        return null;
    }

    private static ApiKeyClientDescriptor? ReadClient(
        ApiKeyAuthenticationOptions source, string keyId, DateTimeOffset evaluationUtc)
    {
        ApiKeyClientDescriptor[] candidates = source.Clients
            .Where(client => string.Equals(client.KeyId, keyId, StringComparison.Ordinal))
            .Take(2).ToArray();
        if (candidates is not [var client] || !client.IsActive || client.TenantId == Guid.Empty
            || client.ExpiresAtUtc is { } expiry && expiry <= evaluationUtc
            || !Enum.TryParse(client.OwnerType, ignoreCase: true, out ExternalApiKeyOwnerType ownerType)
            || !Enum.IsDefined(ownerType)
            || !Guid.TryParse(client.OwnerId, out Guid ownerId) || ownerId == Guid.Empty)
            return null;
        return client;
    }

    private static ApiKeyPrincipalContext Principal(ApiKeyClientDescriptor client) =>
        new(client.KeyId, client.TenantId,
            Enum.Parse<ExternalApiKeyOwnerType>(client.OwnerType, ignoreCase: true),
            Guid.Parse(client.OwnerId), Array.AsReadOnly(client.Scopes.ToArray()))
        {
            CredentialSource = ApiKeyCredentialSource.Configured
        };

    private static bool SamePrincipal(ApiKeyPrincipalContext left, ApiKeyPrincipalContext right) =>
        left.CredentialSource == ApiKeyCredentialSource.Configured
        && right.CredentialSource == ApiKeyCredentialSource.Configured
        && string.Equals(left.KeyId, right.KeyId, StringComparison.Ordinal)
        && left.TenantId == right.TenantId && left.OwnerType == right.OwnerType && left.OwnerId == right.OwnerId
        && left.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).SequenceEqual(
                right.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope)).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private sealed class SourceLease : IConfiguredApiKeyResourceAuthorityLease
    {
        private static readonly TimeSpan MaximumTimerDueTime = TimeSpan.FromMilliseconds(uint.MaxValue - 1L);
        private readonly Lock gate = new();
        private readonly IOptionsMonitor<ApiKeyAuthenticationOptions> options;
        private readonly ApiKeyAuthenticationOptions source;
        private readonly ApiKeyClientDescriptor client;
        private readonly string secretHash;
        private readonly TimeProvider clock;
        private readonly long acquiredTimestamp;
        private readonly TimeSpan? remainingAtAcquisition;
        private readonly CancellationTokenSource validity = new();
        private readonly ITimer? timer;
        private readonly IDisposable? changes;
        private bool active = true;

        internal SourceLease(IOptionsMonitor<ApiKeyAuthenticationOptions> options,
            ApiKeyAuthenticationOptions source, ApiKeyClientDescriptor client,
            ApiKeyPrincipalContext principal, TimeProvider clock, long timestamp, DateTimeOffset observedAt,
            string verifiedHash, DateTimeOffset? expiresAtUtc)
        {
            this.options = options;
            this.source = source;
            this.client = client;
            secretHash = verifiedHash;
            this.clock = clock;
            acquiredTimestamp = timestamp;
            Principal = principal;
            ExpiresAtUtc = expiresAtUtc?.ToUniversalTime();
            remainingAtAcquisition = ExpiresAtUtc - observedAt;
            ValidityCancellation = validity.Token;
            if (ExpiresAtUtc is not null)
                timer = clock.CreateTimer(static state => ((SourceLease)state!).ScheduleExpiry(),
                    this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            changes = options.OnChange((_, name) =>
            {
                if (string.Equals(name, ApiAuthenticationSchemeNames.ApiKey, StringComparison.Ordinal))
                    Invalidate();
            });
            ScheduleExpiry();
        }

        public ApiKeyPrincipalContext Principal { get; }
        public DateTimeOffset? ExpiresAtUtc { get; }
        public CancellationToken ValidityCancellation { get; }

        public ValueTask<bool> RevalidateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate)
            {
                if (!active || ValidityCancellation.IsCancellationRequested) return ValueTask.FromResult(false);
                var now = clock.GetUtcNow();
                var current = options.Get(ApiAuthenticationSchemeNames.ApiKey);
                var candidate = ReadClient(current, Principal.KeyId, now);
                var principal = candidate is null ? null : ConfiguredApiKeyResourceAuthorityReader.Principal(candidate);
                var same = candidate is not null && principal is not null
                    && changes is not null && now.Offset == TimeSpan.Zero
                    && ReferenceEquals(current, source) && ReferenceEquals(candidate, client)
                    && candidate.ExpiresAtUtc == ExpiresAtUtc && candidate.SecretHash == secretHash
                    && SamePrincipal(principal, Principal);
                // Source identity is retained even when replacement metadata happens to be identical.
                if (!same || ValidityCancellation.IsCancellationRequested
                    || Remaining() is { } left && left <= TimeSpan.Zero)
                {
                    validity.Cancel();
                    return ValueTask.FromResult(false);
                }
                return ValueTask.FromResult(true);
            }
        }

        private TimeSpan? Remaining()
        {
            if (ExpiresAtUtc is not { } expiry) return null;
            var absolute = expiry - clock.GetUtcNow();
            var original = remainingAtAcquisition!.Value - clock.GetElapsedTime(acquiredTimestamp);
            return absolute < original ? absolute : original;
        }

        private void ScheduleExpiry()
        {
            lock (gate)
            {
                if (!active || ValidityCancellation.IsCancellationRequested || timer is null) return;
                var remaining = Remaining()!.Value;
                if (remaining <= TimeSpan.Zero
                    || !timer.Change(remaining > MaximumTimerDueTime ? MaximumTimerDueTime : remaining,
                        Timeout.InfiniteTimeSpan))
                    validity.Cancel();
            }
        }

        private void Invalidate()
        {
            lock (gate)
                if (active) validity.Cancel();
        }

        public async ValueTask DisposeAsync()
        {
            lock (gate)
            {
                if (!active) return;
                active = false;
                validity.Cancel();
            }
            changes?.Dispose();
            if (timer is not null) await timer.DisposeAsync();
            validity.Dispose();
        }

        public override string ToString() => nameof(SourceLease);
    }
}
