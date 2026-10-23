using Explore.Application.Configuration;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationReconciliationOptionsTests
{
    [Test]
    public async Task Disabled_recovery_does_not_require_or_invent_a_credential_or_tenant()
    {
        var result = new ModerationReconciliationOptionsValidator().Validate(null, new());
        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("batch")]
    [Arguments("interval")]
    [Arguments("delay")]
    public async Task Enabled_recovery_rejects_unbounded_or_unqualified_execution(string invalid)
    {
        var options = new ModerationReconciliationOptions
        {
            Enabled = true, TenantId = Guid.CreateVersion7()
        };
        switch (invalid)
        {
            case "tenant": options.TenantId = Guid.Empty; break;
            case "batch": options.BatchSize = 1001; break;
            case "interval": options.IntervalSeconds = 0; break;
            case "delay": options.InitialDelaySeconds = -1; break;
        }
        var result = new ModerationReconciliationOptionsValidator().Validate(null, options);
        await Assert.That(result.Failed).IsTrue();
    }
}
