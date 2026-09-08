using System.Text.Json;
using Explore.Application.Features.RegistrationSubmissions;
using Explore.Domain.Enums;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.RegistrationSubmissions;

public sealed class RegistrationAnswerNormalizerTests
{
    public static IEnumerable<Func<(RegistrationFieldTypeEnum Type, string Json, string Canonical)>> ValidCases()
    {
        yield return () => (RegistrationFieldTypeEnum.ShortText, "\"  Cafe\\u0301  \"", "Café");
        yield return () => (RegistrationFieldTypeEnum.LongText, "\"a\\r\\nb\"", "a\nb");
        yield return () => (RegistrationFieldTypeEnum.Integer, "9223372036854775807", "9223372036854775807");
        yield return () => (RegistrationFieldTypeEnum.Decimal, "123.450", "123.450");
        yield return () => (RegistrationFieldTypeEnum.Boolean, "true", "true");
        yield return () => (RegistrationFieldTypeEnum.Date, "\"2024-02-29\"", "2024-02-29");
        yield return () => (RegistrationFieldTypeEnum.Time, "\"23:59:59\"", "23:59:59");
        yield return () => (RegistrationFieldTypeEnum.Instant, "\"2026-08-02T12:34:56Z\"", "2026-08-02T12:34:56.0000000Z");
        yield return () => (RegistrationFieldTypeEnum.Email, "\" Test@example.com \"", "Test@example.com");
        yield return () => (RegistrationFieldTypeEnum.Phone, "\"+32470123456\"", "+32470123456");
        yield return () => (RegistrationFieldTypeEnum.Url, "\"https://example.com/a\"", "https://example.com/a");
        yield return () => (RegistrationFieldTypeEnum.CountryCode, "\"be\"", "BE");
        yield return () => (RegistrationFieldTypeEnum.LanguageTag, "\"fr-be\"", "fr-BE");
        yield return () => (RegistrationFieldTypeEnum.SingleChoice, "\"018f0000-0000-7000-8000-000000000001\"", "018f0000-0000-7000-8000-000000000001");
        yield return () => (RegistrationFieldTypeEnum.MultipleChoice, "[\"018f0000-0000-7000-8000-000000000001\",\"018f0000-0000-7000-8000-000000000002\"]", "018f0000-0000-7000-8000-000000000001,018f0000-0000-7000-8000-000000000002");
        yield return () => (RegistrationFieldTypeEnum.Rating, "5", "5");
        yield return () => (RegistrationFieldTypeEnum.Consent, "true", "true");
    }

    public static IEnumerable<Func<(RegistrationFieldTypeEnum Type, string Json)>> InvalidCases()
    {
        yield return () => (RegistrationFieldTypeEnum.ShortText, "\"<b>x</b>\"");
        yield return () => (RegistrationFieldTypeEnum.LongText, "\"   \"");
        yield return () => (RegistrationFieldTypeEnum.Integer, "\"1\"");
        yield return () => (RegistrationFieldTypeEnum.Decimal, "\"1.2\"");
        yield return () => (RegistrationFieldTypeEnum.Boolean, "\"true\"");
        yield return () => (RegistrationFieldTypeEnum.Date, "\"2023-02-29\"");
        yield return () => (RegistrationFieldTypeEnum.Time, "\"24:00:00\"");
        yield return () => (RegistrationFieldTypeEnum.Instant, "\"2026-08-02 12:34:56\"");
        yield return () => (RegistrationFieldTypeEnum.Email, "\"not-an-email\"");
        yield return () => (RegistrationFieldTypeEnum.Phone, "\"0470123456\"");
        yield return () => (RegistrationFieldTypeEnum.Url, "\"javascript:alert(1)\"");
        yield return () => (RegistrationFieldTypeEnum.CountryCode, "\"ZZ\"");
        yield return () => (RegistrationFieldTypeEnum.LanguageTag, "\"not_a_tag\"");
        yield return () => (RegistrationFieldTypeEnum.SingleChoice, "1");
        yield return () => (RegistrationFieldTypeEnum.MultipleChoice, "[\"018f0000-0000-7000-8000-000000000001\",\"018f0000-0000-7000-8000-000000000001\"]");
        yield return () => (RegistrationFieldTypeEnum.Rating, "0");
        yield return () => (RegistrationFieldTypeEnum.Consent, "false");
    }

