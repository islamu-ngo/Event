namespace Event.Application.UnitTests.Features.ConfigurationManifest;

using System.Reflection;
using Explore.Application.Features.ConfigurationManifest.Compilation;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class ConfigurationImportSessionContractTests
{
    private const string ImportNamespace =
        "Explore.Application.Features.ConfigurationManifest.Importing.";

    private static readonly Assembly ApplicationAssembly =
        typeof(ConfigurationManifestCompiler).Assembly;

    [Test]
    public async Task SessionLimits_BoundUploadsAndAutomaticExpiry()
    {
        Type limits = RequireType("ConfigurationImportSessionLimits");

        int maximumBytes = ReadStatic<int>(limits, "MaximumArtifactBytes");
        TimeSpan defaultLifetime = ReadStatic<TimeSpan>(
            limits,
            "DefaultSessionLifetime");
        TimeSpan maximumLifetime = ReadStatic<TimeSpan>(
            limits,
            "MaximumSessionLifetime");

        await Assert.That(maximumBytes)
            .IsEqualTo(ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes);
        await Assert.That(defaultLifetime).IsGreaterThan(TimeSpan.Zero);
        await Assert.That(defaultLifetime).IsLessThanOrEqualTo(TimeSpan.FromHours(1));
        await Assert.That(maximumLifetime).IsGreaterThanOrEqualTo(defaultLifetime);
        await Assert.That(maximumLifetime).IsLessThanOrEqualTo(TimeSpan.FromHours(1));
    }

    [Test]
    public async Task TrustedTarget_IsSeparateFromPortableArtifactMetadata()
    {
        Type target = RequireType("ConfigurationImportTarget");
        Type artifact = RequireType("ConfigurationImportArtifactReference");
        string[] targetProperties = PublicPropertyNames(target);
        string[] artifactProperties = PublicPropertyNames(artifact);

        await Assert.That(target.IsSealed).IsTrue();
        await Assert.That(targetProperties)
            .Contains("Scope");
        await Assert.That(targetProperties)
            .Contains("TenantId");
        await Assert.That(
                target.GetMethod(
                    "ForInstance",
                    BindingFlags.Public | BindingFlags.Static))
            .IsNotNull();
        await Assert.That(
                target.GetMethod(
                    "ForTenant",
                    BindingFlags.Public | BindingFlags.Static))
            .IsNotNull();
        await Assert.That(artifactProperties)
            .DoesNotContain("Scope");
        await Assert.That(artifactProperties)
            .DoesNotContain("TenantId");
        await Assert.That(artifactProperties)
            .DoesNotContain("Target");
        await Assert.That(artifactProperties)
            .DoesNotContain("Authority");
    }

    [Test]
    public async Task ArtifactReference_HoldsOnlyOpaqueProtectedStorageMetadata()
    {
        Type handle = RequireType("ConfigurationImportArtifactHandle");
        Type artifact = RequireType("ConfigurationImportArtifactReference");
        PropertyInfo[] properties = artifact.GetProperties(
            BindingFlags.Public | BindingFlags.Instance);

        await Assert.That(handle.IsSealed).IsTrue();
        await Assert.That(properties.Select(property => property.Name))
            .IsEquivalentTo(
            [
                "ByteLength",
                "ExpiresAt",
                "Handle",
                "Sha256Digest"
            ]);
        await Assert.That(properties.Single(property => property.Name == "Handle")
            .PropertyType).IsEqualTo(handle);
        await Assert.That(properties.Any(property =>
                property.PropertyType == typeof(byte[])
                || property.PropertyType == typeof(Uri)
                || property.Name.Contains("Path", StringComparison.Ordinal)
                || property.Name.Contains("Content", StringComparison.Ordinal)))
            .IsFalse();
    }

    [Test]
    public async Task ProtectedStore_ExposesNoFilesystemOrRemoteLocation()
    {
        Type store = RequireType("IConfigurationImportArtifactStore");
        Type handle = RequireType("ConfigurationImportArtifactHandle");
        MethodInfo[] methods = store.GetMethods();

        await Assert.That(store.IsInterface).IsTrue();
        await Assert.That(methods.Select(method => method.Name))
            .IsEquivalentTo(
                ["DeleteAsync", "DeleteExpiredAsync", "ReadAsync", "StoreAsync"]);
        await Assert.That(methods
            .SelectMany(method => method.GetParameters())
            .Any(parameter => parameter.ParameterType == typeof(Uri)
                || string.Equals(
                    parameter.Name,
                    "path",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    parameter.Name,
                    "url",
                    StringComparison.OrdinalIgnoreCase)))
            .IsFalse();
        await Assert.That(methods.Single(method => method.Name == "DeleteAsync")
            .GetParameters()
            .Any(parameter => parameter.ParameterType == handle))
            .IsTrue();
    }

    [Test]
    public async Task SessionState_PinsExpiryCancellationReplayAndTargetChecks()
    {
        Type session = RequireType("ConfigurationImportSession");
        Type state = RequireType("ConfigurationImportSessionState");
        string[] states = Enum.GetNames(state);
        string[] methods = session
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .ToArray();

        await Assert.That(states).Contains("Uploaded");
        await Assert.That(states).Contains("PreviewReady");
        await Assert.That(states).Contains("Cancelled");
        await Assert.That(states).Contains("Expired");
        await Assert.That(states).Contains("Consumed");
        await Assert.That(methods).Contains("MatchesTarget");
        await Assert.That(methods).Contains("AuthorizePreview");
        await Assert.That(methods).Contains("Cancel");
        await Assert.That(methods).Contains("Expire");
        await Assert.That(methods).Contains("MarkPreviewReady");
        await Assert.That(methods).Contains("Consume");
    }

    [Test]
    public async Task SessionSnapshot_StoresDigestsNeverBearerTokenOrBytes()
    {
        Type session = RequireType("ConfigurationImportSession");
        string[] names = PublicPropertyNames(session);

        await Assert.That(names).Contains("AccessTokenDigest");
        await Assert.That(names).Contains("Artifact");
        await Assert.That(names).Contains("ExpiresAt");
        await Assert.That(names).Contains("SessionId");
        await Assert.That(names).Contains("State");
        await Assert.That(names).Contains("Target");
        await Assert.That(names.Any(name =>
                name is "AccessToken" or "ArtifactBytes" or "Content"
                    or "FilePath" or "SourceTenantId"))
            .IsFalse();
    }

    [Test]
    public async Task PreviewCategories_CoverEveryRequiredOutcome()
    {
        Type category = RequireType("ConfigurationImportPreviewCategory");
        string[] names = Enum.GetNames(category);

        await Assert.That(names).Contains("Changed");
        await Assert.That(names).Contains("Unchanged");
        await Assert.That(names).Contains("Skipped");
        await Assert.That(names).Contains("Mapped");
        await Assert.That(names).Contains("Blocking");
        await Assert.That(names).Contains("Warning");
        await Assert.That(names).Contains("Omitted");
        await Assert.That(names).Contains("ExternalSetupRequired");
    }

    [Test]
    public async Task PreviewBinding_FencesArtifactTargetRevisionSelectionAndMapping()
    {
        Type binding = RequireType("ConfigurationImportPreviewBinding");
        string[] properties = PublicPropertyNames(binding);

        await Assert.That(properties).Contains("ApplyMode");
        await Assert.That(properties).Contains("ArtifactDigest");
        await Assert.That(properties).Contains("ExpiresAt");
        await Assert.That(properties).Contains("MappingDigest");
        await Assert.That(properties).Contains("RequiredApprovalDigest");
        await Assert.That(properties).Contains("SelectedSectionsDigest");
        await Assert.That(properties).Contains("Target");
        await Assert.That(properties).Contains("TargetRevisionDigest");
        await Assert.That(
                binding.GetMethod(
                    "Matches",
                    BindingFlags.Public | BindingFlags.Instance))
            .IsNotNull();
    }

    [Test]
    public async Task PreviewComposer_IsPureAndCannotReachMutationInfrastructure()
    {
        Type composer = RequireType("ConfigurationImportPreviewComposer");
        ConstructorInfo[] constructors = composer.GetConstructors();
        MethodInfo compose = composer.GetMethod(
            "Compose",
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "Preview composer must expose Compose.");
        string[] forbiddenDependencyTerms =
        [
            "Applier",
            "Audit",
            "Mutation",
            "Outbox",
            "Provider",
            "Repository",
            "TenantCreation",
            "UnitOfWork"
        ];

        await Assert.That(composer.IsSealed).IsTrue();
        await Assert.That(constructors).HasSingleItem();
        await Assert.That(constructors[0].GetParameters()).IsEmpty();
        await Assert.That(compose.GetParameters().Length).IsEqualTo(1);
        await Assert.That(compose.ReturnType.Name)
            .IsEqualTo("ConfigurationImportPreview");
        await Assert.That(constructors
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => forbiddenDependencyTerms.Any(term =>
                parameter.ParameterType.Name.Contains(
                    term,
                    StringComparison.Ordinal))))
            .IsFalse();
    }

    [Test]
    public async Task FailureCodes_CloseExpiryReplayStaleAndAuthorityOutcomes()
    {
        Type failureCodes = RequireType("ConfigurationImportFailureCodes");
        string[] values = failureCodes
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string))
            .Select(field => field.GetRawConstantValue()?.ToString())
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        await Assert.That(values).Contains("configuration_import_expired");
        await Assert.That(values).Contains("configuration_import_replayed");
        await Assert.That(values).Contains("configuration_import_stale_preview");
        await Assert.That(values).Contains("configuration_import_target_mismatch");
        await Assert.That(values).Contains("configuration_import_too_large");
        await Assert.That(values).Contains("configuration_import_cancelled");
        await Assert.That(values.Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(values.Length);
        await Assert.That(values.All(value =>
                value.StartsWith(
                    "configuration_import_",
                    StringComparison.Ordinal)))
            .IsTrue();
    }

    [Test]
    public async Task SupportEvidenceAndTelemetry_AreValueSafeByConstruction()
    {
        Type evidence = RequireType("ConfigurationImportSessionEvidence");
        Type observability = RequireType(
            "ConfigurationImportObservabilityEvent");
        Type observabilityContract = RequireType(
            "ConfigurationImportObservabilityContract");
        string[] forbiddenTerms =
        [
            "AccessToken",
            "Bytes",
            "Connection",
            "Content",
            "Email",
            "FilePath",
            "LegalName",
            "Payload",
            "Phone",
            "Raw",
            "Secret",
            "Uri",
            "Url",
            "Value"
        ];

        await Assert.That(PublicPropertyNames(evidence)
            .Any(name => forbiddenTerms.Any(term =>
                name.Contains(term, StringComparison.OrdinalIgnoreCase))))
            .IsFalse();
        await Assert.That(PublicPropertyNames(observability)
            .Any(name => forbiddenTerms.Any(term =>
                name.Contains(term, StringComparison.OrdinalIgnoreCase))))
            .IsFalse();
        await Assert.That(PublicPropertyNames(evidence))
            .Contains("ArtifactDigest");
        await Assert.That(PublicPropertyNames(evidence))
            .Contains("OutcomeCode");
        await Assert.That(PublicPropertyNames(observability))
            .Contains("OutcomeCode");
        await Assert.That(observability
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Any(parameter => forbiddenTerms.Any(term =>
                (parameter.Name ?? string.Empty).Contains(
                    term,
                    StringComparison.OrdinalIgnoreCase))))
            .IsFalse();
        await Assert.That(observabilityContract
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name))
            .IsEquivalentTo(
            [
                "CreateLogState",
                "CreateMetricTags",
                "CreateTraceTags"
            ]);
    }

    [Test]
    public async Task PublishedContracts_SnapshotCollectionsAndRemainClosed()
    {
        Type preview = RequireType("ConfigurationImportPreview");
        Type previewItem = RequireType("ConfigurationImportPreviewItem");
        Type previewInput = RequireType("ConfigurationImportPreviewInput");
        Type[] types = [preview, previewItem, previewInput];

        foreach (Type type in types)
        {
            await Assert.That(type.IsSealed).IsTrue();
            await Assert.That(type.GetProperties()
                .Any(property =>
                    property.PropertyType.IsArray
                    || IsMutableGeneric(property.PropertyType, typeof(List<>))
                    || IsMutableGeneric(
                        property.PropertyType,
                        typeof(Dictionary<,>))))
                .IsFalse();
        }
    }

    private static Type RequireType(string name) =>
        ApplicationAssembly.GetType($"{ImportNamespace}{name}")
        ?? throw new InvalidOperationException(
            $"Missing configuration import contract: {name}.");

    private static string[] PublicPropertyNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static T ReadStatic<T>(Type type, string name)
    {
        FieldInfo? field = type.GetField(
            name,
            BindingFlags.Public | BindingFlags.Static);
        if (field?.GetValue(null) is T fieldValue)
            return fieldValue;
        PropertyInfo? property = type.GetProperty(
            name,
            BindingFlags.Public | BindingFlags.Static);
        return property?.GetValue(null) is T propertyValue
            ? propertyValue
            : throw new InvalidOperationException(
                $"{type.Name}.{name} is missing or has the wrong type.");
    }

    private static bool IsMutableGeneric(Type type, Type genericDefinition) =>
        type.IsGenericType
        && type.GetGenericTypeDefinition() == genericDefinition;
}
