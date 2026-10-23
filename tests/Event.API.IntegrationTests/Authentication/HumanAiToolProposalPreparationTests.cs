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

/// <summary>Real credentials, native sources and committing owner; only the external PDP is substituted.</summary>
[NotInParallel]
public sealed class HumanAiToolProposalPreparationTests
{
    [Test]
    [Arguments("local", "event")]
    [Arguments("cerbos", "event")]
    [Arguments("local", "draft")]
    [Arguments("cerbos", "draft")]
    [Arguments("local", "template")]
    [Arguments("cerbos", "template")]
    public async Task OriginalProviderCommitsOnlyTheOrdinaryProposal(string provider, string kind)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var rows = await SeedAsync(fixture, tenantAdmin: kind == "template");
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var transport = new Transport(request.Services.GetRequiredService<ExploreDbContext>());
        var preparation = CreatePreparation(request.Services, fixture.Clock, provider, transport);
        var handler = ActivatorUtilities.CreateInstance<ProposeAiToolActionCommandHandler>(request.Services, preparation);
        var command = Command(rows, kind);
        var result = await handler.ExecuteAsync(command, CancellationToken.None);
        await Assert.That(result.IsSuccess).IsTrue();
        await using var observer = CreateSelectedDatabase(fixture);
        var proposal = await observer.AiProposedActions.SingleAsync(value => value.ConversationId == rows.Conversation);
        await Assert.That(proposal.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(proposal.CreatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(proposal.Status).IsEqualTo(AiProposedActionStatus.Proposed);
        await Assert.That(proposal.PayloadJson).IsEqualTo(command.PayloadJson);
        await Assert.That(await observer.AiToolExecutions.AnyAsync(value => value.ProposedActionId == proposal.Id)).IsFalse();
        await Assert.That((await observer.Events.SingleAsync(value => value.Id == rows.Event)).ConcurrencyStamp)
            .IsEqualTo(rows.Stamp);
        await Assert.That(request.Services.GetRequiredService<ExploreDbContext>().TenantFilterTenantId)
            .IsEqualTo(PlatformDefaults.DefaultTenantId);
        if (provider == "cerbos")
        {
            var sent = transport.Requests.Single();
            var resource = sent.Resources.Single().Resource;
            await Assert.That(resource.Scope).IsEqualTo(PlatformDefaults.DefaultTenantId.ToString("D"));
            await Assert.That(resource.Attr["tenantId"].StringValue).IsEqualTo(PlatformDefaults.DefaultTenantId.ToString("D"));
            await Assert.That(transport.Endpoints.Single()).IsEqualTo("http://ordinary-proposal.prepared.test:3593");
            if (kind == "event")
            {
                await Assert.That(resource.Id).IsEqualTo(rows.Event.ToString("D"));
                await Assert.That(resource.Attr.Keys.ToHashSet().SetEquals(["tenantId", "eventId"])).IsTrue();
                await Assert.That(sent.Principal.Attr["eventAssignments"].StructValue.Fields.ContainsKey(rows.Event.ToString("D")))
                    .IsTrue();
            }
            else
            {
                await Assert.That(resource.Id).IsEqualTo(PlatformDefaults.DefaultTenantId.ToString("D"));
                await Assert.That(resource.Attr.ContainsKey("eventId")).IsFalse();
                if (kind == "draft")
                    await Assert.That(resource.Attr["authorizationPhase"].StringValue).IsEqualTo("pre_create");
            }
        }
        else await Assert.That(transport.Requests.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("cerbos", true)]
    [Arguments("local", false)]
    public async Task ProviderDenialRetainsProtocolFailureWithoutPersisting(string provider, bool administrator)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var rows = await SeedAsync(fixture, assignment: false, instanceAdmin: administrator);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var transport = new Transport(request.Services.GetRequiredService<ExploreDbContext>(), deny: true);
        var preparation = CreatePreparation(request.Services, fixture.Clock, provider, transport);
        var handler = ActivatorUtilities.CreateInstance<ProposeAiToolActionCommandHandler>(request.Services, preparation);
        var result = await handler.ExecuteAsync(Command(rows, "event"), CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("tool_authorization_denied");
        await using var observer = CreateSelectedDatabase(fixture);
        await Assert.That(await observer.AiProposedActions.AnyAsync(value => value.ConversationId == rows.Conversation)).IsFalse();
        await Assert.That(transport.Requests.Count).IsEqualTo(provider == "cerbos" ? 1 : 0);
    }

    [Test]
    [Arguments("assignment_revoked")]
    [Arguments("target_deleted")]
    [Arguments("route_changed")]
    [Arguments("equal_request")]
    [Arguments("replaced_principal")]
    [Arguments("repeated_acquire")]
    [Arguments("credential_expired")]
    [Arguments("assignment_expired")]
    [Arguments("changed_status")]
    [Arguments("changed_payload")]
    [Arguments("changed_kind")]
    [Arguments("changed_actor")]
    public async Task ChangedSourcesOrRetainedGraphCannotCommit(string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var rows = await SeedAsync(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateAsync(await fixture.SignInAsync());
        var services = request.Services;
        var repository = services.GetRequiredService<IAiConversationRepository>();
        var candidate = (await repository.GetByIdWithDetailsAsync(rows.Conversation, CancellationToken.None))!;
        var command = Command(rows, "event");
        var transport = new Transport(services.GetRequiredService<ExploreDbContext>());
        var preparation = CreatePreparation(services, fixture.Clock, "cerbos", transport);
        var prepared = await preparation.PrepareAsync(command, candidate, CancellationToken.None);
        if (change is "assignment_revoked" or "target_deleted" or "route_changed")
        {
            await using var revoke = CreateSelectedDatabase(fixture);
            if (change == "assignment_revoked")
                await Assert.That(await revoke.EventRoleAssignments.Where(value => value.EventId == rows.Event)
                    .ExecuteDeleteAsync()).IsEqualTo(1);
            else if (change == "target_deleted")
                await Assert.That(await revoke.Events.Where(value => value.Id == rows.Event)
                    .ExecuteUpdateAsync(update => update.SetProperty(value => value.IsDeleted, true))).IsEqualTo(1);
            else
            {
                revoke.SystemSettings.Add(new()
                {
                    SettingKey = GovernanceSettingKeys.Cerbos.Mode,
                    Value = "\"custom_endpoint\""
                });
                await revoke.SaveChangesAsync();
                await Assert.That(await revoke.SystemSettings.CountAsync(
                    value => value.SettingKey == GovernanceSettingKeys.Cerbos.Mode)).IsEqualTo(1);
            }
        }
        if (change == "replaced_principal") request.Context.User = new ClaimsPrincipal(request.Context.User);
        var lifetime = (await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.Context.User, CancellationToken.None))!;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => services.GetRequiredService<ISerializableAttemptOwner>()
            .ExecuteAsync(async token =>
            {
                await prepared.AcquireAsync(change == "equal_request" ? command with { } : command, token);
                if (change == "repeated_acquire") await prepared.AcquireAsync(command, token);
                var conversation = (await repository.GetByIdForUpdateAsync(command.ConversationId, candidate.ActorId, token))!;
                var action = conversation.ProposeAction(AiProposedActionKind.UpdateEventDraft,
                    command.PayloadJson, null, fixture.Binding.LocalSubjectId, fixture.Clock.GetUtcNow().UtcDateTime);
                prepared.RetainProposal(conversation, action);
                await repository.Update(conversation);
                if (change == "changed_status") conversation.Status = AiConversationStatus.Archived;
                if (change == "changed_payload") action.PayloadJson = """{"eventId":"changed"}""";
                if (change == "changed_kind") action.Kind = AiProposedActionKind.DeleteEvent;
                if (change == "changed_actor") conversation.ActorId = Guid.CreateVersion7();
                if (change is "credential_expired" or "assignment_expired")
                    services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                    {
                        var expires = change == "credential_expired"
                            ? lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                            : rows.AssignmentExpires.AddTicks(1);
                        fixture.Clock.Advance(expires - fixture.Clock.GetUtcNow());
                        return Task.CompletedTask;
                    }, _ => { });
                return true;
            }));
        await using var observer = CreateSelectedDatabase(fixture);
        await Assert.That(await observer.AiProposedActions.AnyAsync(value => value.ConversationId == rows.Conversation)).IsFalse();
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
    }

