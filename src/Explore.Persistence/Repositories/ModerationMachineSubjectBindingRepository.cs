using System.Data;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Installs explicit service provenance under native administrator and host fences.
/// Reads never substitute credentials, human owners or the host maintenance principal.
/// </summary>
public sealed class ModerationMachineSubjectBindingRepository(
    ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine,
    IExternalApiKeyRepository apiKeys, IConfiguredApiKeyResourceAuthorityReader configuredKeys,
    TimeProvider clock) : IModerationMachineSubjectBindingRepository
{
    public async Task<ModerationMachineSubjectBindingResult> EstablishAsync(
        ModerationMachineSubjectInstallation installation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (machine.IsMachineCaller)
            return ModerationMachineSubjectBindingResult.Denied();
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not null
            || context.ChangeTracker.Entries().Any() || context.IsTenantFilterBypassed)
            return ModerationMachineSubjectBindingResult.Unavailable();

        var candidate = ModerationMachineSubjectBinding.Establish(
            installation.Id, installation.HostBindingId, installation.CredentialSource, installation.KeyId,
            installation.AuthorityTenantId, installation.OwnerType, installation.OwnerId, installation.ServicePrincipalId);
        ModerationMachineSubjectBindingResult? established = null;
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
                operation: async token =>
                {
                    context.ChangeTracker.Clear();
                    var result = await AcquireAsync(candidate, create: true, token);
                    if (result.Outcome != ModerationAuthoritySourceOutcome.Established)
                        throw new AbortBinding(result);
                    await context.SaveChangesAsync(token);
                    established = result;
                    return result;
                },
                verifySucceeded: async token =>
                {
                    context.ChangeTracker.Clear();
                    await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                    var result = await AcquireAsync(candidate, create: false, token);
                    return established?.Binding is { } expected
                        && result.Binding is { } actual && actual.Id == expected.Id
                        && actual.Revision == expected.Revision;
                },
                isolationLevel: IsolationLevel.Serializable,
                cancellationToken: cancellationToken);
        }
        catch (AbortBinding aborted)
        {
            return aborted.Result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ModerationMachineSubjectBindingResult.Conflict();
        }
        catch (DbUpdateException exception) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(exception))
        {
            // An absent binding cannot be row-fenced. Its unique source slot rejects competing identities.
            return ModerationMachineSubjectBindingResult.Conflict();
        }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }
    }

    public async Task<ModerationMachineSubjectBinding?> ReadCurrentAsync(
        string keyId, ModerationMachineCredentialSource credentialSource,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyId) || !Enum.IsDefined(credentialSource) || context.IsTenantFilterBypassed)
            return null;
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (host is null)
            return null;
        var binding = await context.ModerationMachineSubjectBindings.AsNoTracking().SingleOrDefaultAsync(
            row => row.HostBindingId == host.Id && row.CredentialSource == credentialSource && row.KeyId == keyId,
            cancellationToken);
        if (binding is null || binding.RevokedAt is not null
            || !string.Equals(binding.KeyId, keyId, StringComparison.Ordinal)
            || !await HasCurrentCredentialAsync(binding, cancellationToken)
            || !await HasLiveServiceAsync(binding.ServicePrincipalId, cancellationToken))
            return null;
        return binding;
    }

    private async Task<ModerationMachineSubjectBindingResult> AcquireAsync(
        ModerationMachineSubjectBinding candidate, bool create, CancellationToken token)
    {
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (host is null || host.Id != candidate.HostBindingId)
            return ModerationMachineSubjectBindingResult.Unavailable();
        var scope = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var authority = await new ModerationQualificationAuthority(context, admin, machine).AcquireAsync(scope, token);
        if (authority is null)
            return await admin.ResolveUserIdAsync(token) is null
                ? ModerationMachineSubjectBindingResult.Unauthenticated()
                : ModerationMachineSubjectBindingResult.Denied();
        if (!authority.IsInstanceAdmin || authority.Binding.Id != candidate.HostBindingId)
            return ModerationMachineSubjectBindingResult.Denied();

        await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, candidate.ServicePrincipalId, token);
        var actorIds = await context.Actors.AsNoTracking()
            .Where(row => row.ServicePrincipalId == candidate.ServicePrincipalId)
            .Select(row => row.Id).ToListAsync(token);
        foreach (var actorId in actorIds.Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<Actor>(context, actorId, token);
        if (candidate.CredentialSource == ModerationMachineCredentialSource.Persisted)
        {
            var key = await apiKeys.GetByKeyIdForAuthentication(candidate.KeyId, token);
            if (key is null)
                return ModerationMachineSubjectBindingResult.Unavailable();
            await RelationalEntityRowFence.AcquireGlobalAsync<ExternalApiKey>(context, key.Id, token);
        }
        if (!await HasCurrentCredentialAsync(candidate, token)
            || !await HasLiveServiceAsync(candidate.ServicePrincipalId, token))
            return ModerationMachineSubjectBindingResult.Unavailable();

        var existing = await context.ModerationMachineSubjectBindings.SingleOrDefaultAsync(
            row => row.HostBindingId == candidate.HostBindingId && row.CredentialSource == candidate.CredentialSource
                && row.KeyId == candidate.KeyId, token);
        if (existing is not null)
        {
            if (existing.Id != candidate.Id || existing.RevokedAt is not null
                || !string.Equals(existing.KeyId, candidate.KeyId, StringComparison.Ordinal)
                || existing.AuthorityTenantId != candidate.AuthorityTenantId
                || existing.OwnerType != candidate.OwnerType || existing.OwnerId != candidate.OwnerId
                || existing.ServicePrincipalId != candidate.ServicePrincipalId)
                return ModerationMachineSubjectBindingResult.Conflict();
            return ModerationMachineSubjectBindingResult.Established(existing);
        }
        if (!create || await context.ModerationMachineSubjectBindings.AnyAsync(row => row.Id == candidate.Id, token))
            return ModerationMachineSubjectBindingResult.Conflict();
        context.ModerationMachineSubjectBindings.Add(candidate);
        authority.Epoch.Advance(authority.Epoch.Revision);
        context.ModerationQualificationEpochs.Update(authority.Epoch);
        return ModerationMachineSubjectBindingResult.Established(candidate);
    }

    private async Task<bool> HasCurrentCredentialAsync(ModerationMachineSubjectBinding binding, CancellationToken token)
    {
        var now = clock.GetUtcNow();
        if (binding.CredentialSource == ModerationMachineCredentialSource.Persisted)
        {
            var key = await apiKeys.GetByKeyIdForAuthentication(binding.KeyId, token);
            return key is not null && string.Equals(key.KeyId, binding.KeyId, StringComparison.Ordinal)
                && key.TenantId == binding.AuthorityTenantId && key.OwnerType == binding.OwnerType
                && key.OwnerId == binding.OwnerId
                && key.ExternalApiKeyStatusId is (int)ExternalApiKeyStatusEnum.Active or (int)ExternalApiKeyStatusEnum.PendingRotation
                && (!key.ExpiresAt.HasValue || key.ExpiresAt.Value > now.UtcDateTime);
        }
        var configured = await configuredKeys.ReadAsync(binding.KeyId, now, token);
        return configured is not null && configured.CredentialSource == ApiKeyCredentialSource.Configured
            && string.Equals(configured.KeyId, binding.KeyId, StringComparison.Ordinal)
            && configured.TenantId == binding.AuthorityTenantId && configured.OwnerType == binding.OwnerType
            && configured.OwnerId == binding.OwnerId;
    }

    private async Task<bool> HasLiveServiceAsync(Guid serviceId, CancellationToken token) =>
        await context.ServicePrincipals.AsNoTracking().AnyAsync(row => row.Id == serviceId && !row.IsDeleted, token)
        && !await context.Actors.AsNoTracking().IgnoreQueryFilters(["SoftDelete"])
            .AnyAsync(row => row.ServicePrincipalId == serviceId && (row.IsDeleted || row.IsSuspended), token);

    private sealed class AbortBinding(ModerationMachineSubjectBindingResult result) : Exception
    {
        public ModerationMachineSubjectBindingResult Result { get; } = result;
    }
}
