using System.Security.Claims;
using System.Text.Json;
using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.AiAssistant.Actions;
using Explore.Application.Features.AiAssistant.Handlers.Commands;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>Actual credential, source leases and committing owner; only the external PDP transport is substituted.</summary>
[NotInParallel]
public sealed class HumanAiModerationProposalPreparationTests
{
    [Test]
    [Arguments("local")]
    [Arguments("cerbos")]
    public async Task ProposalKeepsConversationOwnerAndTruthfulTargetWithoutMutatingEvent(string provider)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var rows = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var context = request.Services.GetRequiredService<ExploreDbContext>();
        var transport = new Transport(context);
        var preparation = CreatePreparation(request.Services, fixture.Clock, provider, transport);
        var handler = ActivatorUtilities.CreateInstance<ProposeAiToolActionCommandHandler>(request.Services, preparation);
        await Assert.That(await request.Services.GetRequiredService<IEventRepository>()
            .GetAuthorizationTargetByIdAsync(rows.Event, CancellationToken.None)).IsNull();
        var result = await handler.ExecuteAsync(Command(rows), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
        await using var observer = CreateTenantDatabase(fixture, PlatformDefaults.DefaultTenantId);
        var proposal = await observer.AiProposedActions.SingleAsync(value => value.ConversationId == rows.Conversation);
        await Assert.That(proposal.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(proposal.CreatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(proposal.Status).IsEqualTo(AiProposedActionStatus.Proposed);
        await using var targetObserver = CreateTenantDatabase(fixture, rows.Target);
        var parent = await targetObserver.Events.SingleAsync(value => value.Id == rows.Event);
        await Assert.That(parent.ConcurrencyStamp).IsEqualTo(rows.Stamp);
        await Assert.That(parent.EventStatusId).IsEqualTo((int)EventStatusEnum.Draft);
        await Assert.That(await observer.AiToolExecutions.AnyAsync(value => value.ProposedActionId == proposal.Id)).IsFalse();
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(await context.Events.AnyAsync(value => value.Id == rows.Event)).IsFalse();
        if (provider == "cerbos")
        {
            var sent = transport.Requests.Single();
            var resource = sent.Resources.Single().Resource;
            await Assert.That(resource.Id).IsEqualTo(rows.Event.ToString("D"));
            await Assert.That(resource.Scope).IsEqualTo(PlatformDefaults.DefaultTenantId.ToString("D"));
            await Assert.That(resource.Attr["tenantId"].StringValue).IsEqualTo(rows.Target.ToString("D"));
            await Assert.That(resource.Attr.Keys.ToHashSet().SetEquals(["tenantId", "eventId"])).IsTrue();
            await Assert.That(sent.Principal.Attr["eventAssignments"].StructValue.Fields[rows.Event.ToString()]
                .StructValue.Fields["tenantId"].StringValue).IsEqualTo(rows.Target.ToString("D"));
            await Assert.That(transport.Endpoints.Single()).IsEqualTo("http://moderation.prepared.test:3593");
        }
        else await Assert.That(transport.Requests.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("ordinary")]
    [Arguments("tenant_admin")]
    [Arguments("machine")]
    [Arguments("wrong_owner")]
    [Arguments("nonmoderation")]
    public async Task NonOriginalAdministratorRequestsNeverAcquireTheDedicatedSource(string control)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var rows = await SeedAsync(fixture, instanceAdmin: control is not ("ordinary" or "tenant_admin"));
        if (control == "wrong_owner")
        {
            await using var seed = CreateTenantDatabase(fixture, PlatformDefaults.DefaultTenantId);
            Guid owner = await seed.Users.Where(value => value.Id != fixture.Binding.LocalSubjectId)
                .Select(value => value.Id).FirstAsync();
            await Assert.That(await seed.AiConversations.Where(value => value.Id == rows.Conversation)
                .ExecuteUpdateAsync(update => update.SetProperty(value => value.UserId, owner))).IsEqualTo(1);
        }
        if (control == "tenant_admin")
        {
            await using var seed = fixture.Native.CreateDatabase();
            var member = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = fixture.Clock.GetUtcNow().UtcDateTime
            };
            seed.TenantUsers.Add(member);
            seed.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = member.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = member,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant
            });
            await seed.SaveChangesAsync();
        }
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Services;
        var candidate = (await services.GetRequiredService<IAiConversationRepository>()
            .GetByIdWithDetailsAsync(rows.Conversation, CancellationToken.None))!;
        if (control == "machine")
            services.GetRequiredService<IMachinePrincipalExecutionAccessor>().SetPrincipal(new ApiKeyPrincipalContext(
                Guid.CreateVersion7().ToString(), PlatformDefaults.DefaultTenantId,
                ExternalApiKeyOwnerType.User, fixture.Binding.LocalSubjectId, ["*"]));
        var command = Command(rows);
        if (control == "nonmoderation")
            command = command with { ToolName = "CreateEventDraft", PayloadJson = """{"title":"Not moderation"}""" };
        var transport = new Transport(services.GetRequiredService<ExploreDbContext>());
        var preparation = CreatePreparation(services, fixture.Clock, "cerbos", transport);
        if (control is "machine" or "nonmoderation")
            await Assert.ThrowsAsync<AuthorizationException>(() => preparation.PrepareAsync(command, candidate, CancellationToken.None));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.PrepareAsync(command, candidate, CancellationToken.None));
        await Assert.That(transport.Requests.Count).IsEqualTo(0);
        await using var observer = CreateTenantDatabase(fixture, PlatformDefaults.DefaultTenantId);
        await Assert.That(await observer.AiProposedActions.AnyAsync(value => value.ConversationId == rows.Conversation)).IsFalse();
    }

    [Test]
    [Arguments("admin_revoked")]
    [Arguments("target_deleted")]
    [Arguments("equal_request")]
    [Arguments("replaced_principal")]
    [Arguments("credential_expired")]
    [Arguments("changed_status")]
    [Arguments("changed_payload")]
    public async Task RevocationAndCustodyChangesCannotCommitPreparedProposal(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var rows = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Services;
        var context = services.GetRequiredService<ExploreDbContext>();
        var repository = services.GetRequiredService<IAiConversationRepository>();
        var candidate = (await repository.GetByIdWithDetailsAsync(rows.Conversation, CancellationToken.None))!;
        var command = Command(rows);
        var transport = new Transport(context);
        var preparation = CreatePreparation(services, fixture.Clock, "cerbos", transport);
        var prepared = await preparation.PrepareAsync(command, candidate, CancellationToken.None);
        if (change is "admin_revoked" or "target_deleted")
        {
            await using var revoke = CreateTenantDatabase(fixture,
                change == "target_deleted" ? rows.Target : PlatformDefaults.DefaultTenantId);
            if (change == "admin_revoked")
                await revoke.PlatformUserRoles.Where(value => value.UserId == fixture.Binding.LocalSubjectId).ExecuteDeleteAsync();
            else
                await Assert.That(await revoke.Events.Where(value => value.Id == rows.Event)
                    .ExecuteUpdateAsync(update => update.SetProperty(value => value.IsDeleted, true))).IsEqualTo(1);
        }
        if (change == "replaced_principal") request.Context.User = new ClaimsPrincipal(request.Context.User);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<ISerializableAttemptOwner>()
            .ExecuteAsync(async token =>
            {
                await prepared.AcquireAsync(change == "equal_request" ? command with { } : command, token);
                var conversation = (await repository.GetByIdForUpdateAsync(command.ConversationId, candidate.ActorId, token))!;
                var action = conversation.ProposeAction(AiProposedActionKind.HeavyModerateEvent,
                    command.PayloadJson, null, fixture.Binding.LocalSubjectId, fixture.Clock.GetUtcNow().UtcDateTime);
                prepared.RetainProposal(conversation, action);
                await repository.Update(conversation);
                if (change == "changed_status") conversation.Status = AiConversationStatus.Archived;
                if (change == "changed_payload") action.PayloadJson = """{"eventId":"changed"}""";
                if (change == "credential_expired")
                {
                    var lifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                        .ObserveAsync(request.Context.User, token))!;
                    services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                    {
                        fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
                        return Task.CompletedTask;
                    }, _ => { });
                }
                return true;
            }));
        await using var observer = CreateTenantDatabase(fixture, PlatformDefaults.DefaultTenantId);
        await Assert.That(await observer.AiProposedActions.AnyAsync(value => value.ConversationId == rows.Conversation)).IsFalse();
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SelectedProviderDenialOrOutageNeverBecomesAdministratorFallback(bool unavailable)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var rows = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Services;
        var transport = new Transport(services.GetRequiredService<ExploreDbContext>(), deny: !unavailable, unavailable: unavailable);
        var preparation = CreatePreparation(services, fixture.Clock, "cerbos", transport);
        var candidate = (await services.GetRequiredService<IAiConversationRepository>()
            .GetByIdWithDetailsAsync(rows.Conversation, CancellationToken.None))!;
        if (unavailable)
            await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(() =>
                preparation.PrepareAsync(Command(rows), candidate, CancellationToken.None));
        else
            await Assert.ThrowsAsync<AuthorizationException>(() =>
                preparation.PrepareAsync(Command(rows), candidate, CancellationToken.None));
        await using var observer = CreateTenantDatabase(fixture, PlatformDefaults.DefaultTenantId);
        await Assert.That(await observer.AiProposedActions.AnyAsync(value => value.ConversationId == rows.Conversation)).IsFalse();
    }

    private static HumanAiModerationProposalPreparation CreatePreparation(
        IServiceProvider services, TimeProvider clock, string provider, Transport transport)
    {
        var options = Options.Create(new CerbosSettings { GrpcEndpoint = "http://moderation.prepared.test:3593", UsePolicyScope = true });
        var cerbos = ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(
            services, transport.Client, transport.Factory, options);
        return new(services.GetRequiredService<IHumanCommitScopePreparation>(),
            services.GetRequiredService<IHttpContextAccessor>(), services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            services.GetRequiredService<IMachinePrincipalAccessor>(), services.GetRequiredService<ITenantContext>(),
            services.GetRequiredService<IAiToolContractRegistry>(), services.GetRequiredService<IEventRepository>(),
            clock, cerbos, Options.Create(new AuthorizationProviderDeploymentOptions { Provider = provider }), options,
            () => services.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => services.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => services.GetRequiredService<IHumanAiModerationProposalTargetSourceFactory>(),
            () => services.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => services.GetRequiredService<IPolicySourceSetFence>(),
            () => services.GetRequiredService<IHumanActionCommitFinalizer>());
    }

    private static ProposeAiToolActionCommand Command(Rows rows) => new()
    {
        ConversationId = rows.Conversation,
        ToolName = "HeavyModerateEvent",
        PayloadJson = JsonSerializer.Serialize(new
        {
            eventId = rows.Event,
            expectedConcurrencyStamp = rows.Stamp,
            managementContextHasModerateHeavy = true,
            reasonCode = "policy_violation",
            destructiveSummary = "Remove this event from public discovery.",
            confirmationPhrase = EventModerationAiActionMapper.HeavyModerationConfirmationPhrase,
            acknowledgedConsequences = true
        })
    };

    private static async Task<Rows> SeedAsync(LocalIdentityLifecycleHttpFixture fixture, bool instanceAdmin = true)
    {
        await using var seed = fixture.Native.CreateDatabase();
        Guid user = fixture.Binding.LocalSubjectId;
        Guid actor = await seed.Actors.Where(value => value.UserId == user).Select(value => value.Id).SingleAsync();
        var target = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Moderation target",
            Slug = $"moderation-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var parent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = target.Id,
            Tenant = target,
            Title = "Exact cross-tenant target",
            ActorId = actor,
            Actor = null!,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
        };
        var conversation = new AiConversation
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            UserId = user,
            CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
            Status = AiConversationStatus.Active
        };
        seed.AddRange(target, parent, conversation,
            EventRoleAssignment.Create(target.Id, parent.Id, user, (int)RoleEnum.EventManager,
                EventRoleAssignmentStatus.Active, fixture.Clock.GetUtcNow().AddDays(-1).UtcDateTime, null, user));
        foreach (var (key, value) in new[]
        {
            (GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled, "true"),
            (GovernanceSettingKeys.Cerbos.Mode, "\"custom_endpoint\""),
            (GovernanceSettingKeys.Cerbos.CustomEndpoint, "\"http://wrong-target-route.test:3593\"")
        })
            seed.Set<TenantSetting>().Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = target.Id,
                Tenant = target,
                SettingKey = key,
                Value = value
            });
        if (instanceAdmin)
        {
            int role = await seed.Roles.Where(value => value.MasterCode == "platform.admin"
                && value.RoleScopeId == (int)RoleScopeEnum.Platform).Select(value => value.Id).SingleAsync();
            seed.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = user,
                User = null!,
                RoleId = role,
                Role = null!,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
        }
        await seed.SaveChangesAsync();
        return new(conversation.Id, target.Id, parent.Id, parent.ConcurrencyStamp);
    }

    private static ExploreDbContext CreateTenantDatabase(LocalIdentityLifecycleHttpFixture fixture, Guid tenant)
    {
        var context = fixture.Native.CreateDatabase();
        context.TenantContext = new SelectedTenant(tenant);
        context.BindTenantLease();
        return context;
    }

    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
    private sealed record Rows(Guid Conversation, Guid Target, Guid Event, Guid Stamp);

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
            Context.Request.Path = "/ai-moderation-proposal";
            accessor = Services.GetRequiredService<IHttpContextAccessor>();
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
        }
        internal Task AuthenticateAsync(string token)
        {
            accessor.HttpContext = Context;
            Context.Request.Headers.Authorization = "Bearer " + token;
            return AuthenticateCoreAsync();
        }
        private async Task AuthenticateCoreAsync()
        {
            var result = await Context.AuthenticateAsync();
            await Assert.That(result.Succeeded).IsTrue();
            Context.User = result.Principal!;
        }
        public async ValueTask DisposeAsync()
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
        }
    }

    private sealed class Transport
    {
        internal ICerbosClient Client { get; } = Substitute.For<ICerbosClient>();
        internal ICerbosClientFactory Factory { get; } = Substitute.For<ICerbosClientFactory>();
        internal List<Cerbos.Api.V1.Request.CheckResourcesRequest> Requests { get; } = [];
        internal List<string> Endpoints { get; } = [];
        internal Transport(ExploreDbContext owner, bool deny = false, bool unavailable = false)
        {
            Factory.GetOrCreate(Arg.Any<string>()).Returns(call =>
            {
                Endpoints.Add(call.ArgAt<string>(0));
                return Client;
            });
            Client.CheckResourcesAsync(Arg.Any<Cerbos.Sdk.Builder.CheckResourcesRequest>(), Arg.Any<Metadata>())
                .Returns(call =>
                {
                    if (owner.Database.CurrentTransaction is not null)
                        throw new InvalidOperationException("Provider evaluation must precede source acquisition.");
                    var sent = call.ArgAt<Cerbos.Sdk.Builder.CheckResourcesRequest>(0).ToCheckResourcesRequest().Clone();
                    Requests.Add(sent);
                    if (unavailable) throw new RpcException(new Status(StatusCode.Unavailable, "External provider unavailable"));
                    var response = new Cerbos.Api.V1.Response.CheckResourcesResponse { RequestId = sent.RequestId };
                    var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                    {
                        Resource = new() { Kind = sent.Resources.Single().Resource.Kind, Id = sent.Resources.Single().Resource.Id }
                    };
                    result.Actions.Add(sent.Resources.Single().Actions.Single(), deny ? Effect.Deny : Effect.Allow);
                    response.Results.Add(result);
                    return new Cerbos.Sdk.Response.CheckResourcesResponse(response);
                });
        }
    }
}
