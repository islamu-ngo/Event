using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// An issuer's asserted measurement, never acceptance, readiness or a passing registration.
/// Version 1 uses the pinned JWT purpose, SHA-256 lowercase hex digests, canonical UUIDv7
/// identities, decimal-string revisions and round-trip UTC timestamps with seven fractional digits.
/// ExecutedCount counts unique case identities; AttemptCount counts whole-run attempts.
/// Authenticity cannot prove that an enrolled issuer honestly performed the asserted work.
/// </summary>
[JsonConverter(typeof(ModerationMeasurementReceiptJsonConverter))]
public sealed record ModerationMeasurementReceipt
{
    public const string RequiredAudience = "islamu.moderation.native-measurement";
    public required string Issuer { get; init; }
    public required Guid ReceiptId { get; init; }
    public required Guid ChallengeId { get; init; }
    public required string ChallengeNonce { get; init; }
    public required Guid RunId { get; init; }
    public required Guid HostBindingId { get; init; }
    public required Guid TrustDomainId { get; init; }
    public required Guid TenantId { get; init; }
    public required ModerationActivationBinding Binding { get; init; }
    public required string ConsumerId { get; init; }
    public required string ConsumerContractDigest { get; init; }
    public required Guid InstallationId { get; init; }
    public required long InstallationRevision { get; init; }
    public required long InstallationWriterGeneration { get; init; }
    public required string RuntimeGeneration { get; init; }
    public required string InventoryFingerprint { get; init; }
    public required string RunnerDigest { get; init; }
    public required string CatalogueDigest { get; init; }
    public required string TestBundleDigest { get; init; }
    public required string ProductArtifactDigest { get; init; }
    public required string ConfigurationDigest { get; init; }
    public required string PrimaryConfigurationDigest { get; init; }
    public required string TestConfigurationDigest { get; init; }
    public required string ProviderCode { get; init; }
    public required string ProviderVersion { get; init; }
    public required ImmutableArray<string> CaseIds { get; init; }
    public required int ExecutedCount { get; init; }
    public required int AttemptCount { get; init; }
    public required string ResultArtifactDigest { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required DateTimeOffset IssuedAt { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    public override string ToString() => nameof(ModerationMeasurementReceipt);
}

/// <summary>
/// Expected public-key authority supplied by the native enrollment owner, never by the JWT.
/// The owner supplies null for missing or retired keys. This snapshot does not implement
/// enrollment, revocation lookup, receipt acceptance or current business authority.
/// </summary>
public sealed record ModerationMeasurementTrustKey
{
    public required string Issuer { get; init; }
    public required Guid KeyId { get; init; }
    public required ImmutableArray<byte> SubjectPublicKeyInfo { get; init; }
    public required Guid HostBindingId { get; init; }
    public required Guid TrustDomainId { get; init; }
    public required Guid TenantId { get; init; }
    public required string ApprovedRunnerDigest { get; init; }
    public required string ApprovedCatalogueDigest { get; init; }
    public required string ApprovedTestBundleDigest { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    public override string ToString() => nameof(ModerationMeasurementTrustKey);
}

public enum ModerationMeasurementVerificationOutcome
{
    Known = 1,
    Rejected = 2
}

public enum ModerationMeasurementVerificationFailure
{
    None = 0,
    UnknownKey = 1,
    InvalidTrustKey = 2,
    InvalidToken = 3,
    InvalidSignature = 4,
    ScopeMismatch = 5,
    BindingMismatch = 6,
    ArtifactMismatch = 7,
    InvalidWindow = 8
}

/// <summary>Known means cryptographic authenticity only, not honest execution or permission.</summary>
public sealed record ModerationMeasurementVerificationResult
{
    private ModerationMeasurementVerificationResult(ModerationMeasurementVerificationOutcome outcome,
        ModerationMeasurementVerificationFailure failureReason, ModerationMeasurementReceipt? receipt,
        DateTimeOffset? validUntil) =>
        (Outcome, FailureReason, Receipt, ValidUntil) = (outcome, failureReason, receipt, validUntil);

    public ModerationMeasurementVerificationOutcome Outcome { get; }
    public ModerationMeasurementVerificationFailure FailureReason { get; }
    public ModerationMeasurementReceipt? Receipt { get; }
    public DateTimeOffset? ValidUntil { get; }

    public static ModerationMeasurementVerificationResult Known(
        ModerationMeasurementReceipt receipt, DateTimeOffset enrollmentValidUntil)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new(ModerationMeasurementVerificationOutcome.Known, ModerationMeasurementVerificationFailure.None,
            receipt, receipt.ValidUntil < enrollmentValidUntil ? receipt.ValidUntil : enrollmentValidUntil);
    }

    public static ModerationMeasurementVerificationResult Reject(ModerationMeasurementVerificationFailure reason)
    {
        if (!Enum.IsDefined(reason) || reason == ModerationMeasurementVerificationFailure.None)
            throw new ArgumentOutOfRangeException(nameof(reason));
        return new(ModerationMeasurementVerificationOutcome.Rejected, reason, null, null);
    }

    public override string ToString() => nameof(ModerationMeasurementVerificationResult);
}

/// <summary>
/// The closed version-1 payload grammar. Explicit parsing preserves Int64 revisions without
/// JSON floating-point loss and reconstructs native immutable jurisdiction/rule values.
/// JSON construction alone produces an untrusted receipt, never a verification result.
/// </summary>
public sealed class ModerationMeasurementReceiptJsonConverter : JsonConverter<ModerationMeasurementReceipt>
{
    public override ModerationMeasurementReceipt Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        Shape(root, "version", "iss", "aud", "receiptId", "challengeId", "challengeNonce", "runId", "hostBindingId",
            "trustDomainId", "tenantId", "binding", "consumerId", "consumerContractDigest",
            "installationId", "installationRevision", "installationWriterGeneration", "runtimeGeneration",
            "inventoryFingerprint", "runnerDigest", "catalogueDigest", "testBundleDigest",
            "productArtifactDigest", "configurationDigest", "primaryConfigurationDigest", "testConfigurationDigest",
            "providerCode", "providerVersion", "caseIds",
            "executedCount", "attemptCount", "resultArtifactDigest", "startedAt", "completedAt", "issuedAt", "validUntil");
        if (PositiveInt(root, "version") != 1 || Text(root, "aud") != ModerationMeasurementReceipt.RequiredAudience)
            throw Invalid();

        var binding = root.GetProperty("binding");
        Shape(binding, "policyId", "policyRevision", "publicationId", "jurisdiction", "rule", "capability", "mode");
        var jurisdiction = binding.GetProperty("jurisdiction");
        Shape(jurisdiction, "kind", "trustDomainId", "solutionId", "instanceId", "tenantId", "communityId", "resourceId", "eventId");
        var rule = binding.GetProperty("rule");
        Shape(rule, "ownerId", "code", "revision");
        AuthorityScope scope;
        ModerationRuleReference ruleReference;
        try
        {
            scope = AuthorityScope.FromCoordinates(Named<ModerationScopeKind>(jurisdiction, "kind"),
                Id(jurisdiction, "trustDomainId"), OptionalId(jurisdiction, "solutionId"),
                OptionalId(jurisdiction, "instanceId"), OptionalId(jurisdiction, "tenantId"),
                OptionalId(jurisdiction, "communityId"), OptionalId(jurisdiction, "resourceId"),
                OptionalId(jurisdiction, "eventId"));
            ruleReference = ModerationRuleReference.Create(Id(rule, "ownerId"), Text(rule, "code"), Revision(rule, "revision"));
        }
        catch (ArgumentException)
        {
            throw Invalid();
        }
        if (ruleReference.OwnerId != scope.Id) throw Invalid();

        var cases = root.GetProperty("caseIds");
        if (cases.ValueKind != JsonValueKind.Array || cases.GetArrayLength() is < 1 or > 256)
            throw Invalid();
        var identities = ImmutableArray.CreateBuilder<string>(cases.GetArrayLength());
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in cases.EnumerateArray())
        {
            string identity = Text(item);
            if (!seen.Add(identity)) throw Invalid();
            identities.Add(identity);
        }
        int executed = PositiveInt(root, "executedCount");
        if (executed != identities.Count) throw Invalid();
        return new()
        {
            Issuer = Text(root, "iss"),
            ReceiptId = Id(root, "receiptId"), ChallengeId = Id(root, "challengeId"),
            ChallengeNonce = Digest(root, "challengeNonce"), RunId = Id(root, "runId"),
            HostBindingId = Id(root, "hostBindingId"), TrustDomainId = Id(root, "trustDomainId"), TenantId = Id(root, "tenantId"),
            Binding = new(Id(binding, "policyId"), Revision(binding, "policyRevision"), Id(binding, "publicationId"),
                scope, ruleReference, Named<ModerationCapability>(binding, "capability"), Named<ModerationNativeMode>(binding, "mode")),
            ConsumerId = Text(root, "consumerId"), ConsumerContractDigest = Digest(root, "consumerContractDigest"),
            InstallationId = Id(root, "installationId"), InstallationRevision = Revision(root, "installationRevision"),
            InstallationWriterGeneration = Revision(root, "installationWriterGeneration"),
            RuntimeGeneration = Text(root, "runtimeGeneration"), InventoryFingerprint = Digest(root, "inventoryFingerprint"),
            RunnerDigest = Digest(root, "runnerDigest"), CatalogueDigest = Digest(root, "catalogueDigest"),
            TestBundleDigest = Digest(root, "testBundleDigest"), ProductArtifactDigest = Digest(root, "productArtifactDigest"),
            ConfigurationDigest = Digest(root, "configurationDigest"), ProviderCode = Text(root, "providerCode"),
            PrimaryConfigurationDigest = Digest(root, "primaryConfigurationDigest"),
            TestConfigurationDigest = Digest(root, "testConfigurationDigest"),
            ProviderVersion = Text(root, "providerVersion"), CaseIds = identities.MoveToImmutable(),
            ExecutedCount = executed, AttemptCount = PositiveInt(root, "attemptCount"),
            ResultArtifactDigest = Digest(root, "resultArtifactDigest"),
            StartedAt = Instant(root, "startedAt"), CompletedAt = Instant(root, "completedAt"),
            IssuedAt = Instant(root, "issuedAt"), ValidUntil = Instant(root, "validUntil")
        };
    }

