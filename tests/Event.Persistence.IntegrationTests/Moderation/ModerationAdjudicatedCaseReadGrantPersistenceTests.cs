using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Persistence;
using ISLAMU.Moderation.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NoticeDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationNoticeReadTests.NoticeDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>Native persistence invariants only; source authored before the model, not executed.</summary>
public sealed class ModerationAdjudicatedCaseReadGrantPersistenceTests
{
    [Test]
    public async Task Finite_grant_round_trips_once_and_revocation_preserves_original_coordinates()
    {
        await using var database = await GrantDatabase.CreateAsync();
        await using var context = database.Context();
        var grant = await context.ModerationAdjudicatedCaseReadGrants.SingleAsync();
        await Assert.That(grant.IsCurrentAt(grant.IssuedAt)).IsTrue();
        await Assert.That(grant.IsCurrentAt(grant.ExpiresAt)).IsFalse();
        var operation = Guid.CreateVersion7();
        grant.Revoke(operation, 1, grant.IssuerUserId, grant.IssuerRoleGrantId,
            grant.IssuerPlatformGrant, grant.IssuedAt.AddSeconds(1));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var retained = await context.ModerationAdjudicatedCaseReadGrants.SingleAsync();
        retained.ValidatePersistedState();
        await Assert.That(retained.Revision).IsEqualTo(2L);
        await Assert.That(retained.RevocationOperationId).IsEqualTo(operation);
        await Assert.That(retained.OriginalEffectId).IsEqualTo(database.Grant.OriginalEffectId);
        await Assert.That(retained.OriginalSnapshotHash).IsEqualTo(database.Grant.OriginalSnapshotHash);
        await Assert.That(retained.ExpiresAt).IsEqualTo(database.Grant.ExpiresAt);
        await Assert.That(retained.IsCurrentAt(retained.IssuedAt.AddSeconds(2))).IsFalse();
    }