    private static HumanAiToolProposalPreparation CreatePreparation(
        IServiceProvider services, TimeProvider clock, string provider, Transport transport)
    {
        var options = Options.Create(new CerbosSettings { GrpcEndpoint = "http://ordinary-proposal.prepared.test:3593", UsePolicyScope = true });
        var cerbos = ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(services, transport.Client, transport.Factory, options);
        return new(services.GetRequiredService<IHumanCommitScopePreparation>(),
            services.GetRequiredService<IHttpContextAccessor>(), services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            services.GetRequiredService<IMachinePrincipalAccessor>(), services.GetRequiredService<ITenantContext>(),
            services.GetRequiredService<IAiToolContractRegistry>(), services.GetRequiredService<IEventRepository>(),
            clock, cerbos, Options.Create(new AuthorizationProviderDeploymentOptions { Provider = provider }), options,
            () => services.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => services.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => services.GetRequiredService<IHumanEventAssignmentSourceFactory>(),
            () => services.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => services.GetRequiredService<IPolicySourceSetFence>(),
            () => services.GetRequiredService<IHumanActionCommitFinalizer>());
    }

    private static ProposeAiToolActionCommand Command(Rows rows, string kind) => new()
    {
        ConversationId = rows.Conversation,
        ToolName = kind switch { "draft" => "CreateEventDraft", "template" => "CreateEventTemplate", _ => "UpdateEventDraft" },
        PayloadJson = kind switch
        {
            "draft" => """
                {"title":"Proposed draft","participationConfiguration":{"participationHandlingModeId":1,"advanceRegistrationObligationId":1}}
                """,
            "template" => JsonSerializer.Serialize(new
            {
                expectedConcurrencyStamp = rows.Stamp,
                managementContextHasEdit = true,
                templateKey = "ordinary-proposal",
                displayName = "Proposed template"
            }),
            _ => JsonSerializer.Serialize(new
            {
                eventId = rows.Event,
                expectedConcurrencyStamp = rows.Stamp,
                expectedParticipationConfigurationConcurrencyStamp = rows.Stamp,
                title = "Proposed title",
                participationConfiguration = new { participationHandlingModeId = 1, advanceRegistrationObligationId = 1 }
            })
        }
    };