    public override void Write(Utf8JsonWriter writer, ModerationMeasurementReceipt value, JsonSerializerOptions options)
    {
        var coordinates = value.Binding.Jurisdiction.Coordinates;
        // Own the wire spelling independently of ambient serializer policies.
        JsonSerializer.Serialize(writer, new
        {
            version = 1, iss = value.Issuer, aud = ModerationMeasurementReceipt.RequiredAudience,
            receiptId = value.ReceiptId, challengeId = value.ChallengeId, challengeNonce = value.ChallengeNonce, runId = value.RunId,
            hostBindingId = value.HostBindingId, trustDomainId = value.TrustDomainId, tenantId = value.TenantId,
            binding = new
            {
                policyId = value.Binding.PolicyId, policyRevision = Decimal(value.Binding.PolicyRevision),
                publicationId = value.Binding.PublicationId,
                jurisdiction = new
                {
                    kind = coordinates.Kind.ToString(), trustDomainId = coordinates.TrustDomainId,
                    solutionId = coordinates.SolutionId, instanceId = coordinates.InstanceId, tenantId = coordinates.TenantId,
                    communityId = coordinates.CommunityId, resourceId = coordinates.ResourceId,
                    eventId = coordinates.EventId
                },
                rule = new
                {
                    ownerId = value.Binding.Rule.OwnerId, code = value.Binding.Rule.Code,
                    revision = Decimal(value.Binding.Rule.Revision)
                },
                capability = value.Binding.Capability.ToString(), mode = value.Binding.Mode.ToString()
            },
            consumerId = value.ConsumerId, consumerContractDigest = value.ConsumerContractDigest,
            installationId = value.InstallationId, installationRevision = Decimal(value.InstallationRevision),
            installationWriterGeneration = Decimal(value.InstallationWriterGeneration),
            runtimeGeneration = value.RuntimeGeneration, inventoryFingerprint = value.InventoryFingerprint,
            runnerDigest = value.RunnerDigest, catalogueDigest = value.CatalogueDigest,
            testBundleDigest = value.TestBundleDigest, productArtifactDigest = value.ProductArtifactDigest,
            configurationDigest = value.ConfigurationDigest, providerCode = value.ProviderCode, providerVersion = value.ProviderVersion,
            primaryConfigurationDigest = value.PrimaryConfigurationDigest, testConfigurationDigest = value.TestConfigurationDigest,
            caseIds = value.CaseIds, executedCount = value.ExecutedCount, attemptCount = value.AttemptCount,
            resultArtifactDigest = value.ResultArtifactDigest, startedAt = Timestamp(value.StartedAt),
            completedAt = Timestamp(value.CompletedAt), issuedAt = Timestamp(value.IssuedAt), validUntil = Timestamp(value.ValidUntil)
        });
    }

