using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database;

internal sealed class PolicyMutationExecutionScopeFactory(
    IServiceProvider provider, IServiceScopeFactory scopes,
    IGlobalExploreDbContextFactory globalFactory, ITenantExploreDbContextFactory tenantFactory,
    ExploreDbContextExecutionLease currentLease) : IPolicyMutationExecutionScopeFactory, IHumanCommitScopePreparation,
    IHumanOriginalCredentialCustody
{
    public HumanCommitSubject? Subject => currentLease.HumanScope?.Subject;
    public void RequireOriginalCredential(HumanAuthenticationLifetime lifetime) =>
        currentLease.RequireOriginalCredential(provider.GetService<IHttpContextAccessor>()?.HttpContext, lifetime);

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
            ? provider.GetRequiredService<PostgresNativeQueueEnrollment>() : null,
            await ResolveLifetimeAsync(provider, cancellationToken));
        if (currentLease.HumanLifetime is not null)
            currentLease.BindOriginalCredential(provider.GetRequiredService<IHttpContextAccessor>().HttpContext
                ?? throw new UnauthorizedAccessException("The original authenticated request is missing."));
    }

    public async ValueTask<IPolicyMutationExecutionScope> CreateAsync(
        PolicyMutationScope selection, CancellationToken cancellationToken)
    {
        if (selection.Purpose != PolicyMutationPurpose.Standard)
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
            context = selection.TenantId is { } target
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
                    ? provider.GetRequiredService<PostgresNativeQueueEnrollment>() : null, lifetime);
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
