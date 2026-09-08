using Explore.Application.Features.Events.Moderation;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.Events.Moderation;

public sealed class EventRedactionSentinelPolicyTests
{
    [Test]
    public async Task DisplayTextSentinel_FitsEveryMappedDisplayTextField()
    {
        var displayRules = EventRedactionSentinelPolicy.FieldRules
            .Where(rule => rule.ValueKind == EventRedactionValueKind.DisplayText)
            .ToArray();

        await Assert.That(displayRules.Length).IsGreaterThan(0);

        foreach (var rule in displayRules)
        {
            await Assert.That(rule.MaxLength).IsNotNull();
            await Assert.That(EventRedactionSentinelPolicy.DisplayText.Length).IsLessThanOrEqualTo(rule.MaxLength!.Value);
        }
    }

    [Test]
    public async Task DeterministicSentinels_FitEveryMappedSlugAndMachineKeyField()
    {
        var id = Guid.Parse("0196c2c4-0000-7000-8000-000000000001");
        var constrainedRules = EventRedactionSentinelPolicy.FieldRules
            .Where(rule => rule.ValueKind is EventRedactionValueKind.DeterministicSlug or EventRedactionValueKind.DeterministicMachineKey)
            .ToArray();

        await Assert.That(constrainedRules.Length).IsGreaterThan(0);

        foreach (var rule in constrainedRules)
        {
            var scope = $"{rule.EntityName}-{rule.FieldName}";
            var sentinel = rule.ValueKind == EventRedactionValueKind.DeterministicSlug
                ? EventRedactionSentinelPolicy.BuildSlugSentinel(id, scope, rule.MaxLength!.Value)
                : EventRedactionSentinelPolicy.BuildMachineKeySentinel(id, scope, rule.MaxLength!.Value);

            await Assert.That(sentinel).StartsWith("redacted-");
            await Assert.That(sentinel).DoesNotContain("illegal");
            await Assert.That(sentinel.Length).IsLessThanOrEqualTo(rule.MaxLength!.Value);
        }
    }

    [Test]
    public async Task FieldRules_CoverRootEventTextAndImageReferences()
    {
        var mappedFields = EventRedactionSentinelPolicy.FieldRules
            .Select(rule => $"{rule.EntityName}.{rule.FieldName}")
            .ToHashSet(StringComparer.Ordinal);

        await Assert.That(mappedFields.Contains("Event.Title")).IsTrue();
        await Assert.That(mappedFields.Contains("Event.Description")).IsTrue();
        await Assert.That(mappedFields.Contains("Event.Content")).IsTrue();
        await Assert.That(mappedFields.Contains("Event.Slug")).IsTrue();
        await Assert.That(mappedFields.Contains("Event.FeaturedImageId")).IsTrue();
        await Assert.That(mappedFields.Contains("Event.BackgroundImageId")).IsTrue();
    }

    [Test]
    public async Task FieldRules_DoNotTreatStorageObjectProviderFieldsAsRetainedText()
    {
        var retainedStorageIdentifierRules = EventRedactionSentinelPolicy.FieldRules
            .Where(rule => rule.EntityName == "StorageObject" &&
                rule.FieldName is "Uri" or "ObjectKey" or "FullName" or "SafeDisplayName")
            .ToArray();
        var imageReferenceRules = EventRedactionSentinelPolicy.FieldRules
            .Where(rule => rule.FieldName.EndsWith("ImageId", StringComparison.Ordinal))
            .ToArray();

        await Assert.That(retainedStorageIdentifierRules.Length).IsEqualTo(0);
        await Assert.That(imageReferenceRules.Length).IsGreaterThan(0);
        await Assert.That(imageReferenceRules.All(rule => rule.ValueKind == EventRedactionValueKind.DeleteStorageObject)).IsTrue();
    }
}
