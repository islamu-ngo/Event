using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Commands;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Http.Json;
using Explore.Domain.Constants;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments(false, "unchanged")]
    [Arguments(false, "binding-revoked")]
    [Arguments(false, "administrator-revoked")]
    [Arguments(false, "principal-replaced")]
    [Arguments(false, "terminal-expiry")]
    [Arguments(true, "unchanged")]
    [Arguments(true, "binding-revoked")]
    [Arguments(true, "terminal-expiry")]
    public async Task OperatorIdentityEditorRetainsOriginalHumanThroughSettlement(bool setup, string change)
    {
        var before = new ConsumerBeforeOwnerTransaction();
        var terminal = new SpecializedBeforeSettlement(InstanceOperatorIdentitySettingKeys.OperatorIdentity);
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            interceptor: change == "terminal-expiry" ? terminal : before);
        Guid grant = setup ? Guid.Empty : await SeedPlatformAdministratorAsync(fixture);
        if (setup)
        {
            await using var pending = fixture.Native.CreateDatabase();
            await pending.InstanceBootstrapStates.ExecuteDeleteAsync();
            pending.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(), DeploymentMode.SingleTenant, fixture.Clock.GetUtcNow().UtcDateTime));
            await pending.SaveChangesAsync();
        }
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
        {
            if (setup)
                builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                    new Dictionary<string, string?> { ["SETUP_SECRET"] = fixture.Native.SetupSecret }));
            builder.ConfigureTestServices(services =>
            {
                RegisterOperatorIdentityCustody(services);
                // Exercise the controller's authenticated-human + accepted-secret alternative
                // using the real signed Local handler, not a claimed scheme or fake lifetime.
                if (setup)
                    services.Configure<AuthenticationOptions>(options =>
                        options.DefaultAuthenticateScheme = ApiAuthenticationSchemeNames.LocalIdentity);
            });
        });
        await using var request = await OperatorIdentityRequestAsync(host.Services, await fixture.SignInAsync(),
            setup ? fixture.Native.SetupSecret : null);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        var context = services.GetRequiredService<ExploreDbContext>();
        before.Owner = context;
        before.BeforeStart = async token =>
        {
            if (change == "principal-replaced")
                request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
            await using var independent = fixture.Native.CreateDatabase();
            if (change == "binding-revoked")
                await independent.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteDeleteAsync(token);
            if (change == "administrator-revoked")
                await independent.PlatformUserRoles.Where(row => row.Id == grant).ExecuteDeleteAsync(token);
        };
        terminal.Owner = context;
        terminal.BeforeSettlement = () =>
        {
            var lifetime = services.GetRequiredService<Explore.Persistence.Database.ExploreDbContextExecutionLease>()
                .HumanLifetime!;
            services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
            {
                fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                    - fixture.Clock.GetUtcNow());
                return Task.CompletedTask;
            }, _ => { });
        };
        var execution = ActivatorUtilities.CreateInstance<SaveInstanceOperatorIdentityCommandHandler>(services)
            .ExecuteAsync(new() { Request = OperatorDraft() }, CancellationToken.None);
        if (change == "unchanged")
            await Assert.That((await execution).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = fixture.Native.CreateDatabase();
        var row = await observer.SystemSettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity);
        await Assert.That(row is not null).IsEqualTo(change == "unchanged");
        if (row is not null)
            await Assert.That(row.UpdatedBy ?? row.CreatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(change == "terminal-expiry" ? terminal.Observed : before.Observed).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("unchanged")]
    [Arguments("generation-replaced")]
    [Arguments("secret-locked")]
    [Arguments("header-replaced")]
    [Arguments("principal-replaced")]
    [Arguments("secret-locked-after-write")]
    [Arguments("generation-replaced-after-write")]
    public async Task OperatorIdentityInteractiveEditRetainsActualSetupTicket(string change)
    {
        var before = new ConsumerBeforeOwnerTransaction();
        var terminal = new SpecializedBeforeSettlement(InstanceOperatorIdentitySettingKeys.OperatorIdentity);
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(
            incompleteSetup: true, persistenceInterceptor: change == "secret-locked" ? terminal : before);
        await using (var seed = native.CreateDatabase())
        {
            seed.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(), DeploymentMode.SingleTenant, DateTime.UtcNow));
            await seed.SaveChangesAsync();
        }
        await using var host = native.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                RegisterOperatorIdentityCustody(services);
                services.AddScoped<OperatorIdentitySettingsWriter>();
                services.AddScoped<IOperatorIdentitySettingsWriter, OperatorWriterProbe>();
            }));
        await using var request = await OperatorIdentityRequestAsync(host.Services, null, native.SetupSecret);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        var context = services.GetRequiredService<ExploreDbContext>();
        before.Owner = context;
        before.BeforeStart = async token =>
        {
            if (change == "header-replaced")
                request.Context.Request.Headers["X-Setup-Secret"] = Guid.CreateVersion7().ToString("N");
            if (change == "principal-replaced")
                request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
            if (change == "generation-replaced")
            {
                await using var independent = native.CreateDatabase();
                var pending = await independent.InstanceBootstrapStates.SingleAsync(token);
                pending.CompleteInteractive(await independent.Users.Select(user => user.Id).FirstAsync(token), DateTime.UtcNow);
                await independent.SaveChangesAsync(token);
            }
        };
        terminal.Owner = context;
        terminal.BeforeSettlement = () => services.GetRequiredService<ISetupSecretProvider>().Lock();
        var probe = (OperatorWriterProbe)services.GetRequiredService<IOperatorIdentitySettingsWriter>();
        probe.AfterWrite = async (mutation, token) =>
        {
            await Assert.That(mutation.Source.IsHuman).IsFalse();
            await Assert.That(mutation.Actor).IsNull();
            // A genuine setup ticket is not a generic scalar-repository privilege.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                services.GetRequiredService<ISystemSettingRepository>().UpsertInCurrentTransactionAsync(
                    new SystemSetting { SettingKey = "operator-unrelated", Value = "denied" }, token));
            if (change == "secret-locked-after-write")
                services.GetRequiredService<ISetupSecretProvider>().Lock();
            if (change == "generation-replaced-after-write")
                await context.InstanceBootstrapStates.ExecuteUpdateAsync(update =>
                    update.SetProperty(row => row.Generation, row => row.Generation + 1), token);
        };
        var execution = ActivatorUtilities.CreateInstance<SaveInstanceOperatorIdentityCommandHandler>(services)
            .ExecuteAsync(new() { Request = OperatorDraft() }, CancellationToken.None);
        if (change == "unchanged")
            await Assert.That((await execution).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(item =>
            item.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity)).IsEqualTo(change == "unchanged");
        await Assert.That(change == "secret-locked" ? terminal.Observed : before.Observed).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        if (change.EndsWith("-after-write", StringComparison.Ordinal))
        {
            await Assert.That(probe.Captured).IsNotNull();
            await Assert.That(await observer.InstanceBootstrapStates.Select(row => row.Status).SingleAsync())
                .IsEqualTo(InstanceBootstrapStatus.Pending);
            await Assert.That(await observer.InstanceBootstrapStates.Select(row => row.Generation).SingleAsync()).IsEqualTo(1L);
        }
    }

    [Test]
    [Arguments("non-administrator")]
    [Arguments("tenant-owner")]
    [Arguments("replacement-unit")]
    public async Task OperatorIdentityCannotBorrowAnUnrelatedAuthorityOrOwner(string boundary)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        if (boundary != "non-administrator") await SeedPlatformAdministratorAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterOperatorIdentityCustody));
        await using var request = await OperatorIdentityRequestAsync(host.Services, await fixture.SignInAsync(), null);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(boundary == "tenant-owner" ? new(PlatformDefaults.DefaultTenantId) : PolicyMutationScope.Instance,
                CancellationToken.None);
        var services = selection.Services;
        var command = new SaveInstanceOperatorIdentityCommand { Request = OperatorDraft() };
        if (boundary == "replacement-unit")
        {
            var source = await services.GetRequiredService<IOperatorIdentityMutationAuthority>()
                .CaptureAsync(command, CancellationToken.None);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                services.GetRequiredService<IOperatorIdentitySettingsWriter>().ObserveAsync(
                    source, new EfCoreUnitOfWork(services.GetRequiredService<ExploreDbContext>()), CancellationToken.None));
        }
        else if (boundary == "tenant-owner")
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ActivatorUtilities.CreateInstance<SaveInstanceOperatorIdentityCommandHandler>(services)
                    .ExecuteAsync(command, CancellationToken.None));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                ActivatorUtilities.CreateInstance<SaveInstanceOperatorIdentityCommandHandler>(services)
                    .ExecuteAsync(command, CancellationToken.None));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row =>
            row.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity)).IsFalse();
    }

    [Test]
    public async Task ActualOperatorIdentityPutUsesTheClosedEditorWithARealLocalAdministrator()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterOperatorIdentityCustody));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/instance-operator-identity")
        {
            Content = JsonContent.Create(OperatorDraft())
        };
        request.Headers.Authorization = new("Bearer", await fixture.SignInAsync());
        using var response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await using var observer = fixture.Native.CreateDatabase();
        var row = await observer.SystemSettings.AsNoTracking().SingleAsync(item =>
            item.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity);
        await Assert.That(row.CreatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
    }

    private static SaveInstanceOperatorIdentityRequestDto OperatorDraft() => new()
    {
        PublicName = "Original operator",
        LegalName = "Original operator",
        OperatorKindCode = "individual",
        JurisdictionCountryCode = "BE",
        PublicContactEmail = "operator@example.org",
        OfficialOrigin = "https://example.org"
    };

    private static void RegisterOperatorIdentityCustody(IServiceCollection services)
    {
        services.AddScoped<IOperatorIdentityMutationAuthority, OperatorIdentityMutationAuthority>();
        services.AddScoped<IOperatorIdentitySettingsWriter, OperatorIdentitySettingsWriter>();
    }

    private static async Task<Request> OperatorIdentityRequestAsync(
        IServiceProvider services, string? bearer, string? secret)
    {
        var scope = services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = "/api/instance-operator-identity";
        if (bearer is not null) context.Request.Headers.Authorization = "Bearer " + bearer;
        if (secret is not null) context.Request.Headers["X-Setup-Secret"] = secret;
        accessor.HttpContext = context;
        var authenticated = await context.AuthenticateAsync();
        if (!authenticated.Succeeded)
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
            throw new InvalidOperationException("The actual operator identity request did not authenticate.");
        }
        context.User = authenticated.Principal!;
        return new Request(scope, accessor, context);
    }
}
