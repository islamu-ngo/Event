using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Domain.Enums;
using Explore.Infrastructure.SupportAccess;
using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Infrastructure.Storage;

/// <summary>
/// Retains original authentication for the actual accounting action. Human administrator
/// and accepted setup custody remain disjoint; neither path borrows a settings issuer.
/// </summary>
public sealed class StorageAccountingOriginalAuthority(
    IServiceProvider services, IHumanCommitScopePreparation human,
    IHttpContextAccessor contexts, TimeProvider clock) : IStorageAccountingOriginalAuthority
{
    public async Task<IStorageAccountingOriginalAdmission> PrepareAsync(
        object command, CancellationToken cancellationToken)
    {
        if (command is not (RecalculateInstanceStorageUsageCommand or RecalculateInstanceStorageUsageDuringSetupCommand))
            throw new UnauthorizedAccessException("Only the actual storage accounting action has accounting custody.");
        await human.PrepareAsync(cancellationToken);
        var owner = services.GetRequiredService<IStorageAccountingOriginalOwner>().Capture();
        var checks = command is RecalculateInstanceStorageUsageCommand administrator
            ? await CaptureHumanAsync(administrator.UserId, requireAdministrator: true, cancellationToken)
            : await CaptureSetupAsync(cancellationToken);
        return new Admission(command, owner, checks.Acquire, checks.Sources, checks.Time);
    }

    private async Task<SourceChecks> CaptureHumanAsync(
        Guid actor, bool requireAdministrator, CancellationToken token)
    {
        var subject = human.Subject ?? throw new UnauthorizedAccessException("Accounting requires its original human.");
        if (actor == Guid.Empty || actor != subject.PrincipalId)
            throw new UnauthorizedAccessException("Accounting requires its original nonempty authenticated actor.");
        var request = contexts.HttpContext ?? throw new UnauthorizedAccessException("The original accounting request is absent.");
        var principal = request.User;
        var observer = services.GetRequiredService<IHumanAuthenticationLifetimeObserver>();
        var lifetime = await observer.ObserveAsync(principal, token)
            ?? throw new UnauthorizedAccessException("Accounting requires original credential lifetime evidence.");
        if (lifetime.AccountKey != subject.AccountKey)
            throw new UnauthorizedAccessException("The accounting actor and credential disagree.");
        var tenants = services.GetRequiredService<ITenantContext>();
        Guid ambientTenant = tenants.TenantId;
        // All source ports bind the selected idle owner, before BEGIN. This command has
        // no ISecureRequest annotation: no route/PDP decision is manufactured for it.
        var sourceSet = services.GetRequiredService<IPolicySourceSetFence>();
        var principalSource = services.GetRequiredService<IHumanPrincipalSourceFence>();
        var supportSource = await services.GetRequiredService<IHumanSupportAccessSourceFactory>().CreateAsync(token);
        var administrator = requireAdministrator ? services.GetRequiredService<IInstanceAdministratorCommitAuthority>() : null;
        var originalPrincipal = await principalSource.ObserveAsync(token);
        var originalSupport = await supportSource.ObserveAsync(token);

        void RequireRequest()
        {
            request.RequestAborted.ThrowIfCancellationRequested();
            if (!ReferenceEquals(contexts.HttpContext, request) || !ReferenceEquals(request.User, principal)
                || !ReferenceEquals(human.Subject, subject) || tenants.TenantId != ambientTenant)
                throw new UnauthorizedAccessException("The original accounting request or human selection changed.");
        }
        void RequirePrincipal(HumanPrincipalSourceFacts facts)
        {
            RequireRequest();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, facts);
            bool isAdministrator = facts.PlatformGrants.Any(grant => facts.Roles.Any(role =>
                role.Id == grant.RoleId && role.RoleScopeId == (int)RoleScopeEnum.Platform
                && role.MatchesPlatformAdministratorCode));
            if (facts.SelectedTenantId is not null || isAdministrator != requireAdministrator)
                throw new UnauthorizedAccessException("Accounting must retain its original Global authority branch.");
        }
        void RequireTime(HumanSupportAccessSourceFacts current)
        {
            RequireRequest();
            var now = clock.GetUtcNow();
            if (!lifetime.IsValidAt(now) || !ReferenceEquals(originalSupport.Forwarding.Subject, subject)
                || !ReferenceEquals(current.Forwarding, originalSupport.Forwarding))
                throw new UnauthorizedAccessException("The accounting credential or support selector changed.");
            // Support is a restriction on the original human, never authority for an
            // instance-wide repair. Retain both snapshots so own writes cannot extend it.
            foreach (var facts in new[] { originalSupport, current })
            {
                var support = HumanSupportAccessProjection.Context(facts, now);
                if (support.WasForwarded && (!support.IsActive || !support.AllowsWrites))
                    throw new UnauthorizedAccessException("The original support session no longer permits accounting.");
            }
        }
        RequirePrincipal(originalPrincipal);
        RequireTime(originalSupport);
        IPolicySourceSetLease? exclusion = null;
        IHumanPrincipalSourceLease? principalLease = null;
        IHumanSupportAccessSourceLease? supportLease = null;
        HumanSupportAccessSourceFacts currentSupport = originalSupport;

        async Task RequireSources(CancellationToken cancellationToken)
        {
            RequireRequest();
            if (!ReferenceEquals(await observer.ObserveAsync(principal, cancellationToken), lifetime))
                throw new UnauthorizedAccessException("The original accounting credential receipt changed.");
            await exclusion!.ValidateAsync(cancellationToken);
            RequirePrincipal(await principalLease!.ValidateAsync(cancellationToken));
            currentSupport = await supportLease!.ValidateAsync(cancellationToken);
            if (administrator is not null
                && !await administrator.IsInstanceAdministratorForCommitAsync(subject.PrincipalId, cancellationToken))
                throw new UnauthorizedAccessException("The original accounting administrator was revoked.");
            RequireRequest();
        }
        return new SourceChecks(async cancellationToken =>
        {
            RequireRequest();
            exclusion = await sourceSet.AcquireAsync(cancellationToken);
            principalLease = await principalSource.AcquireAsync(exclusion, cancellationToken);
            supportLease = await supportSource.AcquireAsync(exclusion, principalLease, cancellationToken: cancellationToken);
            await RequireSources(cancellationToken);
            RequireTime(currentSupport);
        }, RequireSources, () => RequireTime(currentSupport));
    }

    private async Task<SourceChecks> CaptureSetupAsync(CancellationToken token)
    {
        var request = contexts.HttpContext ?? throw new UnauthorizedAccessException("Setup accounting requires its original request.");
        var principal = request.User;
        string method = request.Request.Method;
        string? path = request.Request.Path.Value;
        if (!HttpMethods.IsPost(method)
            || !string.Equals(path, "/api/instance/settings/storage/usage/recalculate", StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Setup accounting belongs only to its actual accepted entrypoint.");
        var authentication = await request.AuthenticateAsync();
        var ticket = authentication.Ticket;
        if (!authentication.Succeeded || ticket is null
            || authentication.Principal is null || !authentication.Principal.Identities.Any(identity =>
                identity.IsAuthenticated && principal.Identities.Contains(identity)))
            throw new UnauthorizedAccessException("Accounting setup requires its actual authenticated ticket.");
        var subject = human.Subject
            ?? throw new UnauthorizedAccessException("The accepted setup request has no supported original actor custody.");
        // The existing controller accepts a non-administrator's authenticated request
        // plus an accepted setup secret. Its route is NOT in the setup scheme allowlist.
        // Preserve that real ticket; never promote it to administrator authority.
        var credential = await CaptureHumanAsync(subject.PrincipalId, requireAdministrator: false, token);
        var ticketPrincipal = authentication.Principal;
        var ticketIdentities = ticketPrincipal.Identities.ToArray();
        string scheme = ticket.AuthenticationScheme;
        DateTimeOffset? issued = ticket.Properties.IssuedUtc;
        DateTimeOffset? expires = ticket.Properties.ExpiresUtc;
        var setup = services.GetRequiredService<ISetupSecretProvider>();
        if (request.Request.Headers["X-Setup-Secret"].Count != 1)
            throw new UnauthorizedAccessException("Accounting setup requires its one original accepted secret.");
        string? secret = request.Request.Headers["X-Setup-Secret"].FirstOrDefault();
        if (await setup.ValidateSecretAsync(secret, token) != SetupSecretValidationOutcome.Accepted)
            throw new UnauthorizedAccessException("The original accounting setup secret is not accepted.");
        var states = services.GetRequiredService<IInstanceBootstrapStateRepository>();
        var pending = await states.GetCurrent(token);
        if (pending is { Status: not InstanceBootstrapStatus.Pending })
            throw new UnauthorizedAccessException("Accounting setup requires the original pending state.");
        string snapshot = JsonSerializer.Serialize(pending);
        void RequireRequestAndTime()
        {
            request.RequestAborted.ThrowIfCancellationRequested();
            if (!ReferenceEquals(contexts.HttpContext, request) || !ReferenceEquals(request.User, principal)
                || !ReferenceEquals(human.Subject, subject) || request.Request.Method != method || request.Request.Path.Value != path
                || request.Request.Headers["X-Setup-Secret"].Count != 1
                || request.Request.Headers["X-Setup-Secret"].FirstOrDefault() != secret
                || !setup.ValidateSecret(secret))
                throw new UnauthorizedAccessException("The original accounting setup custody ended.");
            // The authenticated receipt, not mutable ticket properties, owns expiry
            // and the provider's original clock skew.
            credential.Time();
        }
        async Task RequireSources(CancellationToken cancellationToken)
        {
            RequireRequestAndTime();
            await credential.Sources(cancellationToken);
            var currentTicket = await request.AuthenticateAsync();
            if (!currentTicket.Succeeded || currentTicket.Ticket?.AuthenticationScheme != scheme
                || !ReferenceEquals(currentTicket.Principal, ticketPrincipal)
                || !ticketIdentities.SequenceEqual(currentTicket.Principal.Identities)
                || currentTicket.Properties?.IssuedUtc != issued || currentTicket.Properties?.ExpiresUtc != expires)
                throw new UnauthorizedAccessException("The original accounting setup ticket changed.");
            // The existing lock loader tracks rows. A terminal comparison must read
            // persisted state again, including this attempt's raw/native writes.
            await states.GetCurrentForUpdate(cancellationToken);
            var current = await states.GetCurrent(cancellationToken);
            if (current is { Status: not InstanceBootstrapStatus.Pending }
                || JsonSerializer.Serialize(current) != snapshot)
                throw new UnauthorizedAccessException("The original accounting setup generation changed.");
            RequireRequestAndTime();
        }
        RequireRequestAndTime();
        return new SourceChecks(async cancellationToken =>
        {
            await credential.Acquire(cancellationToken);
            await RequireSources(cancellationToken);
        }, RequireSources, RequireRequestAndTime);
    }

    private sealed record SourceChecks(Func<CancellationToken, Task> Acquire,
        Func<CancellationToken, Task> Sources, Action Time);

    private sealed class Admission(object original, IStorageAccountingOwnerLease owner,
        Func<CancellationToken, Task> acquire, Func<CancellationToken, Task> sources, Action time)
        : IStorageAccountingOriginalAdmission
    {
        private int claimed;
        private int disposed;

        public async Task AcquireAsync(object command, CancellationToken cancellationToken)
        {
            if (!ReferenceEquals(command, original) || Volatile.Read(ref disposed) != 0
                || Interlocked.Exchange(ref claimed, 1) != 0)
                throw new UnauthorizedAccessException("Accounting custody belongs to one original action and attempt.");
            try
            {
                owner.Acquire();
                await acquire(cancellationToken);
                owner.Validate();
            }
            catch { Dispose(); throw; }
        }

        private void RequireActive()
        {
            if (Volatile.Read(ref disposed) != 0 || Volatile.Read(ref claimed) != 1)
                throw new UnauthorizedAccessException("The original accounting attempt is not active.");
            owner.Validate();
        }

        public async Task ValidateSourcesAsync(CancellationToken cancellationToken)
        {
            RequireActive();
            await sources(cancellationToken);
            RequireActive();
        }

        public void ValidateTime() { RequireActive(); time(); }
        public void Dispose() => Interlocked.Exchange(ref disposed, 1);
    }
}

public static class StorageAccountingOriginalAuthorityRegistration
{
    public static IServiceCollection AddStorageAccountingOriginalAuthority(this IServiceCollection services)
    {
        services.TryAddScoped<IStorageAccountingOriginalAuthority, StorageAccountingOriginalAuthority>();
        return services;
    }
}
