using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Infrastructure.Storage;

/// <summary>
/// Retains the actual probe's human and provider custody across source-only transactions.
/// No source lease, generic settings grant, or replacement authority crosses the remote handoff.
/// </summary>
public sealed class StorageProbeOriginalAction(
    IServiceProvider services, IHumanCommitScopePreparation human,
    IHttpContextAccessor contexts, TimeProvider clock, IStorageProbeSourceOwner owner) : IStorageProbeOriginalAction
{
    private int started;
    private object? action;

    public Task<InstanceStorageProviderStatusDto> ExecuteAsync(TestInstanceStorageProviderQuery query, CancellationToken token) =>
        ExecuteOriginalAsync(query, query.UserId, null, token);

    public Task<InstanceStorageProviderStatusDto> ExecuteAsync(TestInstanceStorageProviderDuringSetupQuery query, CancellationToken token) =>
        ExecuteOriginalAsync(query, null, null, token);

    public Task<InstanceStorageProviderStatusDto> ExecuteAsync(TestTenantStorageProviderCommand command, CancellationToken token) =>
        ExecuteOriginalAsync(command, command.UserId, services.GetRequiredService<ITenantContext>().TenantId, token);

    private async Task<InstanceStorageProviderStatusDto> ExecuteOriginalAsync(
        object original, Guid? originalActor, Guid? tenant, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (Interlocked.Exchange(ref started, 1) != 0 || tenant == Guid.Empty)
            throw new UnauthorizedAccessException("Storage probe custody belongs to one original action.");
        action = original;
        // Retain the request before the native owner's initial asynchronous open/admission.
        var request = contexts.HttpContext ?? throw Denied();
        var principal = request.User;
        var tenants = services.GetRequiredService<ITenantContext>();
        Guid ambientTenant = tenants.TenantId;
        if (tenant.HasValue && tenant != ambientTenant) throw Denied();
        string method = request.Request.Method;
        string? path = request.Request.Path.Value;
        await human.PrepareAsync(token);
        var subject = human.Subject ?? throw Denied();
        bool setupBranch = original is TestInstanceStorageProviderDuringSetupQuery;
        if (!setupBranch && (originalActor == Guid.Empty || originalActor != subject.PrincipalId))
            throw Denied();
        await owner.CaptureAsync(tenant, token);
        var observer = services.GetRequiredService<IHumanAuthenticationLifetimeObserver>();
        var lifetime = await observer.ObserveAsync(principal, token) ?? throw Denied();
        if (lifetime.AccountKey != subject.AccountKey || subject.PrincipalId == Guid.Empty)
            throw Denied();
        var sourceSet = services.GetRequiredService<IPolicySourceSetFence>();
        var principalSource = services.GetRequiredService<IHumanPrincipalSourceFence>();
        var supportSource = await services.GetRequiredService<IHumanSupportAccessSourceFactory>().CreateAsync(token);
        var originalPrincipal = await principalSource.ObserveAsync(token);
        var originalSupport = await supportSource.ObserveAsync(token);
        string principalSnapshot = JsonSerializer.Serialize(originalPrincipal);
        string supportSnapshot = JsonSerializer.Serialize(originalSupport);
        var currentSupport = originalSupport;
        Func<CancellationToken, Task>? setupSources = null;
        Action? setupTime = null;

        void RequireRequest()
        {
            token.ThrowIfCancellationRequested();
            request.RequestAborted.ThrowIfCancellationRequested();
            if (!ReferenceEquals(contexts.HttpContext, request) || !ReferenceEquals(request.User, principal)
                || !ReferenceEquals(action, original)
                || !ReferenceEquals(human.Subject, subject) || tenants.TenantId != ambientTenant
                || request.Request.Method != method || request.Request.Path.Value != path)
                throw Denied();
        }
        void RequirePrincipal(HumanPrincipalSourceFacts facts)
        {
            RequireRequest();
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, facts);
            if (facts.SelectedTenantId != tenant || JsonSerializer.Serialize(facts) != principalSnapshot)
                throw Denied();
        }
        void RequireTime()
        {
            RequireRequest();
            if (!lifetime.IsValidAt(clock.GetUtcNow())) throw Denied();
            foreach (var facts in new[] { originalSupport, currentSupport })
            {
                if (!ReferenceEquals(facts.Forwarding.Subject, subject)) throw Denied();
                var support = HumanSupportAccessProjection.Context(facts, clock.GetUtcNow());
                if (support.WasForwarded && (!support.IsActive || !support.AllowsWrites
                    || tenant.HasValue && support.TargetTenantId != tenant))
                    throw Denied();
            }
            setupTime?.Invoke();
        }
        RequirePrincipal(originalPrincipal);
        RequireTime();
        if (setupBranch)
        {
            if (!HttpMethods.IsPost(method)
                || !string.Equals(path, "/api/instance/settings/storage/test", StringComparison.OrdinalIgnoreCase))
                throw Denied();
            var authentication = await request.AuthenticateAsync();
            var ticket = authentication.Ticket;
            if (!authentication.Succeeded || ticket is null || authentication.Principal is null
                || !authentication.Principal.Identities.Any(identity =>
                    identity.IsAuthenticated && principal.Identities.Contains(identity)))
                throw Denied();
            var ticketPrincipal = authentication.Principal;
            var identities = ticketPrincipal.Identities.ToArray();
            var scheme = ticket.AuthenticationScheme;
            var issued = ticket.Properties.IssuedUtc;
            var expires = ticket.Properties.ExpiresUtc;
            var setup = services.GetRequiredService<ISetupSecretProvider>();
            if (request.Request.Headers["X-Setup-Secret"].Count != 1) throw Denied();
            string? secret = request.Request.Headers["X-Setup-Secret"].FirstOrDefault();
            if (await setup.ValidateSecretAsync(secret, token) != SetupSecretValidationOutcome.Accepted)
                throw Denied();
            var states = services.GetRequiredService<IInstanceBootstrapStateRepository>();
            var pending = await states.GetCurrent(token);
            if (pending is { Status: not InstanceBootstrapStatus.Pending }) throw Denied();
            string pendingSnapshot = JsonSerializer.Serialize(pending);
            setupTime = () =>
            {
                if (request.Request.Headers["X-Setup-Secret"].Count != 1
                    || request.Request.Headers["X-Setup-Secret"].FirstOrDefault() != secret
                    || !setup.ValidateSecret(secret))
                    throw Denied();
            };
            setupSources = async ct =>
            {
                var current = await request.AuthenticateAsync();
                if (!current.Succeeded || current.Ticket?.AuthenticationScheme != scheme
                    || !ReferenceEquals(current.Principal, ticketPrincipal)
                    || !identities.SequenceEqual(current.Principal.Identities)
                    || current.Properties?.IssuedUtc != issued || current.Properties?.ExpiresUtc != expires)
                    throw Denied();
                await states.GetCurrentForUpdate(ct);
                if (JsonSerializer.Serialize(await states.GetCurrent(ct)) != pendingSnapshot)
                    throw Denied();
            };
        }

        StorageProbeBinding? binding = null;
        async Task CheckSources(CancellationToken ct)
        {
            RequireRequest();
            if (!ReferenceEquals(await observer.ObserveAsync(principal, ct), lifetime)) throw Denied();
            var exclusion = await sourceSet.AcquireAsync(ct);
            var principalLease = await principalSource.AcquireAsync(exclusion, ct);
            var supportLease = await supportSource.AcquireAsync(exclusion, principalLease, cancellationToken: ct);
            RequirePrincipal(await principalLease.ValidateAsync(ct));
            currentSupport = await supportLease.ValidateAsync(ct);
            if (!ReferenceEquals(currentSupport.Forwarding, originalSupport.Forwarding)
                || JsonSerializer.Serialize(currentSupport) != supportSnapshot)
                throw Denied();
            bool instanceAdmin = await services.GetRequiredService<IInstanceAdministratorCommitAuthority>()
                .IsInstanceAdministratorForCommitAsync(subject.PrincipalId, ct);
            if (setupBranch)
            {
                if (instanceAdmin) throw Denied();
                await setupSources!(ct);
            }
            else if (!instanceAdmin && !(tenant.HasValue
                && await services.GetRequiredService<ITenantAdministratorCommitAuthority>()
                    .IsTenantAdministratorForCommitAsync(subject.PrincipalId, tenant.Value, ct)))
                throw Denied();
            var currentBinding = await StorageProbeBinding.CaptureAsync(services, ambientTenant, ct);
            if (binding is null) binding = currentBinding;
            else binding.RequireSame(currentBinding);
            await exclusion.ValidateAsync(ct);
            RequireRequest();
        }

        // Every check settles under native source locks. No external secret or storage
        // call runs until the physical owner and named-lock wrappers have returned.
        await owner.CheckAsync(CheckSources, RequireTime, token);
        var retained = binding ?? throw Denied();
        var config = await retained.ResolveCredentialsAsync(services, token);
        string provider = await retained.SelectProviderAsync(services, tenant, config, token);
        async Task Handoff(CancellationToken ct)
        {
            owner.RequireIdle();
            await retained.RequireCredentialsAsync(services, config, ct);
            await owner.CheckAsync(CheckSources, RequireTime, ct);
            owner.RequireIdle();
            RequireTime();
        }
        await Handoff(token);
        var result = await StorageProbeTransport.TestAsync(
            provider, config, services.GetRequiredService<IStorageProbeClientFactory>(), Handoff,
            () => { owner.RequireIdle(); RequireTime(); }, token);
        // Even bounded failures are results of the original action; never return them
        // after replacing its principal, setup generation, binding or credential.
        await Handoff(token);
        owner.RequireIdle();
        RequireTime();
        return result;
    }

    private static UnauthorizedAccessException Denied() =>
        new("The original storage probe authority is no longer valid.");
}

public static class StorageProbeOriginalActionRegistration
{
    public static IServiceCollection AddStorageProbeOriginalAction(this IServiceCollection services)
    {
        services.TryAddScoped<IStorageProbeOriginalAction, StorageProbeOriginalAction>();
        services.TryAddSingleton<IStorageProbeClientFactory, StorageProbeClientFactory>();
        return services;
    }
}
