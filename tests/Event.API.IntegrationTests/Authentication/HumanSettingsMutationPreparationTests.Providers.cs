using System.Collections.Immutable;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Models.Common;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments(false, "unchanged")]
    [Arguments(false, "binding-revoked")]
    [Arguments(false, "terminal-expiry")]
    [Arguments(true, "unchanged")]
    [Arguments(true, "binding-revoked")]
    [Arguments(true, "terminal-expiry")]
    [Arguments(true, "effect-probe")]
    public async Task ProviderConsumersRetainOriginalHumanAndExactEffects(bool authorization, string change)
    {
        string key = authorization ? GovernanceSettingKeys.Security.AuthorizationProvider
            : GovernanceSettingKeys.Authentication.GoogleClientId;
        var before = new ConsumerBeforeOwnerTransaction();
        var terminal = new SpecializedBeforeSettlement(key);
        ProviderWriterProbe? originalProbe = null;
        IServiceProvider? originalServices = null;
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            interceptor: change == "terminal-expiry" ? terminal : before);
        await SeedPlatformAdministratorAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authorization:Provider"] = string.Empty }));
            builder.ConfigureTestServices(services =>
            {
                RegisterProviderCustody(services);
                RegisterProviderProbe(services, (probe, selected) =>
                {
                    originalProbe = probe;
                    originalServices = selected;
                    before.Owner = terminal.Owner = selected.GetRequiredService<ExploreDbContext>();
                });
            });
        });
        await using var request = await ProviderRequestAsync(host.Services, await fixture.SignInAsync(), null);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selected.Services;
        var owner = services.GetRequiredService<ExploreDbContext>();
        before.Owner = owner;
        before.BeforeStart = async token =>
        {
            if (change == "binding-revoked")
            {
                await using var revoke = fixture.Native.CreateDatabase();
                await revoke.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteDeleteAsync(token);
            }
        };
        terminal.Owner = owner;
        terminal.BeforeSettlement = () =>
        {
            var ownerServices = originalServices ?? throw new InvalidOperationException("The original provider writer was not observed.");
            var lifetime = ownerServices.GetRequiredService<Explore.Persistence.Database.ExploreDbContextExecutionLease>()
                .HumanLifetime!;
            ownerServices.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
            {
                fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                    - fixture.Clock.GetUtcNow());
                return Task.CompletedTask;
            }, _ => { });
        };
        string? previous;
        await using (var observe = fixture.Native.CreateDatabase())
            previous = await observe.SystemSettings.Where(row => row.SettingKey == key)
                .Select(row => row.Value).SingleOrDefaultAsync();
        Task<BaseCommandResponse<Guid>> execution = authorization
            ? ActivatorUtilities.CreateInstance<UpdateAuthorizationProviderConfigurationCommandHandler>(services).ExecuteAsync(
                new UpdateAuthorizationProviderConfigurationCommand
                {
                    UserId = fixture.Binding.LocalSubjectId,
                    Patch = new()
                    {
                        Configuration = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>.Set(
                        new() { Provider = "local" })
                    }
                }, CancellationToken.None)
            : ActivatorUtilities.CreateInstance<UpdateAuthProviderConfigurationCommandHandler>(services).ExecuteAsync(
                new UpdateAuthProviderConfigurationCommand
                {
                    UserId = fixture.Binding.LocalSubjectId,
                    Patch = new()
                    {
                        Configuration = OptionalUpdate<AuthProviderConfigurationWriteDto>.Set(
                        new() { GoogleClientId = "provider-original-owner" })
                    }
                }, CancellationToken.None);
        bool committed = change is "unchanged" or "effect-probe";
        if (committed)
            await Assert.That((await execution).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = fixture.Native.CreateDatabase();
        var stored = await observer.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == key);
        await Assert.That(stored?.Value).IsEqualTo(committed
            ? authorization ? "\"local\"" : "\"provider-original-owner\"" : previous);
        if (committed)
        {
            await Assert.That(stored!.IsLocked).IsTrue();
            await Assert.That(stored.UpdatedBy ?? stored.CreatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
        }
        await Assert.That(change == "terminal-expiry" ? terminal.Observed : before.Observed).IsTrue();
        if (change == "effect-probe")
        {
            var probe = originalProbe ?? throw new InvalidOperationException("The original provider writer was not observed.");
            await Assert.That(probe.Observed).IsTrue();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                probe.Inner.ExecuteAsync(probe.Captured!, CancellationToken.None));
        }
    }

    [Test]
    [Arguments(false, "unchanged")]
    [Arguments(false, "generation-replaced")]
    [Arguments(false, "secret-locked")]
    [Arguments(false, "header-replaced")]
    [Arguments(false, "principal-replaced")]
    [Arguments(true, "unchanged")]
    [Arguments(true, "generation-replaced")]
    [Arguments(true, "secret-locked")]
    [Arguments(true, "header-replaced")]
    [Arguments(true, "principal-replaced")]
    public async Task ActualSetupConsumersRetainSecretAndPendingGeneration(bool authorization, string change)
    {
        string key = authorization ? GovernanceSettingKeys.Security.AuthorizationProvider
            : GovernanceSettingKeys.Authentication.GoogleClientId;
        var before = new ConsumerBeforeOwnerTransaction();
        var terminal = new SpecializedBeforeSettlement(key);
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(
            incompleteSetup: true, persistenceInterceptor: change == "secret-locked" ? terminal : before);
        await using (var seed = native.CreateDatabase())
        {
            seed.InstanceBootstrapStates.Add(InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(), DeploymentMode.SingleTenant, DateTime.UtcNow));
            await seed.SaveChangesAsync();
        }
        await using var host = native.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authorization:Provider"] = string.Empty }));
            builder.ConfigureTestServices(services =>
            {
                RegisterProviderCustody(services);
                RegisterProviderProbe(services, (_, ownerServices) =>
                    before.Owner = terminal.Owner = ownerServices.GetRequiredService<ExploreDbContext>());
            });
        });
        await using var request = await ProviderRequestAsync(host.Services, null, native.SetupSecret);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selected.Services;
        var owner = services.GetRequiredService<ExploreDbContext>();
        before.Owner = owner;
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
                pending.CompleteInteractive(await independent.Users.Select(user => user.Id).FirstAsync(token),
                    DateTime.UtcNow);
                await independent.SaveChangesAsync(token);
            }
        };
        terminal.Owner = owner;
        terminal.BeforeSettlement = () => services.GetRequiredService<ISetupSecretProvider>().Lock();
        Task<BaseCommandResponse<Guid>> execution = authorization
            ? ActivatorUtilities.CreateInstance<UpdateAuthorizationProviderConfigurationCommandHandler>(services).ExecuteAsync(
                new UpdateAuthorizationProviderConfigurationDuringSetupCommand
                {
                    Patch = new()
                    {
                        Configuration = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>.Set(
                        new() { Provider = "local" })
                    }
                }, CancellationToken.None)
            : ActivatorUtilities.CreateInstance<UpdateAuthProviderConfigurationCommandHandler>(services).ExecuteAsync(
                new UpdateAuthProviderConfigurationDuringSetupCommand
                {
                    Patch = new()
                    {
                        Configuration = OptionalUpdate<AuthProviderConfigurationWriteDto>.Set(
                        new() { GoogleClientId = "setup-original-owner" })
                    }
                }, CancellationToken.None);
        if (change == "unchanged")
            await Assert.That((await execution).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = native.CreateDatabase();
        var stored = await observer.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == key);
        if (change == "unchanged")
        {
            await Assert.That(stored!.Value).IsEqualTo(authorization ? "\"local\"" : "\"setup-original-owner\"");
            await Assert.That(stored.IsLocked).IsTrue();
        }
        else
            await Assert.That(stored).IsNull();
        await Assert.That(change == "secret-locked" ? terminal.Observed : before.Observed).IsTrue();
        await Assert.That(services.GetRequiredService<IHumanCommitScopePreparation>().Subject).IsNull();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ProviderConfigurationSettingsWriter.RequireSetupVisitorAsync(owner, [], null, CancellationToken.None));
    }

    [Test]
    public async Task ProviderSetupEffectsCannotExpandReplayOrEscapeOriginalOwner()
    {
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        await using var host = native.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authorization:Provider"] = string.Empty }));
            builder.ConfigureTestServices(services =>
            {
                RegisterProviderCustody(services);
                RegisterProviderProbe(services);
            });
        });
        await using var authenticated = await ProviderRequestAsync(host.Services, null, native.SetupSecret);
        await using var selection = await authenticated.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        var service = services.GetRequiredService<IAuthorizationProviderConfigurationService>();
        await service.ApplySetupConfigurationAsync(new()
        {
            Patch = new()
            {
                Configuration = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>
                .Set(new() { Provider = "local" })
            }
        });
        var probe = (ProviderWriterProbe)services.GetRequiredService<IProviderConfigurationSettingsWriter>();
        await Assert.That(probe.Observed).IsTrue();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            probe.Inner.ExecuteAsync(probe.Captured!, CancellationToken.None));
        await using var observer = native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.Where(row =>
            row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"local\"");
    }

    [Test]
    public async Task ClaimedSetupSchemeCannotAuthorizeProviderMutation()
    {
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        await using var host = native.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authorization:Provider"] = string.Empty }));
            builder.ConfigureTestServices(RegisterProviderCustody);
        });
        await using var scope = host.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var request = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        accessor.HttpContext = request;
        request.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                authenticationType: Explore.Application.Constants.ApiAuthenticationSchemeNames.SetupSecret));
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                scope.ServiceProvider.GetRequiredService<IAuthorizationProviderConfigurationService>()
                    .ApplySetupConfigurationAsync(new()
                    {
                        Patch = new()
                        {
                            Configuration = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>
                            .Set(new() { Provider = "local" })
                        }
                    }));
        }
        finally { accessor.HttpContext = null; }
        await using var observer = native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)).IsFalse();
    }

    private static void RegisterProviderProbe(IServiceCollection services,
        Action<ProviderWriterProbe, IServiceProvider>? observe = null)
    {
        services.AddScoped<ProviderConfigurationSettingsWriter>();
        services.RemoveAll<IProviderConfigurationSettingsWriter>();
        services.AddScoped<IProviderConfigurationSettingsWriter>(selected =>
        {
            var probe = new ProviderWriterProbe(selected.GetRequiredService<ProviderConfigurationSettingsWriter>(),
                selected.GetRequiredService<ISystemSettingRepository>());
            probe.BeforeObserve = () => observe?.Invoke(probe, selected);
            return probe;
        });
    }

    private sealed class ProviderWriterProbe(
        ProviderConfigurationSettingsWriter inner, ISystemSettingRepository repository)
        : IProviderConfigurationSettingsWriter
    {
        internal ProviderConfigurationSettingsWriter Inner => inner;
        internal ProviderConfigurationMutation? Captured { get; private set; }
        internal bool Observed { get; private set; }
        internal Action? BeforeObserve { get; set; }
        public Task<ProviderConfigurationState> ObserveAsync(IProviderConfigurationMutationSource source,
            IUnitOfWork owner, CancellationToken cancellationToken)
        {
            BeforeObserve?.Invoke();
            return inner.ObserveAsync(source, owner, cancellationToken);
        }
        public void RequireIdle() => inner.RequireIdle();
        public DateTime RepresentTimestamp(DateTime timestamp) => inner.RepresentTimestamp(timestamp);
        public Task RequireUnchangedAsync(ProviderConfigurationState original, CancellationToken cancellationToken) =>
            inner.RequireUnchangedAsync(original, cancellationToken);
        public void PrepareSetupAttempt(ProviderConfigurationMutation mutation) => inner.PrepareSetupAttempt(mutation);
        public Task RequireAppliedAsync(ProviderConfigurationMutation mutation, CancellationToken cancellationToken) =>
            inner.RequireAppliedAsync(mutation, cancellationToken);
        public async Task<ImmutableArray<SettingChangedNotification>> ExecuteAsync(
            ProviderConfigurationMutation mutation, CancellationToken cancellationToken)
        {
            Captured = mutation;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.UpsertInCurrentTransactionAsync(
                new SystemSetting
                {
                    SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
                    Value = "\"cerbos\"",
                    CreatedBy = mutation.Actor,
                    UpdatedBy = mutation.Actor
                }, cancellationToken));
            var result = await inner.ExecuteAsync(mutation, cancellationToken);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inner.ExecuteAsync(mutation, cancellationToken));
            Observed = true;
            return result;
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DeploymentManagedProviderNoChangeStillRequiresOriginalHuman(bool revoke)
    {
        var before = new ConsumerBeforeOwnerTransaction();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: before);
        await SeedPlatformAdministratorAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authorization:Provider"] = "local" }));
            builder.ConfigureTestServices(services =>
            {
                RegisterProviderCustody(services);
                RegisterProviderProbe(services, (_, ownerServices) =>
                    before.Owner = ownerServices.GetRequiredService<ExploreDbContext>());
            });
        });
        await using var request = await ProviderRequestAsync(host.Services, await fixture.SignInAsync(), null);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        before.Owner = services.GetRequiredService<ExploreDbContext>();
        before.BeforeStart = async token =>
        {
            if (revoke)
            {
                await using var independent = fixture.Native.CreateDatabase();
                await independent.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteDeleteAsync(token);
            }
        };
        var execution = services.GetRequiredService<IAuthorizationProviderConfigurationService>()
            .ApplyConfigurationAsync(new()
            {
                UserId = fixture.Binding.LocalSubjectId,
                Patch = new()
                {
                    Configuration = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>
                    .Set(new() { Provider = "local" })
                }
            });
        if (revoke)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        else
            await execution;
        await Assert.That(before.Observed).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualHumanCannotEnterProviderSetupEvenWithSetupTypedCommand(bool authorization)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authorization:Provider"] = string.Empty }));
            builder.ConfigureTestServices(RegisterProviderCustody);
        });
        await using var request = await ProviderRequestAsync(host.Services, await fixture.SignInAsync(), null);
        Task execute = authorization
            ? request.Services.GetRequiredService<IAuthorizationProviderConfigurationService>().ApplySetupConfigurationAsync(
                new UpdateAuthorizationProviderConfigurationDuringSetupCommand
                {
                    Patch = new()
                    {
                        Configuration = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>.Set(
                        new() { Provider = "local" })
                    }
                })
            : request.Services.GetRequiredService<IAuthProviderConfigurationService>().ApplySetupConfigurationAsync(
                new UpdateAuthProviderConfigurationDuringSetupCommand
                {
                    Patch = new() { Configuration = OptionalUpdate<AuthProviderConfigurationWriteDto>.Set(new()) }
                });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execute);
    }

    private static void RegisterProviderCustody(IServiceCollection services)
    {
        services.RemoveAll<IProviderConfigurationSettingsWriter>();
        services.AddScoped<IProviderConfigurationSettingsWriter, ProviderConfigurationSettingsWriter>();
        services.RemoveAll<IProviderConfigurationSetupAuthority>();
        services.AddScoped<IProviderConfigurationSetupAuthority, ProviderConfigurationSetupAuthority>();
        services.RemoveAll<ITrustedSetupSettingsAuthority>();
        services.AddScoped<ITrustedSetupSettingsAuthority, TrustedSetupSettingsAuthority>();
        var originalVisitor = services.Last(descriptor => descriptor.ServiceType == typeof(IVisitorAccessSettingsWriter));
        services.RemoveAll<IVisitorAccessSettingsWriter>();
        services.AddScoped<IVisitorAccessSettingsWriter>(provider => new ProviderVisitorProbe(
            (IVisitorAccessSettingsWriter)originalVisitor.ImplementationFactory!(provider),
            provider.GetRequiredService<ExploreDbContext>()));
    }

    private sealed class ProviderVisitorProbe(IVisitorAccessSettingsWriter inner, ExploreDbContext context)
        : IVisitorAccessSettingsWriter
    {
        public Task<VisitorAccessSettingsWriteResult> ApplyAsync(ImmutableArray<VisitorAccessSettingMutation> mutations,
            HumanCommitSubject subject, SettingsMutationOperation operation, Guid? actorUserId,
            CancellationToken cancellationToken = default) =>
            inner.ApplyAsync(mutations, subject, operation, actorUserId, cancellationToken);

        public async Task<VisitorAccessSettingsWriteResult> ApplyInternalAsync(
            ImmutableArray<VisitorAccessSettingMutation> mutations, Guid? actorUserId,
            CancellationToken cancellationToken = default)
        {
            await ProviderConfigurationSettingsWriter.RequireSetupVisitorAsync(
                context, mutations, actorUserId, cancellationToken);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                ProviderConfigurationSettingsWriter.RequireSetupVisitorAsync(context,
                    mutations.SetItem(0, mutations[0] with { Value = "\"substituted\"" }), actorUserId, cancellationToken));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                ProviderConfigurationSettingsWriter.RequireSetupVisitorAsync(context, mutations,
                    Guid.CreateVersion7(), cancellationToken));
            return await inner.ApplyInternalAsync(mutations, actorUserId, cancellationToken);
        }
    }

    private static async Task<Request> ProviderRequestAsync(IServiceProvider services, string? bearer, string? secret)
    {
        var scope = services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        accessor.HttpContext = context;
        if (bearer is not null) context.Request.Headers.Authorization = "Bearer " + bearer;
        if (secret is not null) context.Request.Headers["X-Setup-Secret"] = secret;
        var authentication = await context.AuthenticateAsync();
        if (!authentication.Succeeded)
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
            throw new InvalidOperationException("The real provider request did not authenticate.");
        }
        context.User = authentication.Principal!;
        return new Request(scope, accessor, context);
    }
}
