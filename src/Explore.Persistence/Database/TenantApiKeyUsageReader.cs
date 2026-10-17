using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Authorization;
using Explore.Application.Features.ExternalApiKeys.Requests.Queries;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Explore.Persistence.Security;
using Explore.Application.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database;

public sealed class TenantApiKeyUsageReader(
    ITenantExploreDbContextFactory factory, IServiceScopeFactory scopes, IServiceProvider provider)
    : IExternalApiKeyTenantUsageReader
{
    public async Task<IReadOnlyList<TenantApiKeyUsageSummary>> ReadAsync(
        GetExternalApiKeyUsageReportRequest request, CancellationToken cancellationToken)
    {
        if (request.TenantId is not { } tenantId || tenantId == Guid.Empty || request.From > request.To)
            throw new AuthorizationException("The exact tenant usage report request is required.");
        var subject = await PolicyMutationExecutionScopeFactory.ResolveSubjectAsync(provider, cancellationToken)
            ?? throw new AuthorizationException("The authenticated application user is required.");
        var lifetime = await PolicyMutationExecutionScopeFactory.ResolveLifetimeAsync(provider, cancellationToken);
        await using var scope = scopes.CreateAsyncScope();
        await using var context = await factory.CreateDbContextAsync(tenantId, cancellationToken);
        context.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(tenantId);
        var lease = scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>();
        lease.Bind(context, tenantId);
        lease.BindHuman(subject, HumanAdministratorCommitPurpose.RuntimeTenant,
            context.Database.IsNpgsql() ? PostgresNativeQueueEnrollment.Bind(
                provider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()) : null, lifetime);
        var authority = new ExternalApiKeyIssuanceAuthority(
            context, scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>(), lease.HumanScope!);
        // Source services resolve only after this exact owner has its original subject/lifetime.
        var prepared = await scope.ServiceProvider.GetRequiredService<IHumanApiKeyManagementPreparation>()
            .PrepareAsync(request, subject.PrincipalId, cancellationToken);
        var unit = new EfCoreUnitOfWork(context);
        var repository = new ExternalApiKeyQuotaRepository(context);
        async Task RequireAdministratorAsync(CancellationToken token)
        {
            if (!await ((IInstanceAdministratorCommitAuthority)authority)
                    .IsInstanceAdministratorForCommitAsync(subject.PrincipalId, token)
                && !await ((ITenantAdministratorCommitAuthority)authority)
                    .IsTenantAdministratorForCommitAsync(subject.PrincipalId, tenantId, token))
                throw new AuthorizationException("Current tenant or instance administration is required.");
        }
        async Task<IReadOnlyList<TenantApiKeyUsageSummary>> ReadOwnerAsync(CancellationToken token)
        {
            if (prepared is not null)
                await prepared.AcquireAsync(request, subject.PrincipalId, token);
            else
            {
                // Keep the existing machine path; human selected decisions never run under locks.
                var decision = await scope.ServiceProvider.GetRequiredService<IAuthorizationProvider>()
                    .AuthorizeAsync(new AuthorizationRequest(ResourceKinds.Tenant, tenantId.ToString("D"),
                        AuthorizationActions.View), token);
                if (!decision.IsAllowed)
                {
                    if (decision.ReasonCode == AuthorizationDecisionReasonCodes.ProviderUnavailable)
                        throw new AuthorizationProviderUnavailableException(ResourceKinds.Tenant, AuthorizationActions.View);
                    throw new AuthorizationException(ResourceKinds.Tenant, AuthorizationActions.View);
                }
            }
            await RequireAdministratorAsync(token);
            if (prepared is not null)
                scope.ServiceProvider.GetRequiredService<IHumanActionCommitFinalizer>()
                    .Register(RequireAdministratorAsync, _ => { });
            return await repository.GetUsageByTenant(tenantId, request.From, request.To, token);
        }
        return prepared is null
            ? await unit.ExecuteSerializableAsync(ReadOwnerAsync, cancellationToken)
            : await scope.ServiceProvider.GetRequiredService<ISerializableAttemptOwner>()
                .ExecuteAsync(ReadOwnerAsync, cancellationToken);
    }
}
