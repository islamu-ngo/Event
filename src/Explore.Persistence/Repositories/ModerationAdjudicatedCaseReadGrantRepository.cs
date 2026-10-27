using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>Owns finite adjudicated-case permissions, never allegation receipts or correction disclosure.</summary>
public sealed partial class ModerationAdjudicatedCaseReadGrantRepository(
    ExploreDbContext context, IPrivacyIdentityFenceAuthority privacy, IAdminContext identity,
    IMachinePrincipalAccessor machine, TimeProvider clock) : IModerationAdjudicatedCaseReadGrantRepository
{
    public Task<ModerationAdjudicatedCaseReadGrant?> GrantAsync(
        GrantModerationAdjudicatedCaseRead request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(Mode.Grant, request.OperationId, request.CaseId, request.ExpectedCaseRevision,
            request, Guid.Empty, 0, cancellationToken);
    }

    public Task<ModerationAdjudicatedCaseReadGrant?> RevokeAsync(Guid grantId, Guid operationId,
        long expectedRevision, CancellationToken cancellationToken = default) =>
        ExecuteAsync(Mode.Revoke, grantId, Guid.Empty, 0, null, operationId, expectedRevision, cancellationToken);

    public Task<ModerationAdjudicatedCaseReadGrant?> ReadCurrentAsync(Guid grantId, Guid caseId,
        long expectedCaseRevision, CancellationToken cancellationToken = default) =>
        ReadCurrentOwnedAsync(grantId, caseId, expectedCaseRevision, cancellationToken);

    private async Task<ModerationAdjudicatedCaseReadGrant?> ExecuteAsync(Mode mode, Guid id, Guid caseId,
        long caseRevision, GrantModerationAdjudicatedCaseRead? request, Guid revokeOperation, long expectedRevision,
        CancellationToken cancellationToken)
    {
        // Do not flush or discard a caller's pending work while acquiring a different authority.
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Adjudicated-case permission requires its own clean native transaction.");
        if (!context.Database.IsRelational() || context.ModerationTrustDomainId is not null
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId is not { } tenant
            || tenant == Guid.Empty || machine.Current is not null || !Uuid(id)
            || mode != Mode.Revoke && (caseId == Guid.Empty || caseRevision is < 1 or long.MaxValue)
            || mode == Mode.Revoke && (!Uuid(revokeOperation) || revokeOperation == id || expectedRevision != 1))
            return null;
        if (request is not null && (request.GranteeUserId == Guid.Empty || request.Original is null
            || !Uuid(request.Original.EffectId) || !Hash(request.Original.SnapshotHash)
            || !Hash(request.Original.EffectBindingHash) || !Instant(request.ValidUntil))) return null;
        try
        {
            return await EfCoreUnitOfWork.ExecuteBootstrapConflictRetryAsync(async () =>
                await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    var caller = await identity.ResolveUserIdAsync(cancellationToken);
                    if (caller is null || caller == Guid.Empty) throw new Unavailable();
                    context.ChangeTracker.Clear();
                    ModerationAdjudicatedCaseReadGrant? result = null;
                    ModerationSourceAuthority? authority = null;
                    Administrator? administrator = null;
                    CaseCoordinates? heldCase = null;
                    string? saved = null;
                    DateTimeOffset acquiredAt = default;
                    IDisposable? settlement = null;

                    async Task Recheck(CancellationToken token)
                    {
                        if (result is null || authority is null || administrator is null || saved is null) return;
                        context.ValidateModerationReadGrantSettlement();
                        if (Snapshot(result) != saved) throw new Unavailable();
                        var now = clock.GetUtcNow();
                        if (now < acquiredAt || machine.Current is not null || context.TenantFilterTenantId != tenant
                            || context.IsTenantFilterBypassed || await identity.ResolveUserIdAsync(token) != caller
                            || !await new ModerationQualificationAuthority(context, identity, machine).ProviderTenantMatchesAsync(tenant, token)
                            || !await AccountCurrentAsync(caller.Value, token)
                            || !await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenant && row.TenantStatus.IsActiveState, token)
                            || !await context.ModerationHostBindings.AsNoTracking().AnyAsync(row => row.Id == authority.Binding.Id
                                && row.TrustDomainId == authority.Binding.TrustDomainId && row.SolutionId == authority.Binding.SolutionId
                                && row.InstanceId == authority.Binding.InstanceId
                                && row.MaintenanceServicePrincipalId == authority.Binding.MaintenanceServicePrincipalId, token)
                            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row => row.Id == authority.Epoch.Id
                                && row.Revision == authority.Epoch.Revision && row.TenantId == tenant, token)) throw new Unavailable();
                        var actual = await context.Set<ModerationAdjudicatedCaseReadGrant>().AsNoTracking()
                            .SingleOrDefaultAsync(row => row.Id == id, token);
                        if (actual is null || Snapshot(actual) != saved
                            || await AdministratorAsync(caller.Value,
                                administrator.GrantId, administrator.Platform, tenant, token) != administrator)
                            throw new Unavailable();
                        var allegation = await context.ModerationCases.AsNoTracking().SingleOrDefaultAsync(row => row.Id == actual.CaseId, token);
                        if (allegation?.Subject is null || !CaseMatches(allegation, heldCase)
                            || allegation.ScopeTrustDomainId != actual.TrustDomainId
                            || mode != Mode.Revoke && allegation.Revision != actual.CaseRevision) throw new Unavailable();
                        if (mode == Mode.Revoke && await context.Set<ModerationAdjudicatedCaseReadGrant>().AsNoTracking()
                            .AnyAsync(row => row.Id != id && row.RevocationOperationId == revokeOperation, token))
                            throw new Unavailable();
                        if (mode != Mode.Revoke)
                        {
                            if (!await AccountCurrentAsync(actual.RecipientUserId, token)
                                || !await AccountCurrentAsync(actual.IssuerUserId, token)
                                 || allegation.Subject.Kind == ModerationSubjectKind.Human
                                    && !await AccountCurrentAsync(allegation.Subject.Id, token)
                                || !actual.IsCurrentAt(InstantNow())
                                || await ReadOriginalAsync(actual.CaseId, actual.CaseRevision, actual.OriginalEffectId,
                                    actual.OriginalSnapshotHash, actual.OriginalEffectBindingHash, authority, token) != actual.OriginalRetainUntil)
                                throw new Unavailable();
                        }
                        if (clock.GetUtcNow() < acquiredAt || mode != Mode.Revoke && clock.GetUtcNow() >= actual.ExpiresAt)
                            throw new Unavailable();
                    }

                    try
                    {
                        return await privacy.ExecuteEnrollmentSerializedAsync(async privacyToken =>
                            await new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAttemptAsync(async token =>
                            {
                                var plan = await ReadPlanAsync(id, tenant, caseId, token) ?? throw new Unavailable();
                                var existing = plan.Grant;
                                if (mode != Mode.Grant && existing is null || mode == Mode.Grant && existing is not null
                                    && !MatchesRequest(existing, request!, caller.Value)) throw new Unavailable();
                                if (plan.Case.Subject is null) throw new Unavailable();
                                var humans = mode == Mode.Revoke ? new[] { caller.Value }
                                    : new[] { caller.Value, request?.GranteeUserId ?? existing!.RecipientUserId,
                                    existing?.IssuerUserId ?? caller.Value }
                                         .Concat(plan.Case.Subject.Kind == ModerationSubjectKind.Human ? new[] { plan.Case.Subject.Id } : [])
                                        .Distinct().Order().ToArray();
                                await using var installation = await RelationalNamedLock.AcquireTransactionAsync(
                                    context, "moderation.authority-source.installation", token);
                                var instance = AuthorityScope.Instance(AuthorityScope.Solution(
                                    AuthorityScope.TrustDomain(plan.Host.TrustDomainId), plan.Host.SolutionId), plan.Host.InstanceId);
                                var qualification = new ModerationQualificationAuthority(context, identity, machine);
                                authority = await qualification.AcquireAdjudicatedReadAdministrationAsync(
                                    AuthorityScope.Tenant(instance, tenant), humans, token);
                                if (authority is null || authority.UserId != caller || authority.Binding.Id != plan.Host.Id
                                    || authority.Binding.TrustDomainId != plan.Host.TrustDomainId
                                    || authority.Binding.SolutionId != plan.Host.SolutionId || authority.Binding.InstanceId != plan.Host.InstanceId
                                    || authority.Binding.MaintenanceServicePrincipalId != plan.Host.MaintenanceServicePrincipalId)
                                    throw new Unavailable();
                                context.ModerationTrustDomainId = authority.Binding.TrustDomainId;
                                await using var operationFence = await RelationalNamedLock.AcquireTransactionAsync(
                                    context, $"moderation.adjudicated-read:{tenant:N}:{id:N}", token);
                                await using var revocationFence = mode == Mode.Revoke
                                    ? await RelationalNamedLock.AcquireTransactionAsync(context,
                                        $"moderation.adjudicated-read-revocation:{tenant:N}:{revokeOperation:N}", token) : null;
                                if (mode == Mode.Revoke && await context.Set<ModerationAdjudicatedCaseReadGrant>().AsNoTracking()
                                    .AnyAsync(row => row.Id != id && row.RevocationOperationId == revokeOperation, token))
                                    throw new Unavailable();
                                if (existing is not null)
                                    await RelationalEntityRowFence.AcquireAsync<ModerationAdjudicatedCaseReadGrant>(context, tenant, row => row.Id, id, token);
                                var current = await context.Set<ModerationAdjudicatedCaseReadGrant>()
                                    .SingleOrDefaultAsync(row => row.Id == id, token);
                                if (Snapshot(current) != Snapshot(existing)) throw new Unavailable();
                                administrator = await AdministratorAsync(caller.Value,
                                    mode != Mode.Revoke && current is not null ? current.IssuerRoleGrantId : null,
                                    mode != Mode.Revoke && current is not null ? current.IssuerPlatformGrant : null, tenant, token);
                                if (administrator is null || !await AccountCurrentAsync(caller.Value, token)) throw new Unavailable();
                                acquiredAt = clock.GetUtcNow();
                                var targetCase = request?.CaseId ?? current!.CaseId;
                                await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, targetCase, token);
                                var allegation = await context.ModerationCases.AsNoTracking().SingleOrDefaultAsync(row => row.Id == targetCase, token);
                                if (allegation?.Subject is null || !CaseMatches(allegation, plan.Case)
                                    || allegation.ScopeTrustDomainId != authority.Binding.TrustDomainId)
                                    throw new Unavailable();
                                heldCase = plan.Case;
                                if (mode == Mode.Revoke)
                                {
                                    current!.ValidatePersistedState();
                                    if (current.HostBindingId != authority.Binding.Id) throw new Unavailable();
                                    if (current.Revision == 1)
                                        current.Revoke(revokeOperation, expectedRevision, caller.Value, administrator.GrantId,
                                            administrator.Platform, InstantNow());
                                    else if (current.RevocationOperationId != revokeOperation || current.RevokedByUserId != caller
                                        || current.RevokedByRoleGrantId != administrator.GrantId
                                        || current.RevokedByPlatformGrant != administrator.Platform) throw new Unavailable();
                                    result = current;
                                }
                                else
                                {
                                    var revision = request?.ExpectedCaseRevision ?? current!.CaseRevision;
                                    if (allegation.Revision != revision
                                        || !await AccountCurrentAsync(request?.GranteeUserId ?? current!.RecipientUserId, token))
                                        throw new Unavailable();
                                    if (allegation.Subject.Kind == ModerationSubjectKind.Human
                                        && !await AccountCurrentAsync(allegation.Subject.Id, token)) throw new Unavailable();
                                    var effectId = request?.Original.EffectId ?? current!.OriginalEffectId;
                                    var snapshotHash = request?.Original.SnapshotHash ?? current!.OriginalSnapshotHash;
                                    var bindingHash = request?.Original.EffectBindingHash ?? current!.OriginalEffectBindingHash;
                                    var ceiling = await ReadOriginalAsync(targetCase, revision, effectId, snapshotHash,
                                        bindingHash, authority, token) ?? throw new Unavailable();
                                    if (current is null)
                                    {
                                        var issuedAt = InstantNow();
                                        if (request!.ValidUntil <= issuedAt || request.ValidUntil > ceiling) throw new Unavailable();
                                        current = ModerationAdjudicatedCaseReadGrant.Grant(id, authority.Binding.Id,
                                            authority.Binding.TrustDomainId, tenant, targetCase, revision, request.GranteeUserId,
                                            caller.Value, administrator.GrantId, administrator.Platform, effectId,
                                            snapshotHash, bindingHash, ceiling, issuedAt, request.ValidUntil);
                                        context.Add(current);
                                    }
                                    if (current.HostBindingId != authority.Binding.Id || current.OriginalRetainUntil != ceiling
                                        || !current.IsCurrentAt(InstantNow())) throw new Unavailable();
                                    result = current;
                                }
                                saved = Snapshot(result);
                                settlement = context.BindModerationReadGrantSettlement(result!);
                                await context.SaveChangesAsync(token);
                                await Recheck(token);
                                return result;
                            }, Recheck, privacyToken), Recheck, cancellationToken);
                    }
                    finally { settlement?.Dispose(); }
                }), cancellationToken);
        }
        catch (ExploreDbContext.ModerationReadGrantSettlementException) { return null; }
        catch (Unavailable) { return null; }
        catch (DbUpdateConcurrencyException) { return null; }
        catch (DbException) { return null; }
        catch (TimeoutException) { return null; }
        finally { context.ChangeTracker.Clear(); context.ModerationTrustDomainId = null; }
    }

    private async Task<Plan?> ReadPlanAsync(Guid id, Guid tenant, Guid requestedCaseId, CancellationToken token) =>
        await EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context, async planning =>
        {
            var host = await planning.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
            if (host is null) return null;
            var previous = planning.ModerationTrustDomainId;
            try
            {
                planning.ModerationTrustDomainId = host.TrustDomainId;
                var grant = await planning.Set<ModerationAdjudicatedCaseReadGrant>().AsNoTracking()
                    .SingleOrDefaultAsync(row => row.TenantId == tenant && row.Id == id, token);
                var targetCase = grant?.CaseId ?? requestedCaseId;
                var allegation = await planning.ModerationCases.AsNoTracking().Where(row => row.Id == targetCase)
                    .Select(row => new CaseCoordinates(row.Id, row.Revision, row.Subject, row.Scope)).SingleOrDefaultAsync(token);
                return allegation is null ? null : new Plan(host, grant, allegation);
            }
            finally { planning.ModerationTrustDomainId = previous; }
        }, token);

    private async Task<bool> AccountCurrentAsync(Guid human, CancellationToken token) =>
        !await privacy.IsSubjectFencedAsync(human, token)
        && await context.Users.AsNoTracking().AnyAsync(row => row.Id == human && !row.IsDeleted, token)
        && !await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == human, token);

    private async Task<Administrator?> AdministratorAsync(Guid human, Guid? selected, bool? platform,
        Guid tenant, CancellationToken token)
    {
        if (!await AccountCurrentAsync(human, token)) return null;
        if (platform != false)
        {
            var grant = await context.PlatformUserRoles.AsNoTracking().Where(row => row.UserId == human
                && (selected == null || row.Id == selected) && row.RoleId == (int)RoleEnum.Admin
                && row.Role.MasterCode == "platform.admin" && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform)
                .OrderBy(row => row.Id).Select(row => new { row.Id, row.GrantedAt, row.GrantedBy }).FirstOrDefaultAsync(token);
            if (grant is not null) return new(grant.Id, true, grant.GrantedAt.Ticks, grant.GrantedBy);
        }
        if (platform == true) return null;
        var tenantGrant = await context.TenantUserRoleGrants.AsNoTracking().Where(row => row.TenantId == tenant
            && row.TenantUser.UserId == human && (selected == null || row.Id == selected)
            && !row.TenantUser.IsDeleted && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
            && row.RevokedAt == null && row.RoleId == (int)RoleEnum.TenantAdmin
            && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.GrantedAt, row.GrantedBy }).FirstOrDefaultAsync(token);
        return tenantGrant is null ? null : new(tenantGrant.Id, false, tenantGrant.GrantedAt.Ticks, tenantGrant.GrantedBy);
    }

    private static bool MatchesRequest(ModerationAdjudicatedCaseReadGrant grant, GrantModerationAdjudicatedCaseRead request,
        Guid caller) => grant.CaseId == request.CaseId && grant.CaseRevision == request.ExpectedCaseRevision
        && grant.RecipientUserId == request.GranteeUserId && grant.IssuerUserId == caller
        && grant.OriginalEffectId == request.Original.EffectId && grant.OriginalSnapshotHash == request.Original.SnapshotHash
        && grant.OriginalEffectBindingHash == request.Original.EffectBindingHash && grant.ExpiresAt == request.ValidUntil;

    private DateTimeOffset InstantNow() => new(clock.GetUtcNow().UtcTicks / TimeSpan.TicksPerMicrosecond
        * TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
    private static bool Instant(DateTimeOffset value) => value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue
        && value.Offset == TimeSpan.Zero && value.Ticks % TimeSpan.TicksPerMicrosecond == 0;
    private static bool Hash(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigitLower);
    private static bool Uuid(Guid value) => value.Version == 7 && value.Variant is >= 8 and <= 11;
    private static string Snapshot(ModerationAdjudicatedCaseReadGrant? grant) => JsonSerializer.Serialize(grant);
    private static bool CaseMatches(ModerationCase allegation, CaseCoordinates? expected) => expected is not null
        && allegation.Id == expected.Id && allegation.Revision == expected.Revision
        && allegation.Subject == expected.Subject && allegation.Scope == expected.Scope;
    private sealed record CaseCoordinates(Guid Id, long Revision, SubjectReference? Subject, AuthorityScope Scope);
    private sealed record Plan(ModerationHostBinding Host, ModerationAdjudicatedCaseReadGrant? Grant, CaseCoordinates Case);
    private sealed record Administrator(Guid GrantId, bool Platform, long GrantedAtTicks, Guid? GrantedBy);
    private sealed class Unavailable : Exception;
    private enum Mode { Grant, Revoke }
}
