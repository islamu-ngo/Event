namespace Event.Application.UnitTests.Settings;

using System.Collections.Immutable;
using Explore.Application.Settings;
using Explore.Domain.Settings.Definitions;

public sealed class PublicationPolicyMutationBoundaryTests
{
    [Test]
    public async Task RepeatedInvalidInputs_ReturnSameStableCompilerCodeWithoutProposedState()
    {
        Guid tenant = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid foreign = Guid.Parse("00000000-0000-0000-0000-000000000002");
        string key = EventSettingDefinitions.RequireApproval.Key;
        var original = new PublicationPolicySettingMutation(
            key, PublicationPolicyMutationKind.Set, "true", tenant, null);
        ImmutableArray<PublicationPolicySettingMutation>[] invalidBatches =
        [
            [original, original with { Kind = PublicationPolicyMutationKind.Remove, JsonValue = null }],
            [original with { TenantId = foreign }],
            [original with { TenantId = null, IsLocked = false }]
        ];
        var codes = new List<string>();
        foreach (var batch in invalidBatches)
        {
            for (int invocation = 0; invocation < 2; invocation++)
            {
                var result = PublicationPolicyProposedStateCompiler.CompileTenant(new(tenant, [], [], batch));
                await Assert.That(result.Success).IsFalse();
                await Assert.That(result.FailureCode).IsEqualTo("event_reporting_intake_policy_invalid");
                await Assert.That(result.BaseTenantState).IsNull();
                await Assert.That(result.TenantStates).IsEmpty();
                codes.Add(result.FailureCode!);
            }
        }
        await Assert.That(codes.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(1);
    }

    [Test]
    public async Task ExternalReportingKey_IsNotOwnedAndFailsAsInvalidCompilerInput()
    {
        Guid tenant = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var external = new PublicationPolicySettingMutation("moderation.reporting.provider",
            PublicationPolicyMutationKind.Set, "true", tenant, null);
        var result = PublicationPolicyProposedStateCompiler.CompileTenant(new(tenant, [], [], [external]));
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("event_reporting_intake_policy_invalid");
        await Assert.That(result.BaseTenantState).IsNull();
        await Assert.That(result.TenantStates).IsEmpty();
    }
}