    private static string Decimal(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Timestamp(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);
    private static JsonException Invalid() => new("Invalid moderation measurement receipt.");

    private static void Shape(JsonElement element, params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Invalid();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw Invalid();
        if (seen.Count != fields.Length) throw Invalid();
    }

    private static string Text(JsonElement parent, string field) => Text(parent.GetProperty(field));

    private static string Text(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { Length: > 0 and <= 256 } value
            || value.Any(character => character is < '!' or > '~'))
            throw Invalid();
        return value;
    }

    private static Guid Id(JsonElement parent, string field)
    {
        string text = Text(parent, field);
        if (!Guid.TryParseExact(text, "D", out var id) || id.ToString("D") != text
            || id.Version != 7 || (id.ToByteArray()[8] & 0xc0) != 0x80) throw Invalid();
        return id;
    }

    private static Guid? OptionalId(JsonElement parent, string field) =>
        parent.GetProperty(field).ValueKind == JsonValueKind.Null ? null : Id(parent, field);

    private static long Revision(JsonElement parent, string field)
    {
        string text = Text(parent, field);
        if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value)
            || value < 1 || Decimal(value) != text) throw Invalid();
        return value;
    }

    private static int PositiveInt(JsonElement parent, string field)
    {
        var element = parent.GetProperty(field);
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int value)
            || value < 1 || element.GetRawText() != value.ToString(CultureInfo.InvariantCulture)) throw Invalid();
        return value;
    }

    private static string Digest(JsonElement parent, string field)
    {
        string value = Text(parent, field);
        if (value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw Invalid();
        return value;
    }

    private static T Named<T>(JsonElement parent, string field) where T : struct, Enum
    {
        string text = Text(parent, field);
        if (!Enum.TryParse<T>(text, out var value) || !Enum.IsDefined(value) || Enum.GetName(value) != text)
            throw Invalid();
        return value;
    }

    private static DateTimeOffset Instant(JsonElement parent, string field)
    {
        string text = Text(parent, field);
        if (!DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            || value.Offset != TimeSpan.Zero || value == DateTimeOffset.MinValue || value == DateTimeOffset.MaxValue
            || Timestamp(value) != text) throw Invalid();
        return value;
    }
}
