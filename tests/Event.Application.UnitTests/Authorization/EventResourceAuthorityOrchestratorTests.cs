using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services;
using Explore.Domain.ValueObjects;
using NSubstitute;

namespace Event.Application.UnitTests.Authorization;

public class EventResourceAuthorityOrchestratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.Parse("018f0000-0000-7000-8000-000000000101");
    private static readonly Guid EventId = Guid.Parse("018f0000-0000-7000-8000-000000000102");
    private static readonly Guid ResourceId = Guid.Parse("018f0000-0000-7000-8000-000000000103");
    private static readonly Guid Subject = Guid.Parse("018f0000-0000-7000-8000-000000000104");
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task ParentPolicyInputsMustBeReauthorizedWhenAssignmentExpiresButModeratorEligibilityRemains()
    {
        var f = new Fixture();
        var principal = new EventModerationPrincipal(Subject, false,
            new([Tenant]), new([]), new([]), new([]), new([]), new([]), new([]), new([]));
        var parent = new EventResourceParentModerationFacts(
            new(principal, Tenant, [EventId],
                [new(EventId, "event.manager", new(["event:update"]),
                    new(true, Now.AddMinutes(-1), Now.AddSeconds(1)))]),
            new(Tenant, EventId, Guid.CreateVersion7(), Subject, null, null,
                null, null, null, null, "native", Subject));
        f.Facts = new(f.Resource, f.Access, Management([], moderator: true), "generation-1", parent);
        f.Route = Remote() with { ParentEventPolicy = new("native-parent-scope") };
        f.OnProvider = (input, _) =>
        {
            bool parentPermits = input.ParentModeration!.Principal.EventAssignments.Single()
                .Roles.Contains("event.manager");
            f.Clock.UtcNow = Now.AddSeconds(1);
            return Task.FromResult(parentPermits
                ? EventResourceProviderDecision.Allow : EventResourceProviderDecision.Deny);
        };
        await using var result = await f.Service.AuthorizeAsync(f.Request with { Action = "moderate" },
            (_, _) => Task.FromResult<IEventResourcePrivatePreparation>(new Preparation("generation-1")));
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Forbidden);
        await Assert.That(f.Inputs.Select(input => input.ParentModeration!.Principal.CanModerate(Tenant)).All(value => value)).IsTrue();
        await Assert.That(f.Inputs.Select(input => input.ParentModeration!.Principal.EventAssignments.Single()
            .Roles.Contains("event.manager")).SequenceEqual([true, false])).IsTrue();
        await Assert.That(result.Lease).IsNull();
    }

    [Test]
    public async Task Native_target_resolution_binds_identifiers_without_preloading_policy_authority()
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Tenant);
        var resolver = new AuthorizationResourceContextResolver(tenantContext: tenant);
        var context = await resolver.ResolveAsync(new object(), ResourceKinds.EventResource, "update",
            ResourceId.ToString("D"), null, CancellationToken.None);
        await Assert.That(context.Facts).IsEqualTo(new EventResourceTargetAuthorizationFacts(Tenant, ResourceId));
    }

    [Test]
    [Arguments(EventResourceAudienceKindEnum.Public, EventResourceAuthorityOutcome.Unavailable)]
    [Arguments(EventResourceAudienceKindEnum.AuthenticatedTenantMember, EventResourceAuthorityOutcome.NotFound)]
    public async Task Unbound_provider_route_cannot_reveal_a_private_resource(
        EventResourceAudienceKindEnum audience, EventResourceAuthorityOutcome expected)
    {
        var f = new Fixture(audience) { Route = null };
        var result = await f.Service.AuthorizeAsync(f.Request,
            (_, _) => throw new InvalidOperationException("Unbound authority cannot prepare content."));
        await Assert.That(result.Outcome).IsEqualTo(expected);
    }

    [Test]
    public async Task Provider_deactivation_before_final_read_remains_non_enumerating()
    {
        var f = new Fixture(EventResourceAudienceKindEnum.AuthenticatedTenantMember);
        f.OnProvider = (_, _) =>
        {
            f.Route = null;
            return Task.FromResult(EventResourceProviderDecision.Allow);
        };
        await using var preparation = new Preparation("generation-1");
        await using var result = await f.Service.AuthorizeAsync(f.Request, (_, _) =>
            Task.FromResult<IEventResourcePrivatePreparation>(preparation));
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(preparation.Disposed).IsTrue();
    }

    [Test]
    public async Task Mutation_replay_cannot_reuse_an_evaluation_instant_before_grant_expiry()
    {
        var f = new Fixture();
        f.Facts = f.Capture(management: Management(
            [new("event:update", new(true, ExpiresAtUtc: Now.AddSeconds(1)))]));
        await using var result = await f.Service.AuthorizeAsync(f.Request with { Action = "update" },
            (_, _) => Task.FromResult<IEventResourcePrivatePreparation>(new Preparation("generation-1")));
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        f.Clock.UtcNow = Now.AddSeconds(1);
        var outcome = await f.UnitOfWork.ExecuteSerializableAsync(ct =>
            f.Service.RecheckMutationAsync(result.Lease!, f.Resource.ConcurrencyStamp, ct));
        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.Forbidden);
    }

    [Test]
    public async Task GovernanceChangeInvalidatesAMutationLeaseEvenWhenProviderInputsStillAllow()
    {
        var f = new Fixture();
        var management = Management([new("event:update", new(true))]);
        f.Facts = f.Capture(management: management);
        await using var result = await f.Service.AuthorizeAsync(f.Request with { Action = "update" },
            (_, _) => Task.FromResult<IEventResourcePrivatePreparation>(new Preparation("generation-1")));
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        var previous = f.Access.GovernancePolicy!;
        var tightened = EventResourceGovernancePolicy.Create(previous.EnabledDeliveryTypes, previous.EnabledAudiences,
            previous.PermittedFileTypes, previous.MaxUploadBytes / 2, previous.AllowUnscannedDocuments,
            previous.ExternalOrigins, previous.AuditRetentionDays, previous.MaxActiveResources, long.MaxValue);
        f.Facts = f.Capture(management: management, access: new(Tenant, Subject, false, f.Access.Parent,
            f.Access.Audience, true, tightened));
        var outcome = await f.UnitOfWork.ExecuteSerializableAsync(ct =>
            f.Service.RecheckMutationAsync(result.Lease!, f.Resource.ConcurrencyStamp, ct));
        await Assert.That(outcome).IsEqualTo(EventResourceAuthorityOutcome.Forbidden);
    }

    [Test]
    public async Task Native_capabilities_resolve_current_authority_without_reusing_previous_allow()
    {
        var f = new Fixture();
        var native = f.NativeAuthorizer();
        var request = new AuthorizationRequest(ResourceKinds.EventResource, ResourceId.ToString("D"), "download",
            Facts: new EventResourceTargetAuthorizationFacts(Tenant, ResourceId));
        await Assert.That((await native.AuthorizeBatchAsync([request], CancellationToken.None)).Single().IsAllowed).IsTrue();
        f.Facts = null;
        await Assert.That((await native.AuthorizeBatchAsync([request], CancellationToken.None)).Single().IsAllowed).IsFalse();
    }

    [Test]
    public async Task Native_capabilities_bind_to_server_subject_when_requests_omit_identity()
    {
        var f = new Fixture(EventResourceAudienceKindEnum.AuthenticatedTenantMember);
        var request = new AuthorizationRequest(ResourceKinds.EventResource, ResourceId.ToString("D"), "download");
        var decisions = await f.NativeAuthorizer().AuthorizeBatchAsync([request], CancellationToken.None);
        await Assert.That(decisions.Single().IsAllowed).IsTrue();
        await Assert.That(f.Inputs.Single().Principal.UserId).IsEqualTo(Subject);
        await Assert.That(f.Inputs.Single().Principal.TenantId).IsEqualTo(Tenant);
    }

    [Test]
    [Arguments("subject")]
    [Arguments("tenant")]
    [Arguments("scope")]
    [Arguments("fact-tenant")]
    [Arguments("fact-resource")]
    [Arguments("generic-facts")]
    [Arguments("machine")]
    public async Task Native_capabilities_cannot_override_server_owned_identity_with_request_facts(string mismatch)
    {
        var f = new Fixture();
        var request = new AuthorizationRequest(ResourceKinds.EventResource, ResourceId.ToString("D"), "download");
        var other = Guid.CreateVersion7();
        request = mismatch switch
        {
            "subject" => request with { Subject = new(other) },
            "tenant" => request with { Tenant = new(other) },
            "scope" => request with { Scope = new(TenantId: other.ToString("D")) },
            "fact-tenant" => request with { Facts = new EventResourceTargetAuthorizationFacts(other, ResourceId) },
            "fact-resource" => request with { Facts = new EventResourceTargetAuthorizationFacts(Tenant, other) },
            "generic-facts" => request with { Facts = InstanceScopedAuthorizationFacts.Instance },
            "machine" => request with { Subject = new(IsMachine: true) },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        };
        await Assert.That((await f.NativeAuthorizer().AuthorizeBatchAsync(
            [request], CancellationToken.None)).Single().IsAllowed).IsFalse();
    }

    [Test]
    public async Task Capability_batch_uses_shared_snapshots_without_fabricating_a_deadline()
    {
        var f = new Fixture { RejectSingleCalls = true };
        EventResourceAuthorityRequest[] requests =
            [f.Request with { Action = "view", DeadlineUtc = null }, f.Request with { DeadlineUtc = null }];
        var results = await f.Service.AuthorizeCapabilitiesAsync(requests);
        await Assert.That(results).IsEquivalentTo(
            [EventResourceAuthorityOutcome.Allowed, EventResourceAuthorityOutcome.Allowed]);
        await Assert.That(f.UnitOfWork.CommittedReads).IsEqualTo(2);
        await Assert.That(f.Inputs.Select(input => input.Action)).IsEquivalentTo(["view", "download"]);
    }

    [Test]
    public async Task Capability_batch_does_not_reuse_allow_after_revocation_before_final_read()
    {
        var f = new Fixture();
        var entered = Signal();
        var resume = Signal();
        f.OnBatchProvider = async (inputs, ct) =>
        {
            entered.SetResult();
            await resume.Task.WaitAsync(Timeout, ct);
            return inputs.Select(_ => EventResourceProviderDecision.Allow).ToArray();
        };
        var pending = f.Service.AuthorizeCapabilitiesAsync([f.Request with { DeadlineUtc = null }]);
        await entered.Task.WaitAsync(Timeout);
        f.Facts = null;
        resume.SetResult();
        await Assert.That(await pending.WaitAsync(Timeout)).IsEquivalentTo([EventResourceAuthorityOutcome.NotFound]);
    }

    [Test]
    public async Task Capability_batch_rejects_partial_provider_results()
    {
        var f = new Fixture();
        f.OnBatchProvider = (_, _) => Task.FromResult<IReadOnlyList<EventResourceProviderDecision>>(
            [EventResourceProviderDecision.Allow]);
        var results = await f.Service.AuthorizeCapabilitiesAsync(
            [f.Request with { Action = "view" }, f.Request]);
        await Assert.That(results).IsEquivalentTo(
            [EventResourceAuthorityOutcome.Unavailable, EventResourceAuthorityOutcome.Unavailable]);
    }

    [Test]
    public async Task Capability_batch_rechecks_grant_expiry_before_returning_affordances()
    {
        var f = new Fixture();
        f.Facts = f.Capture(management: Management(
            [new("event:update", new(true, ExpiresAtUtc: Now.AddSeconds(1)))]));
        f.OnRead = (_, _) =>
        {
            if (f.UnitOfWork.CommittedReads == 1) f.Clock.UtcNow = Now.AddSeconds(1);
            return Task.CompletedTask;
        };
        var results = await f.Service.AuthorizeCapabilitiesAsync(
            [f.Request with { Action = "update", DeadlineUtc = null }]);
        await Assert.That(results).IsEquivalentTo([EventResourceAuthorityOutcome.Forbidden]);
    }

    [Test]
    public async Task Capability_batch_is_cancelled_by_the_original_caller_while_provider_is_pending()
    {
        var f = new Fixture();
        using var cancelled = new CancellationTokenSource();
        var entered = Signal();
        var neverReleased = Signal();
        f.OnBatchProvider = async (_, ct) =>
        {
            entered.SetResult();
            await neverReleased.Task.WaitAsync(Timeout, ct);
            throw new InvalidOperationException("Cancelled request must not resume evaluation.");
        };
        var pending = f.Service.AuthorizeCapabilitiesAsync(
            [f.Request with { DeadlineUtc = null }], cancelled.Token);
        await entered.Task.WaitAsync(Timeout);
        await cancelled.CancelAsync();
        await Assert.That(await pending.WaitAsync(Timeout)).IsEquivalentTo([EventResourceAuthorityOutcome.Cancelled]);
    }

    [Test]
    public async Task Capability_batch_cannot_mix_subject_or_tenant_authority()
    {
        var f = new Fixture();
        var results = await f.Service.AuthorizeCapabilitiesAsync(
            [f.Request, f.Request with { SubjectUserId = Guid.CreateVersion7() }]);
        await Assert.That(results).IsEquivalentTo(
            [EventResourceAuthorityOutcome.Forbidden, EventResourceAuthorityOutcome.Forbidden]);
    }

    [Test]
    public async Task Revocation_during_private_preparation_cannot_reuse_provisional_allow()
    {
        var fixture = new Fixture();
        var entered = Signal();
        var resume = Signal();
        await using var preparation = new Preparation("generation-1");
        var decision = fixture.Service.AuthorizeAsync(fixture.Request, async (_, ct) =>
        {
            entered.SetResult();
            await resume.Task.WaitAsync(Timeout, ct);
            return preparation;
        });
        await entered.Task.WaitAsync(Timeout);
        fixture.Facts = null;
        resume.SetResult();
        var result = await decision.WaitAsync(Timeout);
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(preparation.Disposed).IsTrue();
        await Assert.That(result.Lease).IsNull();
    }

    [Test]
    public async Task Stable_authority_uses_separate_reads_then_a_single_use_header_gate()
    {
        var f = new Fixture();
        await using var preparation = new Preparation("generation-1");
        var result = await f.Service.AuthorizeAsync(f.Request, (_, _) =>
        {
            if (f.UnitOfWork.InTransaction || f.UnitOfWork.CommittedReads != 1)
                throw new InvalidOperationException("Preparation must follow committed A");
            return Task.FromResult<IEventResourcePrivatePreparation>(preparation);
        });
        await using var lease = result.Lease;
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        await Assert.That(f.UnitOfWork.CommittedReads).IsEqualTo(2);
        var before = f.Clock.Samples;
        var released = await f.Service.CompleteHeadersAsync(lease!);
        await Assert.That(released.Preparation).IsEqualTo(preparation);
        await Assert.That(released.Disclosure!.CanAccess).IsTrue();
        await Assert.That(f.Clock.Samples - before).IsEqualTo(1);
        await Assert.That((await f.Service.CompleteHeadersAsync(lease!)).Outcome).IsEqualTo(EventResourceAuthorityOutcome.Forbidden);
    }

    [Test]
    public async Task Captured_policy_and_permission_collections_have_no_mutable_alias()
    {
        var f = new Fixture();
        var grants = new List<EventResourcePermissionGrant> { new("event:update", new(true)) };
        var management = Management(grants);
        f.Facts = f.Capture(management: management);
        grants.Clear();
        f.Resource.Withdraw(f.Resource.ConcurrencyStamp, Subject, Now.UtcDateTime);
        await using var preparation = new Preparation("generation-1");
        var result = await f.Service.AuthorizeAsync(f.Request, (_, _) => Task.FromResult<IEventResourcePrivatePreparation>(preparation));
        await using var lease = result.Lease;
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        await Assert.That(f.Inputs.Single().Principal.HasEventUpdate).IsTrue();
        await Assert.That(f.Inputs.Single().Resource.PublicationStateId).IsEqualTo((int)EventResourcePublicationStateEnum.Published);
        await Assert.That((await f.Service.CompleteHeadersAsync(lease!)).Outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
    }

    [Test]
    [Arguments("membership")]
    [Arguments("organizer")]
    [Arguments("permission")]
    [Arguments("moderation")]
    [Arguments("scope")]
    [Arguments("route")]
    [Arguments("deployment")]
    [Arguments("epoch")]
    [Arguments("policy")]
    [Arguments("attachment")]
    [Arguments("version")]
    [Arguments("disclosure")]
    public async Task Changed_outbound_input_or_attachment_restarts_the_entire_decision(string change)
    {
        var f = new Fixture { Route = Remote() };
        await using var first = new Preparation("generation-1");
        await using var second = new Preparation(change == "attachment" ? "generation-2" : "generation-1");
        f.OnProvider = (input, _) =>
        {
            if (change is not ("attachment" or "version") && f.Inputs.Count == 2 && input == f.Inputs[0])
                throw new InvalidOperationException("Changed provider attributes were not re-frozen");
            return Task.FromResult(EventResourceProviderDecision.Allow);
        };
        var prepared = false;
        var result = await f.Service.AuthorizeAsync(f.Request, (_, _) =>
        {
            if (prepared) return Task.FromResult<IEventResourcePrivatePreparation>(second);
            prepared = true;
            switch (change)
            {
                case "membership": f.Facts = f.Capture(management: Management([], member: false)); break;
                case "organizer": f.Facts = f.Capture(management: Management([], organizer: true)); break;
                case "permission": f.Facts = f.Capture(management: Management([new("event:update", new(true))])); break;
                case "moderation": f.Facts = f.Capture(management: Management([], moderator: true)); break;
                case "scope": f.Route = f.Route with { Scope = "event" }; break;
                case "route": f.Route = f.Route with { GrpcEndpoint = "https://pdp-two.example" }; break;
                case "deployment": f.Route = f.Route with { DeploymentId = EventId }; break;
                case "epoch": f.Route = f.Route with { ActivationEpoch = 3 }; break;
                case "policy": f.Route = f.Route with { PolicyVersion = "2" }; break;
                case "attachment": f.Facts = f.Capture("generation-2"); break;
                case "version": f.Resource.ConcurrencyStamp = EventId; f.Facts = f.Capture(); break;
                case "disclosure":
                    f.Resource.UpdateMetadata(new EventResourceMetadata { Title = "Resource", PublicTitle = "Public",
                        Kind = (EventResourceKindEnum)1, DisclosureMode = EventResourceDisclosureModeEnum.Public },
                        f.Resource.ConcurrencyStamp, Subject, Now.UtcDateTime);
                    f.Facts = f.Capture(); break;
            }
            return Task.FromResult<IEventResourcePrivatePreparation>(first);
        });
        await using var lease = result.Lease;
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        await Assert.That(first.Disposed).IsTrue();
        await Assert.That((await f.Service.CompleteHeadersAsync(lease!)).Preparation).IsEqualTo(second);
        await Assert.That(f.UnitOfWork.CommittedReads).IsEqualTo(4);
    }

    [Test]
    public async Task Repeated_changes_exhaust_two_complete_attempts_and_dispose_both_preparations()
    {
        var f = new Fixture { Route = Remote() };
        await using var first = new Preparation("generation-1");
        await using var second = new Preparation("generation-1");
        var prepared = false;
        var result = await f.Service.AuthorizeAsync(f.Request, (_, _) =>
        {
            f.Route = f.Route with { ActivationEpoch = f.Route.ActivationEpoch + 1 };
            var value = prepared ? second : first;
            prepared = true;
            return Task.FromResult<IEventResourcePrivatePreparation>(value);
        });
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Unavailable);
        await Assert.That(first.Disposed && second.Disposed).IsTrue();
        await Assert.That(f.UnitOfWork.CommittedReads).IsEqualTo(4);
    }

    [Test]
    [Arguments("event:manage-team", false, false)]
    [Arguments("event:manage-finance", false, false)]
    [Arguments("event.registration:manage", false, false)]
    [Arguments("event:publish", false, false)]
    [Arguments("event:update", true, false)]
    [Arguments("both", true, true)]
    [Arguments("organizer", true, true)]
    [Arguments("admin", false, false)]
    public async Task Management_uses_exact_permissions_not_manager_or_admin_shortcuts(string authority, bool update, bool publish)
    {
        var f = new Fixture();
        f.Resource.Withdraw(f.Resource.ConcurrencyStamp, Subject, Now.UtcDateTime);
        EventResourcePermissionGrant[] permissions = authority == "both"
            ? [new("event:update", new(true)), new("event:publish", new(true))]
            : [new(authority, new(true))];
        f.Facts = f.Capture(management: Management(permissions, organizer: authority == "organizer"));
        foreach (var (action, allowed) in new[] { ("update", update), ("publish", publish) })
        {
            await using var preparation = new Preparation("generation-1");
            var result = await f.Service.AuthorizeAsync(f.Request with { Action = action },
                (_, _) => Task.FromResult<IEventResourcePrivatePreparation>(preparation));
            await using var lease = result.Lease;
            await Assert.That(result.Outcome == EventResourceAuthorityOutcome.Allowed).IsEqualTo(allowed);
        }
    }

    [Test]
    public async Task Moderator_can_withdraw_but_cannot_download_restricted_material()
    {
        var f = new Fixture(EventResourceAudienceKindEnum.Organizer);
        var noAudience = new EventResourceAccessFacts(Tenant, Subject, false, f.Access.Parent, [], true, f.Access.GovernancePolicy);
        f.Facts = f.Capture(management: Management([], moderator: true), access: noAudience);
        await using var preparation = new Preparation("generation-1");
        var result = await f.Service.AuthorizeAsync(f.Request with { Action = "moderate" },
            (_, _) => Task.FromResult<IEventResourcePrivatePreparation>(preparation));
        await using var lease = result.Lease;
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        var download = await f.Service.AuthorizeAsync(f.Request, NeverPrepare);
        await Assert.That(download.Outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task Anonymous_and_machine_callers_share_only_the_public_path_without_PDP(bool machine, bool restricted)
    {
        var f = new Fixture(restricted ? EventResourceAudienceKindEnum.AuthenticatedTenantMember : EventResourceAudienceKindEnum.Public);
        var user = machine ? Subject : (Guid?)null;
        f.Request = f.Request with { IsMachineCaller = machine, SubjectUserId = user };
        f.Facts = f.Capture(access: new(Tenant, user, machine, f.Access.Parent, f.Access.Audience, true, f.Access.GovernancePolicy));
        f.OnProvider = (_, _) => throw new InvalidOperationException("Public-only callers must not enter PDP");
        await using var preparation = new Preparation("generation-1");
        var result = await f.Service.AuthorizeAsync(f.Request,
            (_, _) => Task.FromResult<IEventResourcePrivatePreparation>(preparation));
        await using var lease = result.Lease;
        await Assert.That(result.Outcome == EventResourceAuthorityOutcome.Allowed).IsEqualTo(!restricted);
        await Assert.That(f.Inputs.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("missing")]
    [Arguments("unbound")]
    [Arguments("transitioning")]
    [Arguments("failed")]
    [Arguments("malformed")]
    public async Task Unusable_remote_activation_never_enters_PDP_or_preparation(string state)
    {
        var f = new Fixture { Route = Remote() };
        f.Route = state switch
        {
            "missing" => null,
            "unbound" => f.Route with { DeploymentId = null },
            "transitioning" => f.Route with { ActivationState = EventResourceProviderActivationStateEnum.Transitioning },
            "failed" => f.Route with { ActivationState = EventResourceProviderActivationStateEnum.Failed },
            _ => f.Route with { GrpcEndpoint = "not-a-route" }
        };
        var result = await f.Service.AuthorizeAsync(f.Request, NeverPrepare);
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Unavailable);
        await Assert.That(f.Inputs.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("error")]
    [Arguments("partial")]
    [Arguments("deny")]
    public async Task Provider_errors_and_partial_responses_are_bounded_and_fail_closed(string behavior)
    {
        var f = new Fixture();
        f.OnProvider = (_, _) => behavior == "error"
            ? throw new InvalidOperationException("private provider response")
            : Task.FromResult(behavior == "deny" ? EventResourceProviderDecision.Deny : (EventResourceProviderDecision)99);
        var result = await f.Service.AuthorizeAsync(f.Request, NeverPrepare);
        await Assert.That(result.Outcome).IsEqualTo(behavior == "deny"
            ? EventResourceAuthorityOutcome.Forbidden : EventResourceAuthorityOutcome.Unavailable);
    }

    [Test]
    [Arguments("deny")]
    [Arguments("unavailable")]
    [Arguments("error")]
    public async Task HiddenRestrictedResourceDoesNotDiscloseExistenceThroughProviderFailure(string behavior)
    {
        var f = new Fixture(EventResourceAudienceKindEnum.AuthenticatedTenantMember);
        f.OnProvider = (_, _) => behavior == "error"
            ? throw new InvalidOperationException("private provider response")
            : Task.FromResult(behavior == "deny"
                ? EventResourceProviderDecision.Deny : EventResourceProviderDecision.Unavailable);
        var result = await f.Service.AuthorizeAsync(f.Request, NeverPrepare);
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
    }

    [Test]
    [Arguments("provider")]
    [Arguments("preparation")]
    [Arguments("headers")]
    public async Task Clock_advancement_during_each_external_gap_cannot_extend_availability(string gap)
    {
        var f = new Fixture(availability: EventResourceAvailability.Create(absoluteEndUtc: Now.AddSeconds(5)));
        var entered = Signal();
        var resume = Signal();
        if (gap == "provider") f.OnProvider = async (_, ct) =>
        {
            entered.SetResult();
            await resume.Task.WaitAsync(Timeout, ct);
            return EventResourceProviderDecision.Allow;
        };
        await using var preparation = new Preparation("generation-1");
        var pending = f.Service.AuthorizeAsync(f.Request, async (_, ct) =>
        {
            if (gap == "preparation")
            {
                entered.SetResult();
                await resume.Task.WaitAsync(Timeout, ct);
            }
            return preparation;
        });
        if (gap != "headers")
        {
            await entered.Task.WaitAsync(Timeout);
            f.Clock.UtcNow = Now.AddSeconds(5);
            resume.SetResult();
        }
        var result = await pending.WaitAsync(Timeout);
        await using var lease = result.Lease;
        if (gap == "headers")
        {
            f.Clock.UtcNow = Now.AddSeconds(5);
            var headers = await f.Service.CompleteHeadersAsync(lease!);
            await Assert.That(headers.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Forbidden);
            await Assert.That(headers.Preparation).IsNull();
        }
        else await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
        await Assert.That(preparation.Disposed).IsTrue();
    }

    [Test]
    public async Task Staff_expiry_after_B_is_rechecked_from_frozen_facts_at_headers()
    {
        var f = new Fixture(EventResourceAudienceKindEnum.EventStaff);
        var audience = f.Access.Audience.Select(a => a with { ExpiresAtUtc = Now.AddSeconds(5) });
        f.Facts = f.Capture(access: new(Tenant, Subject, false, f.Access.Parent, audience, true, f.Access.GovernancePolicy));
        await using var preparation = new Preparation("generation-1");
        var result = await f.Service.AuthorizeAsync(f.Request, (_, _) => Task.FromResult<IEventResourcePrivatePreparation>(preparation));
        await using var lease = result.Lease;
        f.Clock.UtcNow = Now.AddSeconds(5);
        await Assert.That((await f.Service.CompleteHeadersAsync(lease!)).Outcome).IsEqualTo(EventResourceAuthorityOutcome.Forbidden);
        await Assert.That(preparation.Disposed).IsTrue();
    }

    [Test]
    public async Task Cancellation_after_preparation_disposes_without_starting_B()
    {
        var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        await using var preparation = new Preparation("generation-1");
        var result = await f.Service.AuthorizeAsync(f.Request, (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult<IEventResourcePrivatePreparation>(preparation);
        }, cancellation.Token);
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Cancelled);
        await Assert.That(preparation.Disposed).IsTrue();
        await Assert.That(f.UnitOfWork.CommittedReads).IsEqualTo(1);
    }

    [Test]
    public async Task Mutation_recheck_uses_the_callers_transaction_and_rejects_revocation_and_version_conflict()
    {
        var f = new Fixture();
        f.Facts = f.Capture(management: Management([new("event:update", new(true))]));
        await using var preparation = new Preparation("generation-1");
        var result = await f.Service.AuthorizeAsync(f.Request with { Action = "update" },
            (_, _) => Task.FromResult<IEventResourcePrivatePreparation>(preparation));
        await using var lease = result.Lease;
        var conflict = await f.UnitOfWork.ExecuteSerializableAsync(ct =>
            f.Service.RecheckMutationAsync(lease!, EventId, ct));
        await Assert.That(conflict).IsEqualTo(EventResourceAuthorityOutcome.VersionConflict);
        f.Facts = f.Capture();
        var revoked = await f.UnitOfWork.ExecuteSerializableAsync(ct =>
            f.Service.RecheckMutationAsync(lease!, f.Resource.ConcurrencyStamp, ct));
        await Assert.That(revoked).IsEqualTo(EventResourceAuthorityOutcome.Forbidden);
    }

    [Test]
    public async Task Creation_resolves_parent_identity_and_never_invents_a_persisted_resource()
    {
        var f = new Fixture();
        f.Request = f.Request with { ResourceId = EventId, Action = "create" };
        f.Facts = new(f.Access.Parent, Tenant, Subject, false, Management([new("event:update", new(true))]), f.Access.GovernancePolicy);
        await using var preparation = new Preparation("parent");
        var result = await f.Service.AuthorizeAsync(f.Request, (_, _) => Task.FromResult<IEventResourcePrivatePreparation>(preparation));
        await using var lease = result.Lease;
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        await Assert.That(f.Facts.Policy).IsNull();
        await Assert.That(f.Inputs.Single().Resource.IsCreation).IsTrue();
        await Assert.That(f.Inputs.Single().Resource.Id).IsEqualTo(EventId);
        var recheck = await f.UnitOfWork.ExecuteSerializableAsync(ct => f.Service.RecheckMutationAsync(lease!, Tenant, ct));
        await Assert.That(recheck).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
    }

    [Test]
    public async Task Original_caller_cancellation_is_retained_until_headers()
    {
        var f = new Fixture();
        using var cancellation = new CancellationTokenSource();
        await using var preparation = new Preparation("generation-1");
        var result = await f.Service.AuthorizeAsync(f.Request,
            (_, _) => Task.FromResult<IEventResourcePrivatePreparation>(preparation), cancellation.Token);
        await using var lease = result.Lease;
        cancellation.Cancel();
        await Assert.That((await f.Service.CompleteHeadersAsync(lease!)).Outcome).IsEqualTo(EventResourceAuthorityOutcome.Cancelled);
        await Assert.That(preparation.Disposed).IsTrue();
    }

    [Test]
    public async Task Anonymous_metadata_cannot_release_prepared_private_fields_after_eligibility_expires()
    {
        var f = new Fixture(availability: EventResourceAvailability.Create(absoluteEndUtc: Now.AddSeconds(5)));
        f.Request = f.Request with { SubjectUserId = null, Action = "view" };
        f.Resource.UpdateMetadata(new EventResourceMetadata { Title = "Private", PublicTitle = "Public",
            Kind = (EventResourceKindEnum)1, DisclosureMode = EventResourceDisclosureModeEnum.Teaser },
            f.Resource.ConcurrencyStamp, Subject, Now.UtcDateTime);
        f.Facts = f.Capture(access: new(Tenant, null, false, f.Access.Parent, [], true, f.Access.GovernancePolicy));
        await using var preparation = new Preparation("generation-1");
        var result = await f.Service.AuthorizeAsync(f.Request, (_, _) => Task.FromResult<IEventResourcePrivatePreparation>(preparation));
        await using var lease = result.Lease;
        f.Clock.UtcNow = Now.AddSeconds(5);
        await Assert.That((await f.Service.CompleteHeadersAsync(lease!)).Outcome).IsEqualTo(EventResourceAuthorityOutcome.Forbidden);
        await Assert.That(preparation.Disposed).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Final_read_failure_or_revocation_while_B_is_blocked_disposes_delivery(bool failure)
    {
        var f = new Fixture();
        var entered = Signal();
        var resume = Signal();
        f.OnRead = async (_, ct) =>
        {
            if (f.UnitOfWork.CommittedReads != 1) return;
            entered.SetResult();
            await resume.Task.WaitAsync(Timeout, ct);
            if (failure) throw new InvalidOperationException("private persistence failure");
        };
        await using var preparation = new Preparation("generation-1");
        var pending = f.Service.AuthorizeAsync(f.Request, (_, _) => Task.FromResult<IEventResourcePrivatePreparation>(preparation));
        await entered.Task.WaitAsync(Timeout);
        f.Facts = null;
        resume.SetResult();
        var result = await pending.WaitAsync(Timeout);
        await Assert.That(result.Outcome).IsEqualTo(failure
            ? EventResourceAuthorityOutcome.Unavailable : EventResourceAuthorityOutcome.NotFound);
        await Assert.That(preparation.Disposed).IsTrue();
    }

    [Test]
    public async Task Transaction_replay_reloads_facts_but_does_not_repeat_external_work()
    {
        var f = new Fixture();
        f.UnitOfWork.ReplayReads = true;
        bool providerCompleted = false;
        bool preparationCompleted = false;
        f.OnProvider = (_, _) =>
        {
            if (providerCompleted) throw new InvalidOperationException("PDP replayed with transaction");
            providerCompleted = true;
            return Task.FromResult(EventResourceProviderDecision.Allow);
        };
        await using var preparation = new Preparation("generation-1");
        var result = await f.Service.AuthorizeAsync(f.Request, (_, _) =>
        {
            if (preparationCompleted) throw new InvalidOperationException("Preparation replayed with transaction");
            preparationCompleted = true;
            return Task.FromResult<IEventResourcePrivatePreparation>(preparation);
        });
        await using var lease = result.Lease;
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Allowed);
        await Assert.That(f.EvaluationTimes.SequenceEqual(new[] { Now, Now, Now, Now })).IsTrue();
        await Assert.That((await f.Service.CompleteHeadersAsync(lease!)).Preparation).IsEqualTo(preparation);
    }

    [Test]
    public async Task A_second_attempt_does_not_extend_the_original_deadline()
    {
        var f = new Fixture { Route = Remote() };
        await using var first = new Preparation("generation-1");
        await using var second = new Preparation("generation-1");
        bool prepared = false;
        var result = await f.Service.AuthorizeAsync(f.Request, (_, _) =>
        {
            if (prepared)
            {
                f.Clock.UtcNow = Now.AddMinutes(1);
                return Task.FromResult<IEventResourcePrivatePreparation>(second);
            }
            prepared = true;
            f.Route = f.Route! with { ActivationEpoch = 2 };
            f.Clock.UtcNow = Now.AddSeconds(59);
            return Task.FromResult<IEventResourcePrivatePreparation>(first);
        });
        await Assert.That(result.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Expired);
        await Assert.That(first.Disposed && second.Disposed).IsTrue();
    }

    [Test]
    public async Task Provider_allow_cannot_cross_payload_safety_or_parent_domain_ceilings()
    {
        var f = new Fixture();
        f.Facts = f.Capture(access: new(Tenant, Subject, false, f.Access.Parent, f.Access.Audience, false, f.Access.GovernancePolicy));
        var unsafePayload = await f.Service.AuthorizeAsync(f.Request, NeverPrepare);
        await Assert.That(unsafePayload.Outcome).IsEqualTo(EventResourceAuthorityOutcome.Forbidden);
        f.Facts = f.Capture(access: new(Tenant, Subject, false, f.Access.Parent with { EventEligible = false }, f.Access.Audience, true, f.Access.GovernancePolicy));
        var invisibleParent = await f.Service.AuthorizeAsync(f.Request, NeverPrepare);
        await Assert.That(invisibleParent.Outcome).IsEqualTo(EventResourceAuthorityOutcome.NotFound);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Disclosed_teaser_distinguishes_anonymous_authentication_from_known_subject_denial(bool authenticated)
    {
        var f = new Fixture(EventResourceAudienceKindEnum.Organizer);
        var subject = authenticated ? Subject : (Guid?)null;
        f.Request = f.Request with { SubjectUserId = subject };
        f.Resource.UpdateMetadata(new EventResourceMetadata { Title = "Private", PublicTitle = "Public",
            Kind = (EventResourceKindEnum)1, DisclosureMode = EventResourceDisclosureModeEnum.Teaser },
            f.Resource.ConcurrencyStamp, Subject, Now.UtcDateTime);
        f.Facts = f.Capture(access: new(Tenant, subject, false, f.Access.Parent, [], true, f.Access.GovernancePolicy));
        var result = await f.Service.AuthorizeAsync(f.Request, NeverPrepare);
        await Assert.That(result.Outcome).IsEqualTo(authenticated
            ? EventResourceAuthorityOutcome.Forbidden : EventResourceAuthorityOutcome.AuthenticationRequired);
    }

    private static Task<IEventResourcePrivatePreparation> NeverPrepare(EventResourceAuthorizationFacts facts, CancellationToken ct) =>
        throw new InvalidOperationException("Denied decisions must never prepare delivery");

    private static EventResourceManagementFacts Management(IEnumerable<EventResourcePermissionGrant> permissions,
        bool organizer = false, bool member = true, bool moderator = false) =>
        new(new(organizer), new(member), new(moderator), permissions, true, true);

    private static EventResourceProviderSnapshot Remote() => new(EventResourceProviderMode.Remote, "tenant", "1",
        "https://pdp.example", Tenant, 1, EventResourceProviderActivationStateEnum.Active);

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Fixture : IEventResourceAuthoritySnapshotReader,
        IEventResourceProviderSnapshotReader, IEventResourceAuthorizationProvider
    {
        public TestUnitOfWork UnitOfWork { get; } = new();
        public Clock Clock { get; } = new();
        public EventResource Resource { get; }
        public EventResourceAccessFacts Access { get; }
        public EventResourceAuthorizationFacts? Facts { get; set; }
        public EventResourceProviderSnapshot? Route { get; set; } = new(EventResourceProviderMode.Local, "tenant", "1");
        public EventResourceAuthorityRequest Request { get; set; } = new(Tenant, ResourceId, Subject, false, "download", Now.AddMinutes(1));
        public EventResourceAuthorityOrchestrator Service { get; }
        public List<EventResourceProviderInput> Inputs { get; } = [];
        public List<DateTimeOffset> EvaluationTimes { get; } = [];
        public Func<EventResourceProviderInput, CancellationToken, Task<EventResourceProviderDecision>>? OnProvider { get; set; }
        public Func<DateTimeOffset, CancellationToken, Task>? OnRead { get; set; }
        public bool RejectSingleCalls { get; set; }
        public Func<IReadOnlyList<EventResourceProviderInput>, CancellationToken,
            Task<IReadOnlyList<EventResourceProviderDecision>>>? OnBatchProvider { get; set; }

        public Fixture(EventResourceAudienceKindEnum audience = EventResourceAudienceKindEnum.Public,
            EventResourceAvailability? availability = null)
        {
            Resource = EventResource.CreateDraft(ResourceId, Tenant, EventId, null,
                new EventResourceMetadata { Title = "Resource", Kind = (EventResourceKindEnum)1,
                    DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly },
                EventResourceDeliveryTypeEnum.StoredFile, availability ?? EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(Tenant, EventId, ResourceId, audience)], Subject, Now.UtcDateTime);
            Resource.SetStoredFile(Guid.Parse("018f0000-0000-7000-8000-000000000105"), Resource.ConcurrencyStamp, Subject, Now.UtcDateTime);
            var parent = new EventResourceParentFacts(Tenant, EventId, null, EventStatusEnum.Published,
                false, true, null, false, new EventResourceScheduleFacts(Now, Now.AddHours(1), null, null));
            Access = new EventResourceAccessFacts(Tenant, Subject, false, parent,
                [new EventResourceAudienceFact { TenantId = Tenant, EventId = EventId, SubjectUserId = Subject,
                    Kind = audience, IsCurrent = true }], true, EventResourceGovernancePolicy.Default(long.MaxValue));
            Resource.Publish(parent, true, Resource.ConcurrencyStamp, Subject, Now.UtcDateTime);
            Facts = Capture();
            Service = new(UnitOfWork, this, this, this, Clock);
        }

        public EventResourceAuthorizationFacts Capture(string generation = "generation-1", EventResourceManagementFacts? management = null,
            EventResourceAccessFacts? access = null) => new(Resource, access ?? Access, management ?? new(
                new(false), new(true), new(false), [], true, true), generation);

        public EventResourceCapabilityAuthorizer NativeAuthorizer()
        {
            var user = Substitute.For<ICurrentUserService>();
            user.IsAuthenticated.Returns(true);
            user.UserId.Returns(Subject);
            var tenant = Substitute.For<ITenantContext>();
            tenant.TenantId.Returns(Tenant);
            return new(Service, user, tenant, Substitute.For<IMachinePrincipalAccessor>());
        }

        public async Task<EventResourceAuthorizationFacts?> ReadAsync(EventResourceAuthorityRequest request,
            DateTimeOffset evaluationUtc, CancellationToken cancellationToken)
        {
            if (RejectSingleCalls) throw new InvalidOperationException("Batch cannot issue per-resource reads.");
            if (!UnitOfWork.InTransaction) throw new InvalidOperationException("Authority read outside transaction");
            EvaluationTimes.Add(evaluationUtc);
            if (OnRead is not null) await OnRead(evaluationUtc, cancellationToken);
            return Facts;
        }
        public Task<EventResourceProviderSnapshot?> ReadAsync(Guid tenantId, CancellationToken cancellationToken)
        {
            if (!UnitOfWork.InTransaction) throw new InvalidOperationException("Route read outside transaction");
            return Task.FromResult<EventResourceProviderSnapshot?>(Route);
        }
        public Task<EventResourceProviderDecision> CheckAsync(EventResourceProviderInput input, CancellationToken cancellationToken)
        {
            if (RejectSingleCalls) throw new InvalidOperationException("Batch cannot issue per-resource provider calls.");
            if (UnitOfWork.InTransaction) throw new InvalidOperationException("PDP inside transaction");
            Inputs.Add(input);
            return OnProvider?.Invoke(input, cancellationToken) ?? Task.FromResult(EventResourceProviderDecision.Allow);
        }
        public async Task<IReadOnlyList<EventResourceAuthorizationFacts?>> ReadBatchAsync(
            IReadOnlyList<EventResourceAuthorityRequest> requests, DateTimeOffset evaluationUtc, CancellationToken cancellationToken)
        {
            if (!UnitOfWork.InTransaction) throw new InvalidOperationException("Authority read outside transaction");
            EvaluationTimes.Add(evaluationUtc);
            if (OnRead is not null) await OnRead(evaluationUtc, cancellationToken);
            return requests.Select(_ => Facts).ToArray();
        }
        public Task<IReadOnlyList<EventResourceProviderDecision>> CheckBatchAsync(
            IReadOnlyList<EventResourceProviderInput> inputs, CancellationToken cancellationToken)
        {
            if (UnitOfWork.InTransaction) throw new InvalidOperationException("PDP inside transaction");
            Inputs.AddRange(inputs);
            return OnBatchProvider?.Invoke(inputs, cancellationToken)
                ?? Task.FromResult<IReadOnlyList<EventResourceProviderDecision>>(
                    inputs.Select(_ => EventResourceProviderDecision.Allow).ToArray());
        }
    }

    private sealed class Preparation(string generation) : IEventResourcePrivatePreparation
    {
        public string AttachmentGeneration { get; } = generation;
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public int Samples { get; private set; }
        public override DateTimeOffset GetUtcNow() { Samples++; return UtcNow; }
    }

    private sealed class TestUnitOfWork : IUnitOfWork
    {
        public bool InTransaction { get; private set; }
        public int CommittedReads { get; private set; }
        public bool ReplayReads { get; set; }
        public async Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            if (InTransaction) throw new InvalidOperationException("Nested transaction");
            InTransaction = true;
            try
            {
                if (ReplayReads) await operation(ct);
                var result = await operation(ct);
                CommittedReads++;
                return result;
            }
            finally { InTransaction = false; }
        }
        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
