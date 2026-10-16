using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.DTOs.CustomPropertyDefinition.Validators;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.DTOs.EventCustomProperty.Validators;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.DTOs.EventSessionCustomProperty.Validators;
using Explore.Application.Features.CustomProperties;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation.Results;

namespace Event.Application.UnitTests.DTOs;

public sealed class CustomPropertyCandidateValidationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RuntimeValuesUseTheSameBoundedPatternPolicy(bool session)
    {
        List<string> Validate(string pattern, string value)
        {
            if (session)
                return CustomPropertyRuntimeValueValidator.ValidateSingle(new EventSessionCustomPropertyDefinition
                {
                    Namespace = "tenant.community", Key = "pattern", DisplayName = "Pattern",
                    IsActive = true, PropertyType = PropertyType.Text, RegexPattern = pattern,
                    MinLength = 2, MaxLength = 3
                }, new SetEventSessionCustomPropertyValueDto { TextValue = value });
            return CustomPropertyRuntimeValueValidator.ValidateSingle(new EventCustomPropertyDefinition
            {
                Namespace = "tenant.community", Key = "pattern", DisplayName = "Pattern",
                IsActive = true, PropertyType = PropertyType.Text, RegexPattern = pattern,
                MinLength = 2, MaxLength = 3
            }, new SetEventCustomPropertyValueDto { TextValue = value });
        }

        await Assert.That(Validate("^[a-z]+$", "abc").Count).IsEqualTo(0);
        await Assert.That(Validate("[", "abc").Count).IsEqualTo(1);
        await Assert.That(Validate(@"(a)\1", "aaa").Count).IsEqualTo(1);
        var oversized = Validate("[", "PRIVATE-VALUE-CANARY");
        await Assert.That(oversized.Count).IsEqualTo(1);
        await Assert.That(string.Join(" ", oversized)).DoesNotContain("PRIVATE-VALUE-CANARY");
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Test]
    [Arguments("general")]
    [Arguments("event")]
    [Arguments("session")]
    public async Task RequiredOptions_RejectNullCollectionAndNestedNullWithoutCoercion(string family)
    {
        var collection = await ValidateJson(family, "\"options\":null");
        await Assert.That(collection.IsValid).IsFalse();
        await Assert.That(collection.Errors.Select(error => error.PropertyName).ToArray())
            .IsEquivalentTo(new[] { "Options" });

        var nested = await ValidateJson(family, "\"propertyType\":\"Option\",\"options\":[null]");
        await Assert.That(nested.IsValid).IsFalse();
        await Assert.That(nested.Errors.Select(error => error.PropertyName).ToArray())
            .IsEquivalentTo(new[] { "Options[0]" });

        await Assert.That((await ValidateJson(family, "")).IsValid).IsTrue();
        await Assert.That((await ValidateJson(family, "\"options\":[]")).IsValid).IsTrue();
        await Assert.That((await ValidateJson(family, "\"propertyType\":\"Option\",\"options\":[]")).IsValid).IsFalse();
        await Assert.That((await ValidateJson(family,
            "\"propertyType\":\"Option\",\"options\":[{\"namespace\":\"tenant.community\",\"key\":\"choice\",\"displayName\":\"Choice\",\"value\":\"choice\"}]")).IsValid).IsTrue();
    }

    [Test]
    [Arguments("general")]
    [Arguments("event")]
    [Arguments("session")]
    public async Task PatternCandidates_UseClosedSyntaxAndExactSizeBoundary(string family)
    {
        foreach (var pattern in new[] { "[", "(?=private-pattern-canary)", @"(private-pattern-canary)\1", new string('a', 2049) })
        {
            var invalid = await ValidateJson(family, "\"regexPattern\":" + JsonSerializer.Serialize(pattern));
            await Assert.That(invalid.IsValid).IsFalse();
            await Assert.That(invalid.Errors.Select(error => error.PropertyName).ToArray())
                .IsEquivalentTo(new[] { "RegexPattern" });
        }
        foreach (var pattern in new[] { "(?#" + new string('a', 2044) + ")", "^[a-z]+$", "", " " })
        {
            await Assert.That((await ValidateJson(family, "\"regexPattern\":" + JsonSerializer.Serialize(pattern))).IsValid).IsTrue();
        }
    }

    [Test]
    [Arguments("general")]
    [Arguments("event")]
    [Arguments("session")]
    public async Task CompleteCandidates_RejectInvalidBoundsAndDefaults_PreservePermittedNulls(string family)
    {
        foreach (var fragment in new[]
        {
            "\"minLength\":5,\"maxLength\":4",
            "\"minLength\":-1",
            "\"maxLength\":-1",
            "\"minLength\":2,\"defaultTextValue\":\"x\"",
            "\"regexPattern\":\"^[a-z]+$\",\"defaultTextValue\":\"PRIVATE-VALUE-CANARY\"",
            "\"propertyType\":\"Number\",\"minNumber\":2,\"defaultNumberValue\":1",
            "\"propertyType\":\"DateTime\",\"minDateTime\":\"2026-10-09T00:00:00Z\",\"defaultDateTimeValue\":\"2026-10-08T00:00:00Z\""
        })
        {
            var result = await ValidateJson(family, fragment);
            await Assert.That(result.IsValid).IsFalse();
            await Assert.That(string.Join(" ", result.Errors.Select(error => error.ErrorMessage)))
                .DoesNotContain("PRIVATE-VALUE-CANARY");
        }
        await Assert.That((await ValidateJson(family,
            "\"description\":null,\"defaultTextValue\":null,\"regexPattern\":null,\"minLength\":null,\"maxLength\":null")).IsValid).IsTrue();
        await Assert.That((await ValidateJson(family,
            "\"description\":\"\",\"minLength\":2,\"maxLength\":2,\"regexPattern\":\"^[a-z]+$\",\"defaultTextValue\":\"ab\"")).IsValid).IsTrue();
    }

    private static async Task<ValidationResult> ValidateJson(string family, string fragment)
    {
        var parent = Guid.CreateVersion7();
        var fields = $"\"entityTypeName\":\"Organization\",\"eventId\":\"{parent}\",\"eventSessionId\":\"{parent}\"," +
            "\"namespace\":\"tenant.community\",\"key\":\"candidate\",\"displayName\":\"Candidate\",\"exposureLevel\":\"Internal\"";
        var propertyType = fragment.Contains("\"propertyType\"", StringComparison.Ordinal)
            ? ""
            : "\"propertyType\":\"Text\",";
        var json = "{" + propertyType + fields + (fragment.Length == 0 ? "" : "," + fragment) + "}";
        return family switch
        {
            "general" => await new CreateCustomPropertyDefinitionDtoValidator()
                .ValidateAsync(JsonSerializer.Deserialize<CreateCustomPropertyDefinitionDto>(json, JsonOptions)!),
            "event" => await new CreateEventCustomPropertyDefinitionDtoValidator()
                .ValidateAsync(JsonSerializer.Deserialize<CreateEventCustomPropertyDefinitionDto>(json, JsonOptions)!),
            "session" => await new CreateEventSessionCustomPropertyDefinitionDtoValidator()
                .ValidateAsync(JsonSerializer.Deserialize<CreateEventSessionCustomPropertyDefinitionDto>(json, JsonOptions)!),
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };
    }
}
