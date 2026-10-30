using Explore.Persistence.Services;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Application.Configuration;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Explore.Persistence.Database;

public sealed class InstanceApiKeyExecutionScopeFactory(
    IUserContext userContext, IAdminContext adminContext,
    IHttpContextAccessor httpContext, IServiceProvider services) : IInstanceApiKeyExecutionScopeFactory
{
    private async Task<ExploreDbContext> CreateAsync(
        PrimaryDatabaseRole role, Guid principalId, CancellationToken token)
    {
        if (!userContext.IsAuthenticated || principalId == Guid.Empty
            || await adminContext.ResolveUserIdAsync(token) != principalId)
            throw new AuthorizationException("The canonical authenticated actor is required.");
        return await services.GetRequiredService<InstanceApiKeyDbContextFactories>().CreateAsync(role, token);
    }

    public async ValueTask<IInstanceApiKeyManagementScope> CreateReadAsync(
        Guid principalId, Guid? keyId, CancellationToken cancellationToken)
    {
        if (keyId == Guid.Empty)
            throw new ArgumentException("A selected key identity must be nonempty.", nameof(keyId));
        var context = await CreateAsync(PrimaryDatabaseRole.InstanceKeyRead, principalId, cancellationToken);
        return await CreateManagementAsync(context, PrimaryDatabaseRole.InstanceKeyRead, principalId, keyId, cancellationToken);
    }

    public async ValueTask<IInstanceApiKeyManagementScope> CreateWriteAsync(
        Guid principalId, Guid keyId, CancellationToken cancellationToken)
    {
        if (keyId == Guid.Empty)
            throw new ArgumentException("A selected key identity must be nonempty.", nameof(keyId));
        var context = await CreateAsync(PrimaryDatabaseRole.InstanceKeyWrite, principalId, cancellationToken);
        return await CreateManagementAsync(context, PrimaryDatabaseRole.InstanceKeyWrite, principalId, keyId, cancellationToken);
    }

    private async Task<IInstanceApiKeyManagementScope> CreateManagementAsync(ExploreDbContext context,
        PrimaryDatabaseRole role, Guid principalId, Guid? keyId, CancellationToken token)
    {
        try
        {
            var owner = await CreateOwnerAsync(context, role, principalId, keyId, token);
            return new ManagementScope(context, new InstanceApiKeyCapabilityStore(
                context, role, principalId, keyId, null, httpContext), owner);
        }
        catch { await context.DisposeAsync(); throw; }
    }

    public async ValueTask<IInstanceApiKeyIssuanceScope> CreateIssuanceAsync(
        Guid principalId, string operationKey, CancellationToken cancellationToken)
    {
        if (!ExternalApiKeyIssuanceReceipt.IsValidOperationKey(operationKey))
            throw new ArgumentException("A bounded operation key is required.", nameof(operationKey));
        var context = await CreateAsync(PrimaryDatabaseRole.InstanceKeyIssuance, principalId, cancellationToken);
        try
        {
            var privacy = services.GetRequiredService<IPrivacyIdentityFenceAuthority>();
            if (privacy is EmbeddedPrivacyErasureAuthorityRepository)
                privacy = new EmbeddedPrivacyErasureAuthorityRepository(
                    services.GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>(),
                    services.GetRequiredService<TimeProvider>(),
                    services.GetRequiredService<IOptions<PrivacyErasureOptions>>(),
                    services.GetService<EmbeddedPrivacyErasureAuthorityStorage>(), context);
            var store = new InstanceApiKeyCapabilityStore(
                context, PrimaryDatabaseRole.InstanceKeyIssuance, principalId, null, operationKey, httpContext);
            store.RequireCleanWriteScope(null);
            return new IssuanceScope(context, store, privacy);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    public async ValueTask<IPlatformApiKeyUsageScope> CreateReportAsync(
        Guid principalId, CancellationToken cancellationToken)
    {
        var context = await CreateAsync(PrimaryDatabaseRole.PlatformKeyReport, principalId, cancellationToken);
        try
        {
            var owner = await CreateOwnerAsync(context, PrimaryDatabaseRole.PlatformKeyReport, principalId, null, cancellationToken);
            return new ReportScope(context, new InstanceApiKeyCapabilityStore(
                context, PrimaryDatabaseRole.PlatformKeyReport, principalId, null, null, httpContext), owner);
        }
        catch { await context.DisposeAsync(); throw; }
    }

    private async Task<ActionOwner> CreateOwnerAsync(ExploreDbContext context, PrimaryDatabaseRole role,
        Guid principalId, Guid? keyId, CancellationToken token)
    {
        var request = httpContext.HttpContext
            ?? throw new UnauthorizedAccessException("The original platform key request is required.");
        var principal = request.User;
        var machines = services.GetRequiredService<IMachinePrincipalAccessor>();
        bool machine = machines.IsMachineCaller;
        var tenant = services.GetRequiredService<ITenantContextAccessor>();
        Guid? originalTenant = tenant.TenantId;
        void RequireCustody()
        {
            if (!userContext.IsAuthenticated || machines.IsMachineCaller != machine || tenant.TenantId != originalTenant
                || !ReferenceEquals(httpContext.HttpContext, request) || !ReferenceEquals(request.User, principal))
                throw new UnauthorizedAccessException("Original platform key request custody changed.");
        }
        PlatformApiKeySourceProtocol? sources = null;
        if (!machine)
        {
            var subject = await PolicyMutationExecutionScopeFactory.ResolveSubjectAsync(services, token)
                ?? throw new UnauthorizedAccessException("The original platform key human is required.");
            var lifetime = await PolicyMutationExecutionScopeFactory.ResolveLifetimeAsync(services, token)
                ?? throw new UnauthorizedAccessException("The original validated platform key credential is required.");
            if (subject.PrincipalId != principalId || subject.AccountKey != lifetime.AccountKey
                || !ReferenceEquals(await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                    .ObserveAsync(principal, token), lifetime))
                throw new UnauthorizedAccessException("The original platform key credential belongs to another actor.");
            var forwarding = await HumanSupportForwardingObservation.CaptureAsync(subject,
                services.GetRequiredService<ISupportAccessSessionService>(), tenant, token);
            context.HumanCommitAuditSubject = subject;
            context.HumanCommitContextLease = context.ContextId;
            sources = new PlatformApiKeySourceProtocol(context, role switch
            {
                PrimaryDatabaseRole.InstanceKeyRead => PlatformApiKeySourcePurpose.Read,
                PrimaryDatabaseRole.InstanceKeyWrite => PlatformApiKeySourcePurpose.Write,
                PrimaryDatabaseRole.PlatformKeyReport => PlatformApiKeySourcePurpose.Report,
                _ => throw new InvalidOperationException("The dedicated platform key source purpose is required.")
            }, subject, lifetime, forwarding, services.GetRequiredService<PlatformApiKeySourceOptions>(),
                services.GetRequiredService<InstanceApiKeyDbContextFactories>().SourceEnrollment);
        }
        RequireCustody();
        return new(context, role, keyId, sources, services, RequireCustody);
    }

    /// <summary>
    /// The private issuer is the sole gate to lower effects. Its active admission cannot be
    /// supplied by a caller, copied from another scope, or replaced by transaction/actor flags.
    /// </summary>
    private sealed class ActionOwner(ExploreDbContext context, PrimaryDatabaseRole role, Guid? keyId,
        PlatformApiKeySourceProtocol? sources, IServiceProvider services, Action requireCustody)
    {
        private sealed record Admission(ActionOwner Issuer, InstanceApiKeyAction Action,
            System.Data.Common.DbTransaction Transaction);
        private Admission? active;
        private int consumed;

        internal void Require(InstanceApiKeyAction? effect = null)
        {
            requireCustody();
            if (active is not { } admitted || !ReferenceEquals(admitted.Issuer, this)
                || effect.HasValue && admitted.Action != effect
                || !ReferenceEquals(admitted.Transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                || !RelationalConnectionIdentity.Capture(context).Owns(admitted.Transaction))
                throw new UnauthorizedAccessException("This lower effect requires its actual original action owner.");
        }

        internal async Task<T> ExecuteAsync<T>(InstanceApiKeyAction action,
            Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(operation);
            if (Interlocked.Exchange(ref consumed, 1) != 0)
                throw new InvalidOperationException("A platform key scope belongs to one original attempt.");
            if (!(role == PrimaryDatabaseRole.InstanceKeyRead && action == InstanceApiKeyAction.Read
                || role == PrimaryDatabaseRole.InstanceKeyWrite && action is InstanceApiKeyAction.Update or InstanceApiKeyAction.Revoke
                || role == PrimaryDatabaseRole.PlatformKeyReport && action == InstanceApiKeyAction.Report))
                throw new UnauthorizedAccessException("The original scope does not admit this action.");
            requireCustody();
            var check = PlatformApiKeyActionPolicy.Request(action, keyId);
            IPreparedPlatformApiKeyPolicy? prepared = null;
            var clock = services.GetRequiredService<TimeProvider>();
            if (sources is not null)
                prepared = await services.GetRequiredService<IPlatformApiKeyPolicyPreparation>()
                    .PrepareAsync(action, keyId, sources.Subject, sources.Lifetime,
                        await sources.PrepareAsync(cancellationToken), cancellationToken);
            else
            {
                // Only the captured authenticated machine branch can reach this path.
                var decision = await services.GetRequiredService<IAuthorizationProvider>().AuthorizeAsync(check, cancellationToken);
                if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                    throw new AuthorizationProviderUnavailableException(check.ResourceKind, check.Action);
                if (!decision.IsAllowed) throw new AuthorizationException(check.ResourceKind, check.Action);
            }
            requireCustody();
            var unit = new EfCoreUnitOfWork(context);
            async Task<T> ExecuteBodyAsync(CancellationToken token)
            {
                requireCustody();
                if (sources is not null)
                {
                    var acquired = await sources.AcquireAsync(token);
                    prepared!.ValidateSources(acquired);
                    prepared.ValidateTime(acquired, clock.GetUtcNow());
                    PlatformApiKeySourceFacts? terminal = null;
                    context.RegisterHumanCommitAdmission(sources.Subject, sources.Lifetime, clock, async finalToken =>
                    {
                        requireCustody();
                        terminal = await sources.ReadOwnedAsync(finalToken);
                        prepared.ValidateSources(terminal);
                        requireCustody();
                    }, now =>
                    {
                        requireCustody();
                        prepared.ValidateTime(terminal
                            ?? throw new InvalidOperationException("Final platform key sources did not complete."), now);
                    });
                }
                active = new(this, action, context.Database.CurrentTransaction!.GetDbTransaction());
                try { return await operation(token); }
                finally { active = null; }
            }
            // No policy preparation or callback replay; the real owner alone classifies settlement.
            return await ((ISerializableAttemptOwner)unit).ExecuteAsync(ExecuteBodyAsync, cancellationToken);
        }
    }

    private sealed class ManagementScope(ExploreDbContext context, InstanceApiKeyCapabilityStore store, ActionOwner owner)
        : IInstanceApiKeyManagementScope
    {
        public Task<T> ExecuteAsync<T>(InstanceApiKeyAction action, Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken) => owner.ExecuteAsync(action, operation, cancellationToken);
        public Task<IReadOnlyList<ExternalApiKey>> ReadAsync(CancellationToken cancellationToken)
        {
            owner.Require();
            return store.ReadAsync(cancellationToken);
        }
        public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken)
        {
            owner.Require(InstanceApiKeyAction.Update);
            return store.NameExistsAsync(name, cancellationToken);
        }
        public Task UpdatePolicyAsync(string name, string scopes, DateTime? expiresAt, DateTime changedAt, CancellationToken cancellationToken)
        {
            owner.Require(InstanceApiKeyAction.Update);
            return store.UpdatePolicyAsync(name, scopes, expiresAt, changedAt, cancellationToken);
        }
        public Task<bool> RevokeAsync(DateTime changedAt, CancellationToken cancellationToken)
        {
            owner.Require(InstanceApiKeyAction.Revoke);
            return store.RevokeAsync(changedAt, cancellationToken);
        }
        public async ValueTask DisposeAsync()
        {
            try { await store.DisposeAsync(); }
            finally { await context.DisposeAsync(); }
        }
    }

    private sealed class IssuanceScope(
        ExploreDbContext context, InstanceApiKeyCapabilityStore store, IPrivacyIdentityFenceAuthority privacy)
        : IInstanceApiKeyIssuanceScope
    {
        public IUnitOfWork UnitOfWork { get; } = new EfCoreUnitOfWork(context);
        public IPrivacyIdentityFenceAuthority PrivacyAuthority => privacy;
        public IExternalApiKeyIssuanceReceiptRepository Receipts => store;
        public Task<bool> IsAuthorizedForCommitAsync(CancellationToken cancellationToken) => store.IsAuthorizedForCommitAsync(cancellationToken);
        public async ValueTask DisposeAsync()
        {
            try { await store.DisposeAsync(); }
            finally { await context.DisposeAsync(); }
        }
    }

    private sealed class ReportScope(ExploreDbContext context, InstanceApiKeyCapabilityStore store, ActionOwner owner) : IPlatformApiKeyUsageScope
    {
        public Task<IReadOnlyList<TenantApiKeyUsageSummary>> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
        {
            if (from > to) throw new ArgumentException("The report date range is invalid.");
            return owner.ExecuteAsync(InstanceApiKeyAction.Report, token =>
            {
                owner.Require(InstanceApiKeyAction.Report);
                return store.ReportAsync(from, to, token);
            }, cancellationToken);
        }
        public async ValueTask DisposeAsync()
        {
            try { await store.DisposeAsync(); }
            finally { await context.DisposeAsync(); }
        }
    }
}
