using System.Data.Common;
using System.Security.Claims;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Event.Api.IntegrationTests.Builders;
using Explore.Application.Features.AiAssistant.Handlers.Commands;
using Explore.Application.Features.AiAssistant.Disclosure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Domain.Ai;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    private const string DurableAiDraftPayload = """
        {"title":"Native durable draft","participationConfiguration":{"participationHandlingModeId":1,"advanceRegistrationObligationId":1,"identityAccessModeId":1},"session":{"startTime":"2027-01-01T12:00:00Z","endTime":"2027-01-01T13:00:00Z"}}
        """;

    [Test]
    [Arguments("payload")]
    [Arguments("kind")]
    [Arguments("created")]
    [Arguments("actor")]
    [Arguments("provenance")]
    [Arguments("expired")]
    public async Task NativeAiClaimRechecksExactPreviewAndLifetimeAfterPreflight(string drift)
    {
        DateTime now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
        var clock = new AiClaimClock(now);
        var boundary = new AiClaimAdmissionBoundary();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(persistenceInterceptor: boundary);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var owner = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(database);
        var root = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = owner.TenantId,
            UserId = owner.UserId,
            ActorId = owner.ActorId,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var action = root.ProposeAction(AiProposedActionKind.CreateEventDraft,
            DurableAiDraftPayload, null, owner.UserId, now);
        string displayedVersion = action.ConsentVersionFor(owner.UserId);
        database.AiConversations.Add(root);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, owner.UserId.ToString("D"))], "native-test"))
        };
        boundary.BeforeStart = async () =>
        {
            if (drift == "expired")
            {
                clock.UtcNow = now.AddMinutes(30).AddTicks(1);
                return;
            }
            await using var competing = factory.Services.CreateAsyncScope();
            var db = competing.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var changed = await db.AiProposedActions.SingleAsync(value => value.Id == action.Id);
            switch (drift)
            {
                case "payload":
                    changed.PayloadJson = DurableAiDraftPayload.Replace("Native durable draft", "Changed draft", StringComparison.Ordinal);
                    break;
                case "kind":
                    changed.Kind = AiProposedActionKind.UpdateEventDraft;
                    break;
                case "created":
                    changed.CreatedAt = now.AddTicks(-10);
                    break;
                case "actor":
                    changed.ActingActorId = null;
                    break;
                case "provenance":
                    var message = new AiMessage
                    {
                        Id = Guid.CreateVersion7(),
                        TenantId = owner.TenantId,
                        ConversationId = root.Id,
                        Role = AiMessageRole.Assistant,
                        Content = "Changed source",
                        Sequence = 1,
                        CreatedAt = now
                    };
                    db.AiMessages.Add(message);
                    changed.MessageId = message.Id;
                    break;
            }
            await db.SaveChangesAsync();
        };
        try
        {
            var handler = ActivatorUtilities.CreateInstance<ConfirmAiProposedActionCommandHandler>(scope.ServiceProvider, clock);
            var result = await handler.ExecuteAsync(new()
            {
                ConversationId = root.Id,
                ProposedActionId = action.Id,
                ExpectedVersion = displayedVersion
            }, CancellationToken.None);
            await Assert.That(boundary.Fired).IsTrue();
            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo(
                drift == "expired" ? "proposed_action_expired" : "proposed_action_version_changed");
            database.ChangeTracker.Clear();
            var preserved = await database.AiProposedActions.AsNoTracking().SingleAsync(value => value.Id == action.Id);
            await Assert.That(preserved.Status).IsEqualTo(AiProposedActionStatus.Proposed);
            await Assert.That(preserved.ConfirmedAt).IsNull();
            await Assert.That(preserved.ConfirmedBy).IsNull();
            await Assert.That(await database.AiToolExecutions.CountAsync(value => value.ProposedActionId == action.Id)).IsEqualTo(0);
            await Assert.That(await database.Events.CountAsync(value => value.CreatedBy == owner.UserId)).IsEqualTo(0);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private sealed class AiClaimClock(DateTime now) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }

    private sealed class AiClaimAdmissionBoundary : DbTransactionInterceptor
    {
        public Func<Task>? BeforeStart { get; set; }
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            var beforeStart = BeforeStart;
            if (beforeStart is not null)
            {
                // Remove the callback before the competing native save opens its own transaction.
                BeforeStart = null;
                Fired = true;
                await beforeStart();
            }
            return result;
        }
    }

    [Test]
    public async Task NativeAiConcurrentConfirmationClaimsOnceWithoutRedispatch()
    {
        var boundary = new AiConfirmationCommitBoundary();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(persistenceInterceptor: boundary);
        Guid actionId;
        Guid userId;
        Guid conversationId;
        await using (var seed = factory.Services.CreateAsyncScope())
        {
            var database = seed.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var owner = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(database);
            userId = owner.UserId;
            var root = new AiConversation
            {
                Id = Guid.CreateVersion7(),
                TenantId = owner.TenantId,
                UserId = userId,
                ActorId = owner.ActorId,
                CreatedAt = DateTime.UtcNow,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            conversationId = root.Id;
            actionId = root.ProposeAction(AiProposedActionKind.CreateEventDraft, DurableAiDraftPayload, null, userId, DateTime.UtcNow).Id;
            database.AiConversations.Add(root);
            await database.SaveChangesAsync();
        }
        await using var firstScope = factory.Services.CreateAsyncScope();
        await using var secondScope = factory.Services.CreateAsyncScope();
        var accessor = firstScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString("D"))], "native-test"))
        };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        boundary.Armed = true;
        var held = boundary.FirstCommit.Task;
        var entered = boundary.SecondStart.Task;
        var first = Task.Run(async () => await ActivatorUtilities.CreateInstance<ConfirmAiProposedActionCommandHandler>(firstScope.ServiceProvider)
            .ExecuteAsync(new()
            {
                ConversationId = conversationId,
                ProposedActionId = actionId,
                ExpectedVersion = (await firstScope.ServiceProvider.GetRequiredService<ExploreDbContext>()
                    .AiProposedActions.AsNoTracking().SingleAsync(row => row.Id == actionId)).ConsentVersionFor(userId)
            },
                deadline.Token), deadline.Token);
        Task<Explore.Application.Responses.BaseCommandResponse<Guid>>? second = null;
        try
        {
            await Task.WhenAny(held, first).WaitAsync(deadline.Token);
            if (!held.IsCompleted) await first;
            await Assert.That(held.IsCompleted).IsTrue();
            second = Task.Run(async () => await ActivatorUtilities.CreateInstance<ConfirmAiProposedActionCommandHandler>(secondScope.ServiceProvider)
                .ExecuteAsync(new()
                {
                    ConversationId = conversationId,
                    ProposedActionId = actionId,
                    ExpectedVersion = (await secondScope.ServiceProvider.GetRequiredService<ExploreDbContext>()
                        .AiProposedActions.AsNoTracking().SingleAsync(row => row.Id == actionId)).ConsentVersionFor(userId)
                },
                    deadline.Token), deadline.Token);
            await Task.WhenAny(entered, second).WaitAsync(deadline.Token);
            if (!entered.IsCompleted) await second;
            await Assert.That(entered.IsCompleted).IsTrue();
        }
        finally
        {
            boundary.Release.TrySetResult();
            try
            {
                await first;
                if (second is not null) await second;
            }
            finally
            {
                accessor.HttpContext = null;
            }
        }
        var firstResult = await first;
        var secondResult = await second!;
        await Assert.That(firstResult.IsSuccess).IsTrue();
        if (secondResult.IsSuccess)
            await Assert.That(secondResult.Id).IsEqualTo(firstResult.Id);
        else
            await Assert.That(secondResult.FailureCode).IsEqualTo("proposed_action_authority_changed");
        await using var verify = factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await db.AiToolExecutions.CountAsync(row => row.ProposedActionId == actionId)).IsEqualTo(1);
        await Assert.That((await db.AiProposedActions.AsNoTracking().SingleAsync(row => row.Id == actionId)).Status)
            .IsEqualTo(AiProposedActionStatus.Executed);
        await Assert.That(await db.Events.CountAsync(row => row.CreatedBy == userId)).IsEqualTo(1);
        await Assert.That((await db.AiToolExecutions.SingleAsync(row => row.ProposedActionId == actionId)).PrimaryEventId)
            .IsEqualTo(firstResult.Id);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeAiClaimAndLocalSettlementDoNotCommitPartialExecution(bool failSettlement)
    {
        var boundary = new AiExecutionWriteBoundary(failSettlement);
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(persistenceInterceptor: boundary);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var owner = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(database);
        Guid userId = owner.UserId;
        var root = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = owner.TenantId,
            UserId = userId,
            ActorId = owner.ActorId,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var action = root.ProposeAction(AiProposedActionKind.CreateEventDraft, DurableAiDraftPayload, null, userId, DateTime.UtcNow);
        database.AiConversations.Add(root);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString("D"))], "native-test"))
        };
        try
        {
            var handler = ActivatorUtilities.CreateInstance<ConfirmAiProposedActionCommandHandler>(scope.ServiceProvider);
            var request = new Explore.Application.Features.AiAssistant.Requests.Commands.ConfirmAiProposedActionCommand
            {
                ConversationId = root.Id,
                ProposedActionId = action.Id,
                ExpectedVersion = action.ConsentVersionFor(userId)
            };
            if (failSettlement)
                await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync(request, default));
            else
                await Assert.That((await handler.ExecuteAsync(request, default)).IsSuccess).IsTrue();
            await Assert.That(boundary.Fired).IsTrue();
            database.ChangeTracker.Clear();
            var stored = await database.AiProposedActions.AsNoTracking().SingleAsync(row => row.Id == action.Id);
            await Assert.That(stored.Status).IsEqualTo(failSettlement ? AiProposedActionStatus.Confirmed : AiProposedActionStatus.Executed);
            await Assert.That(stored.ConfirmedBy).IsEqualTo((Guid?)userId);
            var executions = await database.AiToolExecutions.AsNoTracking()
                .Where(row => row.ProposedActionId == action.Id).ToListAsync();
            await Assert.That(executions).Count().IsEqualTo(failSettlement ? 0 : 1);
            if (!failSettlement)
                await Assert.That(executions.Single().Succeeded).IsTrue();
            await Assert.That(await database.Events.CountAsync(row => row.CreatedBy == userId))
                .IsEqualTo(failSettlement ? 0 : 1);
            var replay = await handler.ExecuteAsync(request, default);
            await Assert.That(replay.IsSuccess).IsTrue();
            database.ChangeTracker.Clear();
            await Assert.That(await database.AiToolExecutions.CountAsync(row => row.ProposedActionId == action.Id))
                .IsEqualTo(1);
            await Assert.That(await database.Events.CountAsync(row => row.CreatedBy == userId)).IsEqualTo(1);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeAiGroupActorRequiresItsCurrentMembership(bool revoke)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var user = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(database);
        var group = new Group
        {
            Id = Guid.CreateVersion7(),
            FullName = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var actor = new ActorBuilder().WithActorType(ActorTypeEnum.Group).Build();
        actor.GroupId = group.Id;
        var link = new GroupTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = user.TenantId,
            Tenant = null!,
            GroupId = group.Id,
            Group = group,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var member = new GroupMember
        {
            Id = Guid.CreateVersion7(),
            TenantId = user.TenantId,
            Tenant = null!,
            GroupTenantId = link.Id,
            GroupTenant = link,
            UserId = user.UserId,
            User = null!,
            RoleId = (int)RoleEnum.GroupAdmin,
            Role = null!,
            CreatedAt = DateTime.UtcNow
        };
        database.Groups.Add(group);
        database.Actors.Add(actor);
        database.GroupTenants.Add(link);
        database.GroupMembers.Add(member);
        int permissionId = await database.Permissions.Where(row => row.MasterCode == PermissionCodes.EventCreate)
            .Select(row => row.Id).SingleAsync();
        if (!await database.RolePermissions.AnyAsync(row => row.RoleId == (int)RoleEnum.GroupAdmin && row.PermissionId == permissionId))
            database.RolePermissions.Add(new RolePermission
            {
                RoleId = (int)RoleEnum.GroupAdmin,
                Role = null!,
                PermissionId = permissionId,
                Permission = null!,
                GrantedAt = DateTime.UtcNow
            });
        var root = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = user.TenantId,
            UserId = user.UserId,
            ActorId = actor.Id,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        database.AiConversations.Add(root);
        await database.SaveChangesAsync();
        if (revoke)
            await database.GroupMembers.Where(row => row.Id == member.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
        database.ChangeTracker.Clear();
        var repository = scope.ServiceProvider.GetRequiredService<IAiConversationRepository>();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(async token =>
        {
            var current = await repository.GetByIdForUpdateAsync(root.Id, actor.Id, token);
            if (revoke)
                await Assert.That(current).IsNull();
            else
            {
                await Assert.That(current).IsNotNull();
                current!.AddMessage(AiMessageRole.User, "accepted", user.UserId, DateTime.UtcNow);
                await repository.Update(current);
            }
            return true;
        }, default);
        await Assert.That(await database.AiMessages.CountAsync(row => row.ConversationId == root.Id)).IsEqualTo(revoke ? 0 : 1);
    }

    [Test]
    [Arguments("none")]
    [Arguments("membership")]
    [Arguments("grant")]
    [Arguments("permission")]
    [Arguments("actor")]
    public async Task NativeAiAdmittedActorRequiresItsCurrentEventCreateGrant(string drift)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var publisher = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(database);
        var root = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = publisher.TenantId,
            UserId = publisher.UserId,
            ActorId = publisher.OrganizationActorId,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        database.AiConversations.Add(root);
        await database.SaveChangesAsync();
        switch (drift)
        {
            case "membership":
                await database.OrganizationMembers.Where(row => row.TenantId == publisher.TenantId && row.UserId == publisher.UserId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
                break;
            case "grant":
                await database.Set<RolePermission>().Where(row => row.RoleId == (int)RoleEnum.OrgAdmin
                    && row.Permission.MasterCode == PermissionCodes.EventCreate).ExecuteDeleteAsync();
                break;
            case "permission":
                await database.Set<Permission>().Where(row => row.MasterCode == PermissionCodes.EventCreate)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsActive, false));
                break;
            case "actor":
                await database.Actors.Where(row => row.Id == publisher.OrganizationActorId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsSuspended, true));
                break;
        }
        database.ChangeTracker.Clear();
        var repository = scope.ServiceProvider.GetRequiredService<IAiConversationRepository>();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(async token =>
        {
            var current = await repository.GetByIdForUpdateAsync(root.Id, publisher.OrganizationActorId, token);
            if (drift == "none")
            {
                await Assert.That(current).IsNotNull();
                current!.AddMessage(AiMessageRole.User, "accepted", publisher.UserId, DateTime.UtcNow);
                await repository.Update(current);
            }
            else
                await Assert.That(current).IsNull();
            return true;
        }, default);
        await Assert.That(await database.AiMessages.CountAsync(row => row.ConversationId == root.Id))
            .IsEqualTo(drift == "none" ? 1 : 0);
    }

    [Test]
    [Arguments(false, false, false)]
    [Arguments(true, false, false)]
    [Arguments(false, true, false)]
    [Arguments(false, false, true)]
    public async Task NativeAiProviderRunsOutsideTransactionsAndCannotResurrectCancellation(
        bool cancelDuringProvider, bool cancelRequestAtReturn, bool failedDisclosure)
    {
        var provider = new AiProcessingProviderBoundary();
        string canary = Guid.CreateVersion7().ToString("N");
        var disclosure = new FailedAiDisclosure(canary);
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IAiChatProvider>(provider);
            if (failedDisclosure)
                services.AddSingleton<IAiContextGateway>(disclosure);
        }));
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid userId = (await database.InstanceBootstrapStates.SingleAsync()).CompletedByUserId!.Value;
        foreach (var (key, value, type) in new[]
        {
            (GovernanceSettingKeys.AiAssistant.Enabled, "true", SettingValueType.Boolean),
            (GovernanceSettingKeys.AiAssistant.Provider, AiProviderDefaults.ProviderFake, SettingValueType.String),
            (GovernanceSettingKeys.AiAssistant.ModelId, "native-model", SettingValueType.String)
        })
        {
            var setting = await database.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (setting is null)
                database.SystemSettings.Add(new Explore.Domain.SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = key,
                    Value = value,
                    ValueType = type,
                    IsLocked = true,
                    Category = "AiAssistant",
                    CreatedAt = DateTime.UtcNow
                });
            else
                setting.Value = value;
        }
        var root = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        if (failedDisclosure)
        {
            var publisher = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(database);
            var target = new EventBuilder().WithTenantId(publisher.TenantId)
                .WithActorId(publisher.OrganizationActorId).WithStatus(EventStatusEnum.Published).Build();
            database.Events.Add(target);
            root.TenantId = publisher.TenantId;
            root.UserId = publisher.UserId;
            userId = publisher.UserId;
            root.AddReference(AiReferenceKind.Event, target.Id, target.Title, null, userId, DateTime.UtcNow);
        }
        root.AddMessage(AiMessageRole.User, "question", userId, DateTime.UtcNow);
        var run = root.QueueRun(AiProviderDefaults.ProviderFake, "native-model", DateTime.UtcNow);
        database.AiConversations.Add(root);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        ((MemoryCache)scope.ServiceProvider.GetRequiredService<IMemoryCache>()).Compact(1.0);
        provider.HasTransaction = () => database.Database.CurrentTransaction is not null;
        var signal = provider.Entered.Task;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        if (cancelRequestAtReturn)
            provider.BeforeReturn = deadline.Cancel;
        var processing = ActivatorUtilities.CreateInstance<ProcessAiRunCommandHandler>(scope.ServiceProvider).ExecuteAsync(
            new()
            {
                TenantId = root.TenantId,
                ConversationId = root.Id,
                RunId = run.Id
            }, deadline.Token);
        try
        {
            await Task.WhenAny(signal, processing).WaitAsync(deadline.Token);
            if (!signal.IsCompleted) await processing;
            await Assert.That(signal.IsCompleted).IsTrue();
            await Assert.That(provider.ObservedTransaction).IsFalse();
            if (failedDisclosure)
            {
                await Assert.That(disclosure.Observed).IsTrue();
                await Assert.That(provider.Payload).IsNotNull();
                await Assert.That(System.Text.Json.JsonSerializer.Serialize(provider.Payload)).DoesNotContain(canary);
            }
            if (cancelDuringProvider)
            {
                await using var cancellation = hosted.Services.CreateAsyncScope();
                var accessor = cancellation.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
                accessor.HttpContext = new DefaultHttpContext
                {
                    RequestServices = cancellation.ServiceProvider,
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId.ToString("D"))], "native-test"))
                };
                try
                {
                    await Assert.That((await ActivatorUtilities.CreateInstance<CancelAiRunCommandHandler>(cancellation.ServiceProvider)
                        .ExecuteAsync(new() { ConversationId = root.Id, RunId = run.Id }, deadline.Token)).IsSuccess).IsTrue();
                }
                finally
                {
                    accessor.HttpContext = null;
                }
            }
        }
        finally
        {
            provider.Response.TrySetResult(cancelRequestAtReturn
                ? AiChatProviderResult.Success(new AiChatResponse("Must not be persisted",
                    [new AiProposedActionCandidate(AiProposedActionKind.CreateEventDraft, DurableAiDraftPayload)],
                    new(null, null, null)))
                : AiChatProviderResult.Failure("native_failure", "Native provider failure."));
        }
        await processing;
        database.ChangeTracker.Clear();
        var stored = await database.AiRuns.AsNoTracking().SingleAsync(row => row.Id == run.Id);
        await Assert.That(stored.Status).IsEqualTo(cancelDuringProvider ? AiRunStatus.Cancelled : AiRunStatus.Failed);
        await Assert.That(stored.CompletedAt).IsNotNull();
        await Assert.That(await database.AiMessages.CountAsync(row => row.ConversationId == root.Id)).IsEqualTo(1);
        await Assert.That(await database.AiProposedActions.AnyAsync(row => row.ConversationId == root.Id)).IsFalse();
    }

    [Test]
    [Arguments("transaction")]
    [Arguments("detached")]
    [Arguments("ownership")]
    [Arguments("child")]
    public async Task NativeAiGraphRejectsMissingAuthorityAndChangedParentBindings(string change)
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid userId = (await database.InstanceBootstrapStates.SingleAsync()).CompletedByUserId!.Value;
        var root = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            UserId = userId,
            Title = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        database.AiConversations.Add(root);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var repository = scope.ServiceProvider.GetRequiredService<IAiConversationRepository>();
        if (change == "transaction")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetByIdForUpdateAsync(root.Id, default));
            return;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(async token =>
            {
                var current = change == "detached"
                    ? await repository.GetByIdWithDetailsAsync(root.Id, token)
                    : await repository.GetByIdForUpdateAsync(root.Id, token);
                await Assert.That(current).IsNotNull();
                if (change == "ownership") current!.UserId = Guid.CreateVersion7();
                if (change == "child")
                    current!.AddMessage(AiMessageRole.User, "rejected", userId, DateTime.UtcNow).ConversationId = Guid.CreateVersion7();
                await repository.Update(current!);
                return true;
            }, default));
        database.ChangeTracker.Clear();
        await Assert.That((await database.AiConversations.AsNoTracking().SingleAsync(value => value.Id == root.Id)).UserId)
            .IsEqualTo(userId);
        await Assert.That(await database.AiMessages.AnyAsync(value => value.ConversationId == root.Id)).IsFalse();
    }

    [Test]
    public async Task NativeAiGraphPersistsChildOnlyChangesAndRetainsUnrelatedTracking()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid userId = (await database.InstanceBootstrapStates.SingleAsync()).CompletedByUserId!.Value;
        var root = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        root.AddMessage(AiMessageRole.User, "before", userId, DateTime.UtcNow);
        root.AddReference(AiReferenceKind.Event, Guid.CreateVersion7(), "reference", "before", userId, DateTime.UtcNow);
        database.AiConversations.Add(root);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        var unrelated = await database.InstanceBootstrapStates.SingleAsync();
        var repository = scope.ServiceProvider.GetRequiredService<IAiConversationRepository>();
        Guid priorStamp = root.ConcurrencyStamp;
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(async token =>
        {
            var current = await repository.GetByIdForUpdateAsync(root.Id, token);
            await Assert.That(current).IsNotNull();
            current!.Messages.Single().Content = "after";
            current.References.Single().Summary = "after";
            await repository.Update(current);
            await Assert.That(current.ConcurrencyStamp).IsNotEqualTo(priorStamp);
            await Assert.That(database.Entry(unrelated).State).IsEqualTo(EntityState.Unchanged);
            return true;
        }, default);
        database.ChangeTracker.Clear();
        var stored = await repository.GetByIdWithDetailsAsync(root.Id, default);
        await Assert.That(stored!.Messages.Single().Content).IsEqualTo("after");
        await Assert.That(stored.References.Single().Summary).IsEqualTo("after");
        await Assert.That(stored.ConcurrencyStamp).IsNotEqualTo(priorStamp);
    }

    [Test]
    public async Task NativeAiPrivacyRedactionRetainsItsPurposeAfterOwnerDeletion()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid userId = (await database.InstanceBootstrapStates.SingleAsync()).CompletedByUserId!.Value;
        var root = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        root.AddMessage(AiMessageRole.User, "private", userId, DateTime.UtcNow);
        database.AiConversations.Add(root);
        await database.SaveChangesAsync();
        await database.Users.Where(value => value.Id == userId).ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsDeleted, true));
        database.ChangeTracker.Clear();
        var repository = scope.ServiceProvider.GetRequiredService<IAiConversationRepository>();
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(async token =>
        {
            await Assert.That(await repository.GetByIdForUpdateAsync(root.Id, token)).IsNull();
            var current = await repository.GetByIdForPrivacyUpdateAsync(root.Id, token);
            await Assert.That(current).IsNotNull();
            current!.Messages.Single().Content = "redacted";
            await repository.Update(current);
            return true;
        }, default);
        await Assert.That((await repository.GetByIdWithDetailsAsync(root.Id, default))!.Messages.Single().Content).IsEqualTo("redacted");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeAiCancellationCommitsOrRollsBackTheWholeRootAndRun(bool failAfterRunWrite)
    {
        var boundary = new AiCancellationWriteBoundary(failAfterRunWrite);
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(persistenceInterceptor: boundary);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid userId = (await database.InstanceBootstrapStates.SingleAsync()).CompletedByUserId!.Value;
        var conversation = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            UserId = userId,
            Title = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var run = conversation.QueueRun("native", "native-model", DateTime.UtcNow);
        database.AiConversations.Add(conversation);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();
        boundary.RunId = run.Id;
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString("D"))], "native-test"))
        };
        try
        {
            var handler = ActivatorUtilities.CreateInstance<CancelAiRunCommandHandler>(scope.ServiceProvider);
            var request = new Explore.Application.Features.AiAssistant.Requests.Commands.CancelAiRunCommand
            {
                ConversationId = conversation.Id,
                RunId = run.Id
            };
            if (failAfterRunWrite)
                await Assert.ThrowsAsync<InvalidOperationException>(() => handler.ExecuteAsync(request, default));
            else
                await Assert.That((await handler.ExecuteAsync(request, default)).IsSuccess).IsTrue();
            await Assert.That(boundary.Fired).IsTrue();
            database.ChangeTracker.Clear();
            var storedRoot = await database.AiConversations.AsNoTracking().SingleAsync(value => value.Id == conversation.Id);
            var storedRun = await database.AiRuns.AsNoTracking().SingleAsync(value => value.Id == run.Id);
            await Assert.That(storedRoot.Status).IsEqualTo(failAfterRunWrite
                ? AiConversationStatus.Running : AiConversationStatus.Active);
            await Assert.That(storedRun.Status).IsEqualTo(failAfterRunWrite ? AiRunStatus.Queued : AiRunStatus.Cancelled);
            await Assert.That(storedRoot.UserId).IsEqualTo(userId);
            await Assert.That(storedRun.ConversationId).IsEqualTo(conversation.Id);
            if (failAfterRunWrite)
                await Assert.That(storedRun.CompletedAt).IsNull();
            else
            {
                await Assert.That(storedRun.CompletedAt).IsNotNull();
                var replay = await handler.ExecuteAsync(request, default);
                await Assert.That(replay.IsSuccess).IsTrue();
                await Assert.That(replay.Id).IsEqualTo(run.Id);
                database.ChangeTracker.Clear();
                await Assert.That((await database.AiRuns.AsNoTracking().SingleAsync(value => value.Id == run.Id)).CompletedAt)
                    .IsEqualTo(storedRun.CompletedAt);
            }
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private sealed class AiConfirmationCommitBoundary : DbTransactionInterceptor
    {
        private int _starts;
        private int _commits;
        public bool Armed { get; set; }
        public TaskCompletionSource FirstCommit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && Interlocked.Increment(ref _starts) == 2) SecondStart.TrySetResult();
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && Interlocked.Increment(ref _commits) == 1)
            {
                FirstCommit.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    private sealed class AiExecutionWriteBoundary(bool fail) : DbCommandInterceptor
    {
        private int _fired;
        public bool Fired => Volatile.Read(ref _fired) != 0;

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            var addedIds = eventData.Context!.ChangeTracker.Entries<AiToolExecution>()
                .Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity.Id).ToArray();
            bool execution = command.Parameters.Cast<DbParameter>().Any(parameter =>
                parameter.Value is Guid id && addedIds.Contains(id)
                || parameter.Value is string value && addedIds.Any(id => value.Equals(id.ToString("D"), StringComparison.OrdinalIgnoreCase)));
            if (result.HasRows && execution && Interlocked.CompareExchange(ref _fired, 1, 0) == 0 && fail)
            {
                await result.DisposeAsync();
                throw new InvalidOperationException("Native failure after the actual AI execution insert.");
            }
            return result;
        }
    }

    private sealed class AiProcessingProviderBoundary : IAiChatProvider
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AiChatProviderResult> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Func<bool> HasTransaction { get; set; } = () => false;
        public bool ObservedTransaction { get; private set; }
        public Action? BeforeReturn { get; set; }
        public AiChatPayload? Payload { get; private set; }

        public async Task<AiChatProviderResult> SendAsync(AiChatPayload request, CancellationToken cancellationToken = default)
        {
            Payload = request;
            ObservedTransaction = HasTransaction();
            Entered.TrySetResult();
            var result = await Response.Task.WaitAsync(cancellationToken);
            BeforeReturn?.Invoke();
            return result;
        }
    }

    private sealed class FailedAiDisclosure(string canary) : IAiContextGateway
    {
        public bool Observed { get; private set; }

        public AiContextSanitizedEnvelope Sanitize(AiContextSanitizationInput request)
        {
            Observed = true;
            return AiContextSanitizedEnvelope.Failed(request.EntityName, canary, canary);
        }

        public IReadOnlyList<AiContextSanitizedEnvelope> SanitizeMany(IReadOnlyList<AiContextSanitizationInput> requests) =>
            requests.Select(Sanitize).ToArray();
    }

    private sealed class AiCancellationWriteBoundary(bool fail) : DbCommandInterceptor
    {
        private int _fired;
        public Guid? RunId { get; set; }
        public bool Fired => Volatile.Read(ref _fired) != 0;

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (result > 0 && ObserveRun(command) && fail)
                throw new InvalidOperationException("Native AI cancellation failure after the actual run write.");
            return ValueTask.FromResult(result);
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (result.HasRows && ObserveRun(command) && fail)
            {
                await result.DisposeAsync();
                throw new InvalidOperationException("Native AI cancellation failure after the actual run write.");
            }
            return result;
        }

        private bool ObserveRun(DbCommand command) =>
            RunId is { } target && command.Parameters.Cast<DbParameter>().Any(parameter =>
                parameter.Value is Guid id && id == target
                || parameter.Value is string value && value.Equals(target.ToString("D"), StringComparison.OrdinalIgnoreCase))
            && Interlocked.CompareExchange(ref _fired, 1, 0) == 0;
    }
}
