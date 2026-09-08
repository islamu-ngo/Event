using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Features.CustomProperties;

public sealed class CustomPropertyAutomationConditionPolicyTests
{
    private readonly CustomPropertyAutomationConditionPolicy _policy = new();

    [Test]
    public async Task Evaluate_WithTenantOwnedActiveFilterableDefinition_AllowsAutomationCondition()
    {
        var definition = CreateDefinition();

        var result = _policy.Evaluate(definition);

        await Assert.That(result.IsEligible).IsTrue();
        await Assert.That(result.RequiresProjection).IsTrue();
        await Assert.That(result.NormalizedNamespace).IsEqualTo("tenant.registration");
        await Assert.That(result.NormalizedKey).IsEqualTo("audience_segment");
    }

    [Test]
    public async Task Evaluate_WithReservedNamespace_RejectsAutomationCondition()
    {
        var definition = CreateDefinition(namespaceValue: "platform.registration");

        var result = _policy.Evaluate(definition);

        await Assert.That(result.IsEligible).IsFalse();
        await Assert.That(result.Errors.Any(error => error.Contains("tenant-owned", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Evaluate_WithNonFilterableDefinition_RejectsBecauseProjectionCannotBackCondition()
    {
        var definition = CreateDefinition(isFilterable: false);

        var result = _policy.Evaluate(definition);
        await Assert.That(result.IsEligible).IsFalse();
        await Assert.That(result.Errors.Any(error => error.Contains("filterable", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Evaluate_WithWorkflowCriticalKey_RejectsEavStateLeak()
    {
        var definition = CreateDefinition(key: "registration_status");

        var result = _policy.Evaluate(definition);

        await Assert.That(result.IsEligible).IsFalse();
        await Assert.That(result.Errors.Any(error => error.Contains("Workflow-critical", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Evaluate_WithUrlType_RejectsUnsupportedConditionType()
    {
        var definition = CreateDefinition(propertyType: PropertyType.Url);

        var result = _policy.Evaluate(definition);
        await Assert.That(result.IsEligible).IsFalse();
        await Assert.That(result.Errors.Any(error => error.Contains("not supported", StringComparison.Ordinal))).IsTrue();
    }

    private static EventCustomPropertyDefinition CreateDefinition(
        string namespaceValue = "tenant.registration",
        string key = "audience_segment",
        PropertyType propertyType = PropertyType.Option,
        bool isFilterable = true)
        => new()
        {
            Id = Guid.CreateVersion7(),
            EventId = Guid.CreateVersion7(),
            TenantId = Guid.CreateVersion7(),
            Namespace = namespaceValue,
            Key = key,
            DisplayName = "Audience Segment",
            PropertyType = propertyType,
            IsActive = true,
            IsFilterable = isFilterable,
            ExposureLevel = ExposureLevel.TenantAdminOnly,
            InstantiatedAt = DateTimeOffset.UtcNow,
        };
}