    public static IEnumerable<Func<(RegistrationFieldTypeEnum Type, RegistrationFieldNormalizationSpec Spec,
        string Json, bool IsValid, string? Canonical, string? IssueCode)>> BoundaryCases()
    {
        yield return () => (RegistrationFieldTypeEnum.ShortText,
            new(RegistrationFieldTypeEnum.ShortText, null, null, 3, 10, null, null, null, null),
            "\"abc\"", true, "abc", null);
        yield return () => (RegistrationFieldTypeEnum.LongText,
            new(RegistrationFieldTypeEnum.LongText, null, null, null, 5, null, null, null, null),
            "\"abcde\"", true, "abcde", null);
        yield return () => (RegistrationFieldTypeEnum.Integer,
            new(RegistrationFieldTypeEnum.Integer, -10, 10, null, null, null, null, null, null),
            "-10", true, "-10", null);
        yield return () => (RegistrationFieldTypeEnum.Decimal,
            new(RegistrationFieldTypeEnum.Decimal, -1.25m, 1.25m, null, null, null, null, null, null),
            "1.25", true, "1.25", null);
        yield return () => (RegistrationFieldTypeEnum.Boolean,
            DefaultSpec(RegistrationFieldTypeEnum.Boolean), "false", true, "false", null);
        yield return () => (RegistrationFieldTypeEnum.Date,
            new(RegistrationFieldTypeEnum.Date, null, null, null, null, null,
                new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), null, null),
            "\"2024-01-01\"", true, "2024-01-01", null);
        yield return () => (RegistrationFieldTypeEnum.Time,
            DefaultSpec(RegistrationFieldTypeEnum.Time), "\"23:59:59.9999999\"", true, "23:59:59.9999999", null);
        yield return () => (RegistrationFieldTypeEnum.Instant,
            new(RegistrationFieldTypeEnum.Instant, null, null, null, null, null, null,
                new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero), null),
            "\"2026-08-02T14:00:00+02:00\"", true, "2026-08-02T12:00:00.0000000Z", null);
        yield return () => (RegistrationFieldTypeEnum.Email,
            new(RegistrationFieldTypeEnum.Email, null, null, 6, 6, null, null, null, null),
            "\"a@b.co\"", true, "a@b.co", null);
        yield return () => (RegistrationFieldTypeEnum.Phone,
            new(RegistrationFieldTypeEnum.Phone, null, null, 16, 16, null, null, null, null),
            "\"+123456789012345\"", true, "+123456789012345", null);
        yield return () => (RegistrationFieldTypeEnum.Url,
            new(RegistrationFieldTypeEnum.Url, null, null, null, null, null, null, null, "ftp"),
            "\"ftp://example.com\"", true, "ftp://example.com/", null);
        yield return () => (RegistrationFieldTypeEnum.CountryCode,
            DefaultSpec(RegistrationFieldTypeEnum.CountryCode), "\"B\"", false, null, "INVALID_COUNTRY_CODE");
        yield return () => (RegistrationFieldTypeEnum.LanguageTag,
            DefaultSpec(RegistrationFieldTypeEnum.LanguageTag), "\"en\"", true, "en", null);
        yield return () => (RegistrationFieldTypeEnum.SingleChoice,
            DefaultSpec(RegistrationFieldTypeEnum.SingleChoice),
            "\"00000000-0000-0000-0000-000000000000\"", false, null, "INVALID_OPTION");
        yield return () => (RegistrationFieldTypeEnum.MultipleChoice,
            DefaultSpec(RegistrationFieldTypeEnum.MultipleChoice),
            "[\"018f0000-0000-7000-8000-000000000001\"]", true,
            "018f0000-0000-7000-8000-000000000001", null);
        yield return () => (RegistrationFieldTypeEnum.Rating,
            new(RegistrationFieldTypeEnum.Rating, 1, 5, null, null, null, null, null, null),
            "1", true, "1", null);
        yield return () => (RegistrationFieldTypeEnum.Consent,
            DefaultSpec(RegistrationFieldTypeEnum.Consent), "null", false, null, "CONSENT_NOT_GRANTED");
    }

    [Test]
    [MethodDataSource(nameof(ValidCases))]
    public async Task ValidPortableValueNormalizesWithoutCoercion(RegistrationFieldTypeEnum type, string json, string canonical)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        RegistrationValueNormalizationResult result = RegistrationAnswerNormalizer.Normalize(
            new(type, type == RegistrationFieldTypeEnum.Rating ? 1 : null,
                type == RegistrationFieldTypeEnum.Rating ? 5 : null, null, null, null, null, null, null),
            document.RootElement);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Value!.Canonical).IsEqualTo(canonical);
    }

    [Test]
    [MethodDataSource(nameof(InvalidCases))]
    public async Task InvalidPortableValueIsRejectedWithoutCoercionOrHtml(RegistrationFieldTypeEnum type, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        RegistrationValueNormalizationResult result = RegistrationAnswerNormalizer.Normalize(
            new(type, type == RegistrationFieldTypeEnum.Rating ? 1 : null,
                type == RegistrationFieldTypeEnum.Rating ? 5 : null, null, null, null, null, null, null),
            document.RootElement);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.IssueCode).IsNotNull();
    }

    [Test]
    [MethodDataSource(nameof(BoundaryCases))]
    public async Task PortableValueBoundaryIsAppliedWithoutCoercion(
        RegistrationFieldTypeEnum type,
        RegistrationFieldNormalizationSpec spec,
        string json,
        bool isValid,
        string? canonical,
        string? issueCode)
    {
        await Assert.That(spec.FieldType).IsEqualTo(type);
        using JsonDocument document = JsonDocument.Parse(json);
        RegistrationValueNormalizationResult result = RegistrationAnswerNormalizer.Normalize(spec, document.RootElement);

        await Assert.That(result.IsValid).IsEqualTo(isValid);
        await Assert.That(result.Value?.Canonical).IsEqualTo(canonical);
        await Assert.That(result.IssueCode).IsEqualTo(issueCode);
    }

    private static RegistrationFieldNormalizationSpec DefaultSpec(RegistrationFieldTypeEnum type) =>
        new(type, null, null, null, null, null, null, null, null);
}
