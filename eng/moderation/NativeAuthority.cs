using System.Data;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ISLAMU.Moderation.Measurement;

internal sealed record NativeSnapshot(ModerationConsumerMeasurementChallenge Challenge,
    ModerationMeasurementIssuerEnrollment Issuer, ModerationActivationBinding Binding,
    AuthorityScope Tenant, long QualificationRevision, string Fingerprint, DateTimeOffset ValidUntil);

/// <summary>
/// Read-only preflight/postflight against the selected primary database. A short serializable
/// transaction ends before any child starts. These observations never replace the native
/// accepting writer's current authority, original provenance and final-commit fences.
/// </summary>
internal static class NativeAuthority
{
    internal static ExploreDbContext Context(PrimaryDatabaseConnectionOptions options, Guid tenant)
    {
        if (options.Provider == PrimaryDatabaseProvider.Sqlite)
            Require.That(File.Exists(options.Database), "database-unavailable");
        var builder = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
        builder.ReplaceService<IExecutionStrategyFactory, SingleReadStrategyFactory>();
        if (options.Provider == PrimaryDatabaseProvider.PostgreSql)
            builder.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new(builder.Options) { TenantContext = new BoundTenant(tenant) };
    }

    internal static async Task<NativeSnapshot> ReadAsync(PrimaryDatabaseConnectionOptions options,
        Guid tenantId, Guid challengeId, Guid issuerId, CancellationToken token)
    {
        await using var context = Context(options, tenantId);
        await context.Database.OpenConnectionAsync(token);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        if (options.Provider == PrimaryDatabaseProvider.PostgreSql)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.Transaction = transaction.GetDbTransaction();
            command.CommandText = "SELECT current_setting('app.current_tenant_id', true), rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user";
            command.CommandTimeout = 30;
            await using var reader = await command.ExecuteReaderAsync(token);
            Require.That(await reader.ReadAsync(token) && reader.GetString(0) == tenantId.ToString("D")
                && !reader.GetBoolean(1) && !reader.GetBoolean(2), "database-tenant-authority");
        }
        var host = await context.ModerationHostBindings.AsNoTracking().SingleAsync(token);
        Require.That(await context.ServicePrincipals.AsNoTracking().AnyAsync(
            row => row.Id == host.MaintenanceServicePrincipalId && !row.IsDeleted, token)
            && await context.Tenants.AsNoTracking().AnyAsync(
                row => row.Id == tenantId && row.TenantStatus.IsActiveState, token), "native-host-unavailable");
        var instance = AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(host.TrustDomainId), host.SolutionId), host.InstanceId);
        var tenant = AuthorityScope.Tenant(instance, tenantId);
        var epoch = await context.ModerationQualificationEpochs.AsNoTracking()
            .SingleAsync(row => row.HostBindingId == host.Id && row.TenantId == tenantId, token);
        Require.That(epoch.Revision is > 0 and < long.MaxValue, "native-epoch-invalid");
        // The runtime's mutable moderation trust binding is assembly-internal. This
        // read-only external host replaces ONLY that named filter with its exact
        // independently read host/trust/tenant predicates. All other filters and
        // PostgreSQL RLS stay enabled; no reflection or global bypass is used.
        var challenge = await context.ModerationConsumerMeasurementChallenges.AsNoTracking()
            .IgnoreQueryFilters(["ModerationAuthority"])
            .SingleAsync(row => row.Id == challengeId && row.TenantId == tenantId
                && row.TrustDomainId == host.TrustDomainId && row.HostBindingId == host.Id, token);
        var issuer = await context.ModerationMeasurementIssuerEnrollments.AsNoTracking()
            .IgnoreQueryFilters(["ModerationAuthority"])
            .SingleAsync(row => row.Id == issuerId && row.TenantId == tenantId
                && row.TrustDomainId == host.TrustDomainId && row.HostBindingId == host.Id, token);
        challenge.ValidatePersistedState();
        issuer.ValidatePersistedState();
        var installed = await context.ModerationConsumerInstallations.AsNoTracking()
            .IgnoreQueryFilters(["ModerationAuthority"])
            .Where(row => row.HostBindingId == host.Id && row.TenantId == tenantId && row.TrustDomainId == host.TrustDomainId
                && row.ConsumerId == challenge.ConsumerId && row.ModeCode == challenge.ModeCode)
            .OrderByDescending(row => row.WriterGeneration).FirstAsync(token);
        installed.ValidatePersistedState();
        var now = DateTimeOffset.UtcNow;
        Require.That(challenge.HostBindingId == host.Id && challenge.TrustDomainId == host.TrustDomainId
            && issuer.HostBindingId == host.Id && issuer.TrustDomainId == host.TrustDomainId
            && installed.HostBindingId == host.Id && installed.TrustDomainId == host.TrustDomainId
            && challenge.InstallationId == installed.Id && challenge.InstallationRevision == installed.Revision
            && challenge.InstallationWriterGeneration == installed.WriterGeneration
            && challenge.RuntimeGeneration == installed.RuntimeGeneration
            && challenge.InventoryFingerprint == installed.InventoryFingerprint
            && installed.Revision == 1 && installed.WithdrawnAt is null
            && issuer.Revision == 1 && issuer.RevokedAt is null
            && installed.InstalledAt <= challenge.PreparedAt && challenge.PreparedAt <= now
            && issuer.ApprovedAt <= now && installed.ValidUntil > now
            && challenge.ValidUntil <= installed.ValidUntil && challenge.ValidUntil > now && issuer.ValidUntil > now
            && challenge.WriterGeneration <= epoch.Revision && issuer.WriterGeneration <= epoch.Revision
            && installed.WriterGeneration <= epoch.Revision, "native-source-stale");
        Require.That(!await context.ModerationConsumerMeasurementReceipts.AsNoTracking()
            .IgnoreQueryFilters(["ModerationAuthority"])
            .AnyAsync(row => row.ChallengeId == challengeId && row.TenantId == tenantId
                && row.TrustDomainId == host.TrustDomainId && row.HostBindingId == host.Id, token),
            "challenge-already-accepted");
        Require.That(await AdministratorAsync(context, installed.InstalledByHumanId, tenantId, false, token)
            && await AdministratorAsync(context, challenge.PreparedByHumanId, tenantId, false, token)
            && await AdministratorAsync(context, issuer.ApprovedByHumanId, tenantId, true, token),
            "native-approval-authority");

        var candidate = await context.ModerationPolicyPublicationCandidates.AsNoTracking().AsSplitQuery()
            .IgnoreQueryFilters(["ModerationAuthority"])
            .Include(row => row.Definition!).ThenInclude(row => row.Classes)
            .Include(row => row.Definition!).ThenInclude(row => row.Reasons)
            .Include(row => row.Definition!).ThenInclude(row => row.Modes)
            .Include(row => row.Definition!).ThenInclude(row => row.Evidence)
            .Include(row => row.Definition!).ThenInclude(row => row.Profiles).ThenInclude(row => row.Scopes)
            .Include(row => row.Definition!).ThenInclude(row => row.Profiles).ThenInclude(row => row.Subjects)
            .Include(row => row.Definition!).ThenInclude(row => row.Profiles).ThenInclude(row => row.Effects)
            .Include(row => row.Definition!).ThenInclude(row => row.Profiles).ThenInclude(row => row.Plans)
            .Include(row => row.Definition!).ThenInclude(row => row.Profiles).ThenInclude(row => row.Consumers)
            .Include(row => row.Definition!).ThenInclude(row => row.Profiles).ThenInclude(row => row.Prerequisites)
            .SingleOrDefaultAsync(row => row.Id == challenge.PublicationId && row.HostBindingId == host.Id
                && row.TrustDomainId == host.TrustDomainId && row.TenantId == tenantId, token);
        var publicationGraph = context.ModerationPolicyPublications.AsNoTracking().AsSplitQuery()
            .IgnoreQueryFilters(["ModerationAuthority"])
            .Include(row => row.Classes).Include(row => row.Reasons).Include(row => row.Modes).Include(row => row.Evidence)
            .Include(row => row.Profiles).ThenInclude(row => row.Scopes)
            .Include(row => row.Profiles).ThenInclude(row => row.Subjects)
            .Include(row => row.Profiles).ThenInclude(row => row.Effects)
            .Include(row => row.Profiles).ThenInclude(row => row.Plans)
            .Include(row => row.Profiles).ThenInclude(row => row.Consumers)
            .Include(row => row.Profiles).ThenInclude(row => row.Prerequisites);
        var publication = candidate is not null ? candidate.Definition :
            await publicationGraph.SingleAsync(row => row.Id == challenge.DefinitionId && row.HostBindingId == host.Id
                && row.TrustDomainId == host.TrustDomainId
                && (row.JurisdictionTenantId == null || row.JurisdictionTenantId == tenantId), token);
        Require.That(publication is not null && publication.Id == challenge.DefinitionId, "native-definition-mismatch");
        var selection = await context.ModerationPolicySelections.AsNoTracking()
            .IgnoreQueryFilters(["ModerationAuthority"])
            .SingleOrDefaultAsync(row => row.Id == publication!.PolicyId && row.HostBindingId == host.Id
                && row.TrustDomainId == host.TrustDomainId
                && (row.JurisdictionTenantId == null || row.JurisdictionTenantId == tenantId), token);
        var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        var originalPublication = challenge.OriginalEffectId is null ? null :
            await publicationGraph.SingleAsync(row => row.Id == challenge.PublicationId && row.HostBindingId == host.Id
                && row.TrustDomainId == host.TrustDomainId
                && (row.JurisdictionTenantId == null || row.JurisdictionTenantId == tenantId), token);
        var restored = (originalPublication is not null
            ? ModerationPolicyPublicationProjection.RestoreHistory(originalPublication)
            : candidate is null
            ? ModerationPolicyPublicationProjection.Restore(publication!, inventory)
            : ModerationPolicyPublicationProjection.RestoreCandidate(candidate, inventory))
            ?? throw new MeasurementFailure("native-publication-invalid");
        var requirement = restored.Consumers.Single(row =>
            ModerationPolicyPublicationProjection.Digest(row.Binding) == challenge.BindingDigest);
        var binding = requirement.Binding;
        var contract = requirement.Consumers.Single(row => row.Id == challenge.ConsumerId);
        var selected = candidate is null
            ? selection?.LatestPublicationId == publication!.Id && selection.Revision == publication.Revision
                && publication.AdoptedRevision == publication.Revision
            : (selection?.Revision ?? 0) == candidate.ExpectedRevision
                && selection?.LatestPublicationId == candidate.PreviousPublicationId
                && (selection is null || selection.Id == candidate.PolicyId);
        if (candidate is not null && originalPublication is null)
        {
            candidate.ValidatePersistedState();
            var preparation = new ModerationPolicyPublicationRequest(candidate.OperationId, host.Id,
                candidate.AuthoritySourceId, candidate.AuthorityRevision,
                ModerationPolicyPublicationProjection.WriterGeneration, candidate.PreviousPublicationId,
                ModerationPolicyPublicationProjection.CanonicalProposed(restored.Proposed));
            Require.That(candidate.RequestDigest == ModerationPolicyPublicationProjection.Digest(
                    new { Request = preparation, ValidUntil = candidate.ValidUntil })
                && candidate.PreparedAt <= challenge.PreparedAt && candidate.ValidUntil >= challenge.ValidUntil
                && candidate.WriterGeneration <= epoch.Revision
                && await AdministratorAsync(context, candidate.PreparedByHumanId, tenantId,
                    candidate.Jurisdiction.Kind == ModerationScopeKind.Instance, token), "native-candidate-authority");
        }
        DateTimeOffset? originalUntil = null;
        if (originalPublication is not null)
        {
            var source = await context.ModerationNoticeSources.AsNoTracking()
                .IgnoreQueryFilters(["ModerationAuthority"])
                .Where(row => row.EffectId == challenge.OriginalEffectId && row.TenantId == tenantId)
                .Select(row => new { row.SnapshotHash, row.EffectBindingHash, row.RetainUntil, row.CapturedAt })
                .SingleOrDefaultAsync(token);
            var effect = await context.ModerationEffectIntents.AsNoTracking()
                .IgnoreQueryFilters(["ModerationAuthority"])
                .SingleOrDefaultAsync(row => row.Id == challenge.OriginalEffectId && row.TrustDomainId == host.TrustDomainId, token);
            var contribution = await context.ModerationContributionSelections.AsNoTracking()
                .IgnoreQueryFilters(["ModerationAuthority"])
                .SingleOrDefaultAsync(row => row.EffectId == challenge.OriginalEffectId && row.HostBindingId == host.Id
                    && row.TrustDomainId == host.TrustDomainId && row.TenantId == tenantId, token);
            Require.That(source is not null && source.SnapshotHash == challenge.OriginalSnapshotHash
                && source.EffectBindingHash == challenge.OriginalEffectBindingHash && source.RetainUntil > DateTimeOffset.UtcNow
                && source.RetainUntil >= challenge.ValidUntil && source.CapturedAt <= challenge.PreparedAt
                && effect is not null && effect.OriginalNoticeCleanupId is null
                && effect.PolicyRevision == originalPublication.Revision
                && contribution is not null && contribution.PublicationId == originalPublication.Id
                && contribution.PolicyId == originalPublication.PolicyId && contribution.PolicyRevision == originalPublication.Revision
                && contribution.DecisionId == effect.DecisionId && contribution.DecisionRevision == effect.DecisionRevision
                && originalPublication.AdoptedRevision == originalPublication.Revision
                && originalPublication.PublishedAt <= source.CapturedAt
                && await AdministratorAsync(context, originalPublication.PrincipalId, tenantId,
                    originalPublication.Jurisdiction.Kind == ModerationScopeKind.Instance, token),
                "native-original-custody-invalid");
            originalUntil = source!.RetainUntil;
            if (candidate is not null)
                Require.That(candidate.DefinitionId == challenge.DefinitionId
                    && candidate.ExpectedRevision + 1 == originalPublication.Revision
                    && candidate.PreparedAt <= originalPublication.PublishedAt
                    && candidate.ProposedStateDigest == ModerationPolicyPublicationProjection.ProposedDigest(restored.Proposed)
                    && await AdministratorAsync(context, candidate.PreparedByHumanId, tenantId,
                        originalPublication.Jurisdiction.Kind == ModerationScopeKind.Instance, token),
                    "native-original-definition-invalid");
            selected = true;
        }
        Require.That(selected
            && publication!.TrustDomainId == host.TrustDomainId && publication.SolutionId == host.SolutionId
            && publication.InstanceId == host.InstanceId && publication.PublishedAt <= challenge.PreparedAt
            && publication.QualificationRevision <= epoch.Revision
            && (binding.Jurisdiction == tenant || binding.Jurisdiction == instance)
            && binding.Mode == (ModerationNativeMode)challenge.ModeCode
            && publication.InventoryFingerprint == challenge.InventoryFingerprint
            && inventory.Fingerprint == challenge.InventoryFingerprint
            && ModerationPolicyPublicationProjection.Digest(contract) == challenge.ConsumerContractDigest,
            "native-publication-stale");
        var receipts = new List<ModerationQualificationMutationReceipt>();
        receipts.Add(await OriginalAsync(context, new InstallModerationConsumerSource
        {
            OperationId = Guid.Empty, Target = tenant, ExpectedQualificationRevision = installed.WriterGeneration - 1,
            InstallationId = installed.Id, ConsumerId = installed.ConsumerId, Mode = (ModerationNativeMode)installed.ModeCode,
            ExpectedRuntimeGeneration = installed.RuntimeGeneration, ExpectedInventoryFingerprint = installed.InventoryFingerprint,
            ValidUntil = installed.ValidUntil
        }, ModerationQualificationSourceKind.ConsumerInstallation, installed.Id,
            installed.InstalledByHumanId, installed.WriterGeneration, installed.InstalledAt, host, tenantId, token));
        receipts.Add(await OriginalAsync(context, new PrepareModerationConsumerMeasurementSource
        {
            OriginalEffect = challenge.OriginalEffectId is { } originalEffectId
                ? new(originalEffectId, challenge.OriginalSnapshotHash!, challenge.OriginalEffectBindingHash!) : null,
            OperationId = Guid.Empty, Target = tenant, ExpectedQualificationRevision = challenge.WriterGeneration - 1,
            ChallengeId = challenge.Id, InstallationId = installed.Id, ExpectedInstallationRevision = installed.Revision,
            ConsumerId = challenge.ConsumerId, Binding = binding, ExpectedRuntimeGeneration = challenge.RuntimeGeneration,
            ExpectedInventoryFingerprint = challenge.InventoryFingerprint, ValidUntil = challenge.ValidUntil
        }, ModerationQualificationSourceKind.ConsumerMeasurementChallenge, challenge.Id,
            challenge.PreparedByHumanId, challenge.WriterGeneration, challenge.PreparedAt, host, tenantId, token));
        receipts.Add(await OriginalAsync(context, new EnrollModerationMeasurementIssuerSource
        {
            OperationId = Guid.Empty, Target = tenant, ExpectedQualificationRevision = issuer.WriterGeneration - 1,
            EnrollmentId = issuer.Id, KeyId = issuer.KeyId, Issuer = issuer.Issuer,
            PublicKeySpkiBase64 = issuer.PublicKeySpkiBase64, RunnerDigest = issuer.RunnerDigest,
            CatalogueDigest = issuer.CatalogueDigest, TestBundleDigest = issuer.TestBundleDigest,
            ValidUntil = issuer.ValidUntil
        }, ModerationQualificationSourceKind.MeasurementIssuerEnrollment, issuer.Id,
            issuer.ApprovedByHumanId, issuer.WriterGeneration, issuer.ApprovedAt, host, tenantId, token));
        // Hash only public/native provenance, never credentials or a connection string.
        var fingerprint = Artifacts.Hash(JsonSerializer.SerializeToUtf8Bytes(new
        {
            host, epoch, challenge, issuer, installed, selection, receipts,
            candidate = candidate is null ? null : new
            {
                candidate.Id, candidate.DefinitionId, candidate.PolicyId, candidate.ExpectedRevision,
                candidate.PreviousPublicationId, candidate.PreparedByHumanId, candidate.OperationId,
                candidate.RequestDigest, candidate.ProposedStateDigest, candidate.AuthoritySourceId,
                candidate.AuthorityRevision, candidate.WriterGeneration, candidate.PreparedAt, candidate.ValidUntil
            },
            publicationDigest = ModerationPolicyPublicationProjection.Digest(restored),
            original = originalPublication is null ? null : new
            {
                challenge.OriginalEffectId, challenge.OriginalSnapshotHash, challenge.OriginalEffectBindingHash,
                originalPublication.Id, originalPublication.Revision, originalUntil
            }
        }));
        await transaction.CommitAsync(token);
        return new(challenge, issuer, binding, tenant, epoch.Revision, fingerprint,
            new[] { challenge.ValidUntil, issuer.ValidUntil, installed.ValidUntil,
                originalUntil ?? candidate?.ValidUntil ?? challenge.ValidUntil }.Min());
    }

    private static async Task<bool> AdministratorAsync(
        ExploreDbContext context, Guid human, Guid tenant, bool platformOnly, CancellationToken token)
    {
        if (await context.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.UserId == human
            && !row.User.IsDeleted && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform
            && row.Role.MasterCode == "platform.admin", token)) return true;
        return !platformOnly && await context.TenantUserRoleGrants.AsNoTracking().AnyAsync(row =>
            row.TenantId == tenant && row.TenantUser.UserId == human && row.RevokedAt == null
            && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant
            && row.RoleId == (int)RoleEnum.TenantAdmin && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
            && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted, token);
    }

    private static async Task<ModerationQualificationMutationReceipt> OriginalAsync(
        ExploreDbContext context, ModerationQualificationMutation request,
        ModerationQualificationSourceKind kind, Guid sourceId, Guid human, long generation,
        DateTimeOffset at, ModerationHostBinding host, Guid tenant, CancellationToken token)
    {
        var receipt = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .IgnoreQueryFilters(["ModerationAuthority"]).SingleAsync(row =>
                row.SourceKind == kind && row.SourceId == sourceId && row.ResultRevision == 1
                && row.HostBindingId == host.Id && row.TrustDomainId == host.TrustDomainId
                && row.TenantId == tenant, token);
        receipt.ValidatePersistedState();
        request = request with { OperationId = receipt.Id };
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(
            request.GetType().FullName + "\n" + JsonSerializer.Serialize(request, request.GetType()))));
        Require.That(receipt.RequestDigest == digest && receipt.HostBindingId == host.Id
            && receipt.TrustDomainId == host.TrustDomainId && receipt.TenantId == tenant
            && receipt.PrincipalId == human && receipt.QualificationRevision == generation
            && receipt.CompletedAt == at, "native-original-approval");
        return receipt;
    }

    internal static async Task<string> ProviderVersionAsync(
        PrimaryDatabaseConnectionOptions options, Guid tenant, CancellationToken token)
    {
        await using var context = Context(options, tenant);
        await context.Database.OpenConnectionAsync(token);
        return context.Database.GetDbConnection().ServerVersion;
    }

    private sealed record BoundTenant(Guid TenantId) : ITenantContext;
}

internal sealed class SingleReadStrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
{
    public IExecutionStrategy Create() => new NonRetryingExecutionStrategy(dependencies);
}
