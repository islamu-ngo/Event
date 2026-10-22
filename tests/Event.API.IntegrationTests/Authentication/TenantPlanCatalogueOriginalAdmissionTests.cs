using System.Data.Common;
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
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed partial class TenantPlanCatalogueOriginalAdmissionTests
{
    [Test]
    [Arguments("create-draft")]
    [Arguments("create-version")]
    [Arguments("update-version")]
    [Arguments("clone")]
    [Arguments("publish")]
    [Arguments("archive")]
    public async Task PlanWriterWaitsBeforeBeginAndRetainsEnrollmentThroughSettlement(string operation)
    {
        var transactions = new PlanTransactionObserver();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            interceptor: transactions, configureServices: services =>
                services.Replace(ServiceDescriptor.Scoped<IEventDiscoveryEnrollmentGate>(provider =>
                    new ObservedPlanGate(new EventDiscoveryEnrollmentGate(provider.GetRequiredService<ExploreDbContext>())))));
        var seed = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        await request.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        transactions.Owner = context;
        var gate = (ObservedPlanGate)request.Services.GetRequiredService<IEventDiscoveryEnrollmentGate>();
        var owner = new PlanSettlementObserver((ISerializableAttemptOwner)new EfCoreUnitOfWork(context),
            context, gate, request.Services.GetRequiredService<IHumanActionCommitFinalizer>());
        await using var blockerContext = fixture.Native.CreateDatabase();
        blockerContext.BindTenantLease();
        await using var blocker = new EventDiscoveryEnrollmentGate(blockerContext);
        IAsyncDisposable? blockingLease = await blocker.AcquireAsync(LocalIdentityLifecycleHttpFixture.CancellationToken);
        await using var contenderContext = fixture.Native.CreateDatabase();
        contenderContext.BindTenantLease();
        await using var contender = new EventDiscoveryEnrollmentGate(contenderContext);
        Task<IAsyncDisposable>? competingAcquisition = null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(LocalIdentityLifecycleHttpFixture.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        object command = operation == "create-draft"
            ? new CreateInstanceTenantPlanDraftCommand(Draft(seed.CloneKey))
            : Command(operation, seed, fixture.Binding.LocalSubjectId);
        Task<BaseCommandResponse<Guid>> execution = InvokeAsync(request.Services, command, owner);
        try
        {
            Task firstBoundary = await Task.WhenAny(gate.Requested.Task, owner.Entered.Task, execution)
                .WaitAsync(deadline.Token);
            await Assert.That(ReferenceEquals(firstBoundary, gate.Requested.Task)).IsTrue()
                .Because("Plan authority must wait for enrollment before entering its transaction owner.");
            await Assert.That(gate.Acquisition!.IsCompleted).IsFalse();
            await Assert.That(owner.Entered.Task.IsCompleted).IsFalse();
            await Assert.That(transactions.Starts).IsEqualTo(0);
            await Assert.That(context.Database.CurrentTransaction).IsNull();

            await blockingLease.DisposeAsync();
            blockingLease = null;
            await Task.WhenAny(owner.TerminalReached.Task, execution).WaitAsync(deadline.Token);
            if (!owner.TerminalReached.Task.IsCompleted) await execution;
            await Assert.That(owner.TerminalReached.Task.IsCompleted).IsTrue();
            await Assert.That(transactions.Starts).IsEqualTo(1);
            await Assert.That(context.Database.CurrentTransaction).IsNotNull();
            competingAcquisition = contender.AcquireAsync(deadline.Token);
            await Assert.That(competingAcquisition.IsCompleted).IsFalse();

            owner.ReleaseTerminal.TrySetResult();
            await Task.WhenAny(owner.Settled.Task, execution).WaitAsync(deadline.Token);
            if (!owner.Settled.Task.IsCompleted) await execution;
            await Assert.That(owner.Settled.Task.IsCompleted).IsTrue();
            await Assert.That(context.Database.CurrentTransaction).IsNull();
            gate.RequireCurrent();
            await Assert.That(competingAcquisition.IsCompleted).IsFalse();
            owner.ReleaseSettlement.TrySetResult();
            var result = await execution.WaitAsync(deadline.Token);
            await Assert.That(result.IsSuccess).IsTrue();
            await using var nextLease = await competingAcquisition.WaitAsync(deadline.Token);
            contender.RequireCurrent();
            await using var observed = fixture.Native.CreateDatabase();
            if (operation == "create-draft")
                await Assert.That(await observed.TenantPlans.AnyAsync(row => row.Id == result.Id && row.Key == seed.CloneKey)).IsTrue();
            else
                await RequireEffectAsync(observed, seed, operation, fixture.Binding.LocalSubjectId, deadline.Token);
        }
        finally
        {
            owner.ReleaseTerminal.TrySetResult();
            owner.ReleaseSettlement.TrySetResult();
            if (blockingLease is not null) await blockingLease.DisposeAsync();
            // Drain the real owner on assertion failure before disposing its request scope.
            try { await execution.WaitAsync(deadline.Token); }
            catch { }
            await deadline.CancelAsync();
            if (competingAcquisition is not null)
            {
                try { await (await competingAcquisition).DisposeAsync(); }
                catch (OperationCanceledException) { }
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
            CreateInstanceTenantPlanDraftCommand value => Handler<CreateInstanceTenantPlanDraftCommandHandler>().ExecuteAsync(value, default),
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

    private sealed class ObservedPlanGate(EventDiscoveryEnrollmentGate inner) : IEventDiscoveryEnrollmentGate, IAsyncDisposable
    {
        internal TaskCompletionSource Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<IAsyncDisposable>? Acquisition { get; private set; }

        public Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken = default)
        {
            Acquisition = inner.AcquireAsync(cancellationToken);
            Requested.TrySetResult();
            return Acquisition;
        }

        public void RequireCurrent() => inner.RequireCurrent();
        public bool TryRequireCurrent() => inner.TryRequireCurrent();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class PlanSettlementObserver(ISerializableAttemptOwner inner, ExploreDbContext context,
        IEventDiscoveryEnrollmentGate gate, IHumanActionCommitFinalizer finalizer) : ISerializableAttemptOwner
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource TerminalReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseTerminal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseSettlement { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            T result = await inner.ExecuteAsync(async token =>
            {
                T value = await operation(token);
                finalizer.Register(async finalToken =>
                {
                    gate.RequireCurrent();
                    if (context.Database.CurrentTransaction is null)
                        throw new InvalidOperationException("The terminal read must retain the original transaction.");
                    TerminalReached.TrySetResult();
                    await ReleaseTerminal.Task.WaitAsync(finalToken);
                    gate.RequireCurrent();
                }, _ => { });
                return value;
            }, cancellationToken);
            gate.RequireCurrent();
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("The real transaction owner must settle before returning.");
            Settled.TrySetResult();
            await ReleaseSettlement.Task.WaitAsync(cancellationToken);
            return result;
        }
    }

    private sealed class PlanTransactionObserver : DbTransactionInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        internal int Starts { get; private set; }

        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
            TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner)) Starts++;
            return ValueTask.FromResult(result);
        }
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
