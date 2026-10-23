namespace ISLAMU.ConfigurationManifest.SchemaGenerator;

using System.Text.Json.Nodes;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public static partial class ConfigurationManifestJsonSchemaGenerator
{
    private static void AddModerationDefinitions(IDictionary<string, JsonNode?> definitions)
    {
        definitions.Add("moderationDocument", ClosedObject(new JsonObject
        {
            ["schemaVersion"] = new JsonObject { ["const"] = ConfigurationModerationContentLimits.SchemaVersion },
            ["catalogueRevision"] = ModerationRevision(),
            ["modes"] = ModerationArray(ModerationEnum<ConfigurationModerationMode>(), unique: true),
            ["classes"] = ModerationArray(Ref("moderationClass"), minimum: 1),
            ["reasons"] = ModerationArray(Ref("moderationReason"), minimum: 1),
            ["profiles"] = ModerationArray(Ref("moderationProfile"), minimum: 1),
            ["supportedProfiles"] = ModerationArray(Ref("moderationReference"), unique: true)
        }, ["schemaVersion", "catalogueRevision", "modes", "classes", "reasons", "profiles", "supportedProfiles"]));
        definitions.Add("moderationReference", ClosedObject(new JsonObject
        {
            ["code"] = ModerationText(),
            ["revision"] = ModerationRevision()
        }, ["code", "revision"]));
        definitions.Add("moderationClass", ClosedObject(new JsonObject
        {
            ["reference"] = Ref("moderationReference"),
            ["meaning"] = ModerationText(),
            ["ground"] = ModerationEnum<ConfigurationModerationGround>(),
            ["basis"] = ModerationEnum<ConfigurationModerationBasis>()
        }, ["reference", "meaning", "ground", "basis"]));
        definitions.Add("moderationReason", ClosedObject(new JsonObject
        {
            ["reference"] = Ref("moderationReference"),
            ["meaning"] = ModerationText(),
            ["class"] = Ref("moderationReference")
        }, ["reference", "meaning", "class"]));
        definitions.Add("moderationEffect", ClosedObject(new JsonObject
        {
            ["kind"] = ModerationEnum<ConfigurationModerationEffectKind>(),
            ["mode"] = ModerationEnum<ConfigurationModerationEffectMode>(),
            ["target"] = ModerationEnum<ConfigurationModerationTarget>(),
            ["capability"] = ModerationEnum<ConfigurationModerationCapability>(nullable: true),
            ["execution"] = ModerationEnum<ConfigurationModerationExecution>()
        }, ["kind", "mode", "target", "capability", "execution"]));
        definitions.Add("moderationTiming", ClosedObject(new JsonObject
        {
            ["kind"] = ModerationEnum<ConfigurationModerationTimingKind>(),
            ["duration"] = new JsonObject
            {
                ["type"] = "string",
                ["maxLength"] = 25,
                ["pattern"] = @"^(?:[0-9]{1,8}\.)?(?:[01][0-9]|2[0-3]):[0-5][0-9]:[0-5][0-9](?:\.[0-9]{1,7})?$",
                ["description"] = "Nonnegative .NET TimeSpan constant format, bounded by 10675199.02:48:05.4775807; the wire parser enforces the tick range."
            }
        }, ["kind", "duration"]));
        definitions.Add("moderationProfile", ClosedObject(new JsonObject
        {
            ["rule"] = Ref("moderationReference"),
            ["reason"] = Ref("moderationReference"),
            ["referenceProfile"] = ModerationEnum<ConfigurationModerationReferenceProfile>(),
            ["referenceRevision"] = ModerationRevision(),
            ["grade"] = ModerationEnum<ConfigurationModerationGrade>(),
            ["scopeKinds"] = ModerationArray(ModerationEnum<ConfigurationModerationScopeKind>(), unique: true),
            ["subjectKinds"] = ModerationArray(ModerationEnum<ConfigurationModerationSubjectKind>(), unique: true),
            ["effects"] = ModerationArray(Ref("moderationEffect"), unique: true),
            ["bundle"] = ModerationEnum<ConfigurationModerationBundle>(),
            ["protectionPurpose"] = ModerationEnum<ConfigurationModerationProtectionPurpose>(nullable: true),
            ["strategies"] = ModerationArray(ModerationEnum<ConfigurationModerationStrategy>(), unique: true),
            ["timing"] = Ref("moderationTiming"),
            ["requiredApprovals"] = ModerationNonnegativeInteger(),
            ["contestationCalendarMonths"] = ModerationNonnegativeInteger(),
            ["recognition"] = ModerationEnum<ConfigurationModerationRecognition>(),
            ["recipes"] = ModerationArray(Ref("moderationRecipe"))
        },
        [
            "rule", "reason", "referenceProfile", "referenceRevision", "grade", "scopeKinds",
            "subjectKinds", "effects", "bundle", "protectionPurpose", "strategies", "timing", "requiredApprovals",
            "contestationCalendarMonths", "recognition", "recipes"
        ]));
        definitions.Add("moderationRecipe", ClosedObject(new JsonObject
        {
            ["strategy"] = ModerationEnum<ConfigurationModerationStrategy>(nullable: true),
            ["content"] = ModerationEnum<ConfigurationModerationContentMode>(),
            ["management"] = ModerationEnum<ConfigurationModerationManagementMode>(),
            ["resources"] = ModerationEnum<ConfigurationModerationResourceReadMode>(),
            ["newAdmission"] = ModerationEnum<ConfigurationModerationNewAdmissionMode>(),
            ["existingAdmission"] = ModerationEnum<ConfigurationModerationExistingAdmissionMode>(),
            ["privateDisclosure"] = ModerationEnum<ConfigurationModerationPrivateDisclosureMode>(),
            ["settlement"] = ModerationEnum<ConfigurationModerationSettlementMode>(),
            ["capacity"] = ModerationEnum<ConfigurationModerationCapacityMode>(),
            ["credentials"] = ModerationEnum<ConfigurationModerationCredentialMode>(),
            ["notifications"] = ModerationEnum<ConfigurationModerationNotificationMode>(),
            ["recognition"] = ModerationEnum<ConfigurationModerationRecognitionMode>(),
            ["pendingWork"] = ModerationEnum<ConfigurationModerationPendingWorkMode>(),
            ["restoration"] = ModerationEnum<ConfigurationModerationRestorationMode>()
        },
        [
            "strategy", "content", "management", "resources", "newAdmission", "existingAdmission", "privateDisclosure",
            "settlement", "capacity", "credentials", "notifications", "recognition", "pendingWork", "restoration"
        ]));
        definitions["moderationDocument"]!["$comment"] =
            "The wire parser also rejects duplicate JSON members and duplicate class/reason/profile semantic code+revision keys. Recipe strategy (including the base null strategy) must be unique. Native Application owns complete catalogue/compiler and thirteen-dimension recipe parity; schema acceptance is not draft eligibility or activation authority.";
    }

    private static JsonObject ModerationEnum<T>(bool nullable = false) where T : struct, Enum
    {
        var values = Strings(Enum.GetNames<T>().Order(StringComparer.Ordinal));
        if (nullable)
            values.Add((JsonNode?)null);
        return new JsonObject
        {
            ["type"] = nullable ? Strings(["string", "null"]) : JsonValue.Create("string"),
            ["enum"] = values
        };
    }

    private static JsonObject ModerationArray(JsonObject item, int minimum = 0, bool unique = false) =>
        new()
        {
            ["type"] = "array",
            ["minItems"] = minimum,
            ["maxItems"] = ConfigurationModerationContentLimits.MaximumDefinitions,
            ["uniqueItems"] = unique,
            ["items"] = item
        };

    private static JsonObject ModerationText() => new()
    {
        ["type"] = "string",
        ["minLength"] = 1,
        ["maxLength"] = ConfigurationModerationContentLimits.MaximumTextLength,
        ["pattern"] = @"\S"
    };

    private static JsonObject ModerationRevision() => new()
    {
        ["type"] = "integer",
        ["minimum"] = 1,
        ["maximum"] = long.MaxValue
    };

    private static JsonObject ModerationNonnegativeInteger() => new()
    {
        ["type"] = "integer",
        ["minimum"] = 0,
        ["maximum"] = int.MaxValue
    };
}
