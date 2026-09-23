using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Grpc.Core;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Authorization;

public sealed class EventResourceAuthorizationParityTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LocalAndRemoteCannotWidenTheDomainCeiling(bool domainAllowed)
    {
        var client = Substitute.For<ICerbosClient>();
        var factory = Substitute.For<ICerbosClientFactory>();
        factory.GetOrCreate(Arg.Any<string>()).Returns(client);
        var input = Input(domainAllowed);
        client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(new CheckResourcesResponse(Response(input, Effect.Allow)));
        var provider = new EventResourceAuthorizationProvider(factory);

        var local = await provider.CheckAsync(input, default);
        var remote = await provider.CheckAsync(input with { Route = Remote() }, default);
        var expected = domainAllowed ? EventResourceProviderDecision.Allow : EventResourceProviderDecision.Deny;
        await Assert.That(local).IsEqualTo(expected);
        await Assert.That(remote).IsEqualTo(expected);
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("")]
    public async Task RemoteProjectionUsesOnlyFrozenSubjectResourceScopeAndVersion(string scope)
    {
        var client = Substitute.For<ICerbosClient>();
        var factory = Substitute.For<ICerbosClientFactory>();
        factory.GetOrCreate(Arg.Any<string>()).Returns(client);
        var input = Input(true) with { Route = Remote() with { Scope = scope } };
        Cerbos.Api.V1.Request.CheckResourcesRequest? captured = null;
        client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                captured = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                return new CheckResourcesResponse(Response(input, Effect.Allow));
            });

        var result = await new EventResourceAuthorizationProvider(factory).CheckAsync(input, default);
        await Assert.That(result).IsEqualTo(EventResourceProviderDecision.Allow);
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Principal.Id).IsEqualTo(input.Principal.UserId.ToString("D"));
        await Assert.That(captured.Principal.Attr.Keys).IsEquivalentTo(
            new[] { "tenantId", "isTenantMember", "controlsOrganizer", "hasEventUpdate", "hasEventPublish", "canModerate" });
        var entry = captured.Resources.Single();
        await Assert.That(entry.Resource.Id).IsEqualTo(input.Resource.Id.ToString("D"));
        await Assert.That(entry.Resource.Kind).IsEqualTo(ResourceKinds.EventResource);
        await Assert.That(entry.Resource.Scope).IsEqualTo(input.Route.Scope);
        await Assert.That(entry.Resource.PolicyVersion).IsEqualTo(input.Route.PolicyVersion);
        await Assert.That(entry.Resource.Attr["domainAllowed"].BoolValue).IsTrue();
        await Assert.That(entry.Actions).IsEquivalentTo(new[] { input.Action });
    }

    [Test]
    [Arguments("empty")]
    [Arguments("duplicate")]
    [Arguments("wrong-resource")]
    [Arguments("wrong-action")]
    public async Task PartialOrMisboundRemoteResponsesRemainUnavailable(string failure)
    {
        var input = Input(true) with { Route = Remote() };
        var response = Response(input, Effect.Allow);
        switch (failure)
        {
            case "empty": response.Results.Clear(); break;
            case "duplicate": response.Results.Add(response.Results[0].Clone()); break;
            case "wrong-resource": response.Results[0].Resource.Id = Guid.CreateVersion7().ToString("D"); break;
            case "wrong-action": response.Results[0].Actions.Clear(); response.Results[0].Actions.Add("delete", Effect.Allow); break;
        }
        var client = Substitute.For<ICerbosClient>();
        client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(new CheckResourcesResponse(response));
        var factory = Substitute.For<ICerbosClientFactory>();
        factory.GetOrCreate(Arg.Any<string>()).Returns(client);

        await Assert.That(await new EventResourceAuthorizationProvider(factory).CheckAsync(input, default))
            .IsEqualTo(EventResourceProviderDecision.Unavailable);
    }

    [Test]
    public async Task BatchProjectsMultipleResourcesAndActionsAndBindsReorderedResponse()
    {
        var first = Input(true) with { Route = Remote(), Action = AuthorizationActions.EventResources.Download };
        var second = first with { Action = AuthorizationActions.EventResources.ViewAudit };
        var thirdInput = Input(true);
        var third = thirdInput with
        {
            Route = first.Route,
            Principal = first.Principal,
            Resource = thirdInput.Resource with { TenantId = first.Principal.TenantId },
            Action = AuthorizationActions.EventResources.View
        };
        Cerbos.Api.V1.Request.CheckResourcesRequest? captured = null;
        var client = Substitute.For<ICerbosClient>();
        client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                captured = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
                response.Results.Add(Response(third, Effect.Allow).Results.Single());
                var combined = Response(first, Effect.Allow).Results.Single();
                combined.Actions.Add(second.Action, Effect.Deny);
                response.Results.Add(combined);
                return new CheckResourcesResponse(response);
            });
        var factory = Substitute.For<ICerbosClientFactory>();
        factory.GetOrCreate(Arg.Any<string>()).Returns(client);

        var decisions = await new EventResourceAuthorizationProvider(factory)
            .CheckBatchAsync([first, second, third], default);

        await Assert.That(decisions).IsEquivalentTo([
            EventResourceProviderDecision.Allow,
            EventResourceProviderDecision.Deny,
            EventResourceProviderDecision.Allow]);
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Resources.Count).IsEqualTo(2);
        var projected = captured.Resources.Single(entry => entry.Resource.Id == first.Resource.Id.ToString("D"));
        await Assert.That(projected.Actions).IsEquivalentTo([first.Action, second.Action]);
    }

    [Test]
    public async Task BatchSeparatesActionSpecificFrozenResourceProjections()
    {
        var first = Input(true) with { Route = Remote(), Action = AuthorizationActions.EventResources.Update };
        var second = first with
        {
            Action = AuthorizationActions.EventResources.Publish,
            Resource = first.Resource with { ManagementCeiling = !first.Resource.ManagementCeiling }
        };
        var requests = new List<Cerbos.Api.V1.Request.CheckResourcesRequest>();
        var client = Substitute.For<ICerbosClient>();
        client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(call =>
            {
                var request = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
                requests.Add(request);
                var action = request.Resources.Single().Actions.Single();
                var input = action == first.Action ? first : second;
                return new CheckResourcesResponse(Response(input, Effect.Allow));
            });
        var factory = Substitute.For<ICerbosClientFactory>();
        factory.GetOrCreate(Arg.Any<string>()).Returns(client);

        var decisions = await new EventResourceAuthorizationProvider(factory)
            .CheckBatchAsync([first, second], default);

        await Assert.That(decisions.All(decision => decision == EventResourceProviderDecision.Allow)).IsTrue();
        await Assert.That(requests.Count).IsEqualTo(2);
        await Assert.That(requests.Select(request => request.Resources.Single().Actions.Single()))
            .IsEquivalentTo([first.Action, second.Action]);
        await Assert.That(requests.Select(request => request.Resources.Single().Resource.Attr["managementCeiling"].BoolValue))
            .IsEquivalentTo([first.Resource.ManagementCeiling, second.Resource.ManagementCeiling]);
    }

    [Test]
    public async Task BatchRejectsConflictingDuplicateResourceActionWithoutRemoteCall()
    {
        var first = Input(true) with { Route = Remote(), Action = AuthorizationActions.EventResources.Update };
        var conflicting = first with { Resource = first.Resource with { PublicationCeiling = !first.Resource.PublicationCeiling } };
        var client = Substitute.For<ICerbosClient>();
        var factory = Substitute.For<ICerbosClientFactory>();
        factory.GetOrCreate(Arg.Any<string>()).Returns(client);

        var decisions = await new EventResourceAuthorizationProvider(factory)
            .CheckBatchAsync([first, conflicting], default);

        await Assert.That(decisions.All(decision => decision == EventResourceProviderDecision.Unavailable)).IsTrue();
        await client.DidNotReceive().CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
    }

    [Test]
    [Arguments("partial")]
    [Arguments("duplicate")]
    [Arguments("extra-action")]
    [Arguments("unexpected-effect")]
    public async Task BatchRejectsMalformedOrPartialResponses(string failure)
    {
        var first = Input(true) with { Route = Remote(), Action = AuthorizationActions.EventResources.Update };
        var secondInput = Input(true);
        var second = secondInput with
        {
            Route = first.Route,
            Principal = first.Principal,
            Resource = secondInput.Resource with { TenantId = first.Principal.TenantId }
        };
        var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        response.Results.Add(Response(first, Effect.Allow).Results.Single());
        response.Results.Add(Response(second, Effect.Allow).Results.Single());
        switch (failure)
        {
            case "partial": response.Results.RemoveAt(1); break;
            case "duplicate": response.Results[1] = response.Results[0].Clone(); break;
            case "extra-action": response.Results[0].Actions.Add("unexpected", Effect.Allow); break;
            case "unexpected-effect": response.Results[0].Actions[first.Action] = (Effect)999; break;
        }
        var client = Substitute.For<ICerbosClient>();
        client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(new CheckResourcesResponse(response));
        var factory = Substitute.For<ICerbosClientFactory>();
        factory.GetOrCreate(Arg.Any<string>()).Returns(client);

        var decisions = await new EventResourceAuthorizationProvider(factory)
            .CheckBatchAsync([first, second], default);

        await Assert.That(decisions[0]).IsEqualTo(EventResourceProviderDecision.Unavailable);
        await Assert.That(decisions[1]).IsEqualTo(EventResourceProviderDecision.Unavailable);
    }

    [Test]
    public async Task BatchPropagatesCallerCancellationWhileRemoteCallIsPending()
    {
        var input = Input(true) with { Route = Remote() };
        var callStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<CheckResourcesResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Substitute.For<ICerbosClient>();
        client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(_ =>
            {
                callStarted.SetResult();
                return pending.Task;
            });
        var factory = Substitute.For<ICerbosClientFactory>();
        factory.GetOrCreate(Arg.Any<string>()).Returns(client);
        using var cancellation = new CancellationTokenSource();

        var operation = new EventResourceAuthorizationProvider(factory).CheckBatchAsync([input], cancellation.Token);
        await callStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => operation);
    }

    [Test]
    public async Task BatchBoundsCheckAndDistinctResourceCountsWithoutRemoteCall()
    {
        var input = Input(true) with { Route = Remote() };
        var tooManyChecks = Enumerable.Repeat(input, EventResourceAuthorityRequest.MaximumBatchChecks + 1).ToArray();
        var tooManyResources = Enumerable.Range(0, EventResourceAuthorityRequest.MaximumBatchResources + 1)
            .Select(_ => input with { Resource = input.Resource with { Id = Guid.CreateVersion7() } }).ToArray();
        var client = Substitute.For<ICerbosClient>();
        var factory = Substitute.For<ICerbosClientFactory>();
        factory.GetOrCreate(Arg.Any<string>()).Returns(client);
        var provider = new EventResourceAuthorizationProvider(factory);

        var countBound = await provider.CheckBatchAsync(tooManyChecks, default);
        var resourceBound = await provider.CheckBatchAsync(tooManyResources, default);

        await Assert.That(countBound.All(decision => decision == EventResourceProviderDecision.Unavailable)).IsTrue();
        await Assert.That(resourceBound.All(decision => decision == EventResourceProviderDecision.Unavailable)).IsTrue();
        await client.DidNotReceive().CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>());
    }

    [Test]
    public async Task RemoteModerationWithoutFrozenParentFailsClosed()
    {
        var input = Input(true) with { Route = Remote(), Action = "moderate" };
        input = input with { Principal = input.Principal with { CanModerate = true } };
        var client = Substitute.For<ICerbosClient>();
        client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>())
            .Returns(new CheckResourcesResponse(Response(input, Effect.Allow)));
        var factory = Substitute.For<ICerbosClientFactory>();
        factory.GetOrCreate(Arg.Any<string>()).Returns(client);

        await Assert.That(await new EventResourceAuthorizationProvider(factory).CheckAsync(input, default))
            .IsEqualTo(EventResourceProviderDecision.Deny);
    }

    [Test]
    [Arguments(false, false, true, EventResourceProviderDecision.Deny)]
    [Arguments(true, false, true, EventResourceProviderDecision.Allow)]
    [Arguments(false, true, true, EventResourceProviderDecision.Allow)]
    [Arguments(true, true, true, EventResourceProviderDecision.Allow)]
    [Arguments(true, true, false, EventResourceProviderDecision.Deny)]
    public async Task RemoteModerationRequiresResourceAllowAndEitherCompleteParentAllow(
        bool light, bool heavy, bool resource, EventResourceProviderDecision expected)
    {
        var input = ModerationInput();
        var client = Substitute.For<ICerbosClient>();
        client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>()).Returns(call =>
        {
            var request = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
            return new CheckResourcesResponse(request.Resources[0].Resource.Kind == ResourceKinds.Event
                ? ParentResponse(input, light ? Effect.Allow : Effect.Deny, heavy ? Effect.Allow : Effect.Deny)
                : Response(input, resource ? Effect.Allow : Effect.Deny));
        });
        var factory = Substitute.For<ICerbosClientFactory>();
        factory.GetOrCreate(input.Route.GrpcEndpoint!).Returns(client);
        await Assert.That(await new EventResourceAuthorizationProvider(factory).CheckAsync(input, default)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("missing-action")]
    [Arguments("wrong-action")]
    [Arguments("wrong-resource")]
    [Arguments("wrong-kind")]
    [Arguments("duplicate")]
    [Arguments("unexpected-effect")]
    [Arguments("failure")]
    public async Task ParentAllowCannotRescuePartialMisboundOrFailedParentResults(string failure)
    {
        var input = ModerationInput();
        var parent = ParentResponse(input, Effect.Allow, Effect.Deny);
        switch (failure)
        {
            case "missing-action": parent.Results[0].Actions.Remove("moderate-heavy"); break;
            case "wrong-action": parent.Results[0].Actions.Remove("moderate-heavy"); parent.Results[0].Actions.Add("view", Effect.Allow); break;
            case "wrong-resource": parent.Results[0].Resource.Id = input.Resource.Id.ToString("D"); break;
            case "wrong-kind": parent.Results[0].Resource.Kind = ResourceKinds.EventResource; break;
            case "duplicate": parent.Results.Add(parent.Results[0].Clone()); break;
            case "unexpected-effect": parent.Results[0].Actions["moderate-heavy"] = (Effect)999; break;
        }
        var client = Substitute.For<ICerbosClient>();
        client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>()).Returns(call =>
        {
            var request = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
            if (request.Resources[0].Resource.Kind != ResourceKinds.Event)
                return new CheckResourcesResponse(Response(input, Effect.Allow));
            if (failure == "failure") throw new RpcException(new Status(StatusCode.Unavailable, "offline"));
            return new CheckResourcesResponse(parent);
        });
        var factory = Substitute.For<ICerbosClientFactory>();
        factory.GetOrCreate(input.Route.GrpcEndpoint!).Returns(client);
        await Assert.That(await new EventResourceAuthorizationProvider(factory).CheckAsync(input, default))
            .IsEqualTo(EventResourceProviderDecision.Unavailable);
    }

    [Test]
    [Arguments("user")]
    [Arguments("tenant")]
    [Arguments("parent")]
    [Arguments("route")]
    [Arguments("assignment-map")]
    [Arguments("native-admin")]
    public async Task ParentPrerequisiteMustMatchTheResourceAndNativeModerator(string mismatch)
    {
        var input = ModerationInput();
        var parent = input.ParentModeration!;
        parent = mismatch switch
        {
            "user" => parent with { Principal = parent.Principal with { UserId = Guid.CreateVersion7() } },
            "tenant" => parent with { Resource = parent.Resource with { TenantId = Guid.CreateVersion7() } },
            "parent" => parent with { Resource = parent.Resource with { EventId = Guid.CreateVersion7() } },
            "route" => parent with { Route = new("different-scope") },
            "assignment-map" => parent with { Principal = parent.Principal with { EventAssignments = new([]) } },
            "native-admin" => parent with { Principal = parent.Principal with { AdminTenantIds = new([]) } },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        };
        var provider = new EventResourceAuthorizationProvider(Substitute.For<ICerbosClientFactory>());
        await Assert.That(await provider.CheckAsync(input with { ParentModeration = parent }, default))
            .IsEqualTo(EventResourceProviderDecision.Deny);
    }

    [Test]
    [Arguments("")]
    [Arguments("native-tenant-scope")]
    public async Task ParentProjectionPreservesNativeMapsAndSeparateRoutingWithoutRawClock(string scope)
    {
        var input = ModerationInput(scope);
        var native = input.ParentModeration!.Principal;
        Cerbos.Api.V1.Request.CheckResourcesRequest? captured = null;
        var client = Substitute.For<ICerbosClient>();
        client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>()).Returns(call =>
        {
            var request = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
            if (request.Resources[0].Resource.Kind != ResourceKinds.Event)
                return new CheckResourcesResponse(Response(input, Effect.Allow));
            captured = request;
            // A custom policy may narrow on any populated native map. Empty replacements would allow.
            bool deny = request.Principal.Attr["eventFinanceGroups"].ListValue.Values.Count > 0;
            return new CheckResourcesResponse(ParentResponse(input, deny ? Effect.Deny : Effect.Allow, Effect.Deny));
        });
        var factory = Substitute.For<ICerbosClientFactory>();
        factory.GetOrCreate(input.Route.GrpcEndpoint!).Returns(client);
        await Assert.That(await new EventResourceAuthorizationProvider(factory).CheckAsync(input, default))
            .IsEqualTo(EventResourceProviderDecision.Deny);
        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.Resources.Single().Resource.Scope).IsEqualTo(scope);
        await Assert.That(captured.Resources.Single().Resource.PolicyVersion).IsEqualTo("default");
        await Assert.That(captured.Principal.Attr.Keys).IsEquivalentTo(new[]
        {
            "userId", "isInstanceAdmin", "tenantMemberships", "orgMemberships", "groupMemberships",
            "eventCreateOrganizations", "eventCreateGroups", "eventFinanceOrganizations", "eventFinanceGroups", "eventAssignments"
        });
        await Assert.That(captured.Principal.Attr["tenantMemberships"].StructValue.Fields.Keys)
            .IsEquivalentTo(native.AdminTenantIds.Select(value => value.ToString("D")));
        await Assert.That(captured.Principal.Attr["orgMemberships"].StructValue.Fields.Keys)
            .IsEquivalentTo(native.AdminOrganizationIds.Select(value => value.ToString("D")));
        await Assert.That(captured.Principal.Attr["groupMemberships"].StructValue.Fields.Keys)
            .IsEquivalentTo(native.AdminGroupIds.Select(value => value.ToString("D")));
        foreach (var (key, values) in new[]
        {
            ("eventCreateOrganizations", native.EventCreateOrganizationIds), ("eventCreateGroups", native.EventCreateGroupIds),
            ("eventFinanceOrganizations", native.EventFinanceOrganizationIds), ("eventFinanceGroups", native.EventFinanceGroupIds)
        })
            await Assert.That(captured.Principal.Attr[key].ListValue.Values.Select(value => value.StringValue))
                .IsEquivalentTo(values.Select(value => value.ToString("D")));
        var assignment = captured.Principal.Attr["eventAssignments"].StructValue.Fields[input.Resource.EventId.ToString("D")];
        await Assert.That(assignment.StructValue.Fields["roles"].ListValue.Values.Select(value => value.StringValue))
            .IsEquivalentTo(new[] { "event.manager" });
        await Assert.That(assignment.StructValue.Fields["permissions"].ListValue.Values.Select(value => value.StringValue))
            .IsEquivalentTo(new[] { "event:update" });
        var attributes = captured.Resources.Single().Resource.Attr;
        foreach (var (key, value) in AuthorizationFactAttributeProjection.ToAttributes(input.ParentModeration.Resource)!)
            await Assert.That(attributes[key].StringValue).IsEqualTo((string)value);
    }

    [Test]
    public async Task SiblingModerationChecksShareBoundedDeduplicatedParentBatch()
    {
        var first = ModerationInput();
        var otherEvent = Guid.CreateVersion7();
        var principal = first.ParentModeration!.Principal with
        {
            EventAssignments = new(first.ParentModeration.Principal.EventAssignments.Append(
                new(otherEvent, first.Resource.TenantId, new([]), new([]))))
        };
        first = first with { ParentModeration = first.ParentModeration with { Principal = principal } };
        var inputs = Enumerable.Range(0, 500).Select(index => first with
        {
            Resource = first.Resource with { Id = Guid.CreateVersion7(), EventId = index % 2 == 0 ? first.Resource.EventId : otherEvent },
            ParentModeration = first.ParentModeration with
            {
                Resource = first.ParentModeration.Resource with { EventId = index % 2 == 0 ? first.Resource.EventId : otherEvent }
            }
        }).ToArray();
        var requests = new List<Cerbos.Api.V1.Request.CheckResourcesRequest>();
        var client = Substitute.For<ICerbosClient>();
        client.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>()).Returns(call =>
        {
            var request = call.ArgAt<CheckResourcesRequest>(0).ToCheckResourcesRequest();
            requests.Add(request);
            var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
            foreach (var entry in request.Resources.Reverse())
            {
                var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                {
                    Resource = new() { Id = entry.Resource.Id, Kind = entry.Resource.Kind }
                };
                foreach (string action in entry.Actions)
                    result.Actions.Add(action, entry.Resource.Id == otherEvent.ToString("D") ? Effect.Deny : Effect.Allow);
                response.Results.Add(result);
            }
            return new CheckResourcesResponse(response);
        });
        var factory = Substitute.For<ICerbosClientFactory>();
        factory.GetOrCreate(first.Route.GrpcEndpoint!).Returns(client);
        var decisions = await new EventResourceAuthorizationProvider(factory).CheckBatchAsync(inputs, default);
        await Assert.That(decisions).IsEquivalentTo(Enumerable.Range(0, 500).Select(index =>
            index % 2 == 0 ? EventResourceProviderDecision.Allow : EventResourceProviderDecision.Deny));
        var parents = requests.Single(request => request.Resources[0].Resource.Kind == ResourceKinds.Event);
        await Assert.That(parents.Resources.Select(value => value.Resource.Id))
            .IsEquivalentTo(new[] { first.Resource.EventId.ToString("D"), otherEvent.ToString("D") });
        await Assert.That(parents.Resources.All(value => value.Actions.Count == 2)).IsTrue();
    }

    private static EventResourceProviderInput ModerationInput(string scope = "native-parent")
    {
        var input = Input(true);
        var route = new EventResourceParentPolicyRoute(scope);
        var organization = Guid.CreateVersion7();
        var group = Guid.CreateVersion7();
        var principal = new EventModerationPrincipal(input.Principal.UserId, false,
            new([input.Principal.TenantId]), new([organization]), new([group]), new([organization]), new([group]),
            new([organization]), new([group]), new([new(input.Resource.EventId, input.Resource.TenantId,
                new(["event.manager"]), new(["event:update"]))]));
        return input with
        {
            Action = "moderate", Route = Remote() with { ParentEventPolicy = route },
            Principal = input.Principal with { CanModerate = true },
            ParentModeration = new(route, principal, new(input.Resource.TenantId, input.Resource.EventId,
                Guid.CreateVersion7(), input.Principal.UserId, organization, group, Guid.CreateVersion7(),
                input.Principal.UserId, organization, group, "native", input.Principal.UserId))
        };
    }

    private static Cerbos.Api.V1.Response.CheckResourcesResponse ParentResponse(
        EventResourceProviderInput input, Effect light, Effect heavy)
    {
        var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
        {
            Resource = new() { Id = input.Resource.EventId.ToString("D"), Kind = ResourceKinds.Event }
        };
        result.Actions.Add("moderate-light", light);
        result.Actions.Add("moderate-heavy", heavy);
        response.Results.Add(result);
        return response;
    }

    private static EventResourceProviderInput Input(bool domainAllowed)
    {
        var tenant = Guid.CreateVersion7();
        return new(new(EventResourceProviderMode.Local, "tenant", "1"),
            new(Guid.CreateVersion7(), tenant, true, false, true, false, false),
            new(Guid.CreateVersion7(), tenant, Guid.CreateVersion7(), null,
                2, 1, 1, 1, true, true, domainAllowed, true, false, false, domainAllowed), "download");
    }

    private static EventResourceProviderSnapshot Remote() =>
        new(EventResourceProviderMode.Remote, "tenant", "1", "https://pdp.example.test",
            Guid.CreateVersion7(), 1, EventResourceProviderActivationStateEnum.Active);

    private static Cerbos.Api.V1.Response.CheckResourcesResponse Response(EventResourceProviderInput input, Effect effect)
    {
        var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
        var entry = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
        {
            Resource = new() { Id = input.Resource.Id.ToString("D"), Kind = ResourceKinds.EventResource }
        };
        entry.Actions.Add(input.Action, effect);
        response.Results.Add(entry);
        return response;
    }
}
