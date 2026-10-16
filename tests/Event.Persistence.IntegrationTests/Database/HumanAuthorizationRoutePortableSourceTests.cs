using System.Data;
using System.Collections.Immutable;
using System.Security.Claims;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Database;

public sealed class HumanAuthorizationRoutePortableSourceTests
{
    [Test]
    public async Task OwnTransactionWritesCompareEffectiveRoutesInsteadOfRawRows()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection).UseSnakeCaseNamingConvention();
        await using var context = new ExploreDbContext(options.Options);
        await context.Database.EnsureCreatedAsync();
        context.Set<SettingValueTypeLookup>().Add(new()
        {
            Id = (int)SettingValueType.String,
            MasterCode = "string",
            FullName = "String"
        });
        await context.SaveChangesAsync();
        var subject = (await HumanCommitSubject.ResolveAsync(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "route-source"), new Claim("iss", "https://route.invalid")], "fixture")),
            new ResolvedIdentity(), CancellationToken.None))!;
        var source = new HumanAuthorizationRouteSource(context, subject, PolicySourceSetPurpose.Global);
        var fence = new PolicySourceSetFence(context, PolicySourceSetPurpose.Global);
        var deployment = new HumanAuthorizationRouteDeployment(null, "http://localhost:3593", true);
        var prepared = await source.ObserveAsync();
        await Assert.That(prepared.Settings.All(row => row.System is null && row.Tenant is null)).IsTrue();
        var initial = HumanAuthorizationRouteProjector.Project(prepared, deployment, true, true);
        await Assert.That(initial.Provider).IsEqualTo(HumanAuthorizationRouteProvider.Local);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var held = await source.AcquireAsync(await fence.AcquireAsync());
        // Store a previously absent row with different raw bytes but identical effective Local mode.
        var setting = new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
            Value = "\" LOCAL \"",
            ValueType = SettingValueType.String,
            CreatedAt = DateTime.UtcNow
        };
        context.SystemSettings.Add(setting);
        await context.SaveChangesAsync();
        await Assert.That(HumanAuthorizationRouteProjector.Project(await held.ValidateAsync(), deployment, true, true))
            .IsEqualTo(initial);
        setting.Value = "\"cerbos\"";
        await context.SaveChangesAsync();
        var changed = HumanAuthorizationRouteProjector.Project(await held.ValidateAsync(), deployment, true, true);
        await Assert.That(changed.Provider).IsEqualTo(HumanAuthorizationRouteProvider.CerbosInstance);
        await Assert.That(changed).IsNotEqualTo(initial);
        await Assert.That(changed.PolicyVersion).IsNull();
        // A fenced but non-effective SDK endpoint row must not replace deployment options.
        context.SystemSettings.Add(new()
        {
            Id = Guid.CreateVersion7(),
            SettingKey = GovernanceSettingKeys.Cerbos.GrpcEndpoint,
            Value = "\"https://not-the-sdk-client.invalid:3593\"",
            ValueType = SettingValueType.String,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        await Assert.That(HumanAuthorizationRouteProjector.Project(await held.ValidateAsync(), deployment, true, true))
            .IsEqualTo(changed);
        await transaction.RollbackAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => held.ValidateAsync());
    }

    [Test]
    public async Task ProjectionPreservesLocksTypedFallbackSettingsGovernanceAndDeployment()
    {
        var facts = new HumanAuthorizationRouteFacts(Guid.CreateVersion7(), Guid.CreateVersion7(),
            HumanAuthorizationRouteFacts.Keys.Select(key => new HumanAuthorizationRouteSettingFact(key,
                key == GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled
                    ? new("true", SettingValueType.Boolean, false, null, null, null) : null,
                key == GovernanceSettingKeys.Cerbos.Mode ? new("\" CUSTOM_ENDPOINT \"", false)
                    : key == GovernanceSettingKeys.Cerbos.CustomEndpoint ? new("\"https://tenant.invalid:3593\"", false)
                    : null)).ToImmutableArray());
        var deployment = new HumanAuthorizationRouteDeployment("local", "http://localhost:3593", true);
        var byo = HumanAuthorizationRouteProjector.Project(facts, deployment, false);
        await Assert.That(byo.Provider).IsEqualTo(HumanAuthorizationRouteProvider.CerbosTenant);
        await Assert.That(byo.Endpoint).IsEqualTo("https://tenant.invalid:3593");
        await Assert.That(byo.PolicyScope).IsEqualTo(facts.SelectedTenantId!.Value.ToString("D"));
        var adminChange = facts with
        {
            Settings = facts.Settings.Select(row => row.Key == GovernanceSettingKeys.Cerbos.CustomAdminEndpoint
                ? row with { Tenant = new("\"https://admin.invalid:3592\"", false) } : row).ToImmutableArray()
        };
        var withAdmin = HumanAuthorizationRouteProjector.Project(adminChange, deployment, false);
        await Assert.That(withAdmin.AdminEndpoint).IsEqualTo("https://admin.invalid:3592");
        await Assert.That(byo.IsSemanticallyEquivalentTo(withAdmin)).IsTrue();
        var settings = HumanAuthorizationRouteProjector.Project(facts, deployment, true, true);
        await Assert.That(settings.Provider).IsEqualTo(HumanAuthorizationRouteProvider.Local);
        await Assert.That(HumanAuthorizationRouteProjector.Project(facts, deployment, true).Provider)
            .IsEqualTo(HumanAuthorizationRouteProvider.Denied);
        var locked = facts with
        {
            Settings = facts.Settings.Select(row => row.Key == GovernanceSettingKeys.Cerbos.Mode
                ? row with { System = new("\"shared\"", SettingValueType.String, true, null, null, null) }
                : row).ToImmutableArray()
        };
        await Assert.That(HumanAuthorizationRouteProjector.Project(locked, deployment, false, true).Provider)
            .IsEqualTo(HumanAuthorizationRouteProvider.Local);
        var malformed = facts with
        {
            Settings = facts.Settings.Select(row => row.Key == GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled
                ? row with { System = new("not-a-boolean", SettingValueType.String, false, null, null, null) }
                : row).ToImmutableArray()
        };
        await Assert.That(HumanAuthorizationRouteProjector.Project(malformed, deployment, false, true).Provider)
            .IsEqualTo(HumanAuthorizationRouteProvider.Local);
        var missingCustom = facts with
        {
            Settings = facts.Settings.Select(row => row.Key == GovernanceSettingKeys.Cerbos.CustomEndpoint
                ? row with { Tenant = null } : row).ToImmutableArray()
        };
        await Assert.That(HumanAuthorizationRouteProjector.Project(missingCustom, deployment, false, true).Provider)
            .IsEqualTo(HumanAuthorizationRouteProvider.Denied);
    }

    [Test]
    public async Task ResolvedGuidWithoutProviderAccountCannotSelectRouteOwner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection).UseSnakeCaseNamingConvention();
        await using var context = new ExploreDbContext(options.Options);
        var subject = (await HumanCommitSubject.ResolveAsync(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString("D")),
                new Claim(ClaimTypes.Email, "unbound@route.invalid")], "fixture")),
            new ResolvedIdentity(), CancellationToken.None))!;
        await Assert.That(subject.AccountKey).IsNull();
        Assert.Throws<InvalidOperationException>(() =>
            new HumanAuthorizationRouteSource(context, subject, PolicySourceSetPurpose.Global));
    }

    private sealed class ResolvedIdentity : IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>
    {
        public Task<Guid?> QueryAsync(ResolveCurrentUserIdByIdentityRequest query, CancellationToken cancellationToken = default) =>
            Task.FromResult<Guid?>(Guid.Parse("018e4e5c-7f00-7000-8000-000000000001"));
    }
}
