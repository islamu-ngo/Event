using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Authentication;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.DTOs.Webhooks;
using Explore.Application.Exceptions;
using Explore.Application.Features.Webhooks.Handlers.Commands;
using Explore.Application.Features.Webhooks.Handlers.Queries;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Features.Webhooks.Requests.Queries;
using Explore.Application.Models;
using Explore.Application.Operations.Decorators;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Webhooks;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Grpc.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Event.Api.IntegrationTests.Authentication;

/// <summary>
/// Dispatcher controls with original Local login, native handlers and physical owners.
/// Only external Cerbos/Svix transport is substituted. Runtime execution is deferred to
/// programme final assurance; a source-complete test is not a runtime receipt.
/// </summary>
[NotInParallel]
public sealed class HumanWebhookDispatcherTests
{
    [Test]
    [Arguments("consumer-create", "allow")]
    [Arguments("consumer-create", "deny")]
    [Arguments("consumer-create", "unavailable")]
    [Arguments("consumer-update", "allow")]
    [Arguments("consumer-update", "deny")]
    [Arguments("consumer-update", "unavailable")]
    [Arguments("endpoint-create", "allow")]
    [Arguments("endpoint-create", "deny")]
    [Arguments("endpoint-create", "unavailable")]
    [Arguments("endpoint-update", "allow")]
    [Arguments("endpoint-update", "deny")]
    [Arguments("endpoint-update", "unavailable")]
    [Arguments("rotate", "allow")]
    [Arguments("rotate", "deny")]
    [Arguments("rotate", "unavailable")]
    [Arguments("test", "allow")]
    [Arguments("test", "deny")]
    [Arguments("test", "unavailable")]
    [Arguments("bulk", "allow")]
    [Arguments("bulk", "deny")]
    [Arguments("bulk", "unavailable")]
    public async Task HumanDispatcherLeavesOneFreshSelectedDecisionToActualOwner(string operation, string outcome)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateHumanAsync(await fixture.SignInAsync());
        request.Bind();
        var seed = await SeedAsync(fixture);
        var selected = new Transport(request.Owner, outcome);
        // A stale outer deny must not block a fresh selected allow; a stale outer allow
        // must not turn the selected deny/outage into a successful business result.
        var outer = new Transport(request.Owner, outcome == "allow" ? "deny" : "allow");
        var preparation = Preparation(request.Services, fixture.Clock, selected);
        var execution = DispatchAsync(request.Services, preparation, outer, operation, seed, fixture);
        if (outcome == "allow")
            await Assert.That((await execution).IsSuccess).IsTrue();
        else if (outcome == "deny")
            await Assert.ThrowsAsync<AuthorizationException>(() => execution);
        else
            await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(() => execution);
        await Assert.That(outer.Requests.Count).IsEqualTo(0);
        await Assert.That(selected.Requests.Count).IsEqualTo(1);
        var sent = selected.Requests.Single();
        await Assert.That(sent.Principal.Id).IsEqualTo(fixture.Binding.LocalSubjectId.ToString("D"));
        await Assert.That(sent.Principal.Attr["userId"].StringValue)
            .IsEqualTo(fixture.Binding.LocalSubjectId.ToString("D"));
        await Assert.That(sent.Resources.Single().Resource.Kind).IsEqualTo(ResourceKinds.Webhook);
        await Assert.That(sent.Resources.Single().Resource.Id).IsEqualTo((operation switch
        {
            "consumer-create" => fixture.Binding.LocalSubjectId,
            "consumer-update" or "endpoint-create" => seed.Consumer.Id,
            "bulk" => PlatformDefaults.DefaultTenantId,
            _ => seed.Endpoint.Id
        }).ToString("D"));
        await Assert.That(sent.Resources.Single().Actions.Single()).IsEqualTo(operation switch
        {
            "consumer-create" or "endpoint-create" => AuthorizationActions.Webhooks.Create,
            "consumer-update" or "endpoint-update" => AuthorizationActions.Webhooks.Update,
            "rotate" => AuthorizationActions.Webhooks.RotateSecret,
            "test" => AuthorizationActions.Webhooks.Test,
            _ => AuthorizationActions.Webhooks.BulkReplay
        });
        await AssertProductsAsync(fixture, seed, operation, outcome == "allow");
        await Assert.That(request.Owner.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("consumer-create", false)]
    [Arguments("consumer-create", true)]
    [Arguments("endpoint-update", false)]
    [Arguments("endpoint-update", true)]
    [Arguments("bulk", false)]
    [Arguments("bulk", true)]
    public async Task DecoratedResponseRequiresOriginalPhysicalCommitAcknowledgement(string operation, bool fail)
    {
        var gate = new CommitGate { Fail = fail };
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: gate);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateHumanAsync(await fixture.SignInAsync());
        request.Bind();
        var seed = await SeedAsync(fixture);
        gate.Owner = request.Owner;
        var selected = new Transport(request.Owner, "allow");
        var outer = new Transport(request.Owner, "deny");
        var execution = DispatchAsync(request.Services, Preparation(request.Services, fixture.Clock, selected),
            outer, operation, seed, fixture);
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            await Assert.That(execution.IsCompleted).IsFalse();
            await Assert.That(gate.Acknowledged).IsFalse();
            await Assert.That(request.Owner.Database.CurrentTransaction).IsNotNull();
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        if (fail)
            await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
        else
            await Assert.That((await execution).IsSuccess).IsTrue();
        await Assert.That(gate.Acknowledged).IsEqualTo(!fail);
        await Assert.That(selected.Requests.Count).IsEqualTo(1);
        await Assert.That(outer.Requests.Count).IsEqualTo(0);
        await AssertProductsAsync(fixture, seed, operation, !fail);
        await Assert.That(request.Owner.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments("consumer-create")]
    [Arguments("endpoint-update")]
    [Arguments("bulk")]
    public async Task AnonymousCatalogueDispatchCannotReachMutationOrSelectedProvider(string operation)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        request.Bind();
        var seed = await SeedAsync(fixture);
        await Assert.That(request.Services.GetRequiredService<IMachinePrincipalAccessor>().Current).IsNull();
        var selected = new Transport(request.Owner, "allow");
        var outer = new Transport(request.Owner, "allow");
        var execution = DispatchAsync(request.Services,
            Preparation(request.Services, fixture.Clock, selected), outer, operation, seed, fixture);
        if (operation == "endpoint-update")
            await Assert.That((await execution).IsSuccess).IsFalse();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(selected.Requests.Count).IsEqualTo(0);
        await Assert.That(outer.Requests.Count).IsEqualTo(0);
        await AssertProductsAsync(fixture, seed, operation, false);
    }

    [Test]
    public async Task ExactSixteenHumanSelectorsBypassOnlyPreliminaryDispatcherAuthority()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateHumanAsync(await fixture.SignInAsync());
        request.Bind();
        var transport = new Transport(request.Owner, "unavailable");
        var id = Guid.CreateVersion7();
        var selectedOperations = new List<HumanWebhookResourceOperation>();
        // These are real closed commands, not an invented handler or an authority mock.
        // This test specifies only the outer gate; actual owners are exercised separately.
        (HumanWebhookResourceOperation Operation, Func<Task> Authorize)[] catalogue =
        [
            (HumanWebhookResourceOperation.CreateConsumer, Check(new CreateWebhookConsumerCommand
                { ConsumerKindId = (int)WebhookConsumerKind.User, Name = "catalogue" })),
            (HumanWebhookResourceOperation.CreateEndpoint, Check(new CreateWebhookEndpointCommand
                { ConsumerId = id, Url = "https://example.invalid/catalogue", SecretRef = "fixture-reference" })),
            (HumanWebhookResourceOperation.UpdateConsumer, Check(new UpdateWebhookConsumerProviderModeCommand
                { ConsumerId = id, PendingWorkReason = "maintenance" })),
            (HumanWebhookResourceOperation.UpdateEndpoint, Check(new UpdateWebhookEndpointCommand
                { EndpointId = id, Governance = new() { PendingWorkReason = "maintenance" } })),
            (HumanWebhookResourceOperation.ArchiveEndpoint, Check(new ArchiveWebhookEndpointCommand { EndpointId = id })),
            (HumanWebhookResourceOperation.PauseEndpoint, Check(new PauseWebhookEndpointCommand { EndpointId = id })),
            (HumanWebhookResourceOperation.ResumeEndpoint, Check(new ResumeWebhookEndpointCommand { EndpointId = id })),
            (HumanWebhookResourceOperation.RotateEndpointSecret, Check(new RotateWebhookEndpointSecretCommand
                { EndpointId = id, NewSecretRef = "fixture-reference-next", PendingWorkReason = "maintenance" })),
            (HumanWebhookResourceOperation.TestEndpoint, Check(new TestWebhookEndpointCommand { EndpointId = id })),
            (HumanWebhookResourceOperation.RetryAttempt, Check(new RetryWebhookDeliveryAttemptCommand { AttemptId = id })),
            (HumanWebhookResourceOperation.ScheduleBulkReplay, Check(new ScheduleWebhookBulkReplayCommand
                { TenantId = PlatformDefaults.DefaultTenantId, OperationKey = id, ReasonCode = "maintenance" })),
            (HumanWebhookResourceOperation.CancelBulkReplay, Check(new CancelWebhookBulkReplayCommand
                { TenantId = PlatformDefaults.DefaultTenantId, OperationId = id, ReasonCode = "maintenance" })),
            (HumanWebhookResourceOperation.RepairProviderBinding, Check(new RepairWebhookProviderBindingCommand { ConsumerId = id })),
            (HumanWebhookResourceOperation.ReconcilePublication, Check(new ReconcileWebhookProviderPublicationCommand { PublicationId = id })),
            (HumanWebhookResourceOperation.AbandonPublication, Check(new AbandonWebhookProviderPublicationCommand { PublicationId = id })),
            (HumanWebhookResourceOperation.OpenPortal, Check(new OpenSvixAppPortalCommand { ConsumerId = id }))
        ];
        for (int index = 0; index < catalogue.Length; index++)
        {
            await Assert.That(catalogue[index].Operation).IsEqualTo((HumanWebhookResourceOperation)(index + 1));
            await catalogue[index].Authorize();
            await Assert.That(selectedOperations[index]).IsEqualTo(catalogue[index].Operation);
        }
        await Assert.That(catalogue.Length).IsEqualTo(16);
        await Assert.That(transport.Requests.Count).IsEqualTo(0);

        Func<Task> Check<TCommand>(TCommand command) where TCommand : notnull, ISecureRequest
        {
            var target = HumanWebhookResourceTarget.From(command);
            return async () =>
            {
                selectedOperations.Add(target.Operation);
                // Real resolver without dependencies would reject annotated persisted owners
                // if preliminary resolution happened. No synthetic resolver or enrichment runs.
                await new RequestAuthorization<TCommand>(Cerbos(request.Services, transport),
                    request.Services.GetRequiredService<IMachinePrincipalAccessor>(), new AuthorizationResourceContextResolver())
                    .AuthorizeAsync(command, NullLogger.Instance, Token);
            };
        }
    }

    [Test]
    [Arguments("consumer-create", "allow")]
    [Arguments("consumer-create", "deny")]
    [Arguments("consumer-create", "unavailable")]
    [Arguments("endpoint-update", "allow")]
    [Arguments("endpoint-update", "deny")]
    [Arguments("endpoint-update", "unavailable")]
    public async Task AuthenticatedPersistedApiKeyKeepsAnnotatedOuterPdpAndNativeHandler(string operation, string outcome)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seed = await SeedAsync(fixture);
        var issued = await IssueMachineCredentialAsync(fixture, await fixture.SignInAsync());
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateMachineAsync(issued);
        request.Bind();
        var machine = request.Services.GetRequiredService<IMachinePrincipalAccessor>();
        await Assert.That(machine is MachinePrincipalAccessor).IsTrue();
        await Assert.That(machine.Current!.KeyId).IsEqualTo(issued.KeyId);
        await Assert.That(machine.Current.OwnerId).IsEqualTo(PlatformDefaults.DefaultTenantId);
        await Assert.That(request.Services.GetRequiredService<IPersistedApiKeyProofAccessor>().CurrentProof!.RowId)
            .IsEqualTo(issued.Id);
        var outer = new Transport(request.Owner, outcome);
        var human = new Transport(request.Owner, "deny");
        var execution = DispatchAsync(request.Services, Preparation(request.Services, fixture.Clock, human),
            outer, operation, seed, fixture);
        if (outcome == "allow")
            await Assert.That((await execution).IsSuccess).IsTrue();
        else if (outcome == "deny")
            await Assert.ThrowsAsync<AuthorizationException>(() => execution);
        else
            await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(() => execution);
        await Assert.That(outer.Requests.Count).IsEqualTo(1);
        await Assert.That(human.Requests.Count).IsEqualTo(0);
        var sent = outer.Requests.Single();
        await Assert.That(sent.Principal.Id).IsEqualTo($"api_key:{issued.KeyId}");
        await Assert.That(sent.Principal.Attr["is_machine"].BoolValue).IsTrue();
        await Assert.That(sent.Principal.Attr["owner_id"].StringValue)
            .IsEqualTo(PlatformDefaults.DefaultTenantId.ToString("D"));
        await Assert.That(sent.Resources.Single().Resource.Kind).IsEqualTo(ResourceKinds.Webhook);
        await Assert.That(sent.Resources.Single().Resource.Id).IsEqualTo(
            (operation == "consumer-create" ? PlatformDefaults.DefaultTenantId : seed.Endpoint.Id).ToString("D"));
        await Assert.That(sent.Resources.Single().Actions.Single()).IsEqualTo(operation == "consumer-create"
            ? AuthorizationActions.Webhooks.Create : AuthorizationActions.Webhooks.Update);
        await AssertProductsAsync(fixture, seed, operation, outcome == "allow", machine: true);
    }

    [Test]
    [Arguments("allow")]
    [Arguments("deny")]
    [Arguments("unavailable")]
    public async Task GenuineMachineKeepsExactExistingAnnotationBehaviorAcrossAllSixteenCommands(string outcome)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        var seed = await SeedAsync(fixture, catalogue: true);
        var issued = await IssueMachineCredentialAsync(fixture, await fixture.SignInAsync());
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateMachineAsync(issued);
        request.Bind();
        await Assert.That(request.Services.GetRequiredService<IMachinePrincipalAccessor>() is MachinePrincipalAccessor).IsTrue();
        await Assert.That(request.Services.GetRequiredService<IPersistedApiKeyProofAccessor>().CurrentProof!.RowId)
            .IsEqualTo(issued.Id);
        var transport = new Transport(request.Owner, outcome);
        Guid tenant = PlatformDefaults.DefaultTenantId;
        // Existing annotations are deliberately not expanded to the five handler-owned
        // unannotated commands. Every annotated case must actually reach the real PDP adapter.
        Func<Task>[] catalogue =
        [
            Check(new CreateWebhookConsumerCommand
                { ConsumerKindId = (int)WebhookConsumerKind.Tenant, OwnerId = tenant, Name = "catalogue" },
                tenant, AuthorizationActions.Webhooks.Create),
            Check(new CreateWebhookEndpointCommand
                { ConsumerId = seed.Consumer.Id, Url = "https://example.invalid/catalogue", SecretRef = "fixture-reference" },
                seed.Consumer.Id, AuthorizationActions.Webhooks.Create),
            Check(new UpdateWebhookConsumerProviderModeCommand
                { ConsumerId = seed.Consumer.Id, PendingWorkReason = "maintenance" },
                seed.Consumer.Id, AuthorizationActions.Webhooks.Update),
            Check(new UpdateWebhookEndpointCommand
                { EndpointId = seed.Endpoint.Id, Governance = new() { PendingWorkReason = "maintenance" } },
                seed.Endpoint.Id, AuthorizationActions.Webhooks.Update),
            Unannotated(new ArchiveWebhookEndpointCommand { EndpointId = seed.Endpoint.Id }),
            Unannotated(new PauseWebhookEndpointCommand { EndpointId = seed.Endpoint.Id }),
            Unannotated(new ResumeWebhookEndpointCommand { EndpointId = seed.Endpoint.Id }),
            Check(new RotateWebhookEndpointSecretCommand
                { EndpointId = seed.Endpoint.Id, NewSecretRef = "fixture-reference-next", PendingWorkReason = "maintenance" },
                seed.Endpoint.Id, AuthorizationActions.Webhooks.RotateSecret),
            Check(new TestWebhookEndpointCommand { EndpointId = seed.Endpoint.Id, SourceTenantId = tenant },
                seed.Endpoint.Id, AuthorizationActions.Webhooks.Test),
            Check(new RetryWebhookDeliveryAttemptCommand { AttemptId = seed.AttemptId },
                seed.AttemptId, AuthorizationActions.Webhooks.Retry),
            Check(new ScheduleWebhookBulkReplayCommand
                { TenantId = tenant, OperationKey = seed.OperationKey, ReasonCode = "maintenance" },
                tenant, AuthorizationActions.Webhooks.BulkReplay),
            Check(new CancelWebhookBulkReplayCommand
                { TenantId = tenant, OperationId = seed.OperationKey, ReasonCode = "maintenance" },
                seed.OperationKey, AuthorizationActions.Webhooks.BulkReplay),
            Check(new RepairWebhookProviderBindingCommand { ConsumerId = seed.Consumer.Id },
                seed.Consumer.Id, AuthorizationActions.Webhooks.ManageProvider),
            Unannotated(new ReconcileWebhookProviderPublicationCommand { PublicationId = seed.OperationKey }),
            Unannotated(new AbandonWebhookProviderPublicationCommand { PublicationId = seed.OperationKey }),
            Check(new OpenSvixAppPortalCommand { ConsumerId = seed.Consumer.Id },
                seed.Consumer.Id, AuthorizationActions.Webhooks.OpenProviderPortal)
        ];
        foreach (var check in catalogue) await check();
        await Assert.That(transport.Requests.Count).IsEqualTo(11);

        Func<Task> Check<T>(T command, Guid expectedId, string expectedAction) where T : notnull => async () =>
        {
            int before = transport.Requests.Count;
            var execution = Authorization<T>(request.Services, transport).AuthorizeAsync(command, NullLogger.Instance, Token);
            if (outcome == "allow")
                await execution;
            else if (outcome == "deny")
                await Assert.ThrowsAsync<AuthorizationException>(() => execution);
            else
                await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(() => execution);
            await Assert.That(transport.Requests.Count).IsEqualTo(before + 1);
            var sent = transport.Requests[^1];
            await Assert.That(sent.Principal.Id).IsEqualTo($"api_key:{issued.KeyId}");
            await Assert.That(sent.Principal.Attr["is_machine"].BoolValue).IsTrue();
            await Assert.That(sent.Resources.Single().Resource.Kind).IsEqualTo(ResourceKinds.Webhook);
            await Assert.That(sent.Resources.Single().Resource.Id).IsEqualTo(expectedId.ToString("D"));
            await Assert.That(sent.Resources.Single().Actions.Single()).IsEqualTo(expectedAction);
        };
        Func<Task> Unannotated<T>(T command) where T : notnull => async () =>
        {
            int before = transport.Requests.Count;
            await Authorization<T>(request.Services, transport).AuthorizeAsync(command, NullLogger.Instance, Token);
            await Assert.That(transport.Requests.Count).IsEqualTo(before);
        };
    }

    [Test]
    [Arguments("allow")]
    [Arguments("deny")]
    [Arguments("unavailable")]
    public async Task WebhookQueryOutsideCatalogueRetainsOuterDecisionAndActualQueryHandler(string outcome)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateHumanAsync(await fixture.SignInAsync());
        request.Bind();
        var seed = await SeedAsync(fixture);
        var transport = new Transport(request.Owner, outcome);
        var query = new GetWebhookConsumerByIdQuery { ConsumerId = seed.Consumer.Id };
        var handler = ActivatorUtilities.CreateInstance<GetWebhookConsumerByIdQueryHandler>(request.Services);
        var decorated = new AuthorizationQueryHandlerDecorator<GetWebhookConsumerByIdQuery, WebhookConsumerDto?>(
            handler, Authorization<GetWebhookConsumerByIdQuery>(request.Services, transport),
            NullLogger<RequestAuthorization<GetWebhookConsumerByIdQuery>>.Instance);
        var execution = decorated.QueryAsync(query, Token);
        if (outcome == "allow")
            await Assert.That((await execution)!.Id).IsEqualTo(seed.Consumer.Id);
        else if (outcome == "deny")
            await Assert.ThrowsAsync<AuthorizationException>(() => execution);
        else
            await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(() => execution);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
        await Assert.That(transport.Requests.Single().Resources.Single().Actions.Single())
            .IsEqualTo(AuthorizationActions.Webhooks.View);
    }

    [Test]
    [Arguments("allow")]
    [Arguments("deny")]
    [Arguments("unavailable")]
    public async Task IncomingRedriveOutsideCatalogueStillRequiresItsAnnotatedOuterDecision(string outcome)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateHumanAsync(await fixture.SignInAsync());
        request.Bind();
        var transport = new Transport(request.Owner, outcome);
        var command = new RedriveIncomingWebhookCommand
        {
            TenantId = PlatformDefaults.DefaultTenantId,
            IncomingWebhookMessageId = Guid.CreateVersion7(),
            ExpectedProcessingGeneration = 1,
            Reason = "operator.recovery"
        };
        // Outer-boundary control only: no pretend redrive handler or invented message.
        var execution = Authorization<RedriveIncomingWebhookCommand>(request.Services, transport)
            .AuthorizeAsync(command, NullLogger.Instance, Token);
        if (outcome == "allow")
            await execution;
        else if (outcome == "deny")
            await Assert.ThrowsAsync<AuthorizationException>(() => execution);
        else
            await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(() => execution);
        await Assert.That(transport.Requests.Count).IsEqualTo(1);
        var resource = transport.Requests.Single().Resources.Single();
        await Assert.That(resource.Resource.Id).IsEqualTo(command.IncomingWebhookMessageId.ToString("D"));
        await Assert.That(resource.Actions.Single()).IsEqualTo(AuthorizationActions.Webhooks.RedriveIncoming);
    }

    [Test]
    [Arguments("allow", false)]
    [Arguments("allow", true)]
    [Arguments("deny", false)]
    [Arguments("unavailable", false)]
    public async Task DecoratedPortalUsesOriginalSelectedRequesterBeforeRealRemoteRelease(string outcome, bool failAudit)
    {
        var audit = new PortalAuditGate { Fail = failAudit };
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: audit);
        await using var request = new Request(fixture.Host.Services);
        await request.AuthenticateHumanAsync(await fixture.SignInAsync());
        request.Bind();
        var seed = await SeedAsync(fixture, portal: true);
        audit.Owner = request.Owner;
        var selected = new Transport(request.Owner, outcome);
        var outer = new Transport(request.Owner, outcome == "allow" ? "deny" : "allow");
        var client = Substitute.For<ISvixWebhookClient>();
        int remoteCalls = 0;
        client.GetApplicationAsync(seed.Binding!.ExternalApplicationId!, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (request.Owner.Database.CurrentTransaction is not null)
                    throw new InvalidOperationException("Remote portal access under physical ownership.");
                remoteCalls++;
                return Task.FromResult(new SvixApplicationBindingResult(seed.Binding.ExternalApplicationId!,
                    seed.Binding.ApplicationUid, new Dictionary<string, string>
                    {
                        ["islamu.tenant_id"] = seed.Consumer.TenantId!.Value.ToString("D"),
                        ["islamu.consumer_id"] = seed.Consumer.Id.ToString("D"),
                        ["islamu.owner_id"] = seed.Consumer.OwnerId.ToString("D"),
                        ["islamu.owner_kind_id"] = ((int)seed.Consumer.ConsumerKind).ToString()
                    }));
            });
        client.CreateAppPortalAccessAsync(Arg.Any<SvixAppPortalAccessRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                remoteCalls++;
                // The external transport supplies the returned value; no credential is
                // generated here or used to authenticate the caller.
                return Task.FromResult(new SvixAppPortalAccessResult("https://svix.example/portal/issued", null));
            });
        var service = new SvixAppPortalService(client,
            request.Services.GetRequiredService<IWebhookConsumerRepository>(),
            request.Services.GetRequiredService<IWebhookConsumerProviderBindingRepository>(),
            new HumanWebhookPreparationTests.PortalOptions(), fixture.Clock);
        var handler = new OpenSvixAppPortalCommandHandler(service,
            request.Services.GetRequiredService<IWebhookAuditEventWriter>(),
            request.Services.GetRequiredService<IWebhookConsumerRepository>(),
            Preparation(request.Services, fixture.Clock, selected),
            request.Services.GetRequiredService<IMachinePrincipalAccessor>());
        var command = new OpenSvixAppPortalCommand
        { ConsumerId = seed.Consumer.Id, SessionId = Guid.CreateVersion7().ToString("D"), ExpiresInSeconds = 300 };
        var execution = Decorate(request.Services, outer, handler).ExecuteAsync(command, Token);
        if (outcome == "allow")
        {
            try
            {
                await audit.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
                await Assert.That(execution.IsCompleted).IsFalse();
                await Assert.That(audit.Acknowledged).IsFalse();
            }
            finally
            {
                audit.Release.TrySetResult();
            }
            if (failAudit)
                await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
            else
            {
                var result = await execution;
                await Assert.That(result.IsSuccess).IsTrue();
                await Assert.That(result.Id!.Url).IsEqualTo("https://svix.example/portal/issued");
            }
            await Assert.That(audit.Acknowledged).IsEqualTo(!failAudit);
            await Assert.That(remoteCalls).IsEqualTo(2);
        }
        else
        {
            if (outcome == "deny")
                await Assert.ThrowsAsync<AuthorizationException>(() => execution);
            else
                await Assert.ThrowsAsync<AuthorizationProviderUnavailableException>(() => execution);
            await Assert.That(remoteCalls).IsEqualTo(0);
        }
        await Assert.That(outer.Requests.Count).IsEqualTo(0);
        await Assert.That(selected.Requests.Count).IsEqualTo(1);
        await Assert.That(selected.Requests.Single().Resources.Single().Actions.Single())
            .IsEqualTo(AuthorizationActions.Webhooks.OpenProviderPortal);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.Set<WebhookAuditEvent>().CountAsync(row =>
            row.TargetId == seed.Consumer.Id && row.ActionId == (int)WebhookAuditAction.PortalAccessIssued, Token))
            .IsEqualTo(outcome == "allow" ? 1 : 0);
    }

    private static CancellationToken Token => LocalIdentityLifecycleHttpFixture.CancellationToken;

    private static RequestAuthorization<T> Authorization<T>(IServiceProvider services, Transport transport)
        where T : notnull => new(Cerbos(services, transport),
            services.GetRequiredService<IMachinePrincipalAccessor>(),
            ActivatorUtilities.CreateInstance<AuthorizationResourceContextResolver>(services),
            services.GetService<IAuthorizationContextEnricher<T>>());

    private static ICommandHandler<T, TResult> Decorate<T, TResult>(
        IServiceProvider services, Transport transport, ICommandHandler<T, TResult> handler)
        where T : ICommand<TResult> => new AuthorizationCommandHandlerDecorator<T, TResult>(
            handler, Authorization<T>(services, transport), NullLogger<RequestAuthorization<T>>.Instance);

    private static CerbosAuthorizationService Cerbos(IServiceProvider services, Transport transport) =>
        ActivatorUtilities.CreateInstance<CerbosAuthorizationService>(services,
            transport.Client, transport.Factory,
            Options.Create(new CerbosSettings { GrpcEndpoint = "http://cerbos.webhook.test:3593" }));

    private static HumanWebhookMutationPreparation Preparation(
        IServiceProvider services, TimeProvider clock, Transport transport)
    {
        var options = Options.Create(new CerbosSettings { GrpcEndpoint = "http://cerbos.webhook.test:3593" });
        var registration = new ServiceCollection();
        registration.AddHumanWebhookResourceSources(IdentityDatabaseTopology.Colocated);
        var descriptor = registration.Single(row => row.ServiceType == typeof(IHumanWebhookResourceSourceFactory));
        var sources = (IHumanWebhookResourceSourceFactory)descriptor.ImplementationFactory!(services);
        return new(services.GetRequiredService<IHumanCommitScopePreparation>(),
            services.GetRequiredService<IHttpContextAccessor>(),
            services.GetRequiredService<IHumanAuthenticationLifetimeObserver>(),
            services.GetRequiredService<ITenantContext>(), clock, Cerbos(services, transport),
            Options.Create(new AuthorizationProviderDeploymentOptions { Provider = "cerbos" }), options,
            () => services.GetRequiredService<IHumanPrincipalSourceFence>(),
            () => services.GetRequiredService<IHumanAuthorizationRouteSource>(),
            () => services.GetRequiredService<IHumanSupportAccessSourceFactory>(),
            () => sources, () => services.GetRequiredService<IPolicySourceSetFence>(),
            () => services.GetRequiredService<IHumanActionCommitFinalizer>(),
            () => services.GetRequiredService<ISerializableAttemptOwner>());
    }

    private static Task<BaseCommandResponse<Guid>> DispatchAsync(IServiceProvider services,
        IHumanWebhookMutationPreparation preparation, Transport outer, string operation, Seed seed,
        LocalIdentityLifecycleHttpFixture fixture) => operation switch
        {
            "consumer-create" => Decorate(services, outer,
                ActivatorUtilities.CreateInstance<CreateWebhookConsumerCommandHandler>(services, preparation))
                .ExecuteAsync(new CreateWebhookConsumerCommand
                {
                    ConsumerKindId = (int)(services.GetRequiredService<IMachinePrincipalAccessor>().Current is null
                        ? WebhookConsumerKind.User : WebhookConsumerKind.Tenant),
                    OwnerId = services.GetRequiredService<IMachinePrincipalAccessor>().Current is null
                        ? fixture.Binding.LocalSubjectId : PlatformDefaults.DefaultTenantId,
                    Name = "dispatcher-created",
                    ProviderModeId = (int)WebhookProviderMode.Local
                }, Token),
            "consumer-update" => Decorate(services, outer,
                ActivatorUtilities.CreateInstance<UpdateWebhookConsumerProviderModeCommandHandler>(services, preparation))
                .ExecuteAsync(new UpdateWebhookConsumerProviderModeCommand
                {
                    ConsumerId = seed.Consumer.Id,
                    ProviderModeId = (int)WebhookProviderMode.DryRun,
                    ExpectedConfigurationVersion = 1,
                    PendingWorkDecisionId = (int)WebhookPendingWorkDecision.PreserveExisting,
                    PendingWorkReason = "maintenance"
                }, Token),
            "endpoint-create" => Decorate(services, outer,
                ActivatorUtilities.CreateInstance<CreateWebhookEndpointCommandHandler>(services, preparation))
                .ExecuteAsync(new CreateWebhookEndpointCommand
                {
                    ConsumerId = seed.Consumer.Id,
                    Url = "https://example.invalid/dispatcher-created",
                    SecretRef = "fixture-reference-created",
                    EventTypeIds = [seed.EventTypeId]
                }, Token),
            "endpoint-update" => Decorate(services, outer,
                ActivatorUtilities.CreateInstance<UpdateWebhookEndpointCommandHandler>(services, preparation))
                .ExecuteAsync(new UpdateWebhookEndpointCommand
                {
                    EndpointId = seed.Endpoint.Id,
                    Destination = new() { Url = "https://example.invalid/dispatcher-updated" },
                    Governance = new()
                    {
                        ExpectedConfigurationVersion = 1,
                        PendingWorkDecisionId = (int)WebhookPendingWorkDecision.PreserveExisting,
                        PendingWorkReason = "maintenance"
                    }
                }, Token),
            "rotate" => Decorate(services, outer,
                ActivatorUtilities.CreateInstance<RotateWebhookEndpointSecretCommandHandler>(services, preparation))
                .ExecuteAsync(new RotateWebhookEndpointSecretCommand
                {
                    EndpointId = seed.Endpoint.Id,
                    NewSecretRef = "fixture-reference-next",
                    ExpectedConfigurationVersion = 1,
                    PendingWorkDecisionId = (int)WebhookPendingWorkDecision.PreserveExisting,
                    PendingWorkReason = "maintenance"
                }, Token),
            "test" => Decorate(services, outer,
                ActivatorUtilities.CreateInstance<TestWebhookEndpointCommandHandler>(services, preparation))
                .ExecuteAsync(new TestWebhookEndpointCommand
                { EndpointId = seed.Endpoint.Id, SourceTenantId = PlatformDefaults.DefaultTenantId }, Token),
            "bulk" => Decorate(services, outer,
                ActivatorUtilities.CreateInstance<ScheduleWebhookBulkReplayCommandHandler>(services, preparation))
                .ExecuteAsync(new ScheduleWebhookBulkReplayCommand
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    ActorUserId = fixture.Binding.LocalSubjectId,
                    OperationKey = seed.OperationKey,
                    FromUtc = fixture.Clock.GetUtcNow().AddHours(-1).UtcDateTime,
                    ToUtc = fixture.Clock.GetUtcNow().AddMinutes(1).UtcDateTime,
                    WebhookConsumerId = seed.Consumer.Id,
                    WebhookEndpointId = seed.Endpoint.Id,
                    MaxItems = 1,
                    ReasonCode = "operator.recovery"
                }, Token),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private sealed record Seed(WebhookConsumer Consumer, WebhookEndpoint Endpoint,
        Guid EventTypeId, Guid OperationKey, Guid AttemptId, WebhookConsumerProviderBinding? Binding);

    private static async Task<Seed> SeedAsync(
        LocalIdentityLifecycleHttpFixture fixture, bool portal = false, bool catalogue = false)
    {
        await using var database = fixture.Native.CreateDatabase();
        var now = fixture.Clock.GetUtcNow();
        var member = await database.TenantUsers.SingleOrDefaultAsync(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == fixture.Binding.LocalSubjectId, Token);
        if (member is null)
        {
            member = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = now.UtcDateTime
            };
            database.TenantUsers.Add(member);
        }
        database.TenantUserRoleGrants.Add(new TenantUserRoleGrant
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
        var consumer = WebhookConsumer.Create(WebhookOwnershipScope.Create(WebhookConsumerKind.User,
            PlatformDefaults.DefaultTenantId, null, null, null, fixture.Binding.LocalSubjectId),
            "dispatcher-original", portal ? WebhookProviderMode.Svix : WebhookProviderMode.Local, now.UtcDateTime);
        var endpoint = new WebhookEndpoint
        {
            Id = Guid.CreateVersion7(),
            TenantId = consumer.TenantId,
            ConsumerId = consumer.Id,
            Url = "https://example.invalid/dispatcher-original",
            SecretRef = "fixture-reference",
            SecretVersion = 1,
            SecretActivatedAt = now.UtcDateTime,
            ConfigurationVersion = 1,
            Status = WebhookEndpointStatus.Active,
            DeliveryStateVersion = 1,
            MaxAttempts = 3,
            TimeoutSeconds = 10,
            CreatedAt = now.UtcDateTime
        };
        var eventType = new WebhookEventType
        {
            Id = Guid.CreateVersion7(),
            Name = $"test.dispatcher.{Guid.CreateVersion7():N}",
            GroupName = "test",
            Description = "Dispatcher endpoint",
            SchemaJson = "{}",
            SchemaVersion = 1,
            IsEnabled = true,
            IsPublic = true,
            PayloadRetentionDays = 1,
            CreatedAt = now.UtcDateTime
        };
        var message = WebhookMessage.Create(PlatformDefaults.DefaultTenantId, eventType.Name,
            Guid.CreateVersion7().ToString("D"), "event", Guid.CreateVersion7(), consumer.Id, "{}"u8,
            "application/json", "identity", now.UtcDateTime, now.AddHours(1).UtcDateTime, now.UtcDateTime);
        var plan = WebhookDeliveryPlanSnapshot.Create(message.TenantId, message.Id, consumer.Id,
            WebhookProviderMode.Local, "1", "1", "fixture", "1", now.AddHours(1), now.AddDays(2),
            now.AddDays(3), now.AddDays(2), now.AddDays(2), now);
        var target = WebhookLocalTargetSnapshot.Create(plan, endpoint, 1, now, null, now);
        var lease = Guid.CreateVersion7();
        target.ClaimForDelivery("fixture", lease, now.AddMinutes(1), now);
        target.DeadLetter(lease, target.DeliveryFence, now);
        database.AddRange(consumer, endpoint, eventType, message, plan, target, new WebhookEndpointSubscription
        {
            Id = Guid.CreateVersion7(),
            EndpointId = endpoint.Id,
            EventTypeId = eventType.Id,
            TenantId = consumer.TenantId,
            IsEnabled = true,
            CreatedAt = now.UtcDateTime
        });
        Guid attemptId = Guid.Empty;
        if (catalogue)
        {
            attemptId = Guid.CreateVersion7();
            database.WebhookDeliveryAttempts.Add(new WebhookDeliveryAttempt
            {
                Id = attemptId,
                TenantId = message.TenantId,
                MessageId = message.Id,
                EndpointId = endpoint.Id,
                AttemptNumber = 1,
                Outcome = WebhookDeliveryAttemptOutcome.Failed,
                ScheduledAt = now.UtcDateTime,
                CreatedAt = now.UtcDateTime
            });
        }
        WebhookConsumerProviderBinding? binding = null;
        if (portal)
        {
            var profile = WebhookProviderCapabilityProfile.Create(WebhookProviderKind.Svix, "1.96.1",
                WebhookProviderCapability.AppPortal, "svix-1.96.1-v1", now);
            binding = WebhookConsumerProviderBinding.CreatePending(consumer.TenantId, consumer.Id,
                Guid.CreateVersion7(), "production", profile, WebhookProviderCapability.AppPortal);
            binding.VerifyOwnership(consumer.TenantId, consumer.Id, "app_dispatcher", now);
            database.Add(binding);
            int role = await database.Roles.Where(row => row.MasterCode == "platform.admin"
                && row.RoleScopeId == (int)RoleScopeEnum.Platform).Select(row => row.Id).SingleAsync(Token);
            database.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                RoleId = role,
                Role = null!,
                GrantedAt = now.UtcDateTime
            });
        }
        await database.SaveChangesAsync(Token);
        return new(consumer, endpoint, eventType.Id, Guid.CreateVersion7(), attemptId, binding);
    }

    private static async Task AssertProductsAsync(LocalIdentityLifecycleHttpFixture fixture, Seed seed,
        string operation, bool committed, bool machine = false)
    {
        // Read owner-visible aggregates through the real repositories, not an authority stub.
        await using var request = new Request(fixture.Host.Services);
        request.Bind();
        var consumers = request.Services.GetRequiredService<IWebhookConsumerRepository>();
        var endpoints = request.Services.GetRequiredService<IWebhookEndpointRepository>();
        var consumer = await consumers.GetByIdForOwnerOperationAsync(seed.Consumer.Id, false, Token);
        var created = await consumers.GetByOwnerAndNameAsync(machine
            ? WebhookOwnershipScope.Create(WebhookConsumerKind.Tenant,
                PlatformDefaults.DefaultTenantId, null, null, null, null)
            : seed.Consumer.Ownership, "dispatcher-created", Token);
        await Assert.That(created is not null).IsEqualTo(committed && operation == "consumer-create");
        await Assert.That(consumer!.ConfigurationVersion).IsEqualTo(committed && operation == "consumer-update" ? 2 : 1);
        await Assert.That(consumer.ProviderMode).IsEqualTo(committed && operation == "consumer-update"
            ? WebhookProviderMode.DryRun : WebhookProviderMode.Local);
        var endpoint = (await endpoints.GetByIdForOwnerOperationAsync(seed.Endpoint.Id, false, Token))!;
        var createdEndpoint = await endpoints.GetByConsumerAndUrlForOwnerOperationAsync(
            seed.Consumer.Id, "https://example.invalid/dispatcher-created", Token);
        await Assert.That(createdEndpoint is not null).IsEqualTo(committed && operation == "endpoint-create");
        await Assert.That(endpoint.ConfigurationVersion)
            .IsEqualTo(committed && operation is "endpoint-update" or "rotate" ? 2 : 1);
        await Assert.That(endpoint.Url).IsEqualTo(committed && operation == "endpoint-update"
            ? "https://example.invalid/dispatcher-updated" : seed.Endpoint.Url);
        await Assert.That(endpoint.SecretVersion).IsEqualTo(committed && operation == "rotate" ? 2 : 1);
        var bulk = await request.Services.GetRequiredService<IWebhookBulkReplayRepository>()
            .GetByOperationKeyAsync(PlatformDefaults.DefaultTenantId, seed.OperationKey, Token);
        await Assert.That(bulk is not null).IsEqualTo(committed && operation == "bulk");
        if (bulk is not null)
            await Assert.That(bulk.Status).IsEqualTo(WebhookBulkReplayStatus.Queued);
        // No public audit/queue aggregate query exposes the whole failed-action cohort.
        await using var database = fixture.Native.CreateDatabase();
        await Assert.That(await database.WebhookMessages.CountAsync(row => row.AggregateId == seed.Endpoint.Id, Token))
            .IsEqualTo(committed && operation == "test" ? 1 : 0);
        var audits = await database.Set<WebhookAuditEvent>().AsNoTracking().ToListAsync(Token);
        await Assert.That(audits.Count).IsEqualTo(committed ? 1 : 0);
        if (committed)
            await Assert.That(audits.Single().PrincipalKind)
                .IsEqualTo(machine ? WebhookAuditPrincipalKind.Machine : WebhookAuditPrincipalKind.User);
    }

    private static async Task<ExternalApiKeyIssuanceDto> IssueMachineCredentialAsync(
        LocalIdentityLifecycleHttpFixture fixture, string humanToken)
    {
        // Consume production's acknowledged issuance, rather than defining a secret,
        // hashing a random literal, or manufacturing ApiKeyPrincipalContext.
        using var http = new HttpRequestMessage(HttpMethod.Post, "/api/externalapikey");
        http.Headers.Authorization = new AuthenticationHeaderValue("Bearer", humanToken);
        http.Headers.Add("Idempotency-Key", Guid.CreateVersion7().ToString("N"));
        http.Content = JsonContent.Create(new CreateExternalApiKeyDto
        {
            Name = "Dispatcher machine",
            ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.Tenant,
            Scopes = [ExternalApiKeyScopes.AdminTenant]
        });
        using var response = await fixture.Client.SendAsync(http, Token);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var issued = (await response.Content.ReadFromJsonAsync<ExternalApiKeyIssuanceDto>(Token))!;
        await Assert.That(issued.DisclosureStatus).IsEqualTo(ExternalApiKeyDisclosureStatus.Issued);
        return issued;
    }

    private sealed class Request : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;
        private readonly IHttpContextAccessor _accessor;
        internal IServiceProvider Services => _scope.ServiceProvider;
        internal ExploreDbContext Owner => Services.GetRequiredService<ExploreDbContext>();
        internal DefaultHttpContext Context { get; }
        internal Request(IServiceProvider services)
        {
            _scope = services.CreateAsyncScope();
            _accessor = Services.GetRequiredService<IHttpContextAccessor>();
            Context = new DefaultHttpContext { RequestServices = Services };
            Context.Request.Path = "/human-webhook-dispatcher";
            Services.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(PlatformDefaults.DefaultTenantId);
        }
        internal void Bind() => _accessor.HttpContext = Context;
        internal async Task AuthenticateHumanAsync(string token)
        {
            Bind();
            Context.Request.Headers.Authorization = "Bearer " + token;
            var result = await Context.AuthenticateAsync();
            await Assert.That(result.Succeeded).IsTrue();
            Context.User = result.Principal!;
        }
        internal async Task AuthenticateMachineAsync(ExternalApiKeyIssuanceDto issued)
        {
            Bind();
            var options = Services.GetRequiredService<IOptionsMonitor<ApiKeyAuthenticationOptions>>()
                .Get(ApiAuthenticationSchemeNames.ApiKey);
            Context.Request.Headers[options.HeaderName] = issued.ApiKey!;
            var result = await Context.AuthenticateAsync(ApiAuthenticationSchemeNames.ApiKey);
            await Assert.That(result.Succeeded).IsTrue();
            Context.User = result.Principal!;
        }
        public async ValueTask DisposeAsync()
        {
            _accessor.HttpContext = null;
            await _scope.DisposeAsync();
        }
    }

    private sealed class Transport
    {
        internal ICerbosClient Client { get; } = Substitute.For<ICerbosClient>();
        internal ICerbosClientFactory Factory { get; } = Substitute.For<ICerbosClientFactory>();
        internal List<Cerbos.Api.V1.Request.CheckResourcesRequest> Requests { get; } = [];
        internal Transport(ExploreDbContext owner, string outcome)
        {
            Factory.GetOrCreate(Arg.Any<string>()).Returns(Client);
            Client.CheckResourcesAsync(Arg.Any<Cerbos.Sdk.Builder.CheckResourcesRequest>(), Arg.Any<Metadata>())
                .Returns(call =>
                {
                    if (owner.Database.CurrentTransaction is not null)
                        throw new InvalidOperationException("PDP evaluation must precede source ownership.");
                    var sent = call.ArgAt<Cerbos.Sdk.Builder.CheckResourcesRequest>(0).ToCheckResourcesRequest().Clone();
                    Requests.Add(sent);
                    if (outcome == "unavailable")
                        throw new RpcException(new Status(StatusCode.Unavailable, "Unavailable"));
                    var response = new Cerbos.Api.V1.Response.CheckResourcesResponse { RequestId = sent.RequestId };
                    var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                    {
                        Resource = new() { Kind = sent.Resources.Single().Resource.Kind, Id = sent.Resources.Single().Resource.Id }
                    };
                    result.Actions.Add(sent.Resources.Single().Actions.Single(), outcome == "allow" ? Effect.Allow : Effect.Deny);
                    response.Results.Add(result);
                    return new Cerbos.Sdk.Response.CheckResourcesResponse(response);
                });
        }
    }

    private sealed class CommitGate : DbTransactionInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        internal bool Fail { get; init; }
        internal bool Acknowledged { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(eventData.Context, Owner)) return result;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            if (Fail) throw new InvalidOperationException("Original physical owner commit failed.");
            return result;
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner)) Acknowledged = true;
            return Task.CompletedTask;
        }
    }

    private sealed class PortalAuditGate : SaveChangesInterceptor
    {
        private bool _savingAudit;
        internal ExploreDbContext? Owner { get; set; }
        internal bool Fail { get; init; }
        internal bool Acknowledged { get; private set; }
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            _savingAudit = ReferenceEquals(eventData.Context, Owner) && eventData.Context!.ChangeTracker
                .Entries<WebhookAuditEvent>().Any(row => row.State == EntityState.Added);
            return ValueTask.FromResult(result);
        }
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!_savingAudit) return result;
            _savingAudit = false;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            if (Fail) throw new InvalidOperationException("Original portal audit acknowledgement failed.");
            Acknowledged = true;
            return result;
        }
    }
}