    private static async Task<Rows> SeedAsync(LocalIdentityLifecycleHttpFixture fixture,
        bool assignment = true, bool tenantAdmin = false, bool instanceAdmin = false)
    {
        await using var seed = CreateSelectedDatabase(fixture);
        Guid user = fixture.Binding.LocalSubjectId;
        Guid actor = await seed.Actors.Where(value => value.UserId == user).Select(value => value.Id).SingleAsync();
        var parent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            Title = "Ordinary proposal target",
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
        var expires = fixture.Clock.GetUtcNow().AddMinutes(1);
        seed.AddRange(parent, conversation);
        if (assignment)
            seed.Add(EventRoleAssignment.Create(parent.TenantId, parent.Id, user, (int)RoleEnum.EventManager,
                EventRoleAssignmentStatus.Active, fixture.Clock.GetUtcNow().AddDays(-1).UtcDateTime, expires.UtcDateTime, user));
        if (tenantAdmin)
        {
            var member = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = user,
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
        }
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
        return new(conversation.Id, parent.Id, parent.ConcurrencyStamp, expires);
    }

    private static ExploreDbContext CreateSelectedDatabase(LocalIdentityLifecycleHttpFixture fixture)
    {
        var context = fixture.Native.CreateDatabase();
        context.TenantContext = new ObservationTenant(PlatformDefaults.DefaultTenantId);
        context.BindTenantLease();
        return context;
    }

    private sealed record ObservationTenant(Guid TenantId) : ITenantContext;
    private sealed record Rows(Guid Conversation, Guid Event, Guid Stamp, DateTimeOffset AssignmentExpires);

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
            Context.Request.Path = "/ai-tool-proposal";
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
        internal Transport(ExploreDbContext owner, bool deny = false)
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
