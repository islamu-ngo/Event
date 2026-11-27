using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database;

internal sealed class ConfigurationManifestExportExecutor(
    IServiceProvider provider, IServiceScopeFactory scopes, IGlobalExploreDbContextFactory globalFactory,
    IHttpContextAccessor requests, TimeProvider clock) : IConfigurationManifestExportExecutor
{
    public async Task<ConfigurationManifestExportResult> ExportAsync(
        ConfigurationManifestExportView view, ConfigurationManifestExportScope scope, CancellationToken token)
    {
        var request = requests.HttpContext ?? throw new UnauthorizedAccessException("Export requires a human request.");
        var principal = request.User;
        var identities = principal.Identities.ToArray();
        var account = principal.GetProviderIdentity()?.AccountKey
            ?? throw new UnauthorizedAccessException("Export requires an authenticated provider account.");
        var subject = await PolicyMutationExecutionScopeFactory.ResolveSubjectAsync(provider, token);
        RequireOriginalRequest();
        if (subject is null || subject.AccountKey != account)
            throw new UnauthorizedAccessException("Export requires the original account's resolved human subject.");
        var lifetime = await PolicyMutationExecutionScopeFactory.ResolveLifetimeAsync(provider, token)
            ?? throw new UnauthorizedAccessException("Export requires a validated credential lifetime.");
        RequireLiveCredential();

        ConfigurationManifestExportResult result;
        await using (var child = scopes.CreateAsyncScope())
        {
            var services = child.ServiceProvider;
            var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
            var context = await globalFactory.CreateDbContextAsync(token);
            try
            {
                RequireLiveCredential();
                lease.Bind(context, null);
                lease.BindHuman(subject, HumanAdministratorCommitPurpose.ConfigurationManifestExport,
                    context.Database.IsNpgsql()
                        ? PostgresNativeQueueEnrollment.Bind(provider.GetRequiredService<IConfiguration>()) : null,
                    lifetime);
                lease.BindOriginalCredential(request);
                lease.RequireOriginalCredential(requests.HttpContext, lifetime);
                if (!ReferenceEquals(context, services.GetRequiredService<ExploreDbContext>()))
                    throw new InvalidOperationException("Export services must use the selected Global context.");
            }
            catch
            {
                // Until resolved by the child, the factory lease is ours to release.
                await context.DisposeAsync();
                throw;
            }

            var authority = services.GetRequiredService<IInstanceAdministratorCommitAuthority>();
            var reader = services.GetRequiredService<ConfigurationManifestCurrentStateReader>();
            result = await services.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(async attemptToken =>
            {
                var transaction = context.Database.CurrentTransaction?.GetDbTransaction()
                    ?? throw new InvalidOperationException("Export requires the child's active transaction.");
                await RequireAdministratorAsync();
                var buffered = await reader.ReadInCurrentTransactionAsync(view, scope, attemptToken);
                await RequireAdministratorAsync();
                return buffered;

                async Task RequireAdministratorAsync()
                {
                    RequireAttempt();
                    if (!await authority.IsInstanceAdministratorForCommitAsync(subject.PrincipalId, attemptToken))
                        throw new UnauthorizedAccessException("Export requires current instance administrator authority.");
                    RequireAttempt();
                }

                void RequireAttempt()
                {
                    RequireLiveCredential();
                    lease.RequireOriginalCredential(requests.HttpContext, lifetime);
                    if (lease.HumanScope is not { Purpose: HumanAdministratorCommitPurpose.ConfigurationManifestExport,
                            TenantId: null } human || !ReferenceEquals(human.Subject, subject)
                        || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction))
                        throw new InvalidOperationException("Export must retain its original owner and transaction.");
                    human.RequireOwner(context);
                }
            }, token);
            lease.RequireOriginalCredential(requests.HttpContext, lifetime);
        }

        // Child disposal can also suspend. Do not disclose on an expired or replaced request.
        RequireLiveCredential();
        return result;

        void RequireOriginalRequest()
        {
            if (!ReferenceEquals(requests.HttpContext, request) || !ReferenceEquals(request.User, principal)
                || !principal.Identities.SequenceEqual(identities, ReferenceEqualityComparer.Instance)
                || principal.GetProviderIdentity()?.AccountKey != account)
                throw new UnauthorizedAccessException("The original export credential changed.");
        }

        void RequireLiveCredential()
        {
            RequireOriginalRequest();
            if (lifetime.AccountKey != account || !lifetime.IsValidAt(clock.GetUtcNow()))
                throw new UnauthorizedAccessException("Export requires the original live credential.");
        }
    }
}