    [Test]
    [Arguments("missing-trust")]
    [Arguments("foreign-trust")]
    [Arguments("missing-tenant")]
    [Arguments("foreign-tenant")]
    [Arguments("tenant-bypass")]
    public async Task Both_actual_custody_tenant_and_bound_trust_are_required(string defect)
    {
        await using var database = await GrantDatabase.CreateAsync();
        await using var context = database.Context();
        await Assert.That(await context.ModerationAdjudicatedCaseReadGrants.CountAsync()).IsEqualTo(1);
        switch (defect)
        {
            case "missing-trust": context.ModerationTrustDomainId = null; break;
            case "foreign-trust": context.ModerationTrustDomainId = Guid.CreateVersion7(); break;
            case "missing-tenant": context.TenantContext = null; break;
            case "foreign-tenant": context.TenantContext = new SelectedTenant(Guid.CreateVersion7()); break;
            case "tenant-bypass":
                context.TenantContext = new SelectedTenant(Guid.CreateVersion7());
                context.EnableTenantFilterBypass("Adjudicated grant isolation negative control");
                break;
            default: throw new ArgumentOutOfRangeException(nameof(defect));
        }
        if (defect is "missing-tenant" or "foreign-tenant" or "tenant-bypass")
            context.ModerationTrustDomainId = database.Grant.TrustDomainId;
        await Assert.That(await context.ModerationAdjudicatedCaseReadGrants.AnyAsync()).IsFalse();
        context.TenantContext = new SelectedTenant(database.Grant.TenantId);
        context.ModerationTrustDomainId = database.Grant.TrustDomainId;
        await Assert.That(await context.ModerationAdjudicatedCaseReadGrants.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.HostBindingId))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.TrustDomainId))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.TenantId))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.CaseId))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.CaseRevision))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.RecipientUserId))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.IssuerUserId))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.IssuerRoleGrantId))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.IssuerPlatformGrant))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.OriginalEffectId))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.OriginalSnapshotHash))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.OriginalEffectBindingHash))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.OriginalRetainUntil))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.IssuedAt))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.ExpiresAt))]
    public async Task Valid_revocation_cannot_also_rewrite_or_renew_original_grant(string property)
    {
        await using var database = await GrantDatabase.CreateAsync();
        await using var context = database.Context();
        var grant = await context.ModerationAdjudicatedCaseReadGrants.SingleAsync();
        grant.Revoke(Guid.CreateVersion7(), 1, grant.IssuerUserId, grant.IssuerRoleGrantId,
            grant.IssuerPlatformGrant, grant.IssuedAt.AddSeconds(2));
        var value = context.Entry(grant).Property(property);
        value.CurrentValue = value.CurrentValue switch
        {
            Guid => Guid.CreateVersion7(),
            long revision => revision + 1,
            bool platform => !platform,
            string hash => hash == new string('b', 64) ? new string('c', 64) : new string('b', 64),
            DateTimeOffset instant => property == nameof(ModerationAdjudicatedCaseReadGrant.IssuedAt)
                ? instant.AddSeconds(-1) : instant.AddSeconds(1),
            _ => throw new InvalidOperationException("Unknown immutable grant coordinate.")
        };
        grant.ValidatePersistedState();
        await Assert.That(async () => await context.SaveChangesAsync()).Throws<InvalidOperationException>();
        await using var verify = database.Context();
        var retained = await verify.ModerationAdjudicatedCaseReadGrants.SingleAsync();
        await Assert.That(verify.Entry(retained).Property(property).CurrentValue).IsEqualTo(
            verify.Entry(database.Grant).Property(property).CurrentValue);
        await Assert.That(retained.Revision).IsEqualTo(1L);
        await Assert.That(retained.RevokedAt).IsNull();
    }

    [Test]
    [Arguments("restore")]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.RevokedAt))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.RevocationOperationId))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.RevokedByUserId))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.RevokedByRoleGrantId))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.RevokedByPlatformGrant))]
    public async Task Committed_revocation_is_terminal_and_its_accountability_cannot_be_rewritten(string property)
    {
        await using var database = await GrantDatabase.CreateAsync();
        await using var context = database.Context();
        var grant = await context.ModerationAdjudicatedCaseReadGrants.SingleAsync();
        var operation = Guid.CreateVersion7();
        grant.Revoke(operation, 1, grant.IssuerUserId, grant.IssuerRoleGrantId,
            grant.IssuerPlatformGrant, grant.IssuedAt.AddSeconds(1));
        await context.SaveChangesAsync();
        if (property == "restore")
        {
            context.Entry(grant).Property(row => row.Revision).CurrentValue = 1;
            context.Entry(grant).Property(row => row.RevokedAt).CurrentValue = null;
            context.Entry(grant).Property(row => row.RevocationOperationId).CurrentValue = null;
            context.Entry(grant).Property(row => row.RevokedByUserId).CurrentValue = null;
            context.Entry(grant).Property(row => row.RevokedByRoleGrantId).CurrentValue = null;
            context.Entry(grant).Property(row => row.RevokedByPlatformGrant).CurrentValue = null;
        }
        else
        {
            var value = context.Entry(grant).Property(property);
            value.CurrentValue = value.CurrentValue switch
            {
                Guid => Guid.CreateVersion7(),
                bool platform => !platform,
                DateTimeOffset instant => instant.AddSeconds(1),
                _ => throw new InvalidOperationException("Unknown revocation coordinate.")
            };
        }
        grant.ValidatePersistedState();
        await Assert.That(async () => await context.SaveChangesAsync()).Throws<InvalidOperationException>();
        await using var verify = database.Context();
        await Assert.That((await verify.ModerationAdjudicatedCaseReadGrants.SingleAsync()).RevocationOperationId)
            .IsEqualTo(operation);
    }

    [Test]
    public async Task A_stale_revoker_cannot_replace_the_committed_revocation()
    {
        await using var database = await GrantDatabase.CreateAsync();
        await using var first = database.Context();
        await using var stale = database.Context();
        var winner = await first.ModerationAdjudicatedCaseReadGrants.SingleAsync();
        var loser = await stale.ModerationAdjudicatedCaseReadGrants.SingleAsync();
        var operation = Guid.CreateVersion7();
        winner.Revoke(operation, 1, winner.IssuerUserId, winner.IssuerRoleGrantId, true, winner.IssuedAt);
        loser.Revoke(Guid.CreateVersion7(), 1, loser.IssuerUserId, loser.IssuerRoleGrantId, true, loser.IssuedAt);
        await first.SaveChangesAsync();
        await Assert.That(async () => await stale.SaveChangesAsync()).Throws<DbUpdateConcurrencyException>();
        await using var verify = database.Context();
        await Assert.That((await verify.ModerationAdjudicatedCaseReadGrants.SingleAsync()).RevocationOperationId)
            .IsEqualTo(operation);
    }

    [Test]
    [Arguments("expiry")]
    [Arguments("retention")]
    [Arguments("revision")]
    [Arguments("case-revision")]
    [Arguments("partial-revocation")]
    [Arguments("revocation-without-revision")]
    [Arguments("nonfinite-issued")]
    [Arguments("nonfinite-retention")]
    public async Task Native_constraints_reject_malformed_rows_without_changing_the_original(string defect)
    {
        await using var database = await GrantDatabase.CreateAsync();
        await using var context = database.Context();
        var grants = context.ModerationAdjudicatedCaseReadGrants;
        await Assert.That(async () =>
        {
            _ = defect switch
            {
                "expiry" => await grants.ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, row => row.IssuedAt)),
                "retention" => await grants.ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, database.Grant.OriginalRetainUntil.AddSeconds(1))),
                "revision" => await grants.ExecuteUpdateAsync(update => update.SetProperty(row => row.Revision, 3)),
                "case-revision" => await grants.ExecuteUpdateAsync(update => update.SetProperty(row => row.CaseRevision, long.MaxValue)),
                "partial-revocation" => await grants.ExecuteUpdateAsync(update => update.SetProperty(row => row.RevocationOperationId, Guid.CreateVersion7())),
                "revocation-without-revision" => await grants.ExecuteUpdateAsync(update => update.SetProperty(row => row.RevokedAt, database.Grant.IssuedAt)),
                "nonfinite-issued" => await grants.ExecuteUpdateAsync(update => update.SetProperty(row => row.IssuedAt, DateTimeOffset.MinValue)),
                "nonfinite-retention" => await grants.ExecuteUpdateAsync(update => update.SetProperty(row => row.OriginalRetainUntil, DateTimeOffset.MaxValue)),
                _ => throw new ArgumentOutOfRangeException(nameof(defect))
            };
        }).Throws<SqliteException>();
        var retained = await grants.AsNoTracking().SingleAsync();
        retained.ValidatePersistedState();
        await Assert.That(retained.Revision).IsEqualTo(1L);
        await Assert.That(retained.ExpiresAt).IsEqualTo(database.Grant.ExpiresAt);
    }

    [Test]
    [Arguments("hash")]
    [Arguments("window")]
    [Arguments("precision")]
    [Arguments("offset")]
    [Arguments("partial-revocation")]
    public async Task Tracked_insert_cannot_bypass_domain_state_validation(string defect)
    {
        await using var database = await GrantDatabase.CreateAsync();
        await using var context = database.Context();
        var copy = (ModerationAdjudicatedCaseReadGrant)context.Entry(database.Grant).CurrentValues.ToObject();
        context.Entry(copy).Property(row => row.Id).CurrentValue = Guid.CreateVersion7();
        switch (defect)
        {
            case "hash": context.Entry(copy).Property(row => row.OriginalSnapshotHash).CurrentValue = new string('A', 64); break;
            case "window": context.Entry(copy).Property(row => row.ExpiresAt).CurrentValue = copy.IssuedAt; break;
            case "precision": context.Entry(copy).Property(row => row.ExpiresAt).CurrentValue = copy.ExpiresAt.AddTicks(1); break;
            case "offset": context.Entry(copy).Property(row => row.IssuedAt).CurrentValue = copy.IssuedAt.ToOffset(TimeSpan.FromHours(1)); break;
            case "partial-revocation": context.Entry(copy).Property(row => row.RevokedByPlatformGrant).CurrentValue = false; break;
            default: throw new ArgumentOutOfRangeException(nameof(defect));
        }
        context.ModerationAdjudicatedCaseReadGrants.Add(copy);
        await Assert.That(async () => await context.SaveChangesAsync()).Throws<ArgumentException>();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationAdjudicatedCaseReadGrants.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.HostBindingId))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.TenantId))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.CaseId))]
    [Arguments(nameof(ModerationAdjudicatedCaseReadGrant.OriginalEffectId))]
    public async Task A_new_grant_cannot_reference_a_missing_custody_principal(string property)
    {
        await using var database = await GrantDatabase.CreateAsync();
        await using var context = database.Context();
        var copy = (ModerationAdjudicatedCaseReadGrant)context.Entry(database.Grant).CurrentValues.ToObject();
        context.Entry(copy).Property(row => row.Id).CurrentValue = Guid.CreateVersion7();
        context.Entry(copy).Property(property).CurrentValue = Guid.CreateVersion7();
        copy.ValidatePersistedState();
        context.ModerationAdjudicatedCaseReadGrants.Add(copy);
        await Assert.That(async () => await context.SaveChangesAsync()).Throws<DbUpdateException>();
        await using var verify = database.Context();
        await Assert.That(await verify.ModerationAdjudicatedCaseReadGrants.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task Original_retention_and_erasure_cleanup_can_delete_a_grant()
    {
        await using var database = await GrantDatabase.CreateAsync();
        await using var context = database.Context();
        context.Remove(await context.ModerationAdjudicatedCaseReadGrants.SingleAsync());
        await context.SaveChangesAsync();
        await Assert.That(await context.ModerationAdjudicatedCaseReadGrants.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task Custody_principals_are_restricted_without_pinning_a_mutable_case_revision_or_material_source()
    {
        await using var database = await GrantDatabase.CreateAsync();
        await using var context = database.Context();
        var entity = context.Model.FindEntityType(typeof(ModerationAdjudicatedCaseReadGrant))!;
        var foreignKeys = entity.GetForeignKeys().ToArray();
        await Assert.That(foreignKeys.Length).IsEqualTo(4);
        foreach (var principal in new[] { typeof(ModerationHostBinding), typeof(Tenant), typeof(ModerationCase), typeof(ModerationEffectIntent) })
            await Assert.That(foreignKeys.Single(key => key.PrincipalEntityType.ClrType == principal).DeleteBehavior)
                .IsEqualTo(DeleteBehavior.Restrict);
        await Assert.That(foreignKeys.Any(key => key.Properties.Any(property => property.Name == nameof(ModerationAdjudicatedCaseReadGrant.CaseRevision))))
            .IsFalse();
    }

    private sealed class SelectedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class GrantDatabase(NoticeDatabase notice, ModerationAdjudicatedCaseReadGrant grant) : IAsyncDisposable
    {
        public ModerationAdjudicatedCaseReadGrant Grant { get; } = grant;

        public ExploreDbContext Context()
        {
            var context = notice.Database.Context();
            context.ModerationTrustDomainId = Grant.TrustDomainId;
            return context;
        }

        public static async Task<GrantDatabase> CreateAsync()
        {
            var notice = await NoticeDatabase.CreateAsync();
            await using var context = notice.Database.Context();
            context.ModerationTrustDomainId = notice.Database.Scope.TrustDomainId;
            var source = await context.ModerationNoticeSources.AsNoTracking().SingleAsync(row => row.EffectId == notice.EffectId);
            var decision = await context.ModerationDecisions.AsNoTracking().SingleAsync(row => row.Id == notice.DecisionId);
            var role = await context.PlatformUserRoles.AsNoTracking().SingleAsync(row => row.UserId == notice.Database.AdministratorId);
            var grant = ModerationAdjudicatedCaseReadGrant.Grant(Guid.CreateVersion7(), notice.Database.HostId,
                notice.Database.Scope.TrustDomainId, notice.Database.TenantId, decision.CaseId, 1,
                notice.Database.SubjectId, notice.Database.AdministratorId, role.Id, true, source.EffectId,
                source.SnapshotHash, source.EffectBindingHash, source.RetainUntil!.Value,
                notice.Clock.GetUtcNow(), notice.Clock.GetUtcNow().AddSeconds(30));
            context.ModerationAdjudicatedCaseReadGrants.Add(grant);
            await context.SaveChangesAsync();
            return new GrantDatabase(notice, grant);
        }

        public ValueTask DisposeAsync() => notice.DisposeAsync();
    }
}
