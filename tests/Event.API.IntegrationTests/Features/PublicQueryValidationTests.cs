using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.API.Models;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Domain.Enums;

namespace Event.Api.IntegrationTests.Features;

public sealed class PublicQueryValidationTests
{
    [Test]
    public async Task EventFilterRequest_WhenPaginationIsOutOfRange_IsInvalid()
    {
        var request = new EventFilterRequest
        {
            PageNumber = 0,
            PageSize = 101
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EventFilterRequest.PageNumber)))).IsTrue();
        await Assert.That(results.Any(result => HasMember(result, nameof(EventFilterRequest.PageSize)))).IsTrue();
    }

    [Test]
    public async Task EventFilterRequest_WhenSortFieldIsUnknown_IsInvalid()
    {
        var request = new EventFilterRequest
        {
            SortBy = "drop-table"
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EventFilterRequest.SortBy)))).IsTrue();
    }

    [Test]
    public async Task EventFilterRequest_WhenViewIsUnknown_IsInvalid()
    {
        var request = new EventFilterRequest
        {
            View = "sideways"
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EventFilterRequest.View)))).IsTrue();
    }

    [Test]
    public async Task EventFilterRequest_WhenViewIsUndefinedNumber_IsInvalid()
    {
        var request = new EventFilterRequest
        {
            View = "999"
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EventFilterRequest.View)))).IsTrue();
    }

    [Test]
    public async Task EventFilterRequest_WhenViewIsKnown_IsValid()
    {
        var request = new EventFilterRequest
        {
            View = "upcomingAndOngoing"
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EventFilterRequest.View)))).IsFalse();
    }

    [Test]
    public async Task EventFilterRequest_WhenDateRangeIsInverted_IsInvalid()
    {
        var request = new EventFilterRequest
        {
            DateFrom = new DateOnly(2026, 6, 2),
            DateTo = new DateOnly(2026, 6, 1)
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EventFilterRequest.DateFrom)))).IsTrue();
        await Assert.That(results.Any(result => HasMember(result, nameof(EventFilterRequest.DateTo)))).IsTrue();
    }

    [Test]
    public async Task EventFilterRequest_WhenModeIsUnknown_IsInvalid()
    {
        var request = new EventFilterRequest
        {
            InclusionMode = "xor"
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EventFilterRequest.InclusionMode)))).IsTrue();
    }

    [Test]
    public async Task EventFilterRequest_WhenLookupListContainsNonPositiveValue_IsInvalid()
    {
        var request = new EventFilterRequest
        {
            FormatIds = [1, 0]
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EventFilterRequest.FormatIds)))).IsTrue();
    }

    [Test]
    public async Task EventFilterRequest_WhenGuidFiltersContainEmptyGuid_IsInvalid()
    {
        var request = new EventFilterRequest
        {
            ActorId = Guid.Empty,
            IncludedTagIds = [Guid.NewGuid(), Guid.Empty]
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EventFilterRequest.ActorId)))).IsTrue();
        await Assert.That(results.Any(result => HasMember(result, nameof(EventFilterRequest.IncludedTagIds)))).IsTrue();
    }

    [Test]
    public async Task EventFilterRequest_WhenCustomPropertyFilterShapeIsInvalid_IsInvalid()
    {
        var request = new EventFilterRequest
        {
            CustomPropertyFilters =
            [
                new CustomPropertyFilterCriterion
                {
                    Namespace = "tenant",
                    Key = "region",
                    Operator = CustomPropertyFilterOperator.OptionIn
                }
            ]
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, "CustomPropertyFilters[0].OptionIds"))).IsTrue();
    }

    [Test]
    public async Task EventFilterRequest_WhenCustomPropertyOptionIdIsEmpty_IsInvalid()
    {
        var request = new EventFilterRequest
        {
            CustomPropertyFilters =
            [
                new CustomPropertyFilterCriterion
                {
                    Namespace = "tenant",
                    Key = "region",
                    Operator = CustomPropertyFilterOperator.OptionEquals,
                    OptionId = Guid.Empty
                }
            ]
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, "CustomPropertyFilters[0].OptionId"))).IsTrue();
    }

    [Test]
    public async Task EventSessionFilterRequest_WhenSearchTermIsTooLong_IsInvalid()
    {
        var request = new EventSessionFilterRequest
        {
            CustomPropertySearchTerm = new string('x', 201)
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EventSessionFilterRequest.CustomPropertySearchTerm)))).IsTrue();
    }

    [Test]
    public async Task EventSessionFilterRequest_WhenPaginationIsOutOfRange_IsInvalid()
    {
        var request = new EventSessionFilterRequest
        {
            PageNumber = 0,
            PageSize = 101
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EventSessionFilterRequest.PageNumber)))).IsTrue();
        await Assert.That(results.Any(result => HasMember(result, nameof(EventSessionFilterRequest.PageSize)))).IsTrue();
    }

    [Test]
    public async Task EventSessionFilterRequest_WhenCustomPropertyFilterDateRangeIsInverted_IsInvalid()
    {
        var request = new EventSessionFilterRequest
        {
            CustomPropertyFilters =
            [
                new CustomPropertyFilterCriterion
                {
                    Namespace = "tenant",
                    Key = "track",
                    Operator = CustomPropertyFilterOperator.DateRange,
                    DateFrom = new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero),
                    DateTo = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)
                }
            ]
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, "CustomPropertyFilters[0].DateFrom"))).IsTrue();
        await Assert.That(results.Any(result => HasMember(result, "CustomPropertyFilters[0].DateTo"))).IsTrue();
    }

    [Test]
    public async Task PaginationQueryRequest_WhenPaginationIsOutOfRange_IsInvalid()
    {
        var request = new PaginationQueryRequest
        {
            PageNumber = -1,
            PageSize = 101
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(PaginationQueryRequest.PageNumber)))).IsTrue();
        await Assert.That(results.Any(result => HasMember(result, nameof(PaginationQueryRequest.PageSize)))).IsTrue();
    }

    [Test]
    public async Task EventSeriesListQueryRequest_WhenActorIdIsEmpty_IsInvalid()
    {
        var request = new EventSeriesListQueryRequest
        {
            ActorId = Guid.Empty
        };

        var results = Validate(request);

        await Assert.That(request.PageSize).IsEqualTo(10);
        await Assert.That(results.Any(result => HasMember(result, nameof(EventSeriesListQueryRequest.ActorId)))).IsTrue();
    }

    [Test]
    public async Task EventTemplateListQueryRequest_WhenEventTypeIdIsNonPositive_IsInvalid()
    {
        var request = new EventTemplateListQueryRequest
        {
            EventTypeId = 0
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EventTemplateListQueryRequest.EventTypeId)))).IsTrue();
    }

    [Test]
    public async Task EventSessionTemplateListQueryRequest_WhenParentTemplateIdIsMissing_IsInvalid()
    {
        var request = new EventSessionTemplateListQueryRequest
        {
            EventTemplateId = Guid.Empty
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(EventSessionTemplateListQueryRequest.EventTemplateId)))).IsTrue();
    }

    [Test]
    public async Task NotificationListQueryRequest_WhenLookupFilterIsNonPositive_IsInvalid()
    {
        var request = new NotificationListQueryRequest
        {
            NotificationTypeId = -1,
            NotificationScopeId = 0
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(NotificationListQueryRequest.NotificationTypeId)))).IsTrue();
        await Assert.That(results.Any(result => HasMember(result, nameof(NotificationListQueryRequest.NotificationScopeId)))).IsTrue();
    }

    [Test]
    public async Task ContactShareConsentListQueryRequest_WhenEmailSearchIsTooLong_IsInvalid()
    {
        var request = new ContactShareConsentListQueryRequest
        {
            EmailSearch = new string('x', 201)
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(ContactShareConsentListQueryRequest.EmailSearch)))).IsTrue();
    }

    [Test]
    public async Task ContactShareConsentExportQueryRequest_WhenFormatIsUnknown_IsInvalid()
    {
        var request = new ContactShareConsentExportQueryRequest
        {
            Format = "json"
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(ContactShareConsentExportQueryRequest.Format)))).IsTrue();
    }

    [Test]
    public async Task ContactShareConsentExportQueryRequest_WhenFormatContainsControlCharacter_IsInvalid()
    {
        var request = new ContactShareConsentExportQueryRequest
        {
            Format = "csv\n"
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(ContactShareConsentExportQueryRequest.Format)))).IsTrue();
    }

    [Test]
    public async Task ContactShareConsentExportQueryRequest_WhenFormatIsSupported_NormalizesFormat()
    {
        var request = new ContactShareConsentExportQueryRequest
        {
            Format = " TSV "
        };

        var results = Validate(request);

        await Assert.That(results).IsEmpty();
        await Assert.That(request.GetNormalizedFormat()).IsEqualTo("tsv");
    }

    [Test]
    public async Task ExternalApiKeyUsageReportQueryRequest_WhenDatesAreMissing_IsInvalid()
    {
        var request = new ExternalApiKeyUsageReportQueryRequest();

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(ExternalApiKeyUsageReportQueryRequest.From)))).IsTrue();
        await Assert.That(results.Any(result => HasMember(result, nameof(ExternalApiKeyUsageReportQueryRequest.To)))).IsTrue();
    }

    [Test]
    public async Task ExternalApiKeyUsageReportQueryRequest_WhenDateRangeIsInverted_IsInvalid()
    {
        var request = new ExternalApiKeyUsageReportQueryRequest
        {
            From = new DateOnly(2026, 2, 1),
            To = new DateOnly(2026, 1, 31)
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(ExternalApiKeyUsageReportQueryRequest.From)))).IsTrue();
        await Assert.That(results.Any(result => HasMember(result, nameof(ExternalApiKeyUsageReportQueryRequest.To)))).IsTrue();
    }

    [Test]
    public async Task ExternalApiKeyUsageReportQueryRequest_WhenRangeIsTooLarge_IsInvalid()
    {
        var request = new ExternalApiKeyUsageReportQueryRequest
        {
            From = new DateOnly(2026, 1, 1),
            To = new DateOnly(2027, 1, 2)
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(ExternalApiKeyUsageReportQueryRequest.From)))).IsTrue();
        await Assert.That(results.Any(result => HasMember(result, nameof(ExternalApiKeyUsageReportQueryRequest.To)))).IsTrue();
    }

    [Test]
    public async Task ExternalApiKeyUsageReportQueryRequest_WhenTenantIdIsEmpty_IsInvalid()
    {
        var request = new ExternalApiKeyUsageReportQueryRequest
        {
            From = new DateOnly(2026, 1, 1),
            To = new DateOnly(2026, 1, 31),
            TenantId = Guid.Empty
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(ExternalApiKeyUsageReportQueryRequest.TenantId)))).IsTrue();
    }

    [Test]
    public async Task CustomPropertyGovernanceReportQueryRequest_WhenTenantAndEnumAreInvalid_IsInvalid()
    {
        var request = new CustomPropertyGovernanceReportQueryRequest
        {
            TenantId = Guid.Empty,
            Recommendation = (PromotionRecommendation)999
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(CustomPropertyGovernanceReportQueryRequest.TenantId)))).IsTrue();
        await Assert.That(results.Any(result => HasMember(result, nameof(CustomPropertyGovernanceReportQueryRequest.Recommendation)))).IsTrue();
    }

    [Test]
    public async Task CustomPropertyProjectionDirtyScopesQueryRequest_WhenProjectionNameIsMissing_IsInvalid()
    {
        var request = new CustomPropertyProjectionDirtyScopesQueryRequest
        {
            TenantId = Guid.NewGuid(),
            ProjectionName = " "
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(CustomPropertyProjectionDirtyScopesQueryRequest.ProjectionName)))).IsTrue();
    }

    [Test]
    public async Task TemplateSyncHistoryQueryRequest_WhenPageOrPageSizeIsOutOfRange_IsInvalid()
    {
        var request = new TemplateSyncHistoryQueryRequest
        {
            Page = 0,
            PageSize = 101
        };

        var results = Validate(request);

        await Assert.That(results.Any(result => HasMember(result, nameof(TemplateSyncHistoryQueryRequest.Page)))).IsTrue();
        await Assert.That(results.Any(result => HasMember(result, nameof(TemplateSyncHistoryQueryRequest.PageSize)))).IsTrue();
    }

    private static List<ValidationResult> Validate(object request)
    {
        var results = new List<ValidationResult>();
        var context = new ValidationContext(request);
        Validator.TryValidateObject(request, context, results, validateAllProperties: true);
        return results;
    }

    private static bool HasMember(ValidationResult result, string memberName)
        => result.MemberNames.Contains(memberName, StringComparer.Ordinal);
}

[NotInParallel("ApiTestFixture")]
[ClassDataSource<ApiTestFixture>(Shared = SharedType.PerAssembly)]
public sealed class PublicQueryRuntimeValidationTests(ApiTestFixture fixture)
{
    [Test]
    public async Task EventList_WhenViewIsUnknown_ReturnsValidationProblemDetails()
    {
        var response = await fixture.Client.GetAsync("/api/Event?view=sideways");

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "Validation failed");

        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;

        await Assert.That(root.GetProperty("title").GetString()).IsEqualTo("Validation failed");
        await Assert.That(root.GetProperty("errors").TryGetProperty("view", out _)).IsTrue();
        await Assert.That(content.Contains("sideways", StringComparison.Ordinal)).IsFalse();
    }
}
