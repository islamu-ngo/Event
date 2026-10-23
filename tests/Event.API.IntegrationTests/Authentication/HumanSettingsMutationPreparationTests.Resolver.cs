using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("binding-revoked")]
    [Arguments("administrator-revoked")]
    [Arguments("principal-replaced")]
    [Arguments("request-replaced")]
    [Arguments("request-path-replaced")]
    [Arguments("support-header-replaced")]
    [Arguments("credential-replaced")]
    [Arguments("source-changed")]
    [Arguments("credential-expired")]
    [Arguments("binding-revoked-after-write")]
    [Arguments("administrator-revoked-after-write")]
    [Arguments("terminal-expiry")]
    [Arguments("extended-ticket-horizon")]
    [Arguments("unchanged-value")]
    public async Task ResolverMutationRetainsOriginalHumanAndSourcesThroughSettlement(string change)
    {
        var before = new ConsumerBeforeOwnerTransaction();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: before);
        Guid grant = await SeedPlatformAdministratorAsync(fixture);
        await SeedResolverAsync(fixture);
        if (change == "unchanged-value")
        {
            await using var seed = fixture.Native.CreateDatabase();
            await seed.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Routing.PathPrefix)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"/communities\""));
        }
        // This is another genuinely signed credential, not a fabricated lifetime.
        string replacement = change == "credential-replaced" ? await fixture.SignInAsync() : string.Empty;
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterResolverCustody));
        await using var request = await ResolverRequestAsync(host.Services, await fixture.SignInAsync());
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        var context = services.GetRequiredService<ExploreDbContext>();
        var probe = (ResolverWriterProbe)services.GetRequiredService<IResolverConfigurationSettingsWriter>();
        before.Owner = context;
        before.BeforeStart = async token =>
        {
            if (change == "principal-replaced")
                request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
            if (change == "request-replaced")
                services.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
                { User = request.Context.User, RequestServices = request.Context.RequestServices };
            if (change == "request-path-replaced")
                request.Context.Request.Path = "/api/instance/settings/other";
            if (change == "support-header-replaced")
                request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = Guid.CreateVersion7().ToString("D");
            if (change == "credential-replaced")
                request.Context.Request.Headers.Authorization = "Bearer " + replacement;
            await using var independent = fixture.Native.CreateDatabase();
            if (change == "binding-revoked")
                await independent.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteDeleteAsync(token);
            if (change == "administrator-revoked")
                await independent.PlatformUserRoles.Where(row => row.Id == grant).ExecuteDeleteAsync(token);
            if (change == "source-changed")
                await independent.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Routing.PathPrefix)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"/other\""), token);
            if (change == "credential-expired")
                ExpireResolverCredential(services, fixture);
        };
        probe.AfterWrite = async (_, token) =>
        {
            if (change == "binding-revoked-after-write")
                await context.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteDeleteAsync(token);
            if (change == "administrator-revoked-after-write")
                await context.PlatformUserRoles.Where(row => row.Id == grant).ExecuteDeleteAsync(token);
            if (change == "extended-ticket-horizon")
            {
                var lifetime = services.GetRequiredService<Explore.Persistence.Database.ExploreDbContextExecutionLease>().HumanLifetime!;
                var ticket = await request.Context.AuthenticateAsync();
                ticket.Properties!.ExpiresUtc = lifetime.ExpiresAt.AddYears(1);
                var identity = (ClaimsIdentity)request.Context.User.Identity!;
                var expiry = identity.FindFirst("exp");
                if (expiry is not null) identity.RemoveClaim(expiry);
                identity.AddClaim(new Claim("exp", lifetime.ExpiresAt.AddYears(1).ToUnixTimeSeconds().ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
            }
            if (change is "terminal-expiry" or "extended-ticket-horizon")
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                {
                    ExpireResolverCredential(services, fixture);
                    return Task.CompletedTask;
                }, _ => { });
        };
        var operation = ActivatorUtilities.CreateInstance<UpdateResolverConfigurationCommandHandler>(services)
            .ExecuteAsync(ResolverCommand(fixture), CancellationToken.None);
        if (change is "unchanged" or "unchanged-value")
            await Assert.That((await operation).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
        await using var observer = fixture.Native.CreateDatabase();
        var stored = await observer.SystemSettings.AsNoTracking().SingleAsync(
            row => row.SettingKey == GovernanceSettingKeys.Routing.PathPrefix);
        await Assert.That(stored.Value).IsEqualTo(change is "unchanged" or "unchanged-value" ? "\"/communities\""
            : change == "source-changed" ? "\"/other\"" : "\"/original\"");
        if (change is "unchanged" or "unchanged-value")
            await Assert.That(stored.UpdatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
        if (change.EndsWith("-after-write", StringComparison.Ordinal) || change is "terminal-expiry" or "extended-ticket-horizon")
        {
            await Assert.That(probe.Captured).IsNotNull();
            await Assert.That(await observer.UserExternalLogins.AnyAsync(
                row => row.Id == fixture.Binding.ExternalLoginId)).IsTrue();
            await Assert.That(await observer.PlatformUserRoles.AnyAsync(row => row.Id == grant)).IsTrue();
        }
        await Assert.That(before.Observed).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("value")]
    [Arguments("lock")]
    [Arguments("metadata")]
    [Arguments("created-actor")]
    [Arguments("updated-actor")]
    [Arguments("updated-time")]
    [Arguments("unpatched-source")]
    [Arguments("delete")]
    public async Task ResolverMutationRollsBackExactEffectAndUnpatchedProjectionTampering(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await SeedResolverAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterResolverCustody));
        await using var request = await ResolverRequestAsync(host.Services, await fixture.SignInAsync());
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        var context = services.GetRequiredService<ExploreDbContext>();
        var probe = (ResolverWriterProbe)services.GetRequiredService<IResolverConfigurationSettingsWriter>();
        probe.AfterWrite = async (_, token) =>
        {
            var rows = context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Routing.PathPrefix);
            switch (change)
            {
                case "value": await rows.ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"/substituted\""), token); break;
                case "lock": await rows.ExecuteUpdateAsync(update => update.SetProperty(row => row.IsLocked, true), token); break;
                case "metadata": await rows.ExecuteUpdateAsync(update => update.SetProperty(row => row.Description, "substituted"), token); break;
                case "created-actor": await rows.ExecuteUpdateAsync(update => update.SetProperty(row => row.CreatedBy, Guid.CreateVersion7()), token); break;
                case "updated-actor": await rows.ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedBy, Guid.CreateVersion7()), token); break;
                case "updated-time": await rows.ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAt, DateTime.UnixEpoch), token); break;
                case "delete": await rows.ExecuteDeleteAsync(token); break;
                case "unpatched-source":
                    await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Routing.ResolverPathEnabled)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "false"), token);
                    break;
            }
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            services.GetRequiredService<IResolverConfigService>().ApplyConfigurationAsync(ResolverCommand(fixture)));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Routing.PathPrefix)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"/original\"");
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Routing.ResolverPathEnabled)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("true");
        await Assert.That(probe.Captured).IsNotNull();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("non-administrator")]
    [Arguments("foreign-actor")]
    [Arguments("foreign-issuer")]
    [Arguments("foreign-unit")]
    [Arguments("tenant-owner")]
    public async Task ResolverMutationRejectsForeignAuthorityAndPhysicalOwners(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        if (change != "non-administrator") await SeedPlatformAdministratorAsync(fixture);
        await SeedResolverAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterResolverCustody));
        await using var request = await ResolverRequestAsync(host.Services, await fixture.SignInAsync());
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(change == "tenant-owner" ? new(PlatformDefaults.DefaultTenantId) : PolicyMutationScope.Instance,
                CancellationToken.None);
        var services = selection.Services;
        var command = ResolverCommand(fixture);
        if (change == "foreign-actor") command = command with { UserId = Guid.CreateVersion7() };
        if (change is "foreign-issuer" or "foreign-unit")
        {
            var authority = services.GetRequiredService<IResolverConfigurationMutationAuthority>();
            var source = await authority.CaptureAsync(command, CancellationToken.None);
            if (change == "foreign-issuer")
            {
                var other = ActivatorUtilities.CreateInstance<ResolverConfigurationMutationAuthority>(services);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                {
                    other.RequireIssued(source);
                    return Task.CompletedTask;
                });
            }
            else
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    services.GetRequiredService<IResolverConfigurationSettingsWriter>().ObserveAsync(
                        source, new EfCoreUnitOfWork(services.GetRequiredService<ExploreDbContext>()), CancellationToken.None));
        }
        else if (change == "tenant-owner")
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                services.GetRequiredService<IResolverConfigService>().ApplyConfigurationAsync(command));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                services.GetRequiredService<IResolverConfigService>().ApplyConfigurationAsync(command));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Routing.PathPrefix)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"/original\"");
    }

    [Test]
    public async Task ResolverCapsuleCannotReplayOrEscapeAfterSettlement()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await SeedResolverAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterResolverCustody));
        await using var request = await ResolverRequestAsync(host.Services, await fixture.SignInAsync());
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        var writer = (ResolverWriterProbe)services.GetRequiredService<IResolverConfigurationSettingsWriter>();
        writer.AfterWrite = async (mutation, token) =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ExecuteAsync(mutation, token));
            var replacementWriter = ActivatorUtilities.CreateInstance<ResolverConfigurationSettingsWriter>(services);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => replacementWriter.ExecuteAsync(mutation, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ResolverConfigurationMutation.CompileAsync(
                mutation.OriginalCommand with { }, mutation.Source, mutation.OriginalOwner, mutation.Before,
                fixture.Clock.GetUtcNow().UtcDateTime, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services
                .GetRequiredService<IHumanSettingsMutationPreparation>().RequireCurrentOwnedAsync(
                    Explore.Domain.Settings.SettingScope.Instance, null, null, "resolver.unrelated",
                    HumanSettingsMutationKind.SetValue, "denied", cancellationToken: token));
        };
        await Assert.That((await services.GetRequiredService<IResolverConfigService>()
            .ApplyConfigurationAsync(ResolverCommand(fixture))).IsSuccess).IsTrue();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => writer.ExecuteAsync(writer.Captured!, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<ISerializableAttemptOwner>()
            .ExecuteAsync(async token => { await writer.ExecuteAsync(writer.Captured!, token); return true; }));
    }

    [Test]
    public async Task ResolverPreparationCannotEscapeThroughAnExactValueGenericScalarWrite()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await SeedResolverAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterResolverCustody));
        await using var request = await ResolverRequestAsync(host.Services, await fixture.SignInAsync());
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        var command = ResolverCommand(fixture);
        var source = await services.GetRequiredService<IResolverConfigurationMutationAuthority>()
            .CaptureAsync(command, CancellationToken.None);
        var owner = services.GetRequiredService<IUnitOfWork>();
        var writer = services.GetRequiredService<IResolverConfigurationSettingsWriter>();
        var before = await writer.ObserveAsync(source, owner, CancellationToken.None);
        using var mutation = await ResolverConfigurationMutation.CompileAsync(command, source, owner, before,
            fixture.Clock.GetUtcNow().UtcDateTime, CancellationToken.None);
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var proof = await preparation.PrepareAsync(command, mutation);
        bool provisionallyWritten = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            preparation.ExecutePreparedAsync(proof, async (_, token) =>
            {
                var effect = mutation.Effects.Single();
                await services.GetRequiredService<ISystemSettingRepository>().UpsertInCurrentTransactionAsync(new()
                {
                    SettingKey = effect.Key,
                    Value = effect.Value,
                    ValueType = effect.ValueType,
                    UpdatedAt = effect.UpdatedAt,
                    UpdatedBy = effect.UpdatedBy,
                    Description = "A scalar write is not the original fixed resolver effect."
                }, token);
                provisionallyWritten = true;
                return true;
            }));
        await Assert.That(provisionallyWritten).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        var stored = await observer.SystemSettings.AsNoTracking().SingleAsync(
            row => row.SettingKey == GovernanceSettingKeys.Routing.PathPrefix);
        await Assert.That(ResolverConfigurationSettingSnapshot.From(stored)).IsEqualTo(before[stored.SettingKey]);
        await Assert.That(services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task ApplyConfigurationAsync_WhenOneLeafIsSupplied_WritesOnlyNormalizedKeyAndInvalidatesAfterWrite()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await SeedResolverAsync(fixture, pathEnabled: false);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterResolverCustody));
        await using var request = await ResolverRequestAsync(host.Services, await fixture.SignInAsync());
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        var cache = services.GetRequiredService<IMemoryCache>();
        cache.Remove("ResolverConfigService.Configuration");
        var service = services.GetRequiredService<IResolverConfigService>();
        await Assert.That((await service.GetConfigurationAsync()).PathPrefix).IsEqualTo("/original");
        var writer = (ResolverWriterProbe)services.GetRequiredService<IResolverConfigurationSettingsWriter>();
        writer.AfterWrite = async (mutation, _) =>
        {
            await Assert.That(mutation.Effects.Select(row => row.Key)).IsEquivalentTo([GovernanceSettingKeys.Routing.PathPrefix]);
            await Assert.That((await service.GetConfigurationAsync()).PathPrefix).IsEqualTo("/original");
        };
        var result = await service.ApplyConfigurationAsync(ResolverCommand(fixture) with
        {
            Patch = new() { PathPrefix = OptionalUpdate<string?>.Set(" tenants/ ") }
        });
        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That((await service.GetConfigurationAsync()).PathPrefix).IsEqualTo("/tenants");
        await using var observer = fixture.Native.CreateDatabase();
        var stored = await observer.SystemSettings.AsNoTracking().SingleAsync(
            row => row.SettingKey == GovernanceSettingKeys.Routing.PathPrefix);
        await Assert.That(stored.Value).IsEqualTo("\"/tenants\"");
        await Assert.That(stored.UpdatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Routing.ResolverPathEnabled)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("false");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AllResolverLeavesSettleTogetherOrRollBackWithOriginalAuthority(bool revokeAfterWrite)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid grant = await SeedPlatformAdministratorAsync(fixture);
        await SeedResolverAsync(fixture);
        ImmutableDictionary<string, ResolverConfigurationSettingSnapshot?> baseline;
        await using (var observer = fixture.Native.CreateDatabase())
        {
            var rows = await observer.SystemSettings.AsNoTracking().ToListAsync();
            baseline = ResolverConfigurationMutation.Keys.ToImmutableDictionary(key => key, key =>
            {
                var row = rows.SingleOrDefault(item => item.SettingKey == key);
                return row is null ? null : ResolverConfigurationSettingSnapshot.From(row);
            });
        }
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterResolverCustody));
        await using var request = await ResolverRequestAsync(host.Services, await fixture.SignInAsync());
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        var context = services.GetRequiredService<ExploreDbContext>();
        var writer = (ResolverWriterProbe)services.GetRequiredService<IResolverConfigurationSettingsWriter>();
        writer.AfterWrite = async (mutation, token) =>
        {
            await Assert.That(mutation.Effects.Length).IsEqualTo(7);
            if (revokeAfterWrite)
                await context.PlatformUserRoles.Where(row => row.Id == grant).ExecuteDeleteAsync(token);
        };
        var command = ResolverCommand(fixture) with
        {
            Patch = new()
            {
                HeaderEnabled = OptionalUpdate<bool>.Set(true),
                CustomDomainEnabled = OptionalUpdate<bool>.Set(true),
                SubdomainEnabled = OptionalUpdate<bool>.Set(true),
                PathEnabled = OptionalUpdate<bool>.Set(true),
                PathPrefix = OptionalUpdate<string?>.Set("/communities"),
                InstanceBaseDomain = OptionalUpdate<string?>.Set(" HTTPS://EXAMPLE.ORG/ "),
                AllowTenantCustomDomains = OptionalUpdate<bool>.Set(false)
            }
        };
        var operation = services.GetRequiredService<IResolverConfigService>().ApplyConfigurationAsync(command);
        if (revokeAfterWrite)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
        else
            await Assert.That((await operation).IsSuccess).IsTrue();
        await using var final = fixture.Native.CreateDatabase();
        var stored = await final.SystemSettings.AsNoTracking().ToListAsync();
        foreach (string key in ResolverConfigurationMutation.Keys)
        {
            var row = stored.SingleOrDefault(item => item.SettingKey == key);
            var expected = revokeAfterWrite ? baseline[key] : writer.Captured!.Effects.Single(effect => effect.Key == key);
            await Assert.That(row is null ? null : ResolverConfigurationSettingSnapshot.From(row)).IsEqualTo(expected);
        }
        if (!revokeAfterWrite)
        {
            await Assert.That(stored.Single(row => row.SettingKey == GovernanceSettingKeys.Domains.InstanceBaseDomain).Value)
                .IsEqualTo("\"example.org\"");
            await Assert.That(stored.Single(row => row.SettingKey == GovernanceSettingKeys.Routing.ResolverHeaderEnabled).IsLocked)
                .IsTrue();
        }
        await Assert.That(await final.PlatformUserRoles.AnyAsync(row => row.Id == grant)).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("header-disabled")]
    [Arguments("invalid-path")]
    [Arguments("domain-required")]
    [Arguments("empty")]
    public async Task ResolverRetainsOriginalValidationAndRejectsInvalidGroups(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await SeedResolverAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterResolverCustody));
        await using var request = await ResolverRequestAsync(host.Services, await fixture.SignInAsync());
        var command = ResolverCommand(fixture) with
        {
            Patch = change switch
            {
                "header-disabled" => new() { HeaderEnabled = OptionalUpdate<bool>.Set(false) },
                "invalid-path" => new() { PathPrefix = OptionalUpdate<string?>.Set("invalid/") },
                "domain-required" => new()
                {
                    SubdomainEnabled = OptionalUpdate<bool>.Set(true),
                    InstanceBaseDomain = OptionalUpdate<string?>.Set(null)
                },
                _ => new()
            }
        };
        var response = await ActivatorUtilities.CreateInstance<UpdateResolverConfigurationCommandHandler>(request.Services)
            .ExecuteAsync(command, CancellationToken.None);
        await Assert.That(response.IsSuccess).IsFalse();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Routing.PathPrefix)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"/original\"");
    }

    [Test]
    public async Task ActualResolverPatchUsesTheClosedHumanMutationWithRealAuthentication()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await SeedResolverAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterResolverCustody));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/instance/settings/resolver-config")
        {
            Content = JsonContent.Create(ResolverCommand(fixture).Patch)
        };
        request.Headers.Authorization = new("Bearer", await fixture.SignInAsync());
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using var observer = fixture.Native.CreateDatabase();
        var row = await observer.SystemSettings.AsNoTracking().SingleAsync(
            item => item.SettingKey == GovernanceSettingKeys.Routing.PathPrefix);
        await Assert.That(row.Value).IsEqualTo("\"/communities\"");
        await Assert.That(row.UpdatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
    }

    [Test]
    public async Task ActualAcceptedSetupSecretDoesNotCreateResolverMutationAuthority()
    {
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        await using var host = native.WithWebHostBuilder(builder => builder.ConfigureTestServices(RegisterResolverCustody));
        await using var request = await ResolverRequestAsync(host.Services, null, native.SetupSecret);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => request.Services
            .GetRequiredService<IResolverConfigurationMutationAuthority>().CaptureAsync(
                new() { UserId = Guid.CreateVersion7(), Patch = new() { PathEnabled = OptionalUpdate<bool>.Set(false) } },
                CancellationToken.None));
    }

    private static UpdateResolverConfigurationCommand ResolverCommand(LocalIdentityLifecycleHttpFixture fixture) =>
        new()
        {
            UserId = fixture.Binding.LocalSubjectId,
            Patch = new() { PathPrefix = OptionalUpdate<string?>.Set("/communities") }
        };

    private static async Task SeedResolverAsync(LocalIdentityLifecycleHttpFixture fixture, bool pathEnabled = true)
    {
        await using var seed = fixture.Native.CreateDatabase();
        foreach (var pair in new[]
        {
            (GovernanceSettingKeys.Routing.PathPrefix, "\"/original\"", SettingValueType.String),
            (GovernanceSettingKeys.Routing.ResolverPathEnabled, pathEnabled ? "true" : "false", SettingValueType.Boolean)
        })
        {
            var row = await seed.SystemSettings.SingleOrDefaultAsync(item => item.SettingKey == pair.Item1);
            if (row is null) seed.SystemSettings.Add(new() { SettingKey = pair.Item1, Value = pair.Item2, ValueType = pair.Item3 });
            else { row.Value = pair.Item2; row.ValueType = pair.Item3; row.IsLocked = false; }
        }
        await seed.SaveChangesAsync();
    }

    private static void ExpireResolverCredential(IServiceProvider services, LocalIdentityLifecycleHttpFixture fixture)
    {
        var lifetime = services.GetRequiredService<Explore.Persistence.Database.ExploreDbContextExecutionLease>().HumanLifetime!;
        fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
    }

    private static void RegisterResolverCustody(IServiceCollection services)
    {
        services.AddScoped<IResolverConfigurationMutationAuthority, ResolverConfigurationMutationAuthority>();
        services.AddScoped<ResolverConfigurationSettingsWriter>();
        services.AddScoped<IResolverConfigurationSettingsWriter, ResolverWriterProbe>();
    }

    private static async Task<Request> ResolverRequestAsync(IServiceProvider services, string? bearer, string? setupSecret = null)
    {
        var scope = services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = HttpMethods.Patch;
        context.Request.Path = "/api/instance/settings/resolver-config";
        if (bearer is not null) context.Request.Headers.Authorization = "Bearer " + bearer;
        if (setupSecret is not null) context.Request.Headers["X-Setup-Secret"] = setupSecret;
        accessor.HttpContext = context;
        var authentication = await context.AuthenticateAsync();
        if (!authentication.Succeeded)
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
            throw new InvalidOperationException("The actual resolver request did not authenticate.");
        }
        context.User = authentication.Principal!;
        return new(scope, accessor, context);
    }

    private sealed class ResolverWriterProbe(ResolverConfigurationSettingsWriter actual) : IResolverConfigurationSettingsWriter
    {
        public DateTime NormalizeEffectTimestamp(DateTime timestamp) =>
            actual.NormalizeEffectTimestamp(timestamp);
        internal ResolverConfigurationMutation? Captured { get; private set; }
        internal Func<ResolverConfigurationMutation, CancellationToken, Task>? AfterWrite { get; set; }
        public Task<ImmutableDictionary<string, ResolverConfigurationSettingSnapshot?>> ObserveAsync(
            IResolverConfigurationMutationSource source, IUnitOfWork owner, CancellationToken token) =>
            actual.ObserveAsync(source, owner, token);
        public Task RequireAppliedAsync(ResolverConfigurationMutation mutation, CancellationToken token) =>
            actual.RequireAppliedAsync(mutation, token);
        public async Task ExecuteAsync(ResolverConfigurationMutation mutation, CancellationToken token)
        {
            await actual.ExecuteAsync(mutation, token);
            Captured = mutation;
            if (AfterWrite is { } callback)
            {
                AfterWrite = null;
                await callback(mutation, token);
            }
        }
    }
}
