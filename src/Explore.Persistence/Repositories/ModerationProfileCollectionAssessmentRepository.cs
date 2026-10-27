using System.Data;
using System.Data.Common;
using System.Security.Claims;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Native finite assessment metadata and exact retained withdrawal. No prerequisite or receipt
/// is emitted. The original privacy owner commits the only transaction.
/// </summary>
public sealed partial class ModerationProfileCollectionAssessmentRepository(
    ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine,
    IHttpContextAccessor http, IPrivacyIdentityFenceAuthority privacy, TimeProvider clock,
    ModerationCapabilityRegistry inventory)
    : IModerationProfileCollectionAssessmentRepository
{
    public async Task<ModerationProfileCollectionAssessmentAdoption?> WithdrawAsync(
        WithdrawModerationProfileCollectionAssessment request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Assessment correction owns its complete clean identity unit of work.");
        if (!context.Database.IsRelational() || context.IsTenantFilterBypassed
            || context.TenantContext is null || context.TenantFilterTenantId is null
            || machine.IsMachineCaller || request.ExpectedAdoptionRevision != 1
            || http.HttpContext?.User.Identity?.IsAuthenticated != true)
            return null;

        var originalTrust = context.ModerationTrustDomainId;
        WithdrawalState? retained = null;
        try
        {
            return await privacy.ExecuteEnrollmentSerializedAsync(async token =>
            {
                token.ThrowIfCancellationRequested();
                if (!context.IdentityFenceOwnsTransaction || context.IdentityFenceTransactionFailed
                    || context.Database.CurrentTransaction is not { } transaction
                    || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
                    || transaction.GetDbTransaction().Connection != context.Database.GetDbConnection())
                    throw new WithdrawalUnavailable();
                context.ChangeTracker.Clear();
                var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
                if (host is null || originalTrust is { } trust && trust != host.TrustDomainId)
                    throw new WithdrawalUnavailable();
                context.ModerationTrustDomainId = host.TrustDomainId;
                var discovered = await context.Set<ModerationProfileCollectionAssessmentAdoption>()
                    .AsNoTracking().SingleOrDefaultAsync(row => row.Id == request.AdoptionId, token);
                if (!MatchesIntent(discovered, request)) throw new WithdrawalUnavailable();
                var principal = http.HttpContext!.User;
                var human = await admin.ResolveUserIdAsync(token);
                if (human is null || principal.Identity?.IsAuthenticated != true)
                    throw new WithdrawalUnavailable();
                var account = principal.GetProviderIdentity()?.AccountKey;
                if (account is null && principal.GetPlatformUserId() != human)
                    throw new WithdrawalUnavailable();

                // Discovery is not authorization. Lock canonical people before subordinate rows,
                // and reread the exact original after native host, tenant and epoch qualification.
                foreach (var id in new[] { human.Value, discovered!.Source.SubjectHumanId }.Distinct().Order())
                    await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, id, token);
                await RelationalEntityRowFence.AcquireGlobalAsync<UserPii>(
                    context, discovered!.Source.SubjectHumanId, token);
                var authority = await new ModerationQualificationAuthority(context, admin, machine)
                    .AcquireProfileAssessmentWithdrawalAsync(discovered, token);
                if (authority is null || authority.UserId != human || authority.Binding.Id != host.Id)
                    throw new WithdrawalUnavailable();
                var loginId = account is null ? null : await context.UserExternalLogins.AsNoTracking()
                    .Where(row => row.UserId == human && row.AuthenticationProviderId == (int)account.ProviderKind
                        && row.ProviderKey == account.Value).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
                if (account is not null && loginId is null) throw new WithdrawalUnavailable();
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileCollectionProcedureSource>(
                    context, discovered.Source.ProcedureSourceId, token);
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileCollectionAssessmentSource>(
                    context, discovered.SourceId, token);
                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileCollectionAssessmentAdoption>(
                    context, discovered.Id, token);
                var now = clock.GetUtcNow();
                if (!ModerationProfileCollectionAssessmentSource.IsInstant(now) || now < discovered.AdoptedAt)
                    throw new WithdrawalUnavailable();
                retained = new(transaction, context.Database.GetDbConnection(), context.TenantContext!,
                    principal, account, loginId, authority, authority.Epoch.Revision, discovered, now);
                if (!await CurrentAsync(retained, false, token)) throw new WithdrawalUnavailable();
                var adoption = await context.Set<ModerationProfileCollectionAssessmentAdoption>()
                    .SingleAsync(row => row.Id == discovered.Id, token);
                adoption.Withdraw(request.ExpectedAdoptionRevision, authority.UserId, now);
                await context.SaveChangesAsync(token);
                if (!await CurrentAsync(retained, true, token)) throw new WithdrawalUnavailable();
                return adoption;
            }, async token =>
            {
                // This callback belongs to the actual outer primary commit, not a nested UoW.
                token.ThrowIfCancellationRequested();
                if (retained is null || !await CurrentAsync(retained, true, token))
                    throw new WithdrawalUnavailable();
            }, cancellationToken);
        }
        catch (WithdrawalUnavailable) { return null; }
        catch (DbUpdateConcurrencyException) { return null; }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = originalTrust;
        }
    }

    private static bool MatchesIntent(ModerationProfileCollectionAssessmentAdoption? adoption,
        WithdrawModerationProfileCollectionAssessment request)
    {
        if (adoption is null) return false;
        try { adoption.ValidatePersistedState(); }
        catch (ArgumentException) { return false; }
        var source = adoption.Source;
        var procedure = source.ProcedureSource;
        return adoption.Revision == 1 && adoption.WithdrawnAt is null
            && adoption.Id == request.AdoptionId && adoption.Generation == request.ExpectedAdoptionGeneration
            && source.Id == request.SourceId && source.Generation == request.ExpectedSourceGeneration
            && source.SubjectHumanId == request.SubjectHumanId && source.Binding.Jurisdiction == request.Jurisdiction
            && procedure.PolicyId == request.PolicyId && procedure.PolicyRevision == request.PolicyRevision
            && procedure.PublicationId == request.PublicationId && procedure.ProfileId == request.ProfileId
            && procedure.ModeCode == (int)request.Mode;
    }

    private async Task<bool> CurrentAsync(WithdrawalState original, bool terminal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!FrameCurrent(original) || machine.IsMachineCaller
            || await admin.ResolveUserIdAsync(token) != original.Authority.UserId
            || !Equals(original.Principal.GetProviderIdentity()?.AccountKey, original.Account)
            || original.Account is null && original.Principal.GetPlatformUserId() != original.Authority.UserId
            || await privacy.IsSubjectFencedAsync(original.Authority.UserId, token)
            || await privacy.IsSubjectFencedAsync(original.Adoption.Source.SubjectHumanId, token))
            return false;
        var authority = original.Authority;
        var subject = original.Adoption.Source.SubjectHumanId;
        if (!await context.Users.AsNoTracking().AnyAsync(row => row.Id == authority.UserId && !row.IsDeleted, token)
            || !await context.Users.AsNoTracking().AnyAsync(row => row.Id == subject && !row.IsDeleted, token)
            || !await context.Set<UserPii>().AsNoTracking().AnyAsync(row => row.UserId == subject, token)
            || !await new ModerationQualificationAuthority(context, admin, machine)
                .ProviderTenantMatchesAsync(authority.Tenant.Id, token)
            || !await context.Tenants.AsNoTracking().AnyAsync(
                row => row.Id == authority.Tenant.Id && row.TenantStatus.IsActiveState, token)
            || !await context.ModerationHostBindings.AsNoTracking().AnyAsync(row =>
                row.Id == authority.Binding.Id && row.TrustDomainId == authority.Binding.TrustDomainId
                && row.SolutionId == authority.Binding.SolutionId && row.InstanceId == authority.Binding.InstanceId
                && row.MaintenanceServicePrincipalId == authority.Binding.MaintenanceServicePrincipalId, token)
            || !await context.ServicePrincipals.AsNoTracking().AnyAsync(
                row => row.Id == authority.Binding.MaintenanceServicePrincipalId && !row.IsDeleted, token)
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row =>
                row.Id == authority.Epoch.Id && row.HostBindingId == authority.Binding.Id
                && row.TenantId == authority.Tenant.Id && row.Revision == original.Epoch, token))
            return false;
        if (original.Account is { } account && !await context.UserExternalLogins.AsNoTracking().AnyAsync(row =>
            row.Id == original.LoginId && row.UserId == authority.UserId
            && row.AuthenticationProviderId == (int)account.ProviderKind && row.ProviderKey == account.Value, token))
            return false;
        var current = await context.Set<ModerationProfileCollectionAssessmentAdoption>()
            .AsNoTracking().SingleOrDefaultAsync(row => row.Id == original.Adoption.Id, token);
        if (current is null) return false;
        try { current.ValidatePersistedState(); }
        catch (ArgumentException) { return false; }
        var prior = original.Adoption;
        return current.Source.Matches(prior.Source) && current.SourceId == prior.SourceId
            && current.SourceRevision == prior.SourceRevision && current.SourceGeneration == prior.SourceGeneration
            && current.AdoptedByHumanId == prior.AdoptedByHumanId && current.AdoptedAt == prior.AdoptedAt
            && current.Generation == prior.Generation && current.ValidUntil == prior.ValidUntil
            && current.Revision == (terminal ? 2 : 1)
            && current.WithdrawnByHumanId == (terminal ? authority.UserId : (Guid?)null)
            && current.WithdrawnAt == (terminal ? original.WithdrawnAt : (DateTimeOffset?)null)
            && await admin.ResolveUserIdAsync(token) == authority.UserId && !machine.IsMachineCaller
            && Equals(original.Principal.GetProviderIdentity()?.AccountKey, original.Account)
            && (original.Account is not null || original.Principal.GetPlatformUserId() == authority.UserId)
            && FrameCurrent(original);
    }

    private bool FrameCurrent(WithdrawalState original) =>
        context.IdentityFenceOwnsTransaction && !context.IdentityFenceTransactionFailed
        && ReferenceEquals(context.Database.CurrentTransaction, original.Transaction)
        && ReferenceEquals(context.Database.GetDbConnection(), original.Connection)
        && ReferenceEquals(original.Transaction.GetDbTransaction().Connection, original.Connection)
        && original.Connection.State == ConnectionState.Open
        && original.Transaction.GetDbTransaction().IsolationLevel == IsolationLevel.Serializable
        && !context.IsTenantFilterBypassed && ReferenceEquals(context.TenantContext, original.TenantContext)
        && context.TenantFilterTenantId == original.Authority.Tenant.Id
        && context.ModerationTrustDomainId == original.Authority.Binding.TrustDomainId
        && ReferenceEquals(http.HttpContext?.User, original.Principal) && original.Principal.Identity?.IsAuthenticated == true
        && ModerationProfileCollectionAssessmentSource.IsInstant(clock.GetUtcNow())
        && clock.GetUtcNow() >= original.WithdrawnAt;

    private sealed record WithdrawalState(
        IDbContextTransaction Transaction, DbConnection Connection, ITenantContext TenantContext,
        ClaimsPrincipal Principal, ProviderAccountKey? Account, Guid? LoginId,
        ModerationSourceAuthority Authority, long Epoch,
        ModerationProfileCollectionAssessmentAdoption Adoption, DateTimeOffset WithdrawnAt);

    private sealed class WithdrawalUnavailable : Exception;
}
