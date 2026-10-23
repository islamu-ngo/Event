using System.Collections.Concurrent;
using System.Diagnostics;
using System.Data.Common;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Middleware;
using Explore.API.Telemetry;
using Explore.API.Configuration;
using Explore.API.Scheduling;
using Explore.API.Hateoas;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Constants;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Services.Registration;
using Explore.Application.Features.Events.Handlers.Commands;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.SupportAccess.Handlers.Commands;
using Explore.Application.Telemetry;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Infrastructure;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Ai;
using Explore.Domain.Ai;
using Explore.Persistence;
using FluentValidation.Results;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using NSubstitute;
using Serilog.Core;
using Serilog.Events;

namespace Event.Api.IntegrationTests.Features;

/// <summary>S30 and PC:SCN-OBS-001 exercise the registered API and its final sinks.</summary>
[NotInParallel]
public sealed class ApiDiagnosticSinkTests
{
    [Test]
    public async Task NativeRetentionFailureKeepsContentAndRecoversWithoutPrivateDiagnosticCopies()
    {
        string canary = Guid.NewGuid().ToString("N");
        var fault = new RetentionDiagnosticFault(canary);
        var logs = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(persistenceInterceptor: fault);
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILogEventSink>(logs);
                services.PostConfigure<AiRetentionCleanupSettings>(settings => settings.DryRun = false);
            }));
        using var client = hosted.CreateClient();
        Guid actorId;
        Guid conversationId;
        DateTime now = DateTime.UtcNow;
        await using (var scope = hosted.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            actorId = (await database.InstanceBootstrapStates.SingleAsync()).CompletedByUserId!.Value;
            var conversation = new AiConversation
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                UserId = actorId,
                Title = canary,
                CreatedAt = now.AddDays(-45),
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            conversation.AddMessage(AiMessageRole.User, canary, actorId, now.AddDays(-45));
            database.AiConversations.Add(conversation);
            await database.SaveChangesAsync();
            conversationId = conversation.Id;
            await database.AiConversations.Where(value => value.Id == conversationId).ExecuteUpdateAsync(setters =>
                setters.SetProperty(value => value.CreatedAt, now.AddDays(-45))
                    .SetProperty(value => value.UpdatedAt, (DateTime?)now.AddDays(-45)));
        }
        fault.TargetId = conversationId;
        var cleanup = hosted.Services.GetRequiredService<IAiRetentionCleanupService>();

        var failed = await cleanup.CleanupAllTenantsAsync(now);

        await Assert.That(fault.Fired).IsTrue();
        await Assert.That(failed.FailedTenantCount).IsEqualTo(1);
        await Assert.That(failed.RedactedConversations).IsEqualTo(0);
        await using (var verify = hosted.Services.CreateAsyncScope())
        {
            var database = verify.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await Assert.That((await database.AiConversations.AsNoTracking()
                .SingleAsync(value => value.Id == conversationId)).IsDeleted).IsFalse();
            await Assert.That((await database.AiMessages.AsNoTracking()
                .SingleAsync(value => value.ConversationId == conversationId)).Content).IsEqualTo(canary);
        }

        var recovered = await cleanup.CleanupAllTenantsAsync(now);

        await Assert.That(recovered.FailedTenantCount).IsEqualTo(0);
        await Assert.That(recovered.RedactedConversations).IsEqualTo(1);
        await using (var verify = hosted.Services.CreateAsyncScope())
        {
            var database = verify.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var conversation = await database.AiConversations.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(value => value.Id == conversationId);
            await Assert.That(conversation.IsDeleted).IsTrue();
            await Assert.That(conversation.Title).IsNotEqualTo(canary);
            await Assert.That((await database.AiMessages.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(value => value.ConversationId == conversationId)).Content).IsNotEqualTo(canary);
        }
        string exported = string.Join('\n', logs.Values);
        await Assert.That(exported).Contains(typeof(AiRetentionCleanupService).FullName!);
        await Assert.That(exported).DoesNotContain(canary);
        foreach (Guid id in new[] { actorId, conversationId, PlatformDefaults.DefaultTenantId })
        {
            await Assert.That(exported).DoesNotContain(id.ToString("D"));
            await Assert.That(exported).DoesNotContain(id.ToString("N"));
        }
    }

    [Test]
    [Arguments(SupportAccessModeEnum.ReadOnly, false)]
    [Arguments(SupportAccessModeEnum.ReadOnly, true)]
    [Arguments(SupportAccessModeEnum.Write, false)]
    [Arguments(SupportAccessModeEnum.Write, true)]
    public async Task NativeSupportLifecyclePreservesPrivateAuditDataWithoutDiagnosticCopies(
        SupportAccessModeEnum mode, bool forceStop)
    {
        string canary = Guid.NewGuid().ToString("N");
        var logs = new DiagnosticSink();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => services.AddSingleton<ILogEventSink>(logs));
        await using var seed = fixture.Native.CreateDatabase();
        Guid actorId = fixture.Binding.LocalSubjectId;
        seed.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = actorId,
            User = null!,
            RoleId = (int)RoleEnum.Admin,
            Role = null!,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        foreach (string key in new[]
        {
            GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode
        })
        {
            var setting = await seed.SystemSettings.SingleOrDefaultAsync(value => value.SettingKey == key);
            if (setting is null)
                seed.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = key,
                    Value = "true",
                    ValueType = SettingValueType.Boolean,
                    IsLocked = true,
                    Category = "SupportAccess",
                    CreatedAt = DateTime.UtcNow
                });
            else
                setting.Value = "true";
        }
        await seed.SaveChangesAsync();
        await using var scope = fixture.Host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>()
            .BindExecutionTenant(PlatformDefaults.DefaultTenantId);
        string bearer = await fixture.SignInAsync();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        try
        {
            accessor.HttpContext.Request.Headers.Authorization = "Bearer " + bearer;
            var authentication = await accessor.HttpContext.AuthenticateAsync();
            await Assert.That(authentication.Succeeded).IsTrue();
            accessor.HttpContext.User = authentication.Principal!;
            await scope.ServiceProvider.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
            var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var started = await ActivatorUtilities.CreateInstance<StartSupportAccessSessionCommandHandler>(scope.ServiceProvider)
                .ExecuteAsync(new()
                {
                    TargetTenantId = PlatformDefaults.DefaultTenantId,
                    Mode = mode,
                    DurationMinutes = 5,
                    ReasonCode = $"reason-{canary}",
                    ReasonText = $"private reason {canary}",
                    TicketReference = $"ticket-{canary}"
                }, default);
            await Assert.That(started.IsSuccess).IsTrue();
            var stopped = forceStop
                ? await ActivatorUtilities.CreateInstance<ForceStopSupportAccessSessionCommandHandler>(scope.ServiceProvider)
                    .ExecuteAsync(new() { SessionId = started.Id, EndReasonText = $"private ending {canary}" }, default)
                : await ActivatorUtilities.CreateInstance<StopSupportAccessSessionCommandHandler>(scope.ServiceProvider)
                    .ExecuteAsync(new() { SessionId = started.Id, EndReasonText = $"private ending {canary}" }, default);
            await Assert.That(stopped.IsSuccess).IsTrue();
            database.ChangeTracker.Clear();
            var session = await database.Set<SupportAccessSession>().AsNoTracking().SingleAsync(value => value.Id == started.Id);
            await Assert.That(session.ActorUserId).IsEqualTo((Guid?)actorId);
            await Assert.That(session.TargetTenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
            await Assert.That(session.ModeId).IsEqualTo((int)mode);
            await Assert.That(session.StatusId).IsEqualTo(forceStop
                ? (int)SupportAccessSessionStatusEnum.Revoked : (int)SupportAccessSessionStatusEnum.Stopped);
            await Assert.That(session.ReasonCode).IsEqualTo($"reason-{canary}");
            await Assert.That(session.ReasonText).IsEqualTo($"private reason {canary}");
            await Assert.That(session.TicketReference).IsEqualTo($"ticket-{canary}");
            await Assert.That(session.EndReasonText).IsEqualTo($"private ending {canary}");
            await Assert.That(session.EndedAtUtc).IsNotNull();
            var audits = await database.Set<SupportAccessAuditEvent>().AsNoTracking()
                .Where(value => value.SupportAccessSessionId == started.Id).ToListAsync();
            await Assert.That(audits).Count().IsEqualTo(2);
            await Assert.That(audits.Select(value => value.EventTypeId)).IsEquivalentTo(
                [(int)SupportAccessAuditEventTypeEnum.Started, forceStop
                    ? (int)SupportAccessAuditEventTypeEnum.Revoked : (int)SupportAccessAuditEventTypeEnum.Stopped]);
            foreach (var audit in audits)
            {
                await Assert.That(audit.ActorUserId).IsEqualTo((Guid?)actorId);
                await Assert.That(audit.TargetTenantId).IsEqualTo(session.TargetTenantId);
            }
            string exported = string.Join('\n', logs.Values);
            await Assert.That(exported).Contains(typeof(StartSupportAccessSessionCommandHandler).FullName!);
            await Assert.That(exported).Contains(forceStop
                ? typeof(ForceStopSupportAccessSessionCommandHandler).FullName!
                : typeof(StopSupportAccessSessionCommandHandler).FullName!);
            await Assert.That(exported).DoesNotContain(canary);
            foreach (Guid id in new[] { actorId, session.TargetTenantId, session.Id }.Concat(audits.Select(value => value.Id)))
            {
                await Assert.That(exported).DoesNotContain(id.ToString("D"));
                await Assert.That(exported).DoesNotContain(id.ToString("N"));
            }
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Test]
    public async Task NativeHalRemoteBatchFailureDeniesPermissionLinksWithoutPrivateTransportCopies()
    {
        string canary = Guid.NewGuid().ToString("N");
        bool reached = false;
        var transport = Substitute.For<ICerbosClient>();
        transport.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(_ =>
            {
                reached = true;
                return Task.FromException<Cerbos.Sdk.Response.CheckResourcesResponse>(
                    new RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.Unavailable, canary + "\r\nprivate-provider-detail")));
            });
        var logs = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICerbosClient>();
                services.AddSingleton(transport);
                services.Configure<AuthorizationProviderDeploymentOptions>(options => options.Provider = "cerbos");
                services.AddSingleton<ILogEventSink>(logs);
            }));
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid owner = (await database.InstanceBootstrapStates.SingleAsync()).CompletedByUserId!.Value;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, owner.ToString("D"))], "native-test"));
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = principal };
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = context;
        try
        {
            var evaluator = ActivatorUtilities.CreateInstance<HateoasAuthorizationEvaluator>(scope.ServiceProvider);
            LinkDefinition[] links =
            [
                LinkDefinition.Self("native-self"),
                LinkDefinition.Action("view-settings", "native-settings", "GET")
                    .WithPermission(ResourceKinds.InstanceSetting, AuthorizationActions.View, "instance"),
                LinkDefinition.Action("duplicate-view", "native-settings", "GET")
                    .WithPermission(ResourceKinds.InstanceSetting, AuthorizationActions.View, "instance"),
                LinkDefinition.Action("update-settings", "native-settings", "PATCH")
                    .WithPermission(ResourceKinds.InstanceSetting, AuthorizationActions.Update, "instance")
            ];

            var allowed = await evaluator.AreLinksAllowedAsync(links, principal, context);

            await Assert.That(reached).IsTrue();
            await Assert.That(allowed).Count().IsEqualTo(4);
            await Assert.That(allowed[0]).IsTrue();
            await Assert.That(allowed[1]).IsFalse();
            await Assert.That(allowed[2]).IsFalse();
            await Assert.That(allowed[3]).IsFalse();
            await Assert.That(logs.Values.Any(value =>
                value.Contains(typeof(RuntimeAuthorizationProvider).FullName!, StringComparison.Ordinal))).IsTrue();
            string exported = string.Join('\n', logs.Values);
            await Assert.That(exported).DoesNotContain(canary);
            await Assert.That(exported).DoesNotContain("private-provider-detail");
            await Assert.That(exported).DoesNotContain(owner.ToString("D"));
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Test]
    public async Task NativeHalLookupFailureDeniesPermissionLinksWithoutPrivateExceptionCopies()
    {
        Guid eventId = Guid.NewGuid();
        string canary = Guid.NewGuid().ToString("N");
        var fault = new HalLookupDiagnosticFault(eventId, canary);
        var logs = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(persistenceInterceptor: fault);
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(logs)));
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        var evaluator = ActivatorUtilities.CreateInstance<HateoasAuthorizationEvaluator>(scope.ServiceProvider);
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))], "native-test"));
        LinkDefinition[] links =
        [
            LinkDefinition.Self("native-self"),
            LinkDefinition.Action("manage-team", "native-team", "PATCH", new { id = eventId })
                .WithPermission(ResourceKinds.Event, AuthorizationActions.Events.ManageTeam, eventId.ToString("D"))
        ];
        fault.Armed = true;

        var allowed = await evaluator.AreLinksAllowedAsync(links, principal, context);

        await Assert.That(fault.Fired).IsTrue();
        await Assert.That(allowed).Count().IsEqualTo(2);
        await Assert.That(allowed[0]).IsTrue();
        await Assert.That(allowed[1]).IsFalse();
        await Assert.That(logs.Values.Any(value =>
            value.Contains(typeof(HateoasAuthorizationEvaluator).FullName!, StringComparison.Ordinal))).IsTrue();
        string exported = string.Join('\n', logs.Values);
        await Assert.That(exported).DoesNotContain(canary);
        await Assert.That(exported).DoesNotContain(eventId.ToString("D"));
        await Assert.That(exported).DoesNotContain(eventId.ToString("N"));
    }

    [Test]
    public async Task NativeHalGenerationPreservesValidLinkAndOmitsPrivateFailedRouteValues()
    {
        Guid sourceId = Guid.NewGuid();
        string canary = Guid.NewGuid().ToString("N");
        var logs = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(logs)));
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        var generator = ActivatorUtilities.CreateInstance<HateoasLinkGenerator>(scope.ServiceProvider);
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        string routeName = typeof(Explore.API.Controllers.EventController)
            .GetMethod(nameof(Explore.API.Controllers.EventController.GetAtprotoEventSource))!
            .GetCustomAttribute<HttpGetAttribute>()!.Name!;

        var valid = generator.GenerateLink(
            LinkDefinition.Self(routeName, new { atprotoRecordId = sourceId }), context);
        var missing = generator.GenerateLink(
            LinkDefinition.Self($"missing-{canary}", new { id = sourceId, value = canary }), context);

        await Assert.That(valid).IsNotNull();
        await Assert.That(valid!.Href).Contains(sourceId.ToString());
        await Assert.That(missing).IsNull();
        await Assert.That(logs.Values.Any(value =>
            value.Contains(typeof(HateoasLinkGenerator).FullName!, StringComparison.Ordinal))).IsTrue();
        string exported = string.Join(' ', logs.Values);
        await Assert.That(exported).DoesNotContain(sourceId.ToString());
        await Assert.That(exported).DoesNotContain(canary);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SchedulerAuditDiagnosticsDoNotCopyPrivateRecordIdentity(bool succeeded)
    {
        string canary = Guid.NewGuid().ToString("N");
        var logs = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(logs)));
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        var sink = ActivatorUtilities.CreateInstance<LoggingSchedulerAdminAuditSink>(scope.ServiceProvider);
        var record = new SchedulerAdminAuditRecord(
            SchedulerAdminAuditActions.TriggerJob, $"operator-{canary}", $"group-{canary}",
            $"job-{canary}", succeeded, succeeded ? null : "forbidden", $"correlation-{canary}", DateTime.UtcNow);

        await sink.RecordAsync(record, default);

        await Assert.That(logs.Values.Any(value =>
            value.Contains(typeof(LoggingSchedulerAdminAuditSink).FullName!, StringComparison.Ordinal))).IsTrue();
        await Assert.That(string.Join(' ', logs.Values)).DoesNotContain(canary);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task NativeMissingModerationTargetPreservesRefusalWithoutFinalPrivateCopies(int action)
    {
        Guid eventId = Guid.NewGuid();
        var logs = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(logs)));
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid userId = (await database.InstanceBootstrapStates.SingleAsync()).CompletedByUserId!.Value;
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "native-test"))
        };
        try
        {
            await Assert.That(scope.ServiceProvider.GetRequiredService<ICurrentUserService>().UserId)
                .IsEqualTo(userId);
            var response = action switch
            {
                0 => await ActivatorUtilities.CreateInstance<ModerateEventCommandHandler>(scope.ServiceProvider)
                    .ExecuteAsync(new ModerateEventCommand { Id = eventId }, default),
                1 => await ActivatorUtilities.CreateInstance<HeavyRedactEventCommandHandler>(scope.ServiceProvider)
                    .ExecuteAsync(new HeavyRedactEventCommand { Id = eventId }, default),
                2 => await ActivatorUtilities.CreateInstance<UnmoderateEventCommandHandler>(scope.ServiceProvider)
                    .ExecuteAsync(new UnmoderateEventCommand { Id = eventId }, default),
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };
            await Assert.That(response.IsSuccess).IsFalse();
            await Assert.That(response.Id).IsEqualTo(eventId);
            await Assert.That(await database.Set<EventModerationRecord>()
                .AnyAsync(record => record.EventId == eventId)).IsFalse();
            string producer = action switch
            {
                0 => typeof(ModerateEventCommandHandler).FullName!,
                1 => typeof(HeavyRedactEventCommandHandler).FullName!,
                2 => typeof(UnmoderateEventCommandHandler).FullName!,
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };
            await Assert.That(logs.Values.Any(value => value.Contains(producer, StringComparison.Ordinal))).IsTrue();
            string exported = string.Join(' ', logs.Values);
            await Assert.That(exported).DoesNotContain(eventId.ToString());
            await Assert.That(exported).DoesNotContain(userId.ToString());
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task NativeUnresolvedModeratorRejectsWithoutFinalEventIdentityCopies(int action)
    {
        Guid eventId = Guid.NewGuid();
        var logs = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(logs)));
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = null;

        var response = action switch
        {
            0 => await ActivatorUtilities.CreateInstance<ModerateEventCommandHandler>(scope.ServiceProvider)
                .ExecuteAsync(new ModerateEventCommand { Id = eventId }, default),
            1 => await ActivatorUtilities.CreateInstance<HeavyRedactEventCommandHandler>(scope.ServiceProvider)
                .ExecuteAsync(new HeavyRedactEventCommand { Id = eventId }, default),
            2 => await ActivatorUtilities.CreateInstance<UnmoderateEventCommandHandler>(scope.ServiceProvider)
                .ExecuteAsync(new UnmoderateEventCommand { Id = eventId }, default),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };

        await Assert.That(response.IsSuccess).IsFalse();
        string producer = action switch
        {
            0 => typeof(ModerateEventCommandHandler).FullName!,
            1 => typeof(HeavyRedactEventCommandHandler).FullName!,
            2 => typeof(UnmoderateEventCommandHandler).FullName!,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        await Assert.That(logs.Values.Any(value => value.Contains(producer, StringComparison.Ordinal))).IsTrue();
        await Assert.That(string.Join(' ', logs.Values)).DoesNotContain(eventId.ToString());
    }

    [Test]
    public async Task NativeCommittedOrderAuditKeepsPayloadWithoutFinalMessageIdentityCopies()
    {
        string canary = Guid.NewGuid().ToString("N");
        Guid messageId = Guid.NewGuid();
        Guid aggregateId = Guid.NewGuid();
        var logs = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(logs)));
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        var dispatcher = ActivatorUtilities.CreateInstance<CompositeOutboxMessageDispatcher>(scope.ServiceProvider);
        string payload = JsonSerializer.Serialize(new { content = canary });
        var message = new OutboxMessage
        {
            Id = messageId,
            AggregateId = aggregateId,
            AggregateType = "RegistrationOrder",
            EventType = RegistrationOrderOutboxMessageFactory.ConfirmedEventType,
            Payload = payload
        };

        await dispatcher.DispatchAsync(message, default);

        await Assert.That(message.Payload).IsEqualTo(payload);
        await Assert.That(message.AggregateId).IsEqualTo(aggregateId);
        await Assert.That(logs.Values.Any(value =>
            value.Contains(typeof(CompositeOutboxMessageDispatcher).FullName!, StringComparison.Ordinal))).IsTrue();
        string exported = string.Join(' ', logs.Values);
        await Assert.That(exported).DoesNotContain(messageId.ToString());
        await Assert.That(exported).DoesNotContain(aggregateId.ToString());
        await Assert.That(exported).DoesNotContain(canary);
    }

    [Test]
    public async Task NativeMissingDispatchPointerRejectsWithoutFinalTenantOrPublishIdentityCopies()
    {
        Guid tenant = Guid.NewGuid();
        Guid publish = Guid.NewGuid();
        string consumer = Guid.NewGuid().ToString("N");
        var logs = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(logs)));
        using var client = hosted.CreateClient();
        using var drain = ActivatorUtilities.CreateInstance<EmailDispatchDrainService>(hosted.Services);

        var result = await drain.ProcessSingleAsync(tenant, publish, consumer, default);

        await Assert.That(result.Outcome).IsEqualTo(EmailDispatchDrainOutcome.Missing);
        await Assert.That(logs.Values.Any(value =>
            value.Contains(typeof(EmailDispatchDrainService).FullName!, StringComparison.Ordinal))).IsTrue();
        string exported = string.Join(' ', logs.Values);
        await Assert.That(exported).DoesNotContain(tenant.ToString());
        await Assert.That(exported).DoesNotContain(publish.ToString());
        await Assert.That(exported).DoesNotContain(consumer);
    }

    [Test]
    public async Task NamedDependencyFailurePreservesRemoteResponseWithoutExportingPrivateUrl()
    {
        string canary = Guid.NewGuid().ToString("N");
        var logs = new DiagnosticSink();
        var remote = new PrivateDependencyReply(canary);
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Serilog:MinimumLevel:Default"] = "Debug",
                    ["Serilog:MinimumLevel:Override:System.Net.Http"] = "Debug"
                }));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILogEventSink>(logs);
                services.AddHttpClient("privacy-dependency")
                    .ConfigurePrimaryHttpMessageHandler(() => remote);
            });
        });
        using var client = hosted.CreateClient();
        using var dependency = hosted.Services.GetRequiredService<IHttpClientFactory>().CreateClient("privacy-dependency");

        using var response = await dependency.GetAsync($"https://dependency.example.test/{canary}?token={canary}");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo(canary);
        await Assert.That(remote.RequestCount).IsEqualTo(1);
        await Assert.That(string.Join(' ', logs.Values)).DoesNotContain(canary);
    }

    [Test]
    public async Task NativeAuditFinalSinkDropsInheritedPrivateScopesWithoutLosingProducerBoundScope()
    {
        string canary = Guid.NewGuid().ToString("N");
        var logs = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(logs)));
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid actor = (await database.InstanceBootstrapStates.SingleAsync()).CompletedByUserId!.Value;
        var service = ActivatorUtilities.CreateInstance<ConfigurationChangeLogService>(scope.ServiceProvider);

        using (Serilog.Context.LogContext.PushProperty("UntrustedScope", canary))
        using (Serilog.Context.LogContext.PushProperty("Scope", canary))
            await service.LogChangeAsync(actor, "private." + canary, canary, canary,
                ConfigurationScopeEnum.Instance, actionType: canary);

        var records = await scope.ServiceProvider.GetRequiredService<IConfigurationChangeLogRepository>()
            .GetBySettingKey("private." + canary);
        await Assert.That(records.Count).IsEqualTo(1);
        await Assert.That(records[0].NewValue).IsEqualTo(canary);
        await Assert.That(records[0].ActionType).IsEqualTo(canary);
        await Assert.That(logs.Values.Any(value =>
            value.Contains(typeof(ConfigurationChangeLogService).FullName!, StringComparison.Ordinal))).IsTrue();
        await Assert.That(logs.Values.Any(value => value.StartsWith("Scope=", StringComparison.Ordinal)
            && value.Contains(nameof(ConfigurationScopeEnum.Instance), StringComparison.Ordinal))).IsTrue();
        await Assert.That(string.Join(' ', logs.Values)).DoesNotContain(canary);
    }

    [Test]
    public async Task NativeAuthorizationProviderSelectionOmitsPrivateSettingValueFromFinalLogs()
    {
        string canary = Guid.NewGuid().ToString("N");
        var logs = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Serilog:MinimumLevel:Default"] = "Debug"
                }));
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(logs));
        });
        using var client = hosted.CreateClient();
        await using var scope = hosted.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISystemSettingRepository>().UpsertAsync(new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = GovernanceSettingKeys.Security.AuthorizationProvider,
            Value = JsonSerializer.Serialize(canary),
            ValueType = SettingValueType.String,
            CreatedAt = DateTime.UtcNow
        });
        var provider = ActivatorUtilities.CreateInstance<RuntimeAuthorizationProvider>(
            scope.ServiceProvider, Options.Create(new AuthorizationProviderDeploymentOptions()),
            scope.ServiceProvider.GetRequiredService<ILogger<RuntimeAuthorizationProvider>>());

        var result = await provider.AuthorizeAsync(new AuthorizationRequest(
            ResourceKinds.InstanceSetting, "instance", AuthorizationActions.View));

        await Assert.That(result.IsAllowed).IsFalse();
        await Assert.That(logs.Values.Any(value =>
            value.Contains(typeof(RuntimeAuthorizationProvider).FullName!, StringComparison.Ordinal))).IsTrue();
        await Assert.That(string.Join(' ', logs.Values)).DoesNotContain(canary);
    }

    [Test]
    public async Task SetupPolicyPackageUnavailableRetains503WithoutPrivateExceptionOrPathExports()
    {
        string canary = Guid.NewGuid().ToString("N");
        var logs = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILogEventSink>(logs);
                services.Configure<CerbosPolicyPackageOptions>(options =>
                    options.PoliciesPath = Path.Combine(Path.GetTempPath(), canary, "absent-policy-package"));
            }));
        using var client = hosted.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/api/InstanceOnboarding/authz-provider-configuration/package");
        request.Headers.Add("X-Setup-Secret", factory.SetupSecret);
        Task completed = logs.RequestCompleted;

        using var response = await client.SendAsync(request);
        await completed.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(await response.Content.ReadAsStringAsync()).DoesNotContain(canary);
        await Assert.That(logs.Values.Any(value =>
            value.Contains(typeof(Explore.API.Controllers.InstanceOnboardingController).FullName!,
                StringComparison.Ordinal))).IsTrue();
        await Assert.That(string.Join(' ', logs.Values)).DoesNotContain(canary);
        await Assert.That(string.Join(' ', logs.Values)).DoesNotContain(nameof(PolicyPackageUnavailableException));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PostAuthenticationTenancyLogsExcludeKeyTenantAndPathContent(bool instanceAdmin)
    {
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Guid tenantId = Guid.CreateVersion7();
        Guid requestedTenantId = Guid.CreateVersion7();
        var logs = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(logs)));
        using var client = hosted.CreateClient();
        using var scope = hosted.Services.CreateScope();
        var context = ApiKeyContext(scope.ServiceProvider, canary, tenantId, instanceAdmin);
        if (!instanceAdmin)
            context.Items[ApiTenantResolutionMiddleware.RequestedTenantIdItemKey] = requestedTenantId;
        using var body = new MemoryStream();
        context.Response.Body = body;
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<ApiTenantPostAuthenticationMiddleware>>();
        bool dispatched = false;
        var middleware = new ApiTenantPostAuthenticationMiddleware(_ =>
        {
            dispatched = true;
            return Task.CompletedTask;
        }, logger);
        Task signal = logs.TenancyOutcome;
        using (Serilog.Context.LogContext.PushProperty("UntrustedScope", canary))
            await middleware.InvokeAsync(context,
                scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>(),
                scope.ServiceProvider.GetRequiredService<IProblemDetailsService>(),
                scope.ServiceProvider.GetRequiredService<BusinessMetrics>(),
                Options.Create(new McpAdapterSettings()));
        await signal.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(dispatched).IsFalse();
        await Assert.That(context.Response.StatusCode).IsEqualTo(404);
        body.Position = 0;
        using var reader = new StreamReader(body);
        string emitted = string.Join('\n', logs.Values.Append(await reader.ReadToEndAsync()));
        await Assert.That(emitted).DoesNotContain(canary);
        await Assert.That(emitted).DoesNotContain(tenantId.ToString("D"));
        await Assert.That(emitted).DoesNotContain(requestedTenantId.ToString("D"));
        await Assert.That(emitted).Contains(instanceAdmin ? "tenant_required" : "tenant_mismatch");
    }

    [Test]
    public async Task RegisteredApiKeyRejectionKeepsStatusPolicyAndBudgetWithoutIdentities()
    {
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        Guid tenantId = Guid.CreateVersion7();
        var logs = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(enableRateLimiting: true);
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(logs)));
        using var client = hosted.CreateClient();
        using var scope = hosted.Services.CreateScope();
        var context = ApiKeyContext(scope.ServiceProvider, canary, tenantId, instanceAdmin: false);
        using var body = new MemoryStream();
        context.Response.Body = body;
        var rejection = scope.ServiceProvider.GetRequiredService<IOptions<RateLimiterOptions>>().Value.OnRejected
            ?? throw new InvalidOperationException("Native rate rejection callback is unavailable.");
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
        using RateLimitLease held = limiter.AttemptAcquire();
        using RateLimitLease rejected = limiter.AttemptAcquire();
        await Assert.That(held.IsAcquired).IsTrue();
        await Assert.That(rejected.IsAcquired).IsFalse();
        Task signal = logs.ThrottleOutcome;
        using (Serilog.Context.LogContext.PushProperty("UntrustedScope", canary))
            await rejection(new OnRejectedContext { HttpContext = context, Lease = rejected }, CancellationToken.None);
        await signal.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(context.Response.StatusCode).IsEqualTo(429);
        await Assert.That(context.Response.Headers["X-RateLimit-Remaining"].ToString()).IsEqualTo("0");
        body.Position = 0;
        using var reader = new StreamReader(body);
        string emitted = string.Join('\n', logs.Values.Append(await reader.ReadToEndAsync()));
        await Assert.That(emitted).DoesNotContain(canary);
        await Assert.That(emitted).DoesNotContain(tenantId.ToString("D"));
        await Assert.That(emitted).Contains("rate_limited");
        await Assert.That(emitted).Contains("Authenticated");
    }

    private static DefaultHttpContext ApiKeyContext(IServiceProvider services, string canary, Guid tenantId, bool instanceAdmin)
    {
        var claims = new List<Claim>
        {
            new(ApiAuthenticationClaimTypes.AuthMethod, "api_key"),
            new(ApiAuthenticationClaimTypes.ApiKeyId, canary),
            new(ApiAuthenticationClaimTypes.OwnerId, Guid.CreateVersion7().ToString("D")),
            new(ApiAuthenticationClaimTypes.OwnerType, instanceAdmin ? "InstanceAdmin" : "Tenant")
        };
        if (!instanceAdmin)
            claims.Add(new Claim(ApiAuthenticationClaimTypes.TenantId, tenantId.ToString("D")));
        var context = new DefaultHttpContext { RequestServices = services };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, ApiAuthenticationSchemeNames.ApiKey));
        context.Request.Method = "GET";
        context.Request.Path = $"/api/private/{canary}";
        context.Request.Headers[ApiAuthenticationHeaderNames.ApiKey] = canary;
        context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask,
            RoutePatternFactory.Parse("api/private/{id}"), 0, EndpointMetadataCollection.Empty, "diagnostic boundary"));
        return context;
    }

    [Test]
    public async Task NativeAiTraceExportKeepsCountsWithoutInheritedPrivateContext()
    {
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var exporter = new DiagnosticExporter(ActivityKind.Client);
        using var provider = Sdk.CreateTracerProviderBuilder().AddSource(AiProviderTelemetry.ActivitySourceName)
            .AddProcessor(new ApiDiagnosticTraceProcessor())
            .AddProcessor(new SimpleActivityExportProcessor(exporter)).Build();
        using var parent = new Activity("request-parent").Start();
        parent.SetBaggage("private", canary);
        parent.TraceStateString = $"private={canary}";
        var request = new AiChatPayload("model",
            [new AiChatMessage(AiMessageRole.User, canary)], canary,
            new AiChatOptions(100, 20, 0, 10, false, false));
        Task signal = exporter.RequestCompleted;
        using (Activity? activity = AiProviderTelemetry.StartRequest(AiProviderDefaults.ProviderOpenAi, request))
        {
            if (activity is null) throw new InvalidOperationException("AI activity listener was not registered.");
            await Assert.That(activity.GetBaggageItem("private")).IsEqualTo(canary);
            AiProviderTelemetry.MarkSuccess(activity,
                new AiChatResponse(canary, [], new AiTokenUsage(5, 3, 8), canary, "stop"));
        }
        await signal.WaitAsync(TimeSpan.FromSeconds(10));

        string exported = string.Join('\n', exporter.Values);
        await Assert.That(exported).DoesNotContain(canary);
        await Assert.That(exported).Contains("ai.provider=openai");
        await Assert.That(exported).Contains("ai.message.count=1");
        await Assert.That(exported).Contains("ai.tokens.total=8");
        await Assert.That(request.Messages.Single().Content).IsEqualTo(canary);
        await Assert.That(request.SystemPrompt).IsEqualTo(canary);
    }

    [Test]
    public async Task NativeHttpDependencyExportExcludesUrlsBaggageAndFailureContent()
    {
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var exporter = new DiagnosticExporter(ActivityKind.Client);
        using var provider = Sdk.CreateTracerProviderBuilder().AddSource("System.Net.Http")
            .AddProcessor(new ApiDiagnosticTraceProcessor())
            .AddProcessor(new SimpleActivityExportProcessor(exporter)).Build();
        using var source = new ActivitySource("System.Net.Http");
        Task signal = exporter.RequestCompleted;
        using (Activity? activity = source.StartActivity($"GET https://private.example.test/{canary}", ActivityKind.Client))
        {
            if (activity is null) throw new InvalidOperationException("HTTP activity listener was not registered.");
            activity.SetTag("http.request.method", "GET");
            activity.SetTag("http.response.status_code", 503);
            activity.SetTag("url.full", $"https://private.example.test/?token={canary}");
            activity.SetTag("exception.message", $"{canary}\r\nforged-event");
            activity.SetBaggage("private", canary);
            activity.TraceStateString = $"private={canary}";
            activity.SetStatus(ActivityStatusCode.Error, canary);
        }
        await signal.WaitAsync(TimeSpan.FromSeconds(10));
        string exported = string.Join('\n', exporter.Values);
        await Assert.That(exported).DoesNotContain(canary);
        await Assert.That(exported).DoesNotContain("private.example.test");
        await Assert.That(exported).Contains("http.response.status_code=503");
        await Assert.That(exported).Contains("http-dependency");
    }

    [Test]
    [Arguments("unexpected", "Testing", 500, "unexpected_error")]
    [Arguments("unexpected", "Development", 500, "unexpected_error")]
    [Arguments("bad_request", "Testing", 400, "validation_failed")]
    [Arguments("not_found", "Testing", 404, "resource_not_found")]
    [Arguments("validation", "Testing", 400, "validation_failed")]
    [Arguments("fluent_validation", "Testing", 400, "validation_failed")]
    [Arguments("concurrency", "Testing", 409, "concurrent_update")]
    [Arguments("quota", "Testing", 422, "quota_exceeded")]
    public async Task ExceptionResponsesAndConfiguredSinksExcludeGeneratedContent(
        string kind, string environment, int expectedStatus, string expectedCode)
    {
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string content = $"{canary}@example.test https://private.example.test/?token={canary}\r\nforged-event-{canary}";
        Exception failure = kind switch
        {
            "bad_request" => new BadRequestException(content),
            "not_found" => new NotFoundException(content, canary),
            "validation" => new Explore.Application.Exceptions.ValidationException(
                new ValidationResult([new ValidationFailure(canary, content)])),
            "fluent_validation" => new FluentValidation.ValidationException(
                [new ValidationFailure(canary, content)]),
            "concurrency" => new ConcurrencyConflictException(
                ConcurrencyConflictException.ConcurrentUpdate, content, canary, canary,
                new InvalidOperationException(content)),
            "quota" => new QuotaExceededException(content, canary, 10, 10, 11, canary),
            _ => new InvalidOperationException(content, new Exception(content))
        };
        var logs = new DiagnosticSink();
        var otel = new DiagnosticLogExporter();
        var spans = new DiagnosticExporter();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        using var hosted = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.ConfigureLogging(logging => logging.AddOpenTelemetry(options =>
                options.AddProcessor(new SimpleLogRecordExportProcessor(otel))));
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton(new DiagnosticProbeFailure(failure));
                services.AddSingleton<IStartupFilter>(new DiagnosticScopeFilter(content));
                services.AddControllers().ConfigureApplicationPartManager(manager =>
                    manager.FeatureProviders.Add(new DiagnosticProbeFeatureProvider()));
                services.AddSingleton<ILogEventSink>(logs);
                services.AddOpenTelemetry().WithTracing(tracing =>
                    tracing.AddProcessor(new SimpleActivityExportProcessor(spans)));
            });
        });
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        Task logSignal = logs.RequestCompleted;
        Task spanSignal = spans.RequestCompleted;
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/diagnostic-sink-probe/{canary}?token={canary}");
        request.Headers.Add("X-Correlation-ID", canary);
        request.Headers.Add("X-Request-ID", canary);
        request.Headers.Add("baggage", $"private={canary}");
        using var response = await client.SendAsync(request);
        await Task.WhenAll(logSignal, spanSignal, otel.RequestCompleted).WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That((int)response.StatusCode).IsEqualTo(expectedStatus);
        string body = await response.Content.ReadAsStringAsync();
        string correlation = response.Headers.GetValues("X-Correlation-ID").Single();
        using var document = JsonDocument.Parse(body);
        await Assert.That(document.RootElement.GetProperty("code").GetString()).IsEqualTo(expectedCode);
        await Assert.That(document.RootElement.GetProperty("status").GetInt32()).IsEqualTo(expectedStatus);
        await Assert.That(document.RootElement.GetProperty("correlationId").GetString()).IsEqualTo(correlation);
        await Assert.That(document.RootElement.GetProperty("traceId").GetString()).IsNotEmpty();
        if (kind is "validation" or "fluent_validation")
            await Assert.That(document.RootElement.GetProperty("errors").ValueKind).IsEqualTo(JsonValueKind.Object);
        if (kind == "quota")
        {
            await Assert.That(document.RootElement.GetProperty("limit").GetInt32()).IsEqualTo(10);
            await Assert.That(document.RootElement.GetProperty("attempted").GetInt32()).IsEqualTo(11);
        }
        string exported = string.Join('\n', logs.Values.Concat(spans.Values).Concat(otel.Values).Append(body).Append(correlation));
        await Assert.That(exported).DoesNotContain(canary);
        await Assert.That(exported).DoesNotContain("private.example.test");
        await Assert.That(exported).DoesNotContain("forged-event-");
        await Assert.That(logs.Values.Any(value => value.Contains(expectedCode, StringComparison.Ordinal))).IsTrue();
        await Assert.That(spans.Values.Any(value => value.Contains("diagnostic-sink-probe", StringComparison.Ordinal))).IsTrue();
        await Assert.That(logs.RequestEventCount).IsEqualTo(1);
    }

    [Test]
    public async Task JwtFailureRetainsChallengeWithoutProviderOrQueryContent()
    {
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var logs = new DiagnosticSink();
        var spans = new DiagnosticExporter();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: Explore.Domain.Enums.AuthenticationProviderKind.Keycloak);
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILogEventSink>(logs);
                services.AddOpenTelemetry().WithTracing(tracing =>
                    tracing.AddProcessor(new SimpleActivityExportProcessor(spans)));
            }));
        using var client = hosted.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        Task logSignal = logs.RequestCompleted;
        Task spanSignal = spans.RequestCompleted;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/user?token={canary}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", canary);
        request.Headers.Add("X-Correlation-ID", canary);
        using var response = await client.SendAsync(request);
        await Task.WhenAll(logSignal, spanSignal).WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        string emitted = string.Join('\n', logs.Values.Concat(spans.Values)
            .Append(await response.Content.ReadAsStringAsync())
            .Append(response.Headers.ToString()));
        await Assert.That(emitted).DoesNotContain(canary);
        await Assert.That(emitted).DoesNotContain("error_description");
        await Assert.That(logs.Values.Any(value => value.Contains("authentication_failed", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task RegisteredJwtCallbacksExcludeProviderExceptionAndChallengeDescriptions()
    {
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string content = $"{canary}@example.test\r\nforged-event-{canary}";
        var logs = new DiagnosticSink();
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync(
            primaryProvider: Explore.Domain.Enums.AuthenticationProviderKind.Keycloak);
        using var hosted = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(logs)));
        using var client = hosted.CreateClient();
        using var scope = hosted.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Items["CorrelationId"] = Guid.CreateVersion7().ToString("N");
        var options = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        Task failureSignal = logs.JwtFailure;
        var failure = new AuthenticationFailedContext(context, scheme, options)
        {
            Exception = new InvalidOperationException(content)
        };
        await options.Events.AuthenticationFailed(failure);
        await failureSignal.WaitAsync(TimeSpan.FromSeconds(10));
        var challenge = new JwtBearerChallengeContext(context, scheme, options, new AuthenticationProperties())
        {
            AuthenticateFailure = failure.Exception,
            Error = canary,
            ErrorDescription = content,
            ErrorUri = $"https://private.example.test/{canary}"
        };
        await options.Events.Challenge(challenge);

        await Assert.That(options.IncludeErrorDetails).IsFalse();
        await Assert.That(challenge.Error).IsEqualTo("invalid_token");
        await Assert.That(challenge.ErrorDescription).IsNull();
        await Assert.That(challenge.ErrorUri).IsNull();
        await Assert.That(string.Join('\n', logs.Values)).DoesNotContain(canary);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CallerCorrelationAndCrLfNeverBecomeScopeOrResponseValues(bool crlf)
    {
        string canary = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string incoming = crlf ? $"{canary}\r\nforged-event" : canary;
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-ID"] = incoming;
        context.Request.Headers["X-Request-ID"] = incoming;
        string? observed = null;
        var middleware = new CorrelationIdMiddleware(httpContext =>
        {
            observed = httpContext.Items["CorrelationId"] as string;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context);

        await Assert.That(observed).IsNotNull();
        await Assert.That(Guid.TryParseExact(observed, "N", out _)).IsTrue();
        await Assert.That(observed).DoesNotContain(canary);
    }

    private sealed class DiagnosticProbeFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature) =>
            feature.Controllers.Add(typeof(DiagnosticSinkProbeController).GetTypeInfo());
    }

    private sealed class PrivateDependencyReply(string body) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            { Content = new StringContent(body) });
        }
    }

    private sealed class DiagnosticScopeFilter(string content) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (_, nextMiddleware) =>
            {
                using (Serilog.Context.LogContext.PushProperty("UntrustedScope", content))
                using (Serilog.Context.LogContext.PushProperty("Reason", content))
                using (Serilog.Context.LogContext.PushProperty("Policy", content))
                    await nextMiddleware();
            });
            next(app);
        };
    }

    private sealed class RetentionDiagnosticFault(string canary) : DbCommandInterceptor
    {
        private int _fired;
        public Guid? TargetId { get; set; }
        public bool Fired => Volatile.Read(ref _fired) != 0;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (TargetId is { } target
                && command.Parameters.Cast<DbParameter>().Any(parameter =>
                    parameter.Value is Guid id && id == target
                    || parameter.Value is string value && value.Equals(target.ToString("D"), StringComparison.OrdinalIgnoreCase))
                && Interlocked.CompareExchange(ref _fired, 1, 0) == 0)
                throw new InvalidOperationException($"{canary}\r\nprivate retention target {target:D}");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class HalLookupDiagnosticFault(Guid eventId, string canary) : DbCommandInterceptor
    {
        private int _fired;
        public bool Armed { get; set; }
        public bool Fired => Volatile.Read(ref _fired) != 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            bool target = command.Parameters.Cast<DbParameter>().Any(parameter =>
                parameter.Value is Guid id && id == eventId
                || parameter.Value is string value && value.Contains(eventId.ToString("D"), StringComparison.OrdinalIgnoreCase));
            if (Armed && target && Interlocked.CompareExchange(ref _fired, 1, 0) == 0)
                throw new InvalidOperationException($"{canary}\r\nprivate lookup target {eventId:D}");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class DiagnosticSink : ILogEventSink
    {
        private readonly ConcurrentQueue<string> _values = new();
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _jwtFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _tenancy = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _throttle = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestEventCount;
        public IReadOnlyCollection<string> Values => _values.ToArray();
        public Task RequestCompleted => _completed.Task;
        public Task JwtFailure => _jwtFailure.Task;
        public Task TenancyOutcome => _tenancy.Task;
        public Task ThrottleOutcome => _throttle.Task;
        public int RequestEventCount => Volatile.Read(ref _requestEventCount);
        public void Emit(LogEvent logEvent)
        {
            _values.Enqueue(logEvent.RenderMessage());
            _values.Enqueue(logEvent.Exception?.ToString() ?? string.Empty);
            foreach (var property in logEvent.Properties)
                _values.Enqueue($"{property.Key}={property.Value}");
            if (logEvent.Properties.TryGetValue("Code", out var code)
                && code is ScalarValue { Value: "authentication_failed" })
                _jwtFailure.TrySetResult();
            if (logEvent.Properties.TryGetValue("Code", out code)
                && code is ScalarValue { Value: "tenant_mismatch" or "tenant_required" })
                _tenancy.TrySetResult();
            if (logEvent.Properties.TryGetValue("Code", out code)
                && code is ScalarValue { Value: "rate_limited" })
                _throttle.TrySetResult();
            if (logEvent.Properties.TryGetValue("SourceContext", out var source)
                && source is ScalarValue { Value: string category }
                && category == typeof(RequestLoggingMiddleware).FullName)
            {
                Interlocked.Increment(ref _requestEventCount);
                _completed.TrySetResult();
            }
        }
    }

    private sealed class DiagnosticExporter(ActivityKind expectedKind = ActivityKind.Server) : BaseExporter<Activity>
    {
        private readonly ConcurrentQueue<string> _values = new();
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyCollection<string> Values => _values.ToArray();
        public Task RequestCompleted => _completed.Task;
        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (Activity activity in batch)
            {
                if (activity.Kind != expectedKind) continue;
                _values.Enqueue(activity.DisplayName);
                _values.Enqueue(activity.StatusDescription ?? string.Empty);
                _values.Enqueue(activity.TraceStateString ?? string.Empty);
                foreach (var tag in activity.TagObjects)
                    _values.Enqueue($"{tag.Key}={tag.Value}");
                foreach (var baggage in activity.Baggage)
                    _values.Enqueue($"baggage:{baggage.Key}={baggage.Value}");
                foreach (var activityEvent in activity.Events)
                {
                    _values.Enqueue(activityEvent.Name);
                    foreach (var tag in activityEvent.Tags)
                        _values.Enqueue($"event:{tag.Key}={tag.Value}");
                }
                _completed.TrySetResult();
            }
            return ExportResult.Success;
        }
    }

    private sealed class DiagnosticLogExporter : BaseExporter<LogRecord>
    {
        private readonly ConcurrentQueue<string> _values = new();
        private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyCollection<string> Values => _values.ToArray();
        public Task RequestCompleted => _completed.Task;
        public override ExportResult Export(in Batch<LogRecord> batch)
        {
            foreach (LogRecord record in batch)
            {
                _values.Enqueue(record.FormattedMessage ?? string.Empty);
                _values.Enqueue(record.Exception?.ToString() ?? string.Empty);
                if (record.Attributes is { } attributes)
                    foreach (var attribute in attributes)
                        _values.Enqueue($"{attribute.Key}={attribute.Value}");
                record.ForEachScope((scope, values) =>
                {
                    foreach (var property in scope)
                        values.Enqueue($"scope:{property.Key}={property.Value}");
                }, _values);
                if (record.CategoryName == typeof(RequestLoggingMiddleware).FullName)
                    _completed.TrySetResult();
            }
            return ExportResult.Success;
        }
    }
}

public sealed record DiagnosticProbeFailure(Exception Exception);

[ApiController]
[Route("api/diagnostic-sink-probe")]
public sealed class DiagnosticSinkProbeController(DiagnosticProbeFailure failure) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("{id}", Name = "Phase25DiagnosticProbe")]
    public IActionResult Get(string id) => throw failure.Exception;
}
