using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class TenantPlanCatalogueOriginalAdmissionTests
{
    private static TenantPlanDraft Draft(string key) => new(key, "Catalogue plan", new(20, "EUR", "monthly"), true,
        [new(GovernanceSettingKeys.PublicExperience.EventCatalogLabel, "\"Draft events\"", false)],
        [new(TenantPlanQuotaKeys.AiDailyTenantMessages, 29)]);

    private sealed record Seeded(Guid PlanId, string Key, string CloneKey, Guid PublishedVersionId,
        Guid DraftVersionId, Guid CurrentVersionId, Guid CurrentAssignmentId, Guid PreviousAssignmentId,
        DateTime AssignedAt, Guid GrantId, Guid? SupportId, DateTimeOffset SupportExpiry);

    private static async Task<Seeded> SeedAsync(LocalIdentityLifecycleHttpFixture fixture, bool support = false)
    {
        await using var context = fixture.Native.CreateDatabase();
        context.BindTenantLease();
        DateTime now = fixture.Clock.GetUtcNow().UtcDateTime;
        var plan = new TenantPlan
        {
            Id = Guid.CreateVersion7(),
            Key = "catalogue-" + Guid.CreateVersion7().ToString("N"),
            DisplayName = "Catalogue plan",
            CreatedAt = now
        };
        TenantPlanVersion Version(int number, TenantPlanStatusEnum status, decimal price, string label, long quota)
        {
            var version = new TenantPlanVersion
            {
                Id = Guid.CreateVersion7(),
                TenantPlanId = plan.Id,
                TenantPlan = plan,
                VersionNumber = number,
                TenantPlanStatusId = (int)status,
                PriceAmount = price,
                CurrencyCode = "EUR",
                BillingPeriod = "monthly",
                IsActiveForProvisioning = true,
                CreatedAt = now
            };
            version.Settings.Add(new()
            {
                Id = Guid.CreateVersion7(),
                TenantPlanVersionId = version.Id,
                SettingKey = GovernanceSettingKeys.PublicExperience.EventCatalogLabel,
                JsonValue = label,
                IsLocked = false,
                CreatedAt = now
            });
            version.Quotas.Add(new()
            {
                Id = Guid.CreateVersion7(),
                TenantPlanVersionId = version.Id,
                QuotaKey = TenantPlanQuotaKeys.AiDailyTenantMessages,
                Limit = quota,
                CreatedAt = now
            });
            plan.Versions.Add(version);
            return version;
        }
        var published = Version(1, TenantPlanStatusEnum.Published, 5, "\"Plan events\"", 17);
        var draft = Version(2, TenantPlanStatusEnum.Draft, 20, "\"Draft events\"", 29);
        var currentVersion = Version(3, TenantPlanStatusEnum.Published, 30, "\"Current events\"", 37);
        DateTime assignedAt = now.AddMinutes(-10);
        TenantPlanAssignment Assignment(TenantPlanVersion version, TenantPlanAssignmentStatusEnum status,
            DateTime started, DateTime? ended) => new()
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                TenantPlanId = plan.Id,
                TenantPlan = plan,
                TenantPlanVersionId = version.Id,
                TenantPlanVersion = version,
                TenantPlanAssignmentStatusId = (int)status,
                AssignedByUserId = fixture.Binding.LocalSubjectId,
                AssignedAt = started,
                EndedAt = ended,
                CreatedAt = now
            };
        var previous = Assignment(published, TenantPlanAssignmentStatusEnum.Superseded, now.AddHours(-1), assignedAt);
        var current = Assignment(currentVersion, TenantPlanAssignmentStatusEnum.Active, assignedAt, null);
        Guid grantId = Guid.CreateVersion7();
        context.PlatformUserRoles.Add(new()
        {
            Id = grantId,
            UserId = fixture.Binding.LocalSubjectId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = now
        });
        var member = await context.TenantUsers.SingleOrDefaultAsync(row =>
            row.UserId == fixture.Binding.LocalSubjectId && row.TenantId == PlatformDefaults.DefaultTenantId);
        if (member is null)
        {
            member = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = now
            };
            context.TenantUsers.Add(member);
        }
        context.TenantUserRoleGrants.Add(new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = member.TenantId,
            Tenant = null!,
            TenantUserId = member.Id,
            TenantUser = member,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = now
        });
        Guid? supportId = null;
        DateTimeOffset supportExpiry = fixture.Clock.GetUtcNow().AddMinutes(5);
        var settings = new List<(string Key, string Value)>
        {
            (GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\""),
            (GovernanceSettingKeys.Security.AuthorizationProvider, "\"local\"")
        };
        if (support)
        {
            settings.Add((GovernanceSettingKeys.SupportAccess.Enabled, "true"));
            settings.Add((GovernanceSettingKeys.SupportAccess.AllowWriteMode, "true"));
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Catalogue admission", "catalogue-ticket",
                fixture.Clock.GetUtcNow().AddMinutes(-1), supportExpiry);
            context.SupportAccessSessions.Add(session);
            supportId = session.Id;
        }
        foreach (var (key, value) in settings)
        {
            var setting = await context.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (setting is null) context.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = value });
            else setting.Value = value;
        }
        context.AddRange(plan, previous, current);
        (await context.InstanceBootstrapStates.SingleAsync()).TransitionDeploymentMode(DeploymentMode.MultiTenant);
        await context.SaveChangesAsync();
        await fixture.Host.Services.GetRequiredService<IDeploymentModeProvider>().InvalidateCacheAsync();
        return new(plan.Id, plan.Key, "clone-" + Guid.CreateVersion7().ToString("N"), published.Id, draft.Id,
            currentVersion.Id, current.Id, previous.Id, assignedAt, grantId, supportId, supportExpiry);
    }

    private static async Task<string> SnapshotAsync(ExploreDbContext context, Seeded seed,
        CancellationToken token = default) => JsonSerializer.Serialize(new
        {
            Plans = await context.TenantPlans.AsNoTracking().Where(row => row.Id == seed.PlanId || row.Key == seed.CloneKey)
            .OrderBy(row => row.Key).Select(row => new
            {
                row.Id,
                row.Key,
                row.DisplayName,
                row.CreatedAt,
                row.CreatedBy,
                row.UpdatedAt,
                row.UpdatedBy
            }).ToArrayAsync(token),
            Versions = await context.TenantPlanVersions.AsNoTracking()
            .Where(row => row.TenantPlanId == seed.PlanId || row.TenantPlan.Key == seed.CloneKey)
            .OrderBy(row => row.Id).Select(row => new
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
            }).ToArrayAsync(token),
            Settings = await context.TenantPlanVersionSettings.AsNoTracking()
            .Where(row => row.TenantPlanVersion.TenantPlanId == seed.PlanId || row.TenantPlanVersion.TenantPlan.Key == seed.CloneKey)
            .OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.TenantPlanVersionId,
                row.SettingKey,
                row.JsonValue,
                row.IsLocked,
                row.CreatedAt,
                row.CreatedBy,
                row.UpdatedAt,
                row.UpdatedBy
            }).ToArrayAsync(token),
            Quotas = await context.TenantPlanVersionQuotas.AsNoTracking()
            .Where(row => row.TenantPlanVersion.TenantPlanId == seed.PlanId || row.TenantPlanVersion.TenantPlan.Key == seed.CloneKey)
            .OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.TenantPlanVersionId,
                row.QuotaKey,
                row.Limit,
                row.CreatedAt,
                row.CreatedBy,
                row.UpdatedAt,
                row.UpdatedBy
            }).ToArrayAsync(token),
            Assignments = await context.TenantPlanAssignments.AsNoTracking().Where(row => row.TenantPlanId == seed.PlanId)
            .OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.TenantId,
                row.TenantPlanId,
                row.TenantPlanVersionId,
                row.TenantPlanAssignmentStatusId,
                row.AssignedByUserId,
                row.AssignedAt,
                row.EndedAt,
                row.CreatedAt,
                row.CreatedBy,
                row.UpdatedAt,
                row.UpdatedBy
            }).ToArrayAsync(token)
        });

    private static async Task RequireEffectAsync(ExploreDbContext context, Seeded seed, string operation,
        Guid actor, CancellationToken token)
    {
        var versions = await context.TenantPlanVersions.AsNoTracking().Include(row => row.Settings).Include(row => row.Quotas)
            .Where(row => row.TenantPlanId == seed.PlanId || row.TenantPlan.Key == seed.CloneKey).ToListAsync(token);
        var assignments = await context.TenantPlanAssignments.AsNoTracking().Where(row => row.TenantPlanId == seed.PlanId)
            .ToListAsync(token);
        var published = versions.Single(row => row.Id == seed.PublishedVersionId);
        var draft = versions.Single(row => row.Id == seed.DraftVersionId);
        var current = assignments.Single(row => row.Id == seed.CurrentAssignmentId);
        var previous = assignments.Single(row => row.Id == seed.PreviousAssignmentId);
        await Assert.That(published.PriceAmount).IsEqualTo(5m);
        await Assert.That(published.Settings.Single().JsonValue).IsEqualTo("\"Plan events\"");
        await Assert.That(published.Quotas.Single().Limit).IsEqualTo(17L);
        await Assert.That(current.AssignedAt).IsEqualTo(seed.AssignedAt);
        switch (operation)
        {
            case "clone":
                var clone = versions.Single(row => row.TenantPlanId != seed.PlanId);
                await Assert.That(clone.VersionNumber).IsEqualTo(1);
                await Assert.That(clone.TenantPlanStatusId).IsEqualTo((int)TenantPlanStatusEnum.Draft);
                await Assert.That(clone.PriceAmount).IsEqualTo(5m);
                await Assert.That(clone.Settings.Single().JsonValue).IsEqualTo("\"Plan events\"");
                await Assert.That(clone.Quotas.Single().Limit).IsEqualTo(17L);
                break;
            case "create-version":
                var created = versions.Single(row => row.VersionNumber == 4);
                await Assert.That(created.TenantPlanStatusId).IsEqualTo((int)TenantPlanStatusEnum.Draft);
                await Assert.That(created.PriceAmount).IsEqualTo(20m);
                await Assert.That(created.Settings.Single().JsonValue).IsEqualTo("\"Draft events\"");
                await Assert.That(created.Quotas.Single().Limit).IsEqualTo(29L);
                break;
            case "update-version":
                await Assert.That(draft.TenantPlanStatusId).IsEqualTo((int)TenantPlanStatusEnum.Draft);
                await Assert.That(draft.PriceAmount).IsEqualTo(12m);
                await Assert.That(draft.Settings.Single().JsonValue).IsEqualTo("\"Updated events\"");
                await Assert.That(draft.Quotas.Single().Limit).IsEqualTo(29L);
                break;
            case "publish":
                await Assert.That(draft.TenantPlanStatusId).IsEqualTo((int)TenantPlanStatusEnum.Published);
                await Assert.That(draft.PriceAmount).IsEqualTo(20m);
                await Assert.That(draft.Settings.Single().JsonValue).IsEqualTo("\"Draft events\"");
                await Assert.That(draft.Quotas.Single().Limit).IsEqualTo(29L);
                await Assert.That(current.TenantPlanVersionId).IsEqualTo(seed.DraftVersionId);
                await Assert.That(current.TenantPlanAssignmentStatusId).IsEqualTo((int)TenantPlanAssignmentStatusEnum.Active);
                await Assert.That(previous.TenantPlanVersionId).IsEqualTo(seed.PublishedVersionId);
                break;
            case "archive":
                await Assert.That(published.TenantPlanStatusId).IsEqualTo((int)TenantPlanStatusEnum.Archived);
                await Assert.That(published.IsActiveForProvisioning).IsFalse();
                break;
            case "switch":
                await Assert.That(current.TenantPlanAssignmentStatusId).IsEqualTo((int)TenantPlanAssignmentStatusEnum.Superseded);
                await Assert.That(current.EndedAt).IsNotNull();
                var active = assignments.Single(row => row.TenantPlanAssignmentStatusId == (int)TenantPlanAssignmentStatusEnum.Active);
                await Assert.That(active.Id).IsNotEqualTo(seed.CurrentAssignmentId);
                await Assert.That(active.TenantPlanVersionId).IsEqualTo(seed.PublishedVersionId);
                await Assert.That(active.AssignedByUserId).IsEqualTo(actor);
                await Assert.That(active.EndedAt).IsNull();
                break;
            case "rollback":
                await Assert.That(current.TenantPlanAssignmentStatusId).IsEqualTo((int)TenantPlanAssignmentStatusEnum.RolledBack);
                await Assert.That(current.EndedAt).IsNotNull();
                await Assert.That(previous.TenantPlanAssignmentStatusId).IsEqualTo((int)TenantPlanAssignmentStatusEnum.Active);
                await Assert.That(previous.EndedAt).IsNull();
                await Assert.That(assignments.Count).IsEqualTo(2);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
        if (operation is not ("archive" or "rollback"))
            await Assert.That(published.TenantPlanStatusId).IsEqualTo((int)TenantPlanStatusEnum.Published);
        if (operation is not ("publish" or "switch" or "rollback"))
        {
            await Assert.That(current.TenantPlanVersionId).IsEqualTo(seed.CurrentVersionId);
            await Assert.That(current.TenantPlanAssignmentStatusId).IsEqualTo((int)TenantPlanAssignmentStatusEnum.Active);
            await Assert.That(current.EndedAt).IsNull();
        }
    }
}
