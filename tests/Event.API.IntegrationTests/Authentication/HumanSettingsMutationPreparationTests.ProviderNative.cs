using System.Collections.Immutable;
using System.Data.Common;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Models.Common;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Services;
using Explore.Secrets.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("source")]
    [Arguments("audit")]
    [Arguments("skip-writer")]
    [Arguments("other-family")]
    public async Task SignedAuthorizationConfigurationRetainsItsOwnCompilerAndSource(string change)
    {
        var observer = new NativeProviderObserver { Skip = change == "skip-writer" };
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => RegisterNativeProvider(services, observer));
        await SeedPlatformAdministratorAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Authorization:Provider"] = "" })));
        await using var request = await ProviderRequestAsync(host.Services, await fixture.SignInAsync(), null);
        var service = request.Services.GetRequiredService<IAuthorizationProviderConfigurationService>();
        await using var original = await service.ObserveConfigurationAsync(new UpdateAuthorizationProviderConfigurationCommand
        {
            UserId = fixture.Binding.LocalSubjectId,
            Patch = new() { Configuration = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>.Set(new() { Provider = "local" }) }
        }, default);
        if (change == "source")
        {
            await using var independent = fixture.Native.CreateDatabase();
            const string key = GovernanceSettingKeys.Cerbos.CustomAdminEndpoint;
            await independent.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
            independent.SystemSettings.Add(new() { SettingKey = key, Value = "\"\"" });
            await independent.SaveChangesAsync();
        }
        if (change == "audit")
            observer.AfterWrite = (context, _, token) => context.SystemSettings
                .Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.UpdatedBy, Guid.CreateVersion7()), token);
        if (change == "unchanged") await service.ApplyConfigurationAsync(original, default);
        else if (change == "other-family")
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => request.Services.GetRequiredService<IAuthProviderConfigurationService>()
                .ApplyConfigurationAsync(original, default));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyConfigurationAsync(original, default));
        await using var final = fixture.Native.CreateDatabase();
        foreach (var effect in original.Effects)
        {
            var row = await final.SystemSettings.AsNoTracking().SingleOrDefaultAsync(item => item.SettingKey == effect.Key);
            await Assert.That(row is null ? null : ProviderConfigurationRow.From(row)).IsEqualTo(change == "unchanged" ? effect : original.Before.System[effect.Key]);
        }
    }

    [Test]
    public async Task GenuineSetupAuthorizationRetainsItsDistinctSourceAndFixedAudit()
    {
        var observer = new NativeProviderObserver();
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        await using var host = native.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Authorization:Provider"] = "" }));
            builder.ConfigureTestServices(services => RegisterNativeProvider(services, observer));
        });
        await using var request = await ProviderRequestAsync(host.Services, null, native.SetupSecret);
        var service = request.Services.GetRequiredService<IAuthorizationProviderConfigurationService>();
        await using var original = await service.ObserveConfigurationAsync(new UpdateAuthorizationProviderConfigurationDuringSetupCommand
        {
            Patch = new() { Configuration = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>.Set(new() { Provider = "local" }) }
        }, default);
        await service.ApplyConfigurationAsync(original, default);
        await Assert.That(original.Source is IProviderConfigurationSetupSource).IsTrue();
        await using var final = native.CreateDatabase();
        foreach (var effect in original.Effects)
            await Assert.That(ProviderConfigurationRow.From(await final.SystemSettings.AsNoTracking()
                .SingleAsync(row => row.SettingKey == effect.Key))).IsEqualTo(effect);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyConfigurationAsync(original, default));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProviderHandlerRetainsFirstValidationAbsenceAndRowIdentity(bool replacement)
    {
        var observer = new NativeProviderObserver();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => RegisterNativeProvider(services, observer));
        await SeedPlatformAdministratorAsync(fixture);
        const string key = GovernanceSettingKeys.Authentication.KeycloakAuthority;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await seed.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
            if (replacement)
            {
                seed.SystemSettings.Add(new() { SettingKey = key, Value = "\"\"" });
                await seed.SaveChangesAsync();
            }
        }
        await using var request = await ProviderRequestAsync(fixture.Host.Services, await fixture.SignInAsync(), null);
        bool afterValidation = false;
        var service = new NativeProviderService(request.Services.GetRequiredService<IAuthProviderConfigurationService>(), async token =>
        {
            afterValidation = true;
            await using var competing = fixture.Native.CreateDatabase();
            await competing.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync(token);
            competing.SystemSettings.Add(new() { SettingKey = key, Value = "\"\"" });
            await competing.SaveChangesAsync(token);
        });
        var handler = ActivatorUtilities.CreateInstance<UpdateAuthProviderConfigurationCommandHandler>(request.Services, service);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(NativeProviderCommand(fixture), default));
        await Assert.That(afterValidation).IsTrue();
        await Assert.That(observer.Applied).IsFalse();
        await using var final = fixture.Native.CreateDatabase();
        await Assert.That(await final.SystemSettings.AnyAsync(row => row.SettingKey == GovernanceSettingKeys.Authentication.GoogleClientId
            && row.Value == "\"provider-native-after\"")).IsFalse();
    }

    [Test]
    [Arguments("unchanged")]
    [Arguments("metadata")]
    [Arguments("audit")]
    [Arguments("lock")]
    [Arguments("creation")]
    [Arguments("skip-writer")]
    [Arguments("credential")]
    [Arguments("request")]
    [Arguments("connection")]
    public async Task ActualProviderWriterRequiresOriginalCustodyAndCompleteCanonicalEffects(string change)
    {
        var observer = new NativeProviderObserver { Skip = change == "skip-writer" };
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => RegisterNativeProvider(services, observer));
        await SeedPlatformAdministratorAsync(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            const string key = GovernanceSettingKeys.Authentication.GoogleClientId;
            await seed.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
            seed.SystemSettings.Add(new()
            {
                Id = Guid.CreateVersion7(),
                SettingKey = key,
                Value = "\"before\"",
                ValueType = SettingValueType.String,
                Description = "Original provider metadata",
                Category = "Original category",
                DisplayOrder = 73,
                CreatedAt = fixture.Clock.GetUtcNow().AddDays(-2).UtcDateTime
            });
            await seed.SaveChangesAsync();
        }
        await using var request = await ProviderRequestAsync(fixture.Host.Services, await fixture.SignInAsync(), null);
        var service = request.Services.GetRequiredService<IAuthProviderConfigurationService>();
        await using var original = await service.ObserveConfigurationAsync(NativeProviderCommand(fixture), default);
        if (change == "credential")
        {
            await using var competing = fixture.Native.CreateDatabase();
            await competing.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId).ExecuteDeleteAsync();
        }
        if (change == "request") request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
        if (change == "connection")
        {
            await observer.Context!.Database.CloseConnectionAsync();
            await observer.Context.Database.OpenConnectionAsync();
        }
        observer.AfterWrite = async (context, _, token) =>
        {
            var row = context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Authentication.GoogleClientId);
            if (change == "metadata") await row.ExecuteUpdateAsync(set => set.SetProperty(item => item.Description, "replaced metadata"), token);
            if (change == "audit") await row.ExecuteUpdateAsync(set => set.SetProperty(item => item.UpdatedBy, Guid.CreateVersion7()), token);
            if (change == "lock") await row.ExecuteUpdateAsync(set => set.SetProperty(item => item.IsLocked, false), token);
            if (change == "creation") await row.ExecuteUpdateAsync(set => set.SetProperty(item => item.CreatedAt, original.OccurredAt), token);
        };
        if (change == "unchanged")
        {
            await service.ApplyConfigurationAsync(original, default);
            await using var settled = fixture.Native.CreateDatabase();
            foreach (var effect in original.Effects)
                await Assert.That(ProviderConfigurationRow.From(await settled.SystemSettings.AsNoTracking()
                    .SingleAsync(row => row.SettingKey == effect.Key))).IsEqualTo(effect);
            await Assert.That(observer.Context!.Database.CurrentTransaction).IsNull();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyConfigurationAsync(original, default));
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyConfigurationAsync(original, default));
            await using var rolledBack = fixture.Native.CreateDatabase();
            foreach (var effect in original.Effects)
            {
                var row = await rolledBack.SystemSettings.AsNoTracking().SingleOrDefaultAsync(item => item.SettingKey == effect.Key);
                await Assert.That(row is null ? null : ProviderConfigurationRow.From(row)).IsEqualTo(original.Before.System[effect.Key]);
            }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProviderKeepsActualParticipationValidatorAndRejectsValidationToWriteSourceChanges(bool race)
    {
        var observer = new NativeProviderObserver();
        var interceptor = new ProviderValidationRace(observer);
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: interceptor,
            configureServices: services => RegisterNativeProvider(services, observer));
        await SeedPlatformAdministratorAsync(fixture);
        interceptor.EventId = await SeedProviderParticipationAsync(fixture, !race);
        await using var request = await ProviderRequestAsync(fixture.Host.Services, await fixture.SignInAsync(), null);
        var service = request.Services.GetRequiredService<IAuthProviderConfigurationService>();
        await using var original = await service.ObserveConfigurationAsync(NativeProviderCommand(fixture), default);
        if (race)
        {
            interceptor.Armed = true;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyConfigurationAsync(original, default));
            await Assert.That(interceptor.Observed).IsTrue();
        }
        else
        {
            var failure = await Assert.ThrowsAsync<Explore.Application.Exceptions.ConcurrencyConflictException>(() =>
                service.ApplyConfigurationAsync(original, default));
            await Assert.That(failure!.Code).IsEqualTo("visitor_access_account_required_conflict");
        }
        await using var final = fixture.Native.CreateDatabase();
        var participation = await final.EventParticipationConfigurations.SingleAsync(row => row.Id == interceptor.EventId);
        await Assert.That(participation.IdentityAccessModeId).IsEqualTo((int)(race ? IdentityAccessModeEnum.GuestAllowed : IdentityAccessModeEnum.AccountRequired));
        foreach (var effect in original.Effects)
        {
            var row = await final.SystemSettings.AsNoTracking().SingleOrDefaultAsync(item => item.SettingKey == effect.Key);
            await Assert.That(row is null ? null : ProviderConfigurationRow.From(row)).IsEqualTo(original.Before.System[effect.Key]);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GenuineSetupRequiresItsPrivateCompletionAndCanonicalNullAudit(bool skip)
    {
        var observer = new NativeProviderObserver { Skip = skip };
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        await using var host = native.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => RegisterNativeProvider(services, observer)));
        await using var request = await ProviderRequestAsync(host.Services, null, native.SetupSecret);
        var service = request.Services.GetRequiredService<IAuthProviderConfigurationService>();
        await using var original = await service.ObserveConfigurationAsync(new UpdateAuthProviderConfigurationDuringSetupCommand
        {
            Patch = new()
            {
                Configuration = OptionalUpdate<AuthProviderConfigurationWriteDto>.Set(new()
                { PrimaryProviderId = (int)AuthenticationProviderKind.Local, GoogleClientId = "provider-native-after" })
            }
        }, default);
        await Assert.That(original.Source is IProviderConfigurationSetupSource).IsTrue();
        if (skip)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyConfigurationAsync(original, default));
        else
            await service.ApplyConfigurationAsync(original, default);
        await using var final = native.CreateDatabase();
        foreach (var effect in original.Effects)
        {
            var row = await final.SystemSettings.AsNoTracking().SingleOrDefaultAsync(item => item.SettingKey == effect.Key);
            await Assert.That(row is null ? null : ProviderConfigurationRow.From(row)).IsEqualTo(skip ? original.Before.System[effect.Key] : effect);
            if (!skip) await Assert.That(row!.UpdatedBy).IsNull();
        }
    }

    [Test]
    public async Task OriginalSetupAuthenticationCannotBecomeHumanProviderAuthority()
    {
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        await using var host = native.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => RegisterNativeProvider(services, new())));
        await using var request = await ProviderRequestAsync(host.Services, null, native.SetupSecret);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => request.Services.GetRequiredService<IAuthProviderConfigurationService>()
            .ObserveConfigurationAsync(new UpdateAuthProviderConfigurationCommand
            {
                UserId = Guid.CreateVersion7(),
                Patch = new() { Configuration = OptionalUpdate<AuthProviderConfigurationWriteDto>.Set(new()) }
            }, default));
    }

    [Test]
    public async Task SignedHumanAndRequestFreeInvocationCannotBorrowProviderSetup()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(configureServices: services => RegisterNativeProvider(services, new()));
        await SeedPlatformAdministratorAsync(fixture);
        await using var request = await ProviderRequestAsync(fixture.Host.Services, await fixture.SignInAsync(), null);
        var command = new UpdateAuthProviderConfigurationDuringSetupCommand
        { Patch = new() { Configuration = OptionalUpdate<AuthProviderConfigurationWriteDto>.Set(new()) } };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => request.Services.GetRequiredService<IAuthProviderConfigurationService>()
            .ObserveConfigurationAsync(command, default));
        request.Services.GetRequiredService<IHttpContextAccessor>().HttpContext = null;
        await using var startup = fixture.Host.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => startup.ServiceProvider.GetRequiredService<IAuthProviderConfigurationService>()
            .ObserveConfigurationAsync(command, default));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProviderActivationRequiresAcknowledgedSettlement(bool rejected)
    {
        var observer = new NativeProviderObserver { Skip = rejected };
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(configureServices: services =>
        {
            RegisterNativeProvider(services, observer);
            var original = services.Last(item => item.ServiceType == typeof(IAuthenticationProviderModeCacheInvalidator));
            services.RemoveAll<IAuthenticationProviderModeCacheInvalidator>();
            services.AddScoped<IAuthenticationProviderModeCacheInvalidator>(provider => new ProviderActivationObserver(
                (IAuthenticationProviderModeCacheInvalidator)original.ImplementationFactory!(provider), observer));
        });
        await SeedPlatformAdministratorAsync(fixture);
        await using var request = await ProviderRequestAsync(fixture.Host.Services, await fixture.SignInAsync(), null);
        observer.ObserveCommitted = () =>
        {
            using var independent = fixture.Native.CreateDatabase();
            return observer.Context!.Database.CurrentTransaction is null
                && independent.SystemSettings.Any(row => row.SettingKey == GovernanceSettingKeys.Authentication.GoogleClientId
                    && row.Value == "\"provider-native-after\"");
        };
        var handler = ActivatorUtilities.CreateInstance<UpdateAuthProviderConfigurationCommandHandler>(request.Services);
        if (rejected)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(NativeProviderCommand(fixture), default));
        else
            await Assert.That((await handler.ExecuteAsync(NativeProviderCommand(fixture), default)).IsSuccess).IsTrue();
        await Assert.That(observer.ActivatedAfterCommit).IsEqualTo(rejected ? null : true);
    }

    [Test]
    [Arguments(-1)]
    [Arguments(1)]
    public async Task ProviderCredentialDeadlineRetainsItsOriginalSubmicrosecondBoundary(int ticks)
    {
        var observer = new NativeProviderObserver();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(configureServices: services => RegisterNativeProvider(services, observer));
        await SeedPlatformAdministratorAsync(fixture);
        await using var request = await ProviderRequestAsync(fixture.Host.Services, await fixture.SignInAsync(), null);
        var service = request.Services.GetRequiredService<IAuthProviderConfigurationService>();
        await using var original = await service.ObserveConfigurationAsync(NativeProviderCommand(fixture), default);
        var lifetime = ((IProviderConfigurationHumanSource)original.Source).Lifetime;
        observer.AfterWrite = (_, _, _) =>
        {
            fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(ticks) - fixture.Clock.GetUtcNow());
            return Task.CompletedTask;
        };
        if (ticks > 0) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyConfigurationAsync(original, default));
        else await service.ApplyConfigurationAsync(original, default);
        await using var final = fixture.Native.CreateDatabase();
        foreach (var effect in original.Effects)
        {
            var row = await final.SystemSettings.AsNoTracking().SingleOrDefaultAsync(item => item.SettingKey == effect.Key);
            await Assert.That(row is null ? null : ProviderConfigurationRow.From(row)).IsEqualTo(ticks > 0 ? original.Before.System[effect.Key] : effect);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProviderRetainsOriginalSupportDeadlineAfterSourceReads(bool expire)
    {
        var observer = new NativeProviderObserver();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(configureServices: services => RegisterNativeProvider(services, observer));
        await SeedPlatformAdministratorAsync(fixture);
        var deadline = fixture.Clock.GetUtcNow().AddMinutes(5).AddTicks(7);
        Guid sessionId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (string key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var row = await seed.SystemSettings.SingleOrDefaultAsync(item => item.SettingKey == key);
                if (row is null) seed.SystemSettings.Add(new() { SettingKey = key, Value = "true" });
                else row.Value = "true";
            }
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Provider source custody", "provider-source-test",
                fixture.Clock.GetUtcNow().AddMinutes(-1), deadline);
            seed.SupportAccessSessions.Add(session);
            await seed.SaveChangesAsync();
            sessionId = session.Id;
        }
        await using var request = await ProviderRequestAsync(fixture.Host.Services, await fixture.SignInAsync(), null);
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
        var service = request.Services.GetRequiredService<IAuthProviderConfigurationService>();
        await using var original = await service.ObserveConfigurationAsync(NativeProviderCommand(fixture), default);
        observer.AfterWrite = (_, _, _) =>
        {
            fixture.Clock.Advance((expire ? deadline : deadline.AddTicks(-1)) - fixture.Clock.GetUtcNow());
            return Task.CompletedTask;
        };
        if (expire) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyConfigurationAsync(original, default));
        else await service.ApplyConfigurationAsync(original, default);
        await using var final = fixture.Native.CreateDatabase();
        foreach (var effect in original.Effects)
        {
            var row = await final.SystemSettings.AsNoTracking().SingleOrDefaultAsync(item => item.SettingKey == effect.Key);
            await Assert.That(row is null ? null : ProviderConfigurationRow.From(row)).IsEqualTo(expire ? original.Before.System[effect.Key] : effect);
        }
    }

    private static UpdateAuthProviderConfigurationCommand NativeProviderCommand(LocalIdentityLifecycleHttpFixture fixture) => new()
    {
        UserId = fixture.Binding.LocalSubjectId,
        Patch = new()
        {
            Configuration = OptionalUpdate<AuthProviderConfigurationWriteDto>.Set(new()
            { PrimaryProviderId = (int)AuthenticationProviderKind.Local, GoogleClientId = "provider-native-after" })
        }
    };

    private static async Task<Guid> SeedProviderParticipationAsync(LocalIdentityLifecycleHttpFixture fixture, bool required)
    {
        await using var seed = fixture.Native.CreateDatabase();
        Guid actor = await seed.Actors.Where(row => row.UserId == fixture.Binding.LocalSubjectId).Select(row => row.Id).SingleAsync();
        var entity = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = "Original provider participation source",
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            ActorId = actor,
            Actor = null!,
            OrganizerActorId = actor,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            SessionCount = 1,
            FirstSessionStartUtc = fixture.Clock.GetUtcNow().AddDays(1),
            LastSessionEndUtc = fixture.Clock.GetUtcNow().AddDays(1).AddHours(1)
        };
        entity.ParticipationConfiguration = EventParticipationConfiguration.Create(entity.Id, entity.TenantId,
            (int)ParticipationHandlingModeEnum.PlatformManaged, (int)AdvanceRegistrationObligationEnum.Required,
            (int)(required ? IdentityAccessModeEnum.AccountRequired : IdentityAccessModeEnum.GuestAllowed),
            required ? null : GuestRecoveryPolicyEnum.EmailOptional, fixture.Clock.GetUtcNow().UtcDateTime);
        seed.Events.Add(entity);
        await seed.SaveChangesAsync();
        return entity.Id;
    }

    private static void RegisterNativeProvider(IServiceCollection services, NativeProviderObserver observer)
    {
        services.AddProviderConfigurationMutationAuthority();
        services.RemoveAll<IRetainedSecretResolver>();
        services.AddScoped<IRetainedSecretResolver>(provider => ActivatorUtilities.CreateInstance<SecretResolver>(provider));
        services.RemoveAll<ISecretSource>();
        services.AddSingleton<ISecretSource>(new ProviderAbsentMaterial(SecretSourceType.EnvironmentVariable));
        services.AddSingleton<ISecretSource>(new ProviderAbsentMaterial(SecretSourceType.Infisical));
        services.AddScoped<ProviderConfigurationSettingsWriter>();
        services.RemoveAll<IProviderConfigurationSettingsWriter>();
        services.AddScoped<IProviderConfigurationSettingsWriter>(provider => new NativeProviderWriter(
            provider.GetRequiredService<ProviderConfigurationSettingsWriter>(), provider.GetRequiredService<ExploreDbContext>(),
            provider.GetRequiredService<IVisitorAccessSettingsWriter>(), observer));
    }

    private sealed class ProviderAbsentMaterial(SecretSourceType source) : ISecretSource
    {
        public SecretSourceType SourceType => source;
        public Task<SecretResolutionResult> GetSecretAsync(SecretBinding binding, CancellationToken token = default)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(SecretResolutionResult.Unconfigured); }
        public Task<bool> ValidateAsync(SecretBinding binding, CancellationToken token = default) => Task.FromResult(false);
    }

    private sealed class NativeProviderObserver
    {
        internal ExploreDbContext? Context;
        internal VisitorAccessSettingsWriter? Visitor;
        internal ProviderConfigurationMutation? Mutation;
        internal bool Applied;
        internal bool Skip;
        internal Func<bool>? ObserveCommitted;
        internal bool? ActivatedAfterCommit;
        internal Func<ExploreDbContext, ProviderConfigurationMutation, CancellationToken, Task>? AfterWrite;
    }

    private sealed class ProviderActivationObserver(IAuthenticationProviderModeCacheInvalidator inner, NativeProviderObserver observer)
        : IAuthenticationProviderModeCacheInvalidator
    {
        public void InvalidateInstanceMode()
        {
            if (observer.ObserveCommitted is { } read) observer.ActivatedAfterCommit = read();
            inner.InvalidateInstanceMode();
        }
    }

    private sealed class NativeProviderWriter(ProviderConfigurationSettingsWriter inner, ExploreDbContext context,
        IVisitorAccessSettingsWriter visitor, NativeProviderObserver observer) : IProviderConfigurationSettingsWriter
    {
        public async Task<ProviderConfigurationState> ObserveAsync(IProviderConfigurationMutationSource source, IUnitOfWork owner, CancellationToken token)
        {
            observer.Context = context;
            observer.Visitor = visitor as VisitorAccessSettingsWriter;
            return await inner.ObserveAsync(source, owner, token);
        }
        public void RequireIdle() => inner.RequireIdle();
        public DateTime RepresentTimestamp(DateTime timestamp) => inner.RepresentTimestamp(timestamp);
        public Task RequireUnchangedAsync(ProviderConfigurationState original, CancellationToken token) => inner.RequireUnchangedAsync(original, token);
        public void PrepareSetupAttempt(ProviderConfigurationMutation mutation) => inner.PrepareSetupAttempt(mutation);
        public Task RequireAppliedAsync(ProviderConfigurationMutation mutation, CancellationToken token) => inner.RequireAppliedAsync(mutation, token);
        public async Task<ImmutableArray<SettingChangedNotification>> ExecuteAsync(ProviderConfigurationMutation mutation, CancellationToken token)
        {
            observer.Mutation = mutation;
            if (observer.Skip) return [];
            var result = await inner.ExecuteAsync(mutation, token);
            observer.Applied = true;
            if (observer.AfterWrite is { } after) await after(context, mutation, token);
            return result;
        }
    }

    private sealed class NativeProviderService(IAuthProviderConfigurationService inner, Func<CancellationToken, Task> beforeApply)
        : IAuthProviderConfigurationService
    {
        public Task<AuthProviderConfigurationDto> ReadConfigurationAsync() => inner.ReadConfigurationAsync();
        public Task<bool> IsConfiguredAsync() => inner.IsConfiguredAsync();
        public Task ApplyConfigurationAsync(UpdateAuthProviderConfigurationCommand command, CancellationToken token = default) => inner.ApplyConfigurationAsync(command, token);
        public Task ApplySetupConfigurationAsync(UpdateAuthProviderConfigurationDuringSetupCommand command, CancellationToken token = default) => inner.ApplySetupConfigurationAsync(command, token);
        public Task<ProviderConfigurationMutation> ObserveConfigurationAsync(UpdateAuthProviderConfigurationCommand command, CancellationToken token) => inner.ObserveConfigurationAsync(command, token);
        public Task<ProviderConfigurationMutation> ObserveConfigurationAsync(UpdateAuthProviderConfigurationDuringSetupCommand command, CancellationToken token) => inner.ObserveConfigurationAsync(command, token);
        public async Task ApplyConfigurationAsync(ProviderConfigurationMutation observation, CancellationToken token)
        { await beforeApply(token); await inner.ApplyConfigurationAsync(observation, token); }
    }

    private sealed class ProviderValidationRace(NativeProviderObserver observer) : DbCommandInterceptor
    {
        internal Guid EventId;
        internal bool Armed;
        internal bool Observed;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (!Armed || Observed || !ReferenceEquals(eventData.Context, observer.Context)
                || observer.Mutation is not { } mutation || observer.Visitor is not { } visitor) return result;
            var effects = mutation.Effects.Select(row => new VisitorAccessSettingMutation(null, row.Key,
                VisitorAccessSettingMutationKind.SetValue, row.Value, row.IsLocked)).ToImmutableArray();
            var handoff = ProviderConfigurationSettingsWriter.GetHandoff(observer.Context!, effects,
                (mutation.Source as IProviderConfigurationHumanSource)?.Subject, mutation.Actor);
            if (handoff is null) return result;
            try { visitor.RequireProviderValidated(handoff); }
            catch (UnauthorizedAccessException) { return result; }
            Observed = true;
            var sql = new Explore.Persistence.Security.HumanPrincipalSourceSql(observer.Context!);
            await using var tamper = command.Connection!.CreateCommand();
            tamper.Transaction = command.Transaction;
            tamper.CommandText = $"UPDATE {sql.Table(typeof(EventParticipationConfiguration))} SET "
                + $"{sql.Column(typeof(EventParticipationConfiguration), "IdentityAccessModeId")} = {(int)IdentityAccessModeEnum.AccountRequired}, "
                + $"{sql.Column(typeof(EventParticipationConfiguration), "GuestRecoveryPolicy")} = NULL "
                + $"WHERE {sql.Column(typeof(EventParticipationConfiguration), "Id")} = @event";
            var parameter = tamper.CreateParameter(); parameter.ParameterName = "event"; parameter.Value = EventId;
            tamper.Parameters.Add(parameter);
            await tamper.ExecuteNonQueryAsync(token);
            return result;
        }
    }
}
