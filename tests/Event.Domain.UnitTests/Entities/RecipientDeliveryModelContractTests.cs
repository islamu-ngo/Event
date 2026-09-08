using System.Reflection;
using Explore.Domain;

namespace Event.Domain.UnitTests.Entities;

public sealed class RecipientDeliveryModelContractTests
{
    [Test]
    public async Task NotificationDeliveryPolicyLookup_HasApprovedStableValues()
    {
        Type policyEnum = RequireDomainType("Explore.Domain.Enums.NotificationDeliveryPolicyEnum");

        await Assert.That(ReadEnumValues(policyEnum)).IsEquivalentTo(new Dictionary<string, int>
        {
            ["RegistrationStatusOptional"] = 1,
            ["CriticalEventUpdateOptional"] = 2,
            ["ReportCaseUpdate"] = 3,
            ["ReportFollowUpContact"] = 4,
            ["ModerationAvailabilityRequired"] = 5,
            ["ModerationContextOptional"] = 6,
            ["ReminderOptional"] = 7,
            ["TenantAdministrationRequired"] = 8
        });
    }

    [Test]
    public async Task NotificationDeliveryOutcomeLookup_IsChannelNeutral()
    {
        Type outcomeEnum = RequireDomainType("Explore.Domain.Enums.NotificationDeliveryStatusEnum");

        await Assert.That(ReadEnumValues(outcomeEnum)).IsEquivalentTo(new Dictionary<string, int>
        {
            ["Pending"] = 1,
            ["Queued"] = 2,
            ["Delivered"] = 3,
            ["Skipped"] = 4,
            ["Failed"] = 5,
            ["DeadLettered"] = 6,
            ["Unknown"] = 7,
            ["Parked"] = 8,
            ["Superseded"] = 9
        });
    }

    [Test]
    public async Task NotificationDelivery_ContainsChannelPolicySnapshotAndTypedLinks()
    {
        string[] expectedProperties =
        [
            "ChannelId",
            "DeliveryPolicyId",
            "IsRequired",
            "PolicyVersion",
            "ConsentPurpose",
            "ConsentVersion",
            "PreferenceCategoryCode",
            "PreferenceEnabled",
            "RecipientAddressSource",
            "DisclosureLevel",
            "TemplateKey",
            "TemplateVersion",
            "LinkAllowed",
            "NotificationId",
            "EmailDispatchOutboxId"
        ];

        await Assert.That(MissingProperties(typeof(NotificationDelivery), expectedProperties)).IsEmpty();
    }

    [Test]
    public async Task EmailDispatchOutbox_ContainsExplicitIntentRecipientAndAddressAuthority()
    {
        string[] expectedProperties =
        [
            "NotificationIntentId",
            "RecipientUserId",
            "RecipientAddressSource",
            "ManagedTenantProvisioningOperationId"
        ];

        await Assert.That(MissingProperties(typeof(EmailDispatchOutbox), expectedProperties)).IsEmpty();
        await Assert.That(typeof(EmailDispatchOutbox).GetProperty("UserId")).IsNull();
    }

    [Test]
    public async Task EmailDispatchKind_ReservesDistinctRequiredModerationTransportValue()
    {
        await Assert.That((int)EmailDispatchKind.ModerationAvailabilityRequired).IsEqualTo(15);
        await Assert.That(Enum.GetValues<EmailDispatchKind>().Distinct().Count())
            .IsEqualTo(Enum.GetValues<EmailDispatchKind>().Length);
    }

    private static Type RequireDomainType(string typeName)
    {
        return typeof(NotificationIntent).Assembly.GetType(typeName)
            ?? throw new InvalidOperationException($"Required domain type '{typeName}' is missing.");
    }

    private static Dictionary<string, int> ReadEnumValues(Type enumType)
    {
        return Enum.GetNames(enumType)
            .ToDictionary(name => name, name => Convert.ToInt32(Enum.Parse(enumType, name)), StringComparer.Ordinal);
    }

    private static string[] MissingProperties(Type type, IEnumerable<string> propertyNames)
    {
        return propertyNames
            .Where(name => type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public) is null)
            .ToArray();
    }
}
