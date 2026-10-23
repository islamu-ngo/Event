using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TenantSettingsDocuments;
using Explore.Application.Exceptions;
using Explore.Application.Models.Common;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class TenantBrandingCommitAdmissionTests
{
    [Test]
    [Arguments("stale")]
    [Arguments("invalid")]
    [Arguments("locked")]
    [Arguments("clear")]
    public async Task BusinessValidationAndImmutablePatchRetainTheirOriginalOutcomes(string scenario)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seeded = await SeedAsync(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            if (scenario == "locked")
                await seed.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Branding.DisplayName)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLocked, true));
            if (scenario == "clear")
                await seed.TenantSettingsDocuments.Where(row => row.TenantId == PlatformDefaults.DefaultTenantId
                        && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.PayloadJson,
                        "{\"displayName\":\"Original\",\"logoUrl\":\"https://branding.example/retained-logo\"}"));
        }
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var context = services.GetRequiredService<ExploreDbContext>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var command = Command(scenario == "stale" ? Guid.CreateVersion7() : seeded.Stamp);
        if (scenario is "clear" or "invalid")
            command = command with
            {
                Patch = new PatchTenantBrandingSettingsDocumentDto
                {
                    ExpectedConcurrencyStamp = seeded.Stamp,
                    DisplayName = new() { Value = OptionalUpdate<string?>.Set(null) },
                    Assets = scenario == "invalid" ? new()
                    {
                        LogoUrl = OptionalUpdate<string?>.Set(new string('x', 2049))
                    } : null
                }
            };
        var execution = Handler(services, (ISerializableAttemptOwner)new EfCoreUnitOfWork(context),
            new TypedSettingsDocumentResolver(new TenantSettingsDocumentRepository(context), cache),
            Preparation(services, fixture.Clock)).ExecuteAsync(command, CancellationToken.None);
        if (scenario == "stale")
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => execution);
        else
            await Assert.That((await execution).IsSuccess).IsEqualTo(scenario == "clear");
        await using var observer = fixture.Native.CreateDatabase();
        var document = await observer.TenantSettingsDocuments.AsNoTracking().SingleAsync(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding);
        await Assert.That(document.ConcurrencyStamp == seeded.Stamp).IsEqualTo(scenario != "clear");
        if (scenario == "clear")
        {
            using var payload = System.Text.Json.JsonDocument.Parse(document.PayloadJson);
            await Assert.That(payload.RootElement.GetProperty("displayName").ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Null);
            await Assert.That(payload.RootElement.GetProperty("logoUrl").GetString()).IsEqualTo("https://branding.example/retained-logo");
            await Assert.That(command.Patch.DisplayName!.Value.Value).IsNull();
        }
    }

    [Test]
    public async Task PlatformAdministratorCannotReplaceTheOriginalTenantSettingGrant()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seeded = await SeedAsync(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await seed.TenantUserRoleGrants.Where(row => row.Id == seeded.GrantId)
                .ExecuteDeleteAsync();
            seed.PlatformUserRoles.Add(new Explore.Domain.PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            await seed.SaveChangesAsync();
        }
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Services;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var context = services.GetRequiredService<ExploreDbContext>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var response = await Handler(services, (ISerializableAttemptOwner)new EfCoreUnitOfWork(context),
            new TypedSettingsDocumentResolver(new TenantSettingsDocumentRepository(context), cache),
            Preparation(services, fixture.Clock)).ExecuteAsync(Command(seeded.Stamp), CancellationToken.None);
        await Assert.That(response.IsSuccess).IsFalse();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantSettingsDocuments.Where(row =>
                row.TenantId == PlatformDefaults.DefaultTenantId && row.DocumentKey == SettingsDocumentKeys.Tenant.Branding)
            .Select(row => row.ConcurrencyStamp).SingleAsync()).IsEqualTo(seeded.Stamp);
    }
}
