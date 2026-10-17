using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.InstanceAdmin.Handlers.Commands;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class TenantPlanCatalogueOriginalAdmissionTests
{
    [Test]
    [Arguments("clone")]
    [Arguments("create-version")]
    [Arguments("update-version")]
    [Arguments("publish")]
    [Arguments("archive")]
    [Arguments("switch")]
    [Arguments("rollback")]
    public async Task ActualPlanEffectsRequireOriginalAuthorityThroughTerminalReads(string operation)
    {
        foreach (string change in new[] { "unchanged", "grant", "stamp", "binding", "route", "request",
                     "credential_after_reads", "support_active", "support_revoke", "support_expire_after_reads" })
        {
            await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
            var seed = await SeedAsync(fixture, support: change.StartsWith("support_", StringComparison.Ordinal));
            string before;
            await using (var observer = fixture.Native.CreateDatabase())
                before = await SnapshotAsync(observer, seed);
            var identity = await fixture.ReadIdentityAsync();
            await using var request = new Request(fixture.Host.Services);
            await request.AuthenticateAsync(await fixture.SignInAsync());
            if (seed.SupportId is { } supportId)
            {
                request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = supportId.ToString("D");
                await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>()
                    .GetCurrentAsync()).IsActive).IsTrue();
            }
            await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
            var context = request.Services.GetRequiredService<ExploreDbContext>();
            object command = Command(operation, seed, fixture.Binding.LocalSubjectId);
            bool bodyObserved = false;
            var owner = new BodyBoundary((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), beforeBody: null,
                afterBody: async token =>
                {
                    await RequireEffectAsync(context, seed, operation, fixture.Binding.LocalSubjectId, token);
                    bodyObserved = true;
                    switch (change)
                    {
                        case "grant":
                            await context.PlatformUserRoles.Where(row => row.Id == seed.GrantId).ExecuteDeleteAsync(token);
                            break;
                        case "stamp":
                            await context.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                                .ExecuteUpdateAsync(update => update.SetProperty(row => row.SecurityStamp,
                                    Guid.CreateVersion7().ToString("N")), token);
                            break;
                        case "binding":
                            await context.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey,
                                    Guid.CreateVersion7().ToString("N")), token);
                            break;
                        case "route":
                            await context.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Value, "\"cerbos\""), token);
                            break;
                        case "request":
                            request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
                            break;
                        case "support_revoke":
                            await context.SupportAccessSessions.Where(row => row.Id == seed.SupportId)
                                .ExecuteUpdateAsync(update => update.SetProperty(row => row.StatusId,
                                    (int)SupportAccessSessionStatusEnum.Revoked), token);
                            break;
                    }
                    if (change is "credential_after_reads" or "support_expire_after_reads")
                        request.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async finalToken =>
                        {
                            // Run a real plan read after this action's source callback, then
                            // cross the authentic credential/session horizon before time admission.
                            await context.TenantPlanVersions.AsNoTracking()
                                .SingleAsync(row => row.Id == seed.DraftVersionId, finalToken);
                            DateTimeOffset deadline = seed.SupportExpiry.AddTicks(1);
                            if (change == "credential_after_reads")
                            {
                                var lifetime = (await request.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                                    .ObserveAsync(request.Context.User, finalToken))!;
                                deadline = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
                            }
                            fixture.Clock.Advance(deadline - fixture.Clock.GetUtcNow());
                        }, _ => { });
                });
            async Task ExecuteBodyAsync()
            {
                var result = await InvokeAsync(request.Services, command, owner);
                await Assert.That(result.IsSuccess).IsTrue();
                await Assert.That(result.Id).IsNotEqualTo(Guid.Empty);
            }
            bool accepted = change is "unchanged" or "support_active";
            if (accepted) await ExecuteBodyAsync();
            else await Assert.ThrowsAsync<UnauthorizedAccessException>(ExecuteBodyAsync);
            await Assert.That(bodyObserved).IsTrue().Because(operation + ": " + change);
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            await using var independent = fixture.Native.CreateDatabase();
            if (accepted)
                await RequireEffectAsync(independent, seed, operation, fixture.Binding.LocalSubjectId, default);
            else
                await Assert.That(await SnapshotAsync(independent, seed)).IsEqualTo(before);
            await Assert.That(await independent.PlatformUserRoles.AnyAsync(row => row.Id == seed.GrantId)).IsTrue();
            await Assert.That(await independent.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                .Select(row => row.ProviderKey).SingleAsync()).IsEqualTo(fixture.Binding.LocalSubjectId.ToString("D"));
            await Assert.That(await independent.Set<LocalIdentityUser>().Where(row => row.Id == fixture.Binding.LocalSubjectId)
                .Select(row => row.SecurityStamp).SingleAsync()).IsEqualTo(identity.SecurityStamp);
            await Assert.That(await independent.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Security.AuthorizationProvider)
                .Select(row => row.Value).SingleAsync()).IsEqualTo("\"local\"");
            if (seed.SupportId is { } sessionId)
            {
                var session = await independent.SupportAccessSessions.AsNoTracking().SingleAsync(row => row.Id == sessionId);
                await Assert.That(session.StatusId).IsEqualTo((int)SupportAccessSessionStatusEnum.Active);
                await Assert.That(session.ExpiresAtUtc).IsEqualTo(seed.SupportExpiry);
            }
        }
    }

    [Test]
    [Arguments("clone")]
    [Arguments("create-version")]
    [Arguments("update-version")]
    [Arguments("publish")]
    [Arguments("archive")]
    [Arguments("switch")]
    [Arguments("rollback")]
    public async Task AcquisitionRejectsAuthorityLostAfterPreparationBeforePlanBody(string operation)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        string before = await SnapshotAsync(context, seed);
        bool bodyCompleted = false;
        var owner = new BodyBoundary((ISerializableAttemptOwner)new EfCoreUnitOfWork(context),
            async token => await context.PlatformUserRoles.Where(row => row.Id == seed.GrantId).ExecuteDeleteAsync(token),
            _ => { bodyCompleted = true; return Task.CompletedTask; });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            InvokeAsync(request.Services, Command(operation, seed, fixture.Binding.LocalSubjectId), owner));
        await Assert.That(bodyCompleted).IsFalse();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await SnapshotAsync(observer, seed)).IsEqualTo(before);
        await Assert.That(await observer.PlatformUserRoles.AnyAsync(row => row.Id == seed.GrantId)).IsTrue();
    }

    [Test]
    [Arguments("clone")]
    [Arguments("create-version")]
    [Arguments("update-version")]
    [Arguments("publish")]
    [Arguments("archive")]
    [Arguments("switch")]
    [Arguments("rollback")]
    public async Task PlanAuthorityCannotSubstituteEqualRequestOrReplayOriginalAcquisition(string operation)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        object original = Command(operation, seed, fixture.Binding.LocalSubjectId);
        object equal = EqualRequest(original);
        var prepared = await PrepareAsync(request.Services, original, fixture.Binding.LocalSubjectId);
        var owner = request.Services.GetRequiredService<ISerializableAttemptOwner>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(equal, fixture.Binding.LocalSubjectId, token);
            return true;
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ExecuteAsync(async token =>
        {
            await prepared.AcquireAsync(original, fixture.Binding.LocalSubjectId, token);
            return true;
        }));
        var wrongActor = await PrepareAsync(request.Services, original, fixture.Binding.LocalSubjectId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(async token =>
        {
            await wrongActor.AcquireAsync(original, Guid.CreateVersion7(), token);
            return true;
        }));
        var admitted = await PrepareAsync(request.Services, original, fixture.Binding.LocalSubjectId);
        await owner.ExecuteAsync(async token =>
        {
            await admitted.AcquireAsync(original, fixture.Binding.LocalSubjectId, token);
            return true;
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ExecuteAsync(async token =>
        {
            await admitted.AcquireAsync(original, fixture.Binding.LocalSubjectId, token);
            return true;
        }));
    }

    [Test]
    [Arguments("create-version", false)]
    [Arguments("create-version", true)]
    [Arguments("update-version", false)]
    [Arguments("update-version", true)]
    public async Task CallerOwnedPlanContentCannotChangeAfterRealBody(string operation, bool settingsChange)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        string before = await SnapshotAsync(context, seed);
        var quotas = new List<TenantPlanQuotaLimit> { new(TenantPlanQuotaKeys.AiDailyTenantMessages, 41) };
        var settings = new List<TenantPlanSettingOverride>
        {
            new(GovernanceSettingKeys.PublicExperience.EventCatalogLabel, "\"Caller events\"", false)
        };
        object command = operation == "create-version"
            ? new CreateInstanceTenantPlanVersionDraftCommand(seed.Key,
                Draft(seed.Key) with { QuotaLimits = quotas, SettingOverrides = settings })
            : new UpdateInstanceTenantPlanVersionDraftCommand(seed.DraftVersionId,
                new() { QuotaLimits = new() { Values = quotas }, SettingOverrides = new() { Values = settings } });
        bool bodyObserved = false;
        var owner = new BodyBoundary((ISerializableAttemptOwner)new EfCoreUnitOfWork(context), null, async token =>
        {
            await Assert.That(await context.TenantPlanVersionQuotas.AsNoTracking().AnyAsync(row =>
                row.QuotaKey == TenantPlanQuotaKeys.AiDailyTenantMessages && row.Limit == 41, token)).IsTrue();
            await Assert.That(await context.TenantPlanVersionSettings.AsNoTracking().AnyAsync(row =>
                row.SettingKey == GovernanceSettingKeys.PublicExperience.EventCatalogLabel
                && row.JsonValue == "\"Caller events\"", token)).IsTrue();
            bodyObserved = true;
            if (settingsChange) settings[0] = settings[0] with { JsonValue = "\"Changed after body\"" };
            else quotas.Add(new(TenantPlanQuotaKeys.StorageBytes, 1024));
        });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => InvokeAsync(request.Services, command, owner));
        await Assert.That(bodyObserved).IsTrue();
        await using var independent = fixture.Native.CreateDatabase();
        await Assert.That(await SnapshotAsync(independent, seed)).IsEqualTo(before);
    }

    [Test]
    [Arguments("clone")]
    [Arguments("create-version")]
    [Arguments("update-version")]
    [Arguments("publish")]
    [Arguments("archive")]
    [Arguments("switch")]
    [Arguments("rollback")]
    public async Task OriginalInstanceAdministratorPredicateCannotBeBorrowedFromTenantGrant(string operation)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        await using (var context = fixture.Native.CreateDatabase())
            await context.PlatformUserRoles.Where(row => row.Id == seed.GrantId).ExecuteDeleteAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        await using var beforeContext = fixture.Native.CreateDatabase();
        string before = await SnapshotAsync(beforeContext, seed);
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            InvokeAsync(request.Services, Command(operation, seed, fixture.Binding.LocalSubjectId)));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await SnapshotAsync(observer, seed)).IsEqualTo(before);
    }

    private static object Command(string operation, Seeded seed, Guid actor) => operation switch
    {
        "clone" => new CloneInstanceTenantPlanCommand(seed.PublishedVersionId, seed.CloneKey, "Cloned plan"),
        "create-version" => new CreateInstanceTenantPlanVersionDraftCommand(seed.Key, Draft(seed.Key)),
        "update-version" => new UpdateInstanceTenantPlanVersionDraftCommand(seed.DraftVersionId, new()
        {
            Pricing = new() { Amount = 12, CurrencyCode = "EUR", BillingPeriod = "monthly" },
            SettingOverrides = new() { Values = [new(GovernanceSettingKeys.PublicExperience.EventCatalogLabel, "\"Updated events\"", false)] },
            QuotaLimits = new() { Values = [new(TenantPlanQuotaKeys.AiDailyTenantMessages, 29)] }
        }),
        "publish" => new PublishInstanceTenantPlanVersionCommand(seed.DraftVersionId,
            TenantPlanExistingAssignmentPolicy.MoveExistingTenantsToPublishedVersion),
        "archive" => new ArchiveInstanceTenantPlanVersionCommand(seed.PublishedVersionId),
        "switch" => new SwitchInstanceTenantPlanAssignmentCommand(PlatformDefaults.DefaultTenantId, seed.PublishedVersionId, actor),
        "rollback" => new RollbackInstanceTenantPlanAssignmentCommand(PlatformDefaults.DefaultTenantId, seed.PreviousAssignmentId, actor),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static object EqualRequest(object command) => command switch
    {
        CloneInstanceTenantPlanCommand value => value with { },
        CreateInstanceTenantPlanVersionDraftCommand value => value with { },
        UpdateInstanceTenantPlanVersionDraftCommand value => value with { },
        PublishInstanceTenantPlanVersionCommand value => value with { },
        ArchiveInstanceTenantPlanVersionCommand value => value with { },
        SwitchInstanceTenantPlanAssignmentCommand value => value with { },
        RollbackInstanceTenantPlanAssignmentCommand value => value with { },
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    private static Task<IPreparedHumanAdministrativeAction> PrepareAsync(IServiceProvider services, object command, Guid actor)
    {
        var preparation = services.GetRequiredService<IHumanAdministrativeActionPreparation>();
        return command switch
        {
            CloneInstanceTenantPlanCommand value => preparation.PrepareAsync(value, actor),
            CreateInstanceTenantPlanVersionDraftCommand value => preparation.PrepareAsync(value, actor),
            UpdateInstanceTenantPlanVersionDraftCommand value => preparation.PrepareAsync(value, actor),
            PublishInstanceTenantPlanVersionCommand value => preparation.PrepareAsync(value, actor),
            ArchiveInstanceTenantPlanVersionCommand value => preparation.PrepareAsync(value, actor),
            SwitchInstanceTenantPlanAssignmentCommand value => preparation.PrepareAsync(value, actor),
            RollbackInstanceTenantPlanAssignmentCommand value => preparation.PrepareAsync(value, actor),
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
    }

    private static Task<BaseCommandResponse<Guid>> InvokeAsync(IServiceProvider services, object command,
        ISerializableAttemptOwner? owner = null)
    {
        T Handler<T>() where T : class => owner is null
            ? ActivatorUtilities.CreateInstance<T>(services)
            : ActivatorUtilities.CreateInstance<T>(services, owner);
        return command switch
        {
            CloneInstanceTenantPlanCommand value => Handler<CloneInstanceTenantPlanCommandHandler>().ExecuteAsync(value, default),
            CreateInstanceTenantPlanVersionDraftCommand value => Handler<CreateInstanceTenantPlanVersionDraftCommandHandler>().ExecuteAsync(value, default),
            UpdateInstanceTenantPlanVersionDraftCommand value => Handler<UpdateInstanceTenantPlanVersionDraftCommandHandler>().ExecuteAsync(value, default),
            PublishInstanceTenantPlanVersionCommand value => Handler<PublishInstanceTenantPlanVersionCommandHandler>().ExecuteAsync(value, default),
            ArchiveInstanceTenantPlanVersionCommand value => Handler<ArchiveInstanceTenantPlanVersionCommandHandler>().ExecuteAsync(value, default),
            SwitchInstanceTenantPlanAssignmentCommand value => Handler<SwitchInstanceTenantPlanAssignmentCommandHandler>().ExecuteAsync(value, default),
            RollbackInstanceTenantPlanAssignmentCommand value => Handler<RollbackInstanceTenantPlanAssignmentCommandHandler>().ExecuteAsync(value, default),
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
    }

    private sealed class BodyBoundary(ISerializableAttemptOwner inner,
        Func<CancellationToken, Task>? beforeBody, Func<CancellationToken, Task>? afterBody) : ISerializableAttemptOwner
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) => inner.ExecuteAsync(async token =>
        {
            if (beforeBody is not null) await beforeBody(token);
            T result = await operation(token);
            if (afterBody is not null) await afterBody(token);
            return result;
        }, cancellationToken);
    }

    private sealed class Request : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope;
        private readonly IHttpContextAccessor accessor;
        internal IServiceProvider Services => scope.ServiceProvider;
        internal DefaultHttpContext Context { get; }

        internal Request(IServiceProvider services)
        {
            scope = services.CreateAsyncScope();
            Context = new DefaultHttpContext { RequestServices = Services };
            accessor = Services.GetRequiredService<IHttpContextAccessor>();
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
            // Install in the caller's execution context before any async authentication.
            accessor.HttpContext = Context;
        }

        internal async Task AuthenticateAsync(string credential)
        {
            await Assert.That(ReferenceEquals(accessor.HttpContext, Context)).IsTrue();
            Context.Request.Headers.Authorization = "Bearer " + credential;
            var result = await Context.AuthenticateAsync();
            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(ReferenceEquals(accessor.HttpContext, Context)).IsTrue();
            Context.User = result.Principal!;
        }

        public async ValueTask DisposeAsync()
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
        }
    }
}
