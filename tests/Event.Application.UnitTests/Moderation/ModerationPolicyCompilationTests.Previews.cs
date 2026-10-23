using System.Text.Json.Nodes;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Application.UnitTests.Moderation;

public sealed partial class ModerationPolicyCompilationTests
{
    [Test]
    public async Task Profile_previews_preserve_every_selected_profile_mode_and_complete_requirement_set()
    {
        var state = State(Enum.GetValues<ModerationReferenceProfileId>()) with
        {
            Modes = [ModerationNativeMode.RemoteStrict, ModerationNativeMode.Embedded]
        };
        var result = Compile(state);
        var draft = result.Draft!;
        await Assert.That(result.Previews.Length).IsEqualTo(54);
        foreach (var preview in result.Previews)
        {
            var rule = preview.Prerequisites.Rule;
            var mode = preview.Prerequisites.Mode;
            await Assert.That(preview.Publication).IsEqualTo(draft.Publication);
            await Assert.That(preview.Effects.SequenceEqual(
                draft.Effects.Where(item => item.Rule == rule && item.Mode == mode))).IsTrue();
            await Assert.That(preview.Consequences.SequenceEqual(
                draft.Consequences.Where(item => item.Rule == rule && item.Mode == mode))).IsTrue();
            await Assert.That(preview.Consumers.SequenceEqual(
                draft.Consumers.Where(item => item.Binding.Rule == rule && item.Binding.Mode == mode))).IsTrue();
            await Assert.That(preview.Prerequisites.ActivationGates.IsEmpty).IsFalse();
            await Assert.That(preview.Diagnostics.All(item =>
                (item.Rule is null || item.Rule == rule) && (item.Mode is null || item.Mode == mode))).IsTrue();
        }
    }

    [Test]
    public async Task Implemented_source_preview_keeps_native_qualification_unresolved()
    {
        var registry = SourceImplementedInventory();
        var state = State(ModerationReferenceProfileId.CFG05) with
        {
            InventoryFingerprint = registry.Fingerprint, RequestedState = ModerationPolicyRequestedState.Active
        };
        var result = ModerationPolicyProposedStateCompiler.Compile(state, Context(), registry);
        var preview = result.Previews.Single();
        await Assert.That(preview.Disposition)
            .IsEqualTo(ModerationPolicyPreviewDisposition.RequiresNativeQualification);
        await Assert.That(preview.Diagnostics.Any(item =>
            item.Kind == ModerationPolicyDiagnosticKind.NativeQualificationRequired)).IsTrue();
        await Assert.That(preview.Prerequisites.NativePrerequisites
            .Contains(ModerationReferencePrerequisite.ActualPublicationReview)).IsTrue();
    }

    [Test]
    public async Task Unsupported_remote_mode_does_not_label_the_embedded_preview_as_unsupported()
    {
        var source = JsonNode.Parse(ModerationCapabilityRegistry.ReadInventory())!.AsObject();
        foreach (var consumer in source["consumers"]!.AsArray())
            consumer!["supportState"] = nameof(ModerationSourceSupport.ImplementedUnverified);
        foreach (var mode in source["modes"]!.AsArray())
            mode!["supportState"] = mode["mode"]!.GetValue<string>() == nameof(ModerationNativeMode.RemoteStrict)
                ? nameof(ModerationSourceSupport.Unimplemented)
                : nameof(ModerationSourceSupport.ImplementedUnverified);
        var registry = ModerationCapabilityRegistry.Parse(source.ToJsonString());
        var state = State(ModerationReferenceProfileId.CFG08) with
        {
            InventoryFingerprint = registry.Fingerprint,
            Modes = [ModerationNativeMode.RemoteStrict, ModerationNativeMode.Embedded]
        };
        var result = ModerationPolicyProposedStateCompiler.Compile(state, Context(), registry);
        var remote = result.Previews.Single(item => item.Prerequisites.Mode == ModerationNativeMode.RemoteStrict);
        var embedded = result.Previews.Single(item => item.Prerequisites.Mode == ModerationNativeMode.Embedded);
        await Assert.That(remote.Disposition).IsEqualTo(ModerationPolicyPreviewDisposition.Blocked);
        await Assert.That(remote.Diagnostics.Any(item => item.Kind == ModerationPolicyDiagnosticKind.UnsupportedMode
            && item.Mode == ModerationNativeMode.RemoteStrict)).IsTrue();
        await Assert.That(embedded.Disposition)
            .IsEqualTo(ModerationPolicyPreviewDisposition.RequiresNativeQualification);
        await Assert.That(embedded.Diagnostics.Any(item =>
            item.Kind == ModerationPolicyDiagnosticKind.UnsupportedMode)).IsFalse();
    }

    [Test]
    public async Task Retired_profile_definitions_remain_in_the_draft_without_an_activation_preview()
    {
        var state = State(ModerationReferenceProfileId.CFG05);
        var result = Compile(state with { SupportedProfiles = [] });
        await Assert.That(result.Draft!.Profiles.Length).IsEqualTo(1);
        await Assert.That(result.Previews.IsEmpty).IsTrue();
    }

    [Test]
    public async Task Invalid_compilation_cannot_return_partial_profile_previews()
    {
        var state = State(ModerationReferenceProfileId.CFG05);
        var result = Compile(state with { InventoryFingerprint = "stale" });
        await Assert.That(result.Draft).IsNull();
        await Assert.That(result.Previews.IsEmpty).IsTrue();
    }
}
