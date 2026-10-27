using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

/// <summary>
/// Exercises the production inventory parser and evidence matcher, not native consumer conformance.
/// Synthetic passing observations below model trusted participant inputs; they certify no deployed owner.
/// </summary>
public sealed class ModerationCapabilityRegistryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 14, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Resource_read_set_closes_management_payload_upload_and_derived_aliases()
    {
        var registry = ModerationCapabilityRegistry.Parse(InventoryJson());
        string[] expected =
        [
            "native.operations", "native.machine", "control.lifecycle", "queued.moderation",
            "resource.audience.detail", "resource.audience.page", "resource.audience.actions",
            "resource.management.detail", "resource.management.list", "resource.management.audit",
            "resource.management.export", "resource.payload", "resource.redirect",
            "resource.upload.reserve", "resource.upload.finalize", "resource.upload.cancel",
            "resource.upload.replay", "storage.generic", "discovery.public", "calendar.export",
            "queued.notification", "queued.federation", "queued.webhook"
        ];

        await Assert.That(registry.RequiredConsumers(ModerationCapability.ReadResource, ModerationNativeMode.Embedded)
            .ToHashSet(StringComparer.Ordinal).SetEquals(expected)).IsTrue();
        await Assert.That(registry.RequiredConsumers(ModerationCapability.ReadResource, ModerationNativeMode.RemoteStrict)
            .ToHashSet(StringComparer.Ordinal).SetEquals([.. expected, "remote.receiver"])).IsTrue();
        await Assert.That(registry.EffectClosures.Count).IsEqualTo(12);
        foreach (var effect in Enum.GetValues<ModerationCapability>())
            foreach (var mode in Enum.GetValues<ModerationNativeMode>())
                await Assert.That(registry.RequiredConsumers(effect, mode).IsEmpty).IsFalse();
    }

    [Test]
    public async Task Unsupported_owners_remain_required_and_ordinary_erasure_does_not_require_recognition()
    {
        var registry = ModerationCapabilityRegistry.Parse(InventoryJson());
        await Assert.That(registry.Consumers["registration.settlement"].SupportState)
            .IsEqualTo(ModerationSourceSupport.Unimplemented);
        await Assert.That(registry.RequiredConsumers(ModerationCapability.Participate, ModerationNativeMode.Embedded)
            .Contains("registration.settlement")).IsTrue();
        await Assert.That(registry.Consumers["queued.moderation"].SupportState)
            .IsEqualTo(ModerationSourceSupport.Unimplemented);
        await Assert.That(registry.RequiredConsumers(ModerationCapability.Participate, ModerationNativeMode.Embedded)
            .Contains("queued.moderation")).IsTrue();
        await Assert.That(registry.RequiredConsumers(ModerationCapability.Enrollment, ModerationNativeMode.Embedded)
            .Contains("identity.continuity")).IsTrue();
        await Assert.That(registry.RequiredConsumers(ModerationCapability.PrivacyErasure, ModerationNativeMode.Embedded)
            .ToHashSet(StringComparer.Ordinal).SetEquals(["control.erasure"])).IsTrue();
        await Assert.That(registry.Modes[ModerationNativeMode.RemoteStrict].SupportState)
            .IsEqualTo(ModerationSourceSupport.Unimplemented);
    }

    [Test]
    public async Task Reviewed_scheduling_exclusion_is_not_a_supported_or_required_consumer()
    {
        var registry = ModerationCapabilityRegistry.Parse(InventoryJson());
        await Assert.That(registry.Consumers.ContainsKey("publication.scheduled")).IsFalse();
        foreach (var capability in Enum.GetValues<ModerationCapability>())
            foreach (var mode in Enum.GetValues<ModerationNativeMode>())
                await Assert.That(registry.RequiredConsumers(capability, mode)
                    .Contains("publication.scheduled")).IsFalse();
        await Assert.That(registry.RequiredConsumers(ModerationCapability.Publish, ModerationNativeMode.Embedded)
            .Contains("queued.moderation")).IsTrue();
    }

    [Test]
    [Arguments("unknown-field")]
    [Arguments("missing-terminal")]
    [Arguments("empty-terminal")]
    [Arguments("unknown-support")]
    [Arguments("numeric-enum")]
    [Arguments("unknown-effect")]
    [Arguments("duplicate-consumer")]
    [Arguments("unknown-required")]
    [Arguments("duplicate-required")]
    [Arguments("empty-required")]
    [Arguments("missing-effect")]
    [Arguments("missing-global-mode")]
    [Arguments("missing-effect-mode")]
    [Arguments("duplicate-effect")]
    [Arguments("duplicate-effect-mode")]
    [Arguments("orphan-consumer")]
    [Arguments("unknown-format")]
    [Arguments("zero-revision")]
    [Arguments("null-source-path")]
    [Arguments("nested-unknown-field")]
    [Arguments("noncanonical-id")]
    [Arguments("noncanonical-mode")]
    [Arguments("string-format")]
    [Arguments("empty-aliases")]
    public async Task Malformed_inventory_cannot_shrink_or_extend_the_finite_contract(string mutation)
    {
        var document = JsonNode.Parse(InventoryJson())!.AsObject();
        var consumers = document["consumers"]!.AsArray();
        var consumer = consumers[0]!.AsObject();
        var effects = document["effects"]!.AsArray();
        var effect = effects[0]!.AsObject();
        var modes = effect["modes"]!.AsArray();
        var required = modes[0]!["requiredConsumers"]!.AsArray();
        switch (mutation)
        {
            case "unknown-field": document["plugins"] = new JsonArray(); break;
            case "missing-terminal": consumer.Remove("terminalCheckpoint"); break;
            case "empty-terminal": consumer["terminalCheckpoint"] = ""; break;
            case "unknown-support": consumer["supportState"] = "Ready"; break;
            case "numeric-enum": consumer["freshnessClass"] = "1"; break;
            case "unknown-effect": effect["capability"] = "Chat"; break;
            case "duplicate-consumer": consumers.Add(consumer.DeepClone()); break;
            case "unknown-required": required.Add("not.in.inventory"); break;
            case "duplicate-required": required.Add(required[0]!.DeepClone()); break;
            case "empty-required": required.Clear(); break;
            case "missing-effect": effects.RemoveAt(0); break;
            case "missing-global-mode": document["modes"]!.AsArray().RemoveAt(0); break;
            case "missing-effect-mode": modes.RemoveAt(0); break;
            case "duplicate-effect": effects.Add(effect.DeepClone()); break;
            case "duplicate-effect-mode": modes.Add(modes[0]!.DeepClone()); break;
            case "orphan-consumer":
                var orphan = consumer.DeepClone();
                orphan["id"] = "unreferenced.consumer";
                consumers.Add(orphan);
                break;
            case "unknown-format": document["formatVersion"] = 2; break;
            case "zero-revision": document["revision"] = 0; break;
            case "null-source-path": consumer["sourcePaths"] = null; break;
            case "nested-unknown-field": modes[0]!["allowed"] = true; break;
            case "noncanonical-id": consumer["id"] = "Native.Operations"; break;
            case "noncanonical-mode": modes[0]!["mode"] = "embedded"; break;
            case "string-format": document["formatVersion"] = "1"; break;
            case "empty-aliases": consumer["aliases"] = new JsonArray(); break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        await Assert.That(() => ModerationCapabilityRegistry.Parse(document.ToJsonString())).Throws<JsonException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Duplicate_object_fields_are_rejected_instead_of_last_value_winning(bool nested)
    {
        string json = InventoryJson();
        string duplicate = nested
            ? json.Replace("\"terminalCheckpoint\":", "\"entryCheckpoint\":\"duplicate\",\"terminalCheckpoint\":",
                StringComparison.Ordinal)
            : "{\"formatVersion\":1," + json[(json.IndexOf('{') + 1)..];

        await Assert.That(() => ModerationCapabilityRegistry.Parse(duplicate)).Throws<JsonException>();
    }

    [Test]
    public async Task Declarations_and_source_observations_alone_are_not_runtime_conformance()
    {
        var fixture = Evidence();
        var match = fixture.Registry.Match(fixture.Binding, [], fixture.Sources, Now);

        await Assert.That(match.Matches).IsFalse();
        await Assert.That(match.Registrations.IsEmpty).IsTrue();
        await Assert.That(match.Issues.Any(issue => issue.Kind == ModerationInventoryMismatch.MissingConsumer)).IsTrue();
    }

    [Test]
    [Arguments(ModerationNativeMode.Embedded)]
    [Arguments(ModerationNativeMode.RemoteStrict)]
    public async Task Exact_current_synthetic_evidence_matches_without_becoming_an_activation_permit(ModerationNativeMode mode)
    {
        var fixture = Evidence(mode);
        var match = fixture.Registry.Match(fixture.Binding, fixture.Registrations.Reverse(),
            fixture.Sources.Reverse(), Now);

        await Assert.That(match.Matches).IsTrue();
        await Assert.That(match.RequiredConsumers.Length).IsEqualTo(mode == ModerationNativeMode.Embedded ? 23 : 24);
        await Assert.That(match.Registrations.Length).IsEqualTo(match.RequiredConsumers.Length);
    }

    [Test]
    [Arguments("missing", ModerationInventoryMismatch.MissingConsumer)]
    [Arguments("extra-known", ModerationInventoryMismatch.ExtraConsumer)]
    [Arguments("unknown", ModerationInventoryMismatch.UnknownConsumer)]
    [Arguments("duplicate", ModerationInventoryMismatch.DuplicateConsumer)]
    [Arguments("missing-source", ModerationInventoryMismatch.MissingCurrentSource)]
    [Arguments("extra-source", ModerationInventoryMismatch.ExtraCurrentSource)]
    [Arguments("duplicate-source", ModerationInventoryMismatch.DuplicateCurrentSource)]
    [Arguments("reused-source", ModerationInventoryMismatch.ReusedSourceIdentity)]
    [Arguments("old-evidence", ModerationInventoryMismatch.StaleObservation)]
    [Arguments("old-source", ModerationInventoryMismatch.StaleObservation)]
    [Arguments("failed", ModerationInventoryMismatch.ConformanceNotPassed)]
    [Arguments("not-run", ModerationInventoryMismatch.ConformanceNotPassed)]
    [Arguments("unknown-conformance", ModerationInventoryMismatch.ConformanceNotPassed)]
    [Arguments("tenant", ModerationInventoryMismatch.BindingMismatch)]
    [Arguments("trust-domain", ModerationInventoryMismatch.BindingMismatch)]
    [Arguments("policy", ModerationInventoryMismatch.BindingMismatch)]
    [Arguments("revision", ModerationInventoryMismatch.BindingMismatch)]
    [Arguments("publication", ModerationInventoryMismatch.BindingMismatch)]
    [Arguments("rule", ModerationInventoryMismatch.BindingMismatch)]
    [Arguments("mode", ModerationInventoryMismatch.BindingMismatch)]
    [Arguments("effect", ModerationInventoryMismatch.BindingMismatch)]
    [Arguments("source-binding", ModerationInventoryMismatch.BindingMismatch)]
    [Arguments("fingerprint", ModerationInventoryMismatch.InventoryMismatch)]
    [Arguments("entry", ModerationInventoryMismatch.ContractMismatch)]
    [Arguments("terminal", ModerationInventoryMismatch.ContractMismatch)]
    [Arguments("subject", ModerationInventoryMismatch.ContractMismatch)]
    [Arguments("target", ModerationInventoryMismatch.ContractMismatch)]
    [Arguments("authorization", ModerationInventoryMismatch.ContractMismatch)]
    [Arguments("fence", ModerationInventoryMismatch.ContractMismatch)]
    [Arguments("freshness", ModerationInventoryMismatch.ContractMismatch)]
    [Arguments("test", ModerationInventoryMismatch.ContractMismatch)]
    [Arguments("packet", ModerationInventoryMismatch.ContractMismatch)]
    [Arguments("source-identity", ModerationInventoryMismatch.SourceMismatch)]
    [Arguments("source-revision", ModerationInventoryMismatch.SourceMismatch)]
    [Arguments("source-generation", ModerationInventoryMismatch.SourceMismatch)]
    [Arguments("empty-source", ModerationInventoryMismatch.InvalidSource)]
    [Arguments("invalid-generation", ModerationInventoryMismatch.InvalidSource)]
    [Arguments("expired-source", ModerationInventoryMismatch.ExpiredSourceAuthority)]
    [Arguments("unbounded-source", ModerationInventoryMismatch.ExpiredSourceAuthority)]
    [Arguments("non-utc-source", ModerationInventoryMismatch.ExpiredSourceAuthority)]
    [Arguments("expired-measurement", ModerationInventoryMismatch.ExpiredConformanceAuthority)]
    [Arguments("unbounded-measurement", ModerationInventoryMismatch.ExpiredConformanceAuthority)]
    [Arguments("non-utc-measurement", ModerationInventoryMismatch.ExpiredConformanceAuthority)]
    public async Task One_bad_fact_rejects_the_complete_evidence_set(string mutation, ModerationInventoryMismatch expected)
    {
        var fixture = Evidence();
        var registrations = fixture.Registrations.ToList();
        var sources = fixture.Sources.ToList();
        var first = registrations[0];
        var binding = first.Binding;
        registrations[0] = mutation switch
        {
            "old-evidence" => first with { VerifiedAt = Now.AddTicks(-1) },
            "expired-measurement" => first with { ValidUntil = Now },
            "unbounded-measurement" => first with { ValidUntil = DateTimeOffset.MaxValue },
            "non-utc-measurement" => first with { ValidUntil = Now.AddHours(1).ToOffset(TimeSpan.FromHours(1)) },
            "failed" => first with { Conformance = ModerationNativeConformance.Failed },
            "not-run" => first with { Conformance = ModerationNativeConformance.NotRun },
            "unknown-conformance" => first with { Conformance = (ModerationNativeConformance)99 },
            "tenant" => first with { Binding = binding with { Jurisdiction = Tenant() } },
            "trust-domain" => first with { Binding = binding with { Jurisdiction = DifferentTrustDomain(binding.Jurisdiction) } },
            "policy" => first with { Binding = binding with { PolicyId = Guid.CreateVersion7() } },
            "revision" => first with { Binding = binding with { PolicyRevision = 2 } },
            "publication" => first with { Binding = binding with { PublicationId = Guid.CreateVersion7() } },
            "rule" => first with { Binding = binding with { Rule = ModerationRuleReference.Create(binding.Jurisdiction.Id, "conduct", 2) } },
            "mode" => first with { Binding = binding with { Mode = ModerationNativeMode.RemoteStrict } },
            "effect" => first with { Binding = binding with { Capability = ModerationCapability.Upload } },
            "fingerprint" => first with { InventoryFingerprint = "different-inventory" },
            "entry" => first with { Contract = first.Contract with { EntryCheckpoint = "other" } },
            "terminal" => first with { Contract = first.Contract with { TerminalCheckpoint = "" } },
            "subject" => first with { Contract = first.Contract with { SubjectResolver = "caller-body" } },
            "target" => first with { Contract = first.Contract with { TargetResolver = "unqualified-id" } },
            "authorization" => first with { Contract = first.Contract with { AuthorizationOwner = "admin-shortcut" } },
            "fence" => first with { Contract = first.Contract with { WriterFence = "none" } },
            "freshness" => first with { Contract = first.Contract with { FreshnessClass = ModerationConsumerFreshness.PublicSnapshot } },
            "test" => first with { Contract = first.Contract with { ConformanceTest = "unrelated.test" } },
            "packet" => first with { Contract = first.Contract with { OwningPacket = "other" } },
            "source-identity" => first with { SourceId = Guid.CreateVersion7() },
            "source-revision" => first with { SourceRevision = 2 },
            "source-generation" => first with { WriterGeneration = "generation-2" },
            "empty-source" => first with { SourceId = Guid.Empty },
            "invalid-generation" => first with { WriterGeneration = "" },
            _ => first
        };
        switch (mutation)
        {
            case "missing": registrations.RemoveAt(0); break;
            case "extra-known":
                registrations.Add(first with { Contract = fixture.Registry.Consumers["remote.receiver"].Contract });
                break;
            case "unknown": registrations.Add(first with { Contract = first.Contract with { Id = "unknown.consumer" } }); break;
            case "duplicate": registrations.Add(first); break;
            case "missing-source": sources.RemoveAt(0); break;
            case "extra-source": sources.Add(sources[0] with { ConsumerId = "remote.receiver", SourceId = Guid.CreateVersion7() }); break;
            case "duplicate-source": sources.Add(sources[0]); break;
            case "reused-source": sources[1] = sources[1] with { SourceId = sources[0].SourceId }; break;
            case "old-source": sources[0] = sources[0] with { ObservedAt = Now.AddTicks(-1) }; break;
            case "expired-source": sources[0] = sources[0] with { ValidUntil = Now }; break;
            case "unbounded-source": sources[0] = sources[0] with { ValidUntil = DateTimeOffset.MaxValue }; break;
            case "non-utc-source": sources[0] = sources[0] with { ValidUntil = Now.AddHours(1).ToOffset(TimeSpan.FromHours(1)) }; break;
            case "source-binding": sources[0] = sources[0] with { Binding = binding with { PolicyRevision = 2 } }; break;
        }

        var match = fixture.Registry.Match(binding, registrations, sources, Now);

        await Assert.That(match.Matches).IsFalse();
        await Assert.That(match.Registrations.IsEmpty).IsTrue();
        await Assert.That(match.Issues.Any(issue => issue.Kind == expected)).IsTrue();
    }

    [Test]
    public async Task New_observation_time_cannot_reuse_previous_matching_facts()
    {
        var fixture = Evidence();
        var match = fixture.Registry.Match(fixture.Binding, fixture.Registrations, fixture.Sources, Now.AddTicks(1));

        await Assert.That(match.Matches).IsFalse();
        await Assert.That(match.Issues.All(issue => issue.Kind == ModerationInventoryMismatch.StaleObservation)).IsTrue();
    }

    [Test]
    public async Task Inventory_revision_change_invalidates_old_registration_receipts()
    {
        var fixture = Evidence();
        var next = JsonNode.Parse(InventoryJson())!;
        next["revision"] = 2;
        var nextRegistry = ModerationCapabilityRegistry.Parse(next.ToJsonString());

        var match = nextRegistry.Match(fixture.Binding, fixture.Registrations, fixture.Sources, Now);

        await Assert.That(match.Matches).IsFalse();
        await Assert.That(match.Issues.All(issue => issue.Kind == ModerationInventoryMismatch.InventoryMismatch)).IsTrue();
    }

    [Test]
    public async Task Returned_evidence_is_a_snapshot_not_a_mutable_participant_list()
    {
        var fixture = Evidence();
        var registrations = fixture.Registrations.ToList();
        var sources = fixture.Sources.ToList();
        var match = fixture.Registry.Match(fixture.Binding, registrations, sources, Now);
        registrations.Clear();
        sources.Clear();

        await Assert.That(match.Matches).IsTrue();
        await Assert.That(match.Registrations.Length).IsEqualTo(23);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task Malformed_publication_coordinates_never_match(int slot)
    {
        var fixture = Evidence();
        var binding = slot switch
        {
            0 => fixture.Binding with { PolicyId = Guid.Empty },
            1 => fixture.Binding with { PolicyRevision = 0 },
            2 => fixture.Binding with { Mode = (ModerationNativeMode)99 },
            3 => fixture.Binding with { PublicationId = Guid.NewGuid() },
            4 => fixture.Binding with { Rule = ModerationRuleReference.Create(Guid.CreateVersion7(), "conduct", 1) },
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };
        var match = fixture.Registry.Match(binding, fixture.Registrations, fixture.Sources, Now);

        await Assert.That(match.Matches).IsFalse();
        await Assert.That(match.Issues.Single().Kind).IsEqualTo(ModerationInventoryMismatch.InvalidBinding);
    }

    private static EvidenceFixture Evidence(ModerationNativeMode mode = ModerationNativeMode.Embedded)
    {
        var registry = ModerationCapabilityRegistry.Parse(InventoryJson());
        var scope = Tenant();
        var binding = new ModerationActivationBinding(Guid.CreateVersion7(), 1, Guid.CreateVersion7(), scope,
            ModerationRuleReference.Create(scope.Id, "conduct", 1), ModerationCapability.ReadResource, mode);
        var registrations = registry.RequiredConsumers(binding.Capability, mode)
            .Select(id => new ModerationCapabilityRegistration(binding, registry.Fingerprint,
                registry.Consumers[id].Contract, Guid.CreateVersion7(), 1, "generation-1",
                ModerationNativeConformance.Passed, Now, Now.AddHours(1))).ToImmutableArray();
        var sources = registrations.Select(registration => new ModerationCapabilitySourceObservation(
            binding, registration.Contract.Id, registration.SourceId, 1, "generation-1", Now, Now.AddHours(1))).ToImmutableArray();
        return new(registry, binding, registrations, sources);
    }

    private static AuthorityScope Tenant() => AuthorityScope.Tenant(AuthorityScope.Instance(
        AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
        Guid.CreateVersion7()), Guid.CreateVersion7());

    private static AuthorityScope DifferentTrustDomain(AuthorityScope tenant) => AuthorityScope.Tenant(
        AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()),
            tenant.Parent!.Parent!.Id), tenant.Parent.Id), tenant.Id);

    private static string InventoryJson() => ModerationCapabilityRegistry.ReadInventory();

    private sealed record EvidenceFixture(
        ModerationCapabilityRegistry Registry,
        ModerationActivationBinding Binding,
        ImmutableArray<ModerationCapabilityRegistration> Registrations,
        ImmutableArray<ModerationCapabilitySourceObservation> Sources);
}
