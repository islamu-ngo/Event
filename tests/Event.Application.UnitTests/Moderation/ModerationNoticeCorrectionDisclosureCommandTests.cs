using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

/// <summary>Pure authorable-shape cases, not proof of pending-source authenticity, recipient authority or delivery.</summary>
public sealed class ModerationNoticeCorrectionDisclosureCommandTests
{
    private static readonly AuthorityScope Tenant = AuthorityScope.Tenant(
        AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Id(30)), Id(31)), Id(32)), Id(33));
    private static readonly DateTimeOffset Until = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private const string SafeText = "Independently approved correction text must not appear in diagnostics.";

    [Test]
    [Arguments(ModerationScopeKind.Tenant)]
    [Arguments(ModerationScopeKind.Event)]
    [Arguments(ModerationScopeKind.Resource)]
    public async Task Approval_preserves_exact_intent_and_pending_hashes_without_selecting_an_audience(ModerationScopeKind kind)
    {
        var command = Create() with
        {
            Target = kind switch
            {
                ModerationScopeKind.Event => AuthorityScope.Event(Tenant, Id(34)),
                ModerationScopeKind.Resource => AuthorityScope.Resource(Tenant, Id(34)),
                _ => Tenant
            }
        };
        await Assert.That((await Validate(command)).IsValid).IsTrue();
        await Assert.That(command.ToMutation()).IsEqualTo(new ApproveModerationNoticeCorrectionDisclosureSource
        {
            OperationId = Id(1),
            Target = command.Target,
            ExpectedQualificationRevision = 13,
            Intent = command.Intent,
            ExpectedPendingSnapshotHash = new string('a', 64),
            ExpectedPendingEffectBindingHash = new string('b', 64)
        });
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Operation_identity_requires_uuidv7_with_the_rfc_variant(int kind)
    {
        var operationId = kind switch
        {
            0 => Guid.Empty,
            1 => Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab"),
            _ => Guid.Parse("019a0000-0000-7000-7000-000000000001")
        };
        await Assert.That((await Validate(Create() with { OperationId = operationId })).IsValid).IsFalse();
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    [Arguments(long.MaxValue)]
    public async Task Qualification_epoch_must_be_positive_and_advanceable(long revision)
    {
        await Assert.That((await Validate(Create() with { ExpectedQualificationRevision = revision })).IsValid).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task Target_requires_a_native_tenant_or_its_direct_event_or_resource(int kind)
    {
        var community = AuthorityScope.Community(Tenant, Id(35));
        var target = kind switch
        {
            0 => null,
            1 => AuthorityScope.TrustDomain(Id(30)),
            2 => Tenant.Parent!.Parent,
            3 => Tenant.Parent,
            4 => community,
            _ => AuthorityScope.Resource(community, Id(36))
        };
        await Assert.That((await Validate(Create() with { Target = target! })).IsValid).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    public async Task Both_pending_hashes_require_exact_lowercase_ascii_sha256_shape(int kind)
    {
        var hash = kind switch
        {
            0 => null,
            1 => string.Empty,
            2 => new string('a', 63),
            3 => new string('a', 65),
            4 => new string('A', 64),
            5 => new string('g', 64),
            _ => new string('a', 63) + "\n"
        };
        var command = Create();
        await Assert.That((await Validate(command with { ExpectedPendingSnapshotHash = hash! })).IsValid).IsFalse();
        await Assert.That((await Validate(command with { ExpectedPendingEffectBindingHash = hash! })).IsValid).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    public async Task Correction_approval_delegates_full_intent_validation_without_revealing_text(int kind)
    {
        var command = Create();
        var intent = command.Intent;
        var invalid = kind switch
        {
            0 => null,
            1 => intent with { ApprovalId = Guid.Empty },
            2 => intent with { EffectId = Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab") },
            3 => intent with { ExpectedDecisionRevision = 1 },
            4 => intent with { ExpectedCaseRevision = 0 },
            5 => intent with { ExpectedPolicyRevision = long.MaxValue },
            6 => intent with { SelectionDigest = new string('A', 64) },
            7 => intent with { RuleMeaning = null! },
            8 => intent with { Facts = default },
            9 => intent with { Facts = [null!] },
            10 => intent with { ValidUntil = Until.ToOffset(TimeSpan.FromHours(1)) },
            _ => intent with { RetainUntil = DateTimeOffset.MaxValue }
        };
        var validation = await Validate(command with { Intent = invalid! });
        await Assert.That(validation.IsValid).IsFalse();
        foreach (var error in validation.Errors)
            await Assert.That(error.ErrorMessage.Contains(SafeText, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Original_pending_audience_cannot_be_reselected_by_correction_text(bool useDefault)
    {
        var command = Create();
        var representatives = useDefault ? default(ImmutableArray<ModerationNoticeContentRepresentative>)
            : [new ModerationNoticeContentRepresentative(Id(40), 1, Id(41))];
        await Assert.That((await Validate(command with
        {
            Intent = command.Intent with { ContentRepresentatives = representatives }
        })).IsValid).IsFalse();
    }

    [Test]
    [Arguments(-1, true)]
    [Arguments(0, true)]
    [Arguments(1, false)]
    public async Task Text_validity_cannot_outlive_its_requested_retention(int seconds, bool expected)
    {
        var command = Create();
        await Assert.That((await Validate(command with
        {
            Intent = command.Intent with { ValidUntil = command.Intent.RetainUntil.AddSeconds(seconds) }
        })).IsValid).IsEqualTo(expected);
    }

    [Test]
    public async Task Command_and_mutation_keep_the_base_name_only_redaction()
    {
        var command = Create();
        await Assert.That(command.ToString()).IsEqualTo(nameof(ModerationQualificationCommand));
        await Assert.That(command.ToMutation().ToString()).IsEqualTo(nameof(ModerationQualificationMutation));
        var validation = await Validate(command with { Intent = command.Intent with { Facts = default } });
        await Assert.That(validation.IsValid).IsFalse();
        foreach (var error in validation.Errors)
            await Assert.That(error.ToString().Contains(SafeText, StringComparison.Ordinal)).IsFalse();
    }

    private static Task<FluentValidation.Results.ValidationResult> Validate(ApproveModerationNoticeCorrectionDisclosureCommand command) =>
        new ModerationNoticeCorrectionDisclosureCommandValidator().ValidateAsync(command);

    private static ApproveModerationNoticeCorrectionDisclosureCommand Create() => new()
    {
        OperationId = Id(1),
        Target = Tenant,
        ExpectedQualificationRevision = 13,
        ExpectedPendingSnapshotHash = new string('a', 64),
        ExpectedPendingEffectBindingHash = new string('b', 64),
        Intent = new ModerationNoticeDisclosureIntent(Id(2), Id(3), Id(4), 2, Id(5), 1, Id(6), 1,
            Id(7), Id(8), Id(9), new string('c', 64), new(SafeText), new(SafeText), [new(SafeText)],
            new(SafeText), new(SafeText), new(SafeText), new(SafeText), Until, Until.AddDays(1))
        {
            ContentRepresentatives = []
        }
    };

    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");
}
