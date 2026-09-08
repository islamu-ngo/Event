namespace Event.Api.IntegrationTests.Features;

using System.Reflection;
using Explore.API.Extensions;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

public sealed class ConfigurationImportBoundaryContractTests
{
    private static readonly Assembly ApiAssembly =
        typeof(RateLimitingExtensions).Assembly;

    [Test]
    public async Task ImportBoundary_UsesCanonicalArtifactSizeLimit()
    {
        Type boundary = RequireType("ConfigurationImportApiBoundary");
        int maximumBytes = ReadStatic<int>(boundary, "MaximumUploadBytes");

        await Assert.That(maximumBytes)
            .IsEqualTo(ConfigurationPortabilityContentLimits.MaximumArtifactUtf8Bytes);
    }

    [Test]
    public async Task ImportBoundary_DeclaresDedicatedRateAndTimeoutPolicies()
    {
        Type boundary = RequireType("ConfigurationImportApiBoundary");
        string ratePolicy = ReadStatic<string>(
            boundary,
            "UploadRateLimitPolicy");
        string timeoutPolicy = ReadStatic<string>(
            boundary,
            "UploadRequestTimeoutPolicy");

        await Assert.That(ratePolicy).IsNotEqualTo(RateLimitingExtensions.WritePolicy);
        await Assert.That(ratePolicy).IsNotEqualTo(
            RateLimitingExtensions.AuthenticatedPolicy);
        await Assert.That(ratePolicy).StartsWith("ConfigurationImport");
        await Assert.That(timeoutPolicy).StartsWith("ConfigurationImport");
    }

    [Test]
    public async Task ProblemPolicy_ExposesOnlyStableCodeAndRetryMetadata()
    {
        Type contract = RequireType("ConfigurationImportProblem");
        string[] properties = contract
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(contract.IsSealed).IsTrue();
        await Assert.That(properties)
            .IsEquivalentTo(["Code", "RetryAfterSeconds", "Status"]);
        await Assert.That(properties.Any(name =>
                name.Contains("Artifact", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Content", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Digest", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Identity", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Path", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Token", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Value", StringComparison.OrdinalIgnoreCase)))
            .IsFalse();
    }

    private static Type RequireType(string name) =>
        ApiAssembly.GetType($"Explore.API.ConfigurationImport.{name}")
        ?? throw new InvalidOperationException(
            $"Missing configuration import API contract: {name}.");

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
}
