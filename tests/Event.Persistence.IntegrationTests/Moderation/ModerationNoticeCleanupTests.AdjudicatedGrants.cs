using System.Data;
using System.Security.Cryptography;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeCleanupTests
{
    // SOURCE UNRUN/unbuilt: native lifecycle regressions authored before the consumers.
    [Test]
    [Arguments("original")]
    [Arguments("missing-source")]
    [Arguments("wrong-hash")]
    public async Task Original_ceiling_disposes_all_bound_grants_without_requiring_original_proof(string original)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var expired = await fixture.SeedAsync(defect: original == "original" ? null : original);
        var live = await fixture.SeedAsync(retainUntil: Ceiling.AddDays(1));
        await SeedCleanupReadGrantsAsync(fixture, expired, 101);
        var survivor = (await SeedCleanupReadGrantsAsync(fixture, live, 1))[0];
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer)).PruneAsync(null, 100);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        await Assert.That(result.Purged).IsEqualTo(1);
        await Assert.That(result.OriginalWritesProven).IsEqualTo(original == "original" ? 1 : 0);
        await using var inspect = fixture.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.ModerationAdjudicatedCaseReadGrants.CountAsync()).IsEqualTo(1);
        await Assert.That(await inspect.ModerationAdjudicatedCaseReadGrants.AnyAsync(row => row.Id == survivor)).IsTrue();
        await Assert.That(await inspect.ModerationNoticeSources.AnyAsync(row => row.EffectId == expired)).IsFalse();
        await Assert.That(await inspect.ModerationNoticeDisclosureApprovals.AnyAsync(row => row.EffectId == expired)).IsFalse();
        await Assert.That(await inspect.ModerationCases.CountAsync()).IsEqualTo(2);
        await Assert.That(await inspect.ModerationSanctions.CountAsync()).IsEqualTo(2);
        await Assert.That((await inspect.ModerationEffectIntents.SingleAsync(row => row.Id == expired)).CompletedAt).IsNull();
    }

    [Test]
    public async Task Failed_native_payload_disposal_rolls_back_grants_with_original_source_and_approval()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effect = await fixture.SeedAsync(nativePointer: true);
        await SeedCleanupReadGrantsAsync(fixture, effect, 3);
        await using var writer = fixture.Context();
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer)).PruneAsync(null, 10);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Unavailable);
        await using var inspect = fixture.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.ModerationAdjudicatedCaseReadGrants.CountAsync()).IsEqualTo(3);
        await Assert.That(await inspect.ModerationNoticeSources.AnyAsync(row => row.EffectId == effect)).IsTrue();
        await Assert.That(await inspect.ModerationNoticeDisclosureApprovals.AnyAsync(row => row.EffectId == effect)).IsTrue();
    }

    [Test]
    public async Task Grant_reappearing_at_final_flush_prevents_original_cleanup_commit()
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effect = await fixture.SeedAsync();
        await SeedCleanupReadGrantsAsync(fixture, effect, 1);
        ModerationAdjudicatedCaseReadGrant retained;
        await using (var read = fixture.Context())
        {
            read.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            retained = await read.ModerationAdjudicatedCaseReadGrants.AsNoTracking().SingleAsync();
        }
        var reinserted = false;
        var observer = new CleanupSaved(async context =>
        {
            if (reinserted) return;
            reinserted = true;
            context.Add(retained);
            await context.SaveChangesAsync();
        });
        await using var writer = fixture.Context(observer);
        var result = await fixture.Repository(writer, await fixture.AuthenticateAsync(writer)).PruneAsync(null, 10);
        await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Unavailable);
        await Assert.That(observer.Observed).IsTrue();
        await using var inspect = fixture.Context();
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.ModerationAdjudicatedCaseReadGrants.CountAsync()).IsEqualTo(1);
        await Assert.That(await inspect.ModerationNoticeSources.AnyAsync(row => row.EffectId == effect)).IsTrue();
        await Assert.That((await inspect.ModerationEffectIntents.SingleAsync()).OriginalNoticeCleanupId).IsNull();
    }

    [Test]
    [Arguments("recipient", false)]
    [Arguments("issuer", false)]
    [Arguments("revoker", false)]
    [Arguments("recipient", true)]
    [Arguments("issuer", true)]
    [Arguments("revoker", true)]
    public async Task Exact_saga_erases_only_named_humans_grants_in_every_tenant_and_preserves_pending_work(
        string actor, bool rollback)
    {
        await using var fixture = await CleanupDatabase.CreateAsync();
        var effect = await fixture.SeedAsync();
        var subject = fixture.Database.UrgencyAssessorId;
        Guid otherTenant = Guid.CreateVersion7();
        await using (var seed = fixture.Context())
        {
            seed.Tenants.Add(new Tenant
            {
                Id = otherTenant,
                FullName = "Former grant custody",
                Slug = $"former-{otherTenant:N}",
                TenantStatusId = (int)TenantStatusEnum.Suspended,
                TenantStatus = null!
            });
            await seed.SaveChangesAsync();
        }
        var first = await SeedCleanupReadGrantsAsync(fixture, effect, 101, actor, subject);
        var second = await SeedCleanupReadGrantsAsync(fixture, effect, 101, actor, subject, otherTenant);
        var survivor = (await SeedCleanupReadGrantsAsync(fixture, effect, 1))[0];
        var now = OriginalAt.AddMinutes(3).UtcDateTime;
        var fact = PrivacyErasureIntent.Record(Guid.CreateVersion7(), 1, PrivacyErasureSubjectKind.User,
            subject, PrivacyErasureReasonCode.SubjectErasureRequest, 1, now, now);
        await using (var context = fixture.Context())
        {
            context.PrivacyErasureSagas.Add(PrivacyErasureSaga.Start(fact, fact.AuthoritySequence,
                RandomNumberGenerator.GetBytes(32), now.AddHours(1), now));
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var tracked = await context.ModerationAdjudicatedCaseReadGrants.SingleAsync(row => row.Id == first[0]);
            var pending = await context.ModerationAdjudicatedCaseReadGrants.SingleAsync(row => row.Id == survivor);
            pending.Revoke(Guid.CreateVersion7(), 1, pending.IssuerUserId, pending.IssuerRoleGrantId, true,
                OriginalAt.AddMinutes(3));
            context.ModerationTrustDomainId = null;
            context.TenantContext = null;
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await new UserLocationPrivacyErasureRepository(context).EraseModerationCaseNarrativesAsync(fact, default);
            await Assert.That(context.Entry(tracked).State).IsEqualTo(EntityState.Detached);
            await Assert.That(context.Entry(pending).State).IsEqualTo(EntityState.Modified);
            await context.SaveChangesAsync();
            if (!rollback) await transaction.CommitAsync();
        }
        await using var inspect = fixture.Context();
        var all = inspect.ModerationAdjudicatedCaseReadGrants.IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure);
        await Assert.That(await all.CountAsync(row => first.Contains(row.Id) || second.Contains(row.Id)))
            .IsEqualTo(rollback ? 202 : 0);
        await Assert.That((await all.SingleAsync(row => row.Id == survivor)).Revision).IsEqualTo(rollback ? 1L : 2L);
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.ModerationNoticeSources.AnyAsync(row => row.EffectId == effect)).IsTrue();
        await Assert.That(await inspect.ModerationCases.CountAsync()).IsEqualTo(1);
        await Assert.That(await inspect.ModerationSanctions.CountAsync()).IsEqualTo(1);
        await Assert.That((await inspect.ModerationEffectIntents.SingleAsync()).OriginalNoticeCleanupId).IsNull();
    }

    // Storage seeding exercises disposal, not the native owner's permission to issue these rows.
    private static async Task<Guid[]> SeedCleanupReadGrantsAsync(CleanupDatabase fixture, Guid effectId, int count,
        string? actor = null, Guid subject = default, Guid? tenant = null)
    {
        await using var context = fixture.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var approval = await context.ModerationNoticeDisclosureApprovals.AsNoTracking().SingleAsync(row => row.EffectId == effectId);
        var decision = await context.ModerationDecisions.AsNoTracking().SingleAsync(row => row.Id == approval.DecisionId);
        var role = await context.PlatformUserRoles.AsNoTracking().SingleAsync(row => row.UserId == fixture.Database.AdministratorId);
        var source = await context.ModerationNoticeSources.AsNoTracking().SingleOrDefaultAsync(row => row.EffectId == effectId);
        var ids = new List<Guid>();
        for (var index = 0; index < count; index++)
        {
            var grant = ModerationAdjudicatedCaseReadGrant.Grant(Guid.CreateVersion7(), fixture.Database.HostId,
                fixture.Database.Scope.TrustDomainId, tenant ?? fixture.Database.TenantId, decision.CaseId, 1,
                actor == "recipient" ? subject : fixture.Database.SubjectId,
                actor == "issuer" ? subject : fixture.Database.AdministratorId, role.Id, true, effectId,
                source?.SnapshotHash ?? new string('a', 64), source?.EffectBindingHash ?? new string('b', 64),
                approval.RetainUntil, OriginalAt.AddMinutes(2),
                index % 3 == 1 ? OriginalAt.AddDays(1) : approval.RetainUntil);
            if (index % 3 == 2 || actor == "revoker")
                grant.Revoke(Guid.CreateVersion7(), 1, actor == "revoker" ? subject : fixture.Database.ReviewerId,
                    role.Id, true, OriginalAt.AddMinutes(3));
            context.Add(grant);
            ids.Add(grant.Id);
        }
        await context.SaveChangesAsync();
        return ids.ToArray();
    }
}
