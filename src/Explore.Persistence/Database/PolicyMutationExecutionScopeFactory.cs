using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Persistence.Security;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Explore.Persistence.Database;

internal sealed class ExploreDbContextExecutionLease
{
    private RelationalConnectionIdentity? _connection;
    private DbContextId _contextId;
    internal ExploreDbContext? Context { get; private set; }
    internal Guid? SelectedTenantId { get; private set; }
    internal PolicyMutationPurpose? PolicyPurpose { get; private set; }
    internal HumanAdministratorCommitScope? HumanScope { get; private set; }
    internal HumanAuthenticationLifetime? HumanLifetime { get; private set; }
    private HttpContext? _humanRequest;
    private ClaimsPrincipal? _humanPrincipal;
    private ClaimsIdentity[]? _humanIdentities;

    internal void BindOriginalCredential(HttpContext request)
    {
        if (_humanRequest is not null || HumanLifetime is null || HumanScope is null
            || Context is null || Context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Original credential custody requires one validated human binding.");
        _humanRequest = request;
        _humanPrincipal = request.User;
        _humanIdentities = request.User.Identities.ToArray();
        if (!_humanIdentities.Any(identity => identity.IsAuthenticated))
            throw new UnauthorizedAccessException("The original credential has no validating identity.");
    }

    internal void RequireOriginalCredential(HttpContext? request, HumanAuthenticationLifetime lifetime)
    {
        if (request is null || _humanRequest is null || !ReferenceEquals(request, _humanRequest)
            || !ReferenceEquals(request.User, _humanPrincipal) || !ReferenceEquals(lifetime, HumanLifetime)
            || _humanIdentities is null
            || !request.User.Identities.SequenceEqual(_humanIdentities, ReferenceEqualityComparer.Instance)
            || request.User.GetProviderIdentity()?.AccountKey != lifetime.AccountKey
            || HumanScope?.Subject.AccountKey != lifetime.AccountKey)
            throw new UnauthorizedAccessException("The original selected request, principal or credential changed.");
        RequireOriginalOwner(Context ?? throw new UnauthorizedAccessException("The original owner is missing."));
    }

    internal void BindHuman(HumanCommitSubject subject, HumanAdministratorCommitPurpose purpose,
        PostgresNativeQueueEnrollment? enrollment, HumanAuthenticationLifetime? lifetime = null)
    {
        if (Context is null || HumanScope is not null)
            throw new InvalidOperationException("A human commit scope requires one original database lease.");
        if (lifetime is not null && lifetime.AccountKey != subject.AccountKey)
            throw new UnauthorizedAccessException("The validated credential must belong to the resolved human.");
        HumanScope = new(Context, subject, purpose, enrollment);
        HumanLifetime = lifetime;
        Context.HumanCommitAuditSubject = subject;
        Context.HumanCommitContextLease = Context.ContextId;
    }

    internal void Bind(ExploreDbContext context, Guid? selectedTenantId, PolicyMutationPurpose? policyPurpose = null)
    {
        if (Context is not null)
            throw new InvalidOperationException("An execution scope cannot replace its database lease.");
        context.ValidateTenantLease();
        Context = context;
        SelectedTenantId = selectedTenantId;
        PolicyPurpose = policyPurpose;
        _contextId = context.ContextId;
        _connection = context.Database.IsRelational() ? RelationalConnectionIdentity.Capture(context) : null;
    }

    internal void RequireOriginalOwner(ExploreDbContext context)
    {
        context.ValidateTenantLease();
        if (!ReferenceEquals(Context, context) || context.ContextId != _contextId
            || context.TenantFilterTenantId != SelectedTenantId
            || (_connection is not null && !_connection.Matches(context))
            || (_connection is not null && context.Database.CurrentTransaction?.GetDbTransaction() is { } transaction
                && !_connection.Owns(transaction))
            || (HumanScope is { } human && (human.TenantId != SelectedTenantId
                || context.IsTenantFilterBypassed
                || (human.Purpose == HumanAdministratorCommitPurpose.ConfigurationImport)
                    != context.IsConfigurationInstanceImportOwner)))
            throw new InvalidOperationException("An operation must retain its original context, connection and tenant.");
    }
}

internal sealed class PolicyMutationExecutionScopeFactory(
    IServiceProvider provider, IServiceScopeFactory scopes,
    IGlobalExploreDbContextFactory globalFactory, ITenantExploreDbContextFactory tenantFactory,
    ExploreDbContextExecutionLease currentLease) : IPolicyMutationExecutionScopeFactory, IHumanCommitScopePreparation,
        IHumanConfigurationImportOwnerFactory, IMixedSettingsExecutionScopeAccess,
        IHumanConfigurationImportPublicationFactory, IHumanOriginalCredentialCustody
{
    public HumanCommitSubject? Subject => currentLease.HumanScope?.Subject;
    public void RequireOriginalCredential(HumanAuthenticationLifetime lifetime) =>
        currentLease.RequireOriginalCredential(provider.GetService<IHttpContextAccessor>()?.HttpContext, lifetime);

    public Task<IHumanConfigurationImportPublicationStore> ObservePublicationAsync(
        HumanConfigurationImportAuthority authority, Guid operationId, string artifactDigest,
        System.Collections.Immutable.ImmutableArray<HumanConfigurationImportSettingEffect> effects,
        DateTime occurredAt, CancellationToken cancellationToken) =>
        HumanConfigurationImportPublicationStore.ObserveAsync(
            provider.GetRequiredService<ExploreDbContext>(), currentLease, provider,
            authority, operationId, artifactDigest, effects, occurredAt, cancellationToken);

    public IServiceProvider BorrowMixedSettings(Explore.Application.Features.Settings.MixedSettingsBatchMutation mutation)
    {
        mutation.RequireActive();
        currentLease.RequireOriginalOwner(provider.GetRequiredService<ExploreDbContext>());
        provider.GetRequiredService<Explore.Application.Features.EmailDispatch.IHumanEmailMutationPreparation>()
            .RequireMixedSource(mutation.Source);
        if (currentLease.PolicyPurpose != PolicyMutationPurpose.MixedSettings
            || currentLease.SelectedTenantId != mutation.TenantId
            || !ReferenceEquals(currentLease.HumanScope?.Subject, mutation.Source.Subject))
            throw new UnauthorizedAccessException("A joined mixed writer cannot select another physical owner.");
        return provider;
    }

    public IHumanConfigurationImportSourceReader GetImportSourceReader()
    {
        var context = provider.GetRequiredService<ExploreDbContext>();
        currentLease.RequireOriginalOwner(context);
        return new ConfigurationImportHumanSourceReader(context);
    }

    public IOriginalOwnerSettlementBoundary GetImportSettlementBoundary(HumanAuthenticationLifetime lifetime)
    {
        var context = provider.GetRequiredService<ExploreDbContext>();
        currentLease.RequireOriginalOwner(context);
        if (!ReferenceEquals(currentLease.HumanLifetime, lifetime))
            throw new UnauthorizedAccessException("Import settlement requires original credential custody.");
        if (context.Database.IsSqlite()
            && PrivacyErasureDurabilityOptions.GetTopology(provider.GetRequiredService<IConfiguration>())
                == PrivacyErasureAuthorityTopology.CoLocated)
            return provider.GetRequiredService<IPrivacyErasureAuthority>() as IOriginalOwnerSettlementBoundary
                ?? throw new InvalidOperationException("Co-located SQLite import requires its actual outer identity owner.");
        return provider.GetRequiredService<IOriginalOwnerSettlementBoundary>();
    }

    public async ValueTask<IPolicyMutationExecutionScope> CreateFreshImportOwnerAsync(
        HumanConfigurationImportAuthority original, CancellationToken cancellationToken)
    {
        var previous = provider.GetRequiredService<ExploreDbContext>();
        currentLease.RequireOriginalOwner(previous);
        RequireOriginalCredential(original.Lifetime);
        if (previous.Database.CurrentTransaction is not null
            || !ReferenceEquals(currentLease.HumanScope?.Subject, original.Subject)
            || !ReferenceEquals(currentLease.HumanLifetime, original.Lifetime)
            || currentLease.SelectedTenantId != original.Target.TenantId
            || previous.IsConfigurationInstanceImportOwner != (original.Target.Scope == ConfigurationImportScope.Instance))
            throw new InvalidOperationException("Fresh evidence requires the settled original import selection.");
        var scope = scopes.CreateAsyncScope();
        ExploreDbContext? context = null;
        bool captured = false;
        try
        {
            context = original.Target.TenantId is { } tenant
                ? await tenantFactory.CreateDbContextAsync(tenant, cancellationToken)
                : await globalFactory.CreateDbContextAsync(cancellationToken);
            if (original.Target.Scope == ConfigurationImportScope.Instance)
                context.BindConfigurationInstanceImportOwner();
            context.CurrentUserService = provider.GetService<ICurrentUserService>();
            if ((original.Target.TenantId ?? original.Forwarding.ResolvedTenantId) is { } tenantId)
                scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
            var lease = scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.Bind(context, original.Target.TenantId);
            lease.BindHuman(original.Subject, original.Target.Scope == ConfigurationImportScope.Instance
                    ? HumanAdministratorCommitPurpose.ConfigurationImport : HumanAdministratorCommitPurpose.RuntimeTenant,
                context.Database.IsNpgsql()
                    ? PostgresNativeQueueEnrollment.Bind(provider.GetRequiredService<IConfiguration>()) : null,
                original.Lifetime);
            lease.BindOriginalCredential(provider.GetRequiredService<IHttpContextAccessor>().HttpContext
                ?? throw new UnauthorizedAccessException("The original authenticated request is missing."));
            _ = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            captured = true;
            return new OwnedScope(scope);
        }
        catch
        {
            if (!captured && context is not null)
                await context.DisposeAsync();
            await scope.DisposeAsync();
            throw;
        }
    }

    public async ValueTask PrepareAsync(CancellationToken cancellationToken)
    {
        var context = provider.GetRequiredService<ExploreDbContext>();
        if (currentLease.Context is not null)
            currentLease.RequireOriginalOwner(context);
        if (currentLease.HumanScope is not null)
            return;
        var principal = provider.GetService<IHttpContextAccessor>()?.HttpContext?.User;
        if (principal is null || (principal.GetProviderIdentity() is null && principal.GetPlatformUserId() is null))
            return;
        if (context.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Trusted human identity must be bound before the owning transaction.");

        var subject = await ResolveSubjectAsync(provider, cancellationToken);
        if (subject is null)
            return;
        if (currentLease.Context is null)
            currentLease.Bind(context, context.BindTenantLease());
        var purpose = context.IsConfigurationInstanceImportOwner
            ? HumanAdministratorCommitPurpose.ConfigurationImport
            : currentLease.PolicyPurpose == PolicyMutationPurpose.MixedSettings
                ? currentLease.SelectedTenantId.HasValue
                    ? HumanAdministratorCommitPurpose.MixedSettingsTenant : HumanAdministratorCommitPurpose.MixedSettingsInstance
            : currentLease.PolicyPurpose == PolicyMutationPurpose.EmailDelivery
                ? currentLease.SelectedTenantId.HasValue
                    ? HumanAdministratorCommitPurpose.EmailTenant : HumanAdministratorCommitPurpose.EmailInstance
                : currentLease.SelectedTenantId.HasValue
                    ? HumanAdministratorCommitPurpose.RuntimeTenant : HumanAdministratorCommitPurpose.GlobalInstance;
        currentLease.BindHuman(subject, purpose, context.Database.IsNpgsql()
            ? PostgresNativeQueueEnrollment.Bind(provider.GetRequiredService<IConfiguration>()) : null,
            await ResolveLifetimeAsync(provider, cancellationToken));
        if (currentLease.HumanLifetime is not null)
            currentLease.BindOriginalCredential(provider.GetRequiredService<IHttpContextAccessor>().HttpContext
                ?? throw new UnauthorizedAccessException("The original authenticated request is missing."));
    }

    public async ValueTask<IPolicyMutationExecutionScope> CreateAsync(
        PolicyMutationScope selection, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(selection.Purpose))
            throw new ArgumentOutOfRangeException(nameof(selection));
        if (currentLease.Context is not null)
        {
            currentLease.RequireOriginalOwner(provider.GetRequiredService<ExploreDbContext>());
            if (currentLease.SelectedTenantId != selection.TenantId
                || (currentLease.PolicyPurpose ?? PolicyMutationPurpose.Standard) != selection.Purpose)
                throw new InvalidOperationException("A policy operation cannot change its owning database authority.");
            return new BorrowedScope(provider);
        }

        if (provider.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Mutation purpose must be selected before the owning transaction.");

        var originalRequest = provider.GetService<IHttpContextAccessor>()?.HttpContext;
        var originalPrincipal = originalRequest?.User;
        Guid? originalAmbientTenant = provider.GetService<ITenantContextAccessor>()?.TenantId;
        var subject = await ResolveSubjectAsync(provider, cancellationToken);
        var lifetime = subject is null ? null : await ResolveLifetimeAsync(provider, cancellationToken);
        if (selection.Purpose == PolicyMutationPurpose.MixedSettings
            && (subject is null || lifetime is null || selection.TenantId == Guid.Empty
                || selection.TenantId.HasValue && selection.TenantId != originalAmbientTenant
                || !ReferenceEquals(originalRequest, provider.GetService<IHttpContextAccessor>()?.HttpContext)
                || !ReferenceEquals(originalPrincipal, originalRequest?.User)
                || originalAmbientTenant != provider.GetService<ITenantContextAccessor>()?.TenantId))
            throw new UnauthorizedAccessException("Mixed settings require original target, request and credential custody before selection.");

        var scope = scopes.CreateAsyncScope();
        ExploreDbContext? context = null;
        bool captured = false;
        try
        {
            context = selection.Purpose == PolicyMutationPurpose.MixedSettings
                ? await (globalFactory as IMixedSettingsDbContextFactory
                    ?? throw new NotSupportedException("The selected Global factory has no mixed settings capability."))
                    .CreateMixedSettingsAsync(selection.TenantId, cancellationToken)
                : selection.Purpose == PolicyMutationPurpose.EmailDelivery
                ? await provider.GetRequiredService<EmailPolicyMutationDbContextFactories>().CreateAsync(selection, cancellationToken)
                : selection.TenantId is { } target
                    ? await tenantFactory.CreateDbContextAsync(target, cancellationToken)
                    : await globalFactory.CreateDbContextAsync(cancellationToken);
            context.CurrentUserService = provider.GetService<ICurrentUserService>();
            var parentTenant = provider.GetService<ITenantContextAccessor>();
            Guid? tenantFacts = selection.TenantId ?? (selection.Purpose == PolicyMutationPurpose.MixedSettings
                ? originalAmbientTenant : parentTenant?.TenantId);
            if (selection.Purpose == PolicyMutationPurpose.MixedSettings
                && (!ReferenceEquals(originalRequest, provider.GetService<IHttpContextAccessor>()?.HttpContext)
                    || !ReferenceEquals(originalPrincipal, originalRequest?.User)
                    || originalAmbientTenant != parentTenant?.TenantId))
                throw new UnauthorizedAccessException("The original mixed request changed during physical selection.");
            if (tenantFacts is { } tenantId)
                scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
            var lease = scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
            lease.Bind(context, selection.TenantId, selection.Purpose);
            if (subject is not null)
            {
                var purpose = selection.Purpose switch
                {
                    PolicyMutationPurpose.Standard => selection.TenantId.HasValue
                        ? HumanAdministratorCommitPurpose.RuntimeTenant : HumanAdministratorCommitPurpose.GlobalInstance,
                    PolicyMutationPurpose.EmailDelivery => selection.TenantId.HasValue
                        ? HumanAdministratorCommitPurpose.EmailTenant : HumanAdministratorCommitPurpose.EmailInstance,
                    PolicyMutationPurpose.MixedSettings => selection.TenantId.HasValue
                        ? HumanAdministratorCommitPurpose.MixedSettingsTenant : HumanAdministratorCommitPurpose.MixedSettingsInstance,
                    _ => throw new ArgumentOutOfRangeException(nameof(selection))
                };
                lease.BindHuman(subject, purpose, context.Database.IsNpgsql()
                    ? PostgresNativeQueueEnrollment.Bind(provider.GetRequiredService<IConfiguration>()) : null, lifetime);
                if (lifetime is not null)
                    lease.BindOriginalCredential(provider.GetRequiredService<IHttpContextAccessor>().HttpContext
                        ?? throw new UnauthorizedAccessException("The original authenticated request is missing."));
            }
            _ = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            captured = true;
            return new OwnedScope(scope);
        }
        catch
        {
            if (!captured && context is not null)
                await context.DisposeAsync();
            await scope.DisposeAsync();
            throw;
        }
    }

    internal static async Task<HumanCommitSubject?> ResolveSubjectAsync(
        IServiceProvider services, CancellationToken cancellationToken)
    {
        if (services.GetRequiredService<ExploreDbContextExecutionLease>().HumanScope is { } existing)
            return existing.Subject;
        var principal = services.GetService<IHttpContextAccessor>()?.HttpContext?.User;
        if (principal is null || (principal.GetProviderIdentity() is null && principal.GetPlatformUserId() is null))
            return null;
        if (services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Trusted human identity must be resolved before the owning transaction.");
        return await HumanCommitSubject.ResolveAsync(principal,
            services.GetRequiredService<IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>>(),
            cancellationToken);
    }

    internal static async ValueTask<HumanAuthenticationLifetime?> ResolveLifetimeAsync(
        IServiceProvider services, CancellationToken cancellationToken)
    {
        if (services.GetRequiredService<ExploreDbContextExecutionLease>().HumanLifetime is { } retained)
        {
            services.GetRequiredService<ExploreDbContextExecutionLease>().RequireOriginalCredential(
                services.GetService<IHttpContextAccessor>()?.HttpContext, retained);
            return retained;
        }
        var observer = services.GetService<IHumanAuthenticationLifetimeObserver>();
        var principal = services.GetService<IHttpContextAccessor>()?.HttpContext?.User;
        return observer is not null && principal is not null
            ? await observer.ObserveAsync(principal, cancellationToken) : null;
    }

    private sealed class OwnedScope(AsyncServiceScope scope) : IPolicyMutationExecutionScope
    {
        public IServiceProvider Services => scope.ServiceProvider;
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }

    /// <summary>The existing owner, not a nested operation, disposes the lease.</summary>
    private sealed class BorrowedScope(IServiceProvider services) : IPolicyMutationExecutionScope
    {
        public IServiceProvider Services => services;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
