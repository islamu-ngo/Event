using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class TenantPlanCatalogueOriginalAdmissionTests
{
    [Test]
    public async Task LocalPlanAuthoringPreservesPublishedContentAndTypedValidation()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        string credential = await fixture.SignInAsync();
        await using var observer = fixture.Native.CreateDatabase();
        string publishedBefore = await VersionSnapshotAsync(observer, seed.PublishedVersionId);
        var patch = (UpdateInstanceTenantPlanVersionDraftCommand)Command("update-version", seed, fixture.Binding.LocalSubjectId);
        var updated = await ExecuteFreshAsync(fixture, credential, patch);
        await Assert.That(updated.IsSuccess).IsTrue();
        await Assert.That(updated.Id).IsEqualTo(seed.DraftVersionId);
        await RequireEffectAsync(observer, seed, "update-version", fixture.Binding.LocalSubjectId, default);
        var rejected = await ExecuteFreshAsync(fixture, credential,
            new UpdateInstanceTenantPlanVersionDraftCommand(seed.PublishedVersionId, patch.Update));
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.Errors!).Contains("tenant_plan_version_not_draft");
        var mismatch = await ExecuteFreshAsync(fixture, credential,
            new CreateInstanceTenantPlanVersionDraftCommand(seed.Key, Draft("wrong-key")));
        await Assert.That(mismatch.IsSuccess).IsFalse();
        await Assert.That(mismatch.Errors!).Contains("tenant_plan_key_mismatch");
        var invalid = await ExecuteFreshAsync(fixture, credential,
            new CreateInstanceTenantPlanVersionDraftCommand(seed.Key, Draft(seed.Key) with { Pricing = new(-1, "EUR", "monthly") }));
        await Assert.That(invalid.IsSuccess).IsFalse();
        await Assert.That(invalid.Errors!).Contains(TenantPlanValidationErrorCodes.NegativePrice);
        var clone = await ExecuteFreshAsync(fixture, credential, Command("clone", seed, fixture.Binding.LocalSubjectId));
        await Assert.That(clone.IsSuccess).IsTrue();
        await RequireEffectAsync(observer, seed, "clone", fixture.Binding.LocalSubjectId, default);
        await Assert.That(await VersionSnapshotAsync(observer, seed.PublishedVersionId)).IsEqualTo(publishedBefore);
        var archive = await ExecuteFreshAsync(fixture, credential, Command("archive", seed, fixture.Binding.LocalSubjectId));
        await Assert.That(archive.IsSuccess).IsTrue();
        await RequireEffectAsync(observer, seed, "archive", fixture.Binding.LocalSubjectId, default);
    }

    [Test]
    public async Task LocalPublicationKeepsPinnedAssignmentsThenMovesEveryActiveAssignmentAtomically()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        Guid otherTenant = Guid.CreateVersion7();
        Guid otherAssignment = Guid.CreateVersion7();
        await using (var context = fixture.Native.CreateDatabase())
        {
            context.Tenants.Add(new Tenant
            {
                Id = otherTenant,
                Slug = "other-" + otherTenant.ToString("N"),
                FullName = "Other plan tenant",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!,
                CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            context.TenantPlanAssignments.Add(new TenantPlanAssignment
            {
                Id = otherAssignment,
                TenantId = otherTenant,
                TenantPlanId = seed.PlanId,
                TenantPlanVersionId = seed.CurrentVersionId,
                TenantPlanAssignmentStatusId = (int)TenantPlanAssignmentStatusEnum.Active,
                AssignedByUserId = fixture.Binding.LocalSubjectId,
                AssignedAt = seed.AssignedAt,
                CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            await context.SaveChangesAsync();
        }
        string credential = await fixture.SignInAsync();
        await using var observer = fixture.Native.CreateDatabase();
        string originalVersion = await VersionSnapshotAsync(observer, seed.CurrentVersionId);
        var pinned = await ExecuteFreshAsync(fixture, credential, new PublishInstanceTenantPlanVersionCommand(
            seed.DraftVersionId, TenantPlanExistingAssignmentPolicy.LeaveExistingTenantsPinned));
        await Assert.That(pinned.IsSuccess).IsTrue();
        foreach (Guid id in new[] { seed.CurrentAssignmentId, otherAssignment })
            await Assert.That(await observer.TenantPlanAssignments.Where(row => row.Id == id)
                .Select(row => row.TenantPlanVersionId).SingleAsync()).IsEqualTo(seed.CurrentVersionId);
        var created = await ExecuteFreshAsync(fixture, credential, Command("create-version", seed, fixture.Binding.LocalSubjectId));
        await Assert.That(created.IsSuccess).IsTrue();
        var moved = await ExecuteFreshAsync(fixture, credential, new PublishInstanceTenantPlanVersionCommand(
            created.Id, TenantPlanExistingAssignmentPolicy.MoveExistingTenantsToPublishedVersion));
        await Assert.That(moved.IsSuccess).IsTrue();
        foreach (Guid id in new[] { seed.CurrentAssignmentId, otherAssignment })
        {
            var assignment = await observer.TenantPlanAssignments.AsNoTracking().SingleAsync(row => row.Id == id);
            await Assert.That(assignment.TenantPlanVersionId).IsEqualTo(created.Id);
            await Assert.That(assignment.TenantPlanAssignmentStatusId).IsEqualTo((int)TenantPlanAssignmentStatusEnum.Active);
            await Assert.That(assignment.AssignedAt).IsEqualTo(seed.AssignedAt);
        }
        await Assert.That(await VersionSnapshotAsync(observer, seed.CurrentVersionId)).IsEqualTo(originalVersion);
        await Assert.That(await observer.TenantPlanAssignments.Where(row => row.Id == seed.PreviousAssignmentId)
            .Select(row => row.TenantPlanVersionId).SingleAsync()).IsEqualTo(seed.PublishedVersionId);
    }

    [Test]
    public async Task LocalSwitchIsIdempotentAndRollbackRequiresExactEligibleTenantTarget()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        string credential = await fixture.SignInAsync();
        Guid actor = fixture.Binding.LocalSubjectId;
        await using var observer = fixture.Native.CreateDatabase();
        string before = await SnapshotAsync(observer, seed);
        var repeated = await ExecuteFreshAsync(fixture, credential, new SwitchInstanceTenantPlanAssignmentCommand(
            PlatformDefaults.DefaultTenantId, seed.CurrentVersionId, actor));
        await Assert.That(repeated.IsSuccess).IsTrue();
        await Assert.That(repeated.Id).IsEqualTo(seed.CurrentAssignmentId);
        await Assert.That(await SnapshotAsync(observer, seed)).IsEqualTo(before);
        var wrongTarget = await ExecuteFreshAsync(fixture, credential, new RollbackInstanceTenantPlanAssignmentCommand(
            PlatformDefaults.DefaultTenantId, seed.CurrentAssignmentId, actor));
        await Assert.That(wrongTarget.IsSuccess).IsFalse();
        await Assert.That(wrongTarget.Errors!).Contains("tenant_plan_rollback_assignment_not_eligible");
        var foreign = await ExecuteFreshAsync(fixture, credential, new RollbackInstanceTenantPlanAssignmentCommand(
            Guid.CreateVersion7(), seed.PreviousAssignmentId, actor));
        await Assert.That(foreign.IsSuccess).IsFalse();
        await Assert.That(foreign.Errors!).Contains("tenant_plan_active_assignment_not_found");
        await Assert.That(await SnapshotAsync(observer, seed)).IsEqualTo(before);
        var rollback = await ExecuteFreshAsync(fixture, credential, Command("rollback", seed, actor));
        await Assert.That(rollback.IsSuccess).IsTrue();
        await Assert.That(rollback.Id).IsEqualTo(seed.PreviousAssignmentId);
        await RequireEffectAsync(observer, seed, "rollback", actor, default);
    }

    [Test]
    public async Task LocalSwitchPreservesOriginalRequestAssignmentAuditIdentity()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        await using var observer = fixture.Native.CreateDatabase();
        Guid auditActor = (await observer.InstanceBootstrapStates.AsNoTracking().SingleAsync()).CompletedByUserId!.Value;
        await Assert.That(auditActor).IsNotEqualTo(fixture.Binding.LocalSubjectId);
        var result = await ExecuteFreshAsync(fixture, await fixture.SignInAsync(),
            new SwitchInstanceTenantPlanAssignmentCommand(PlatformDefaults.DefaultTenantId, seed.PublishedVersionId, auditActor));
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(await observer.TenantPlanAssignments.Where(row => row.Id == result.Id)
            .Select(row => row.AssignedByUserId).SingleAsync()).IsEqualTo(auditActor);
    }

    private static async Task<BaseCommandResponse<Guid>> ExecuteFreshAsync(LocalIdentityLifecycleHttpFixture fixture,
        string credential, object command)
    {
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(credential);
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        return await InvokeAsync(request.Services, command);
    }

    private static async Task<string> VersionSnapshotAsync(ExploreDbContext context, Guid id) => JsonSerializer.Serialize(new
    {
        Version = await context.TenantPlanVersions.AsNoTracking().Where(row => row.Id == id)
            .Select(row => new
            {
                row.Id,
                row.TenantPlanId,
                row.VersionNumber,
                row.TenantPlanStatusId,
                row.PriceAmount,
                row.CurrencyCode,
                row.BillingPeriod,
                row.IsActiveForProvisioning,
                row.CreatedAt,
                row.CreatedBy,
                row.UpdatedAt,
                row.UpdatedBy
            }).SingleAsync(),
        Settings = await context.TenantPlanVersionSettings.AsNoTracking().Where(row => row.TenantPlanVersionId == id)
            .OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.SettingKey,
                row.JsonValue,
                row.IsLocked,
                row.CreatedAt,
                row.CreatedBy,
                row.UpdatedAt,
                row.UpdatedBy
            }).ToArrayAsync(),
        Quotas = await context.TenantPlanVersionQuotas.AsNoTracking().Where(row => row.TenantPlanVersionId == id)
            .OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.QuotaKey,
                row.Limit,
                row.CreatedAt,
                row.CreatedBy,
                row.UpdatedAt,
                row.UpdatedBy
            }).ToArrayAsync()
    });
}
