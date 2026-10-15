namespace Event.Domain.UnitTests.Moderation;

public sealed class ModerationNoticeSourceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Inert_capture_cannot_create_an_ungoverned_subject_notice()
    {
        var effect = Effect();
        var source = ModerationNoticeSource.Capture(effect, null, "{\"Version\":1}", null);
        await Assert.That(() => ModerationNotice.Create(Guid.CreateVersion7(), source,
            ModerationNoticeAudience.Subject, effect.SubjectKind, effect.SubjectId, null,
            ModerationNoticeDeliveryDisposition.NoTenantProjection)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("<script>private</script>")]
    [Arguments("text\u0000text")]
    public async Task Approved_text_rejects_empty_markup_and_control_characters(string value)
    {
        await Assert.That(() => new ModerationNoticeSafeText(value)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Capture_keeps_original_payload_when_effect_later_completes()
    {
        var effect = Effect();
        var source = ModerationNoticeSource.Capture(effect, null, "{\"Version\":1}", Now.AddYears(1));
        var leaseId = Guid.CreateVersion7();
        effect.Claim(leaseId, 1, Now, Now.AddMinutes(5));
        effect.Complete(leaseId, 2, Now.AddMinutes(1));
        await Assert.That(source.EffectId).IsEqualTo(effect.Id);
        await Assert.That(source.CapturedAt).IsEqualTo(Now);
        await Assert.That(source.SnapshotJson).IsEqualTo("{\"Version\":1}");
        await Assert.That(source.SnapshotHash.Length).IsEqualTo(64);
        await Assert.That(source.Matches(effect)).IsTrue();
    }

    [Test]
    public async Task Same_effect_identity_cannot_accept_a_different_decision_revision()
    {
        var effect = Effect();
        var source = ModerationNoticeSource.Capture(effect, null, "{\"Version\":1}", Now.AddYears(1));
        await Assert.That(source.Matches(Effect(effect.Id, 2))).IsFalse();
    }

    [Test]
    [Arguments("operation")]
    [Arguments("policy")]
    [Arguments("qualification")]
    [Arguments("kind")]
    [Arguments("time")]
    public async Task Original_capture_cannot_be_rebound_to_newer_authority_or_another_transition(string changed)
    {
        var effect = Effect();
        var source = ModerationNoticeSource.Capture(effect, null, "{\"OriginalReason\":\"retained-reason\"}", Now.AddYears(1));
        var other = ModerationEffectIntent.Create(effect.Id,
            changed == "operation" ? Id(20) : effect.OperationId,
            effect.DecisionId, effect.DecisionRevision, null, null,
            effect.TrustDomainId, effect.ScopeKind, effect.ScopeId, effect.SubjectKind, effect.SubjectId,
            changed == "kind" ? 2 : effect.OperationKind,
            changed == "policy" ? 2 : effect.PolicyRevision,
            changed == "qualification" ? 2 : effect.QualificationRevision,
            changed == "time" ? Now.AddMinutes(1) : Now, null, null);
        await Assert.That(source.Matches(other)).IsFalse();
        await Assert.That(source.Matches(effect)).IsTrue();
        await Assert.That(source.SnapshotJson).IsEqualTo("{\"OriginalReason\":\"retained-reason\"}");
        await Assert.That(effect.CompletedAt).IsNull();
    }

    [Test]
    public async Task Capturing_a_notice_does_not_complete_the_effect()
    {
        var effect = Effect();
        var source = ModerationNoticeSource.Capture(effect, null, "{\"Version\":1}", Now.AddYears(1));
        var notice = ModerationNotice.Create(Guid.CreateVersion7(), source, ModerationNoticeAudience.Subject,
            effect.SubjectKind, effect.SubjectId, null, ModerationNoticeDeliveryDisposition.NoTenantProjection);
        await Assert.That(effect.CompletedAt is null).IsTrue();
        await Assert.That(notice.EffectId).IsEqualTo(effect.Id);
        await Assert.That(notice.NativeIntentId is null).IsTrue();
    }

    [Test]
    public async Task Subject_notice_cannot_inherit_another_persons_identity()
    {
        var source = ModerationNoticeSource.Capture(Effect(), null, "{\"Version\":1}", Now.AddYears(1));
        await Assert.That(() => ModerationNotice.Create(Guid.CreateVersion7(), source,
            ModerationNoticeAudience.Subject, 1, Guid.CreateVersion7(), null,
            ModerationNoticeDeliveryDisposition.NoTenantProjection)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Native_projection_requires_a_real_tenant_and_human_recipient()
    {
        var source = ModerationNoticeSource.Capture(Effect(), null, "{\"Version\":1}", Now.AddYears(1));
        await Assert.That(() => ModerationNotice.Create(Guid.CreateVersion7(), source,
            ModerationNoticeAudience.Subject, source.SubjectKind, source.SubjectId, Guid.CreateVersion7(),
            ModerationNoticeDeliveryDisposition.NativePortalEmailUnavailable)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Diagnostic_formatting_does_not_print_safe_content()
    {
        var text = new ModerationNoticeSafeText("An individually approved explanation.");
        var source = ModerationNoticeSource.Capture(Effect(), null, "{\"Version\":1}", Now.AddYears(1));
        await Assert.That(text.ToString()).IsEqualTo(nameof(ModerationNoticeSafeText));
        await Assert.That(source.ToString()).IsEqualTo(nameof(ModerationNoticeSource));
    }

    private static ModerationEffectIntent Effect(Guid? id = null, long revision = 1) =>
        ModerationEffectIntent.Create(id ?? Guid.CreateVersion7(), Id(1), Id(2), revision,
            null, null, Id(3), 3, Id(4), 1, Id(5), 1, 1, 1, Now, null, null);

    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");
}
