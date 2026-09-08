using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TUnit.Assertions;
using TUnit.Core;

namespace Explore.Blazor.Client.Tests.Services;

public class ContactShareConsentServiceTests
{
    private readonly IContactShareConsentClient _apiClient;
    private readonly ILogger<ContactShareConsentService> _logger;
    private readonly ContactShareConsentService _service;

    public ContactShareConsentServiceTests()
    {
        _apiClient = Substitute.For<IContactShareConsentClient>();
        _logger = Substitute.For<ILogger<ContactShareConsentService>>();
        _service = new ContactShareConsentService(_apiClient, _logger);
    }

    #region CheckConsentForOrganizerAsync

    [Test]
    public async Task CheckConsentForOrganizerAsync_ReturnsTrue_WhenApiReturnsTrue()
    {
        var actorId = Guid.NewGuid();
        _apiClient.CheckConsentForOrganizerAsync(actorId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await _service.CheckConsentForOrganizerAsync(actorId);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task CheckConsentForOrganizerAsync_ReturnsFalse_WhenApiReturnsFalse()
    {
        var actorId = Guid.NewGuid();
        _apiClient.CheckConsentForOrganizerAsync(actorId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await _service.CheckConsentForOrganizerAsync(actorId);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CheckConsentForOrganizerAsync_ReturnsFalse_WhenApiThrows404()
    {
        var actorId = Guid.NewGuid();
        _apiClient.CheckConsentForOrganizerAsync(actorId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Not found", 404));

        var result = await _service.CheckConsentForOrganizerAsync(actorId);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task CheckConsentForOrganizerAsync_ReturnsFalse_WhenApiThrowsServerError()
    {
        var actorId = Guid.NewGuid();
        _apiClient.CheckConsentForOrganizerAsync(actorId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Server error"));

        var result = await _service.CheckConsentForOrganizerAsync(actorId);

        await Assert.That(result).IsFalse();
    }

    #endregion

    #region GetMyConsentsAsync

    [Test]
    public async Task GetMyConsentsAsync_ReturnsMappedViewModels_WhenApiSucceeds()
    {
        var consents = new List<UserContactShareConsentDto>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RecipientActorId = Guid.NewGuid(),
                OrganizationName = "Test Org",
                PurposeCode = "ORGANIZER_FUTURE_COMMUNICATIONS",
                Status = 1, // Granted
                EmailSnapshot = "user@example.com",
                GrantedAt = TestTime.UtcNow.AddDays(-5)
            }
        };
        _apiClient.GetUserContactShareConsentsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(consents);

        var result = await _service.GetMyConsentsAsync();

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].OrganizationName).IsEqualTo("Test Org");
        await Assert.That(result[0].Status).IsEqualTo("Granted");
        await Assert.That(result[0].EmailSnapshot).IsEqualTo("user@example.com");
    }

    [Test]
    public async Task GetMyConsentsAsync_ReturnsEmptyList_WhenApiThrows()
    {
        _apiClient.GetUserContactShareConsentsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Error", 500));

        var result = await _service.GetMyConsentsAsync();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetMyConsentsAsync_MapsWithdrawnStatus_Correctly()
    {
        var consents = new List<UserContactShareConsentDto>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RecipientActorId = Guid.NewGuid(),
                OrganizationName = "Org",
                PurposeCode = "ORGANIZER_FUTURE_COMMUNICATIONS",
                Status = 2, // Withdrawn
                EmailSnapshot = "user@example.com",
                GrantedAt = TestTime.UtcNow,
                WithdrawnAt = TestTime.UtcNow
            }
        };
        _apiClient.GetUserContactShareConsentsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(consents);

        var result = await _service.GetMyConsentsAsync();

        await Assert.That(result[0].Status).IsEqualTo("Withdrawn");
    }

    #endregion

    #region WithdrawConsentAsync

    [Test]
    public async Task WithdrawConsentAsync_ReturnsTrue_WhenApiSucceeds()
    {
        var consentId = Guid.NewGuid();
        _apiClient.WithdrawContactShareConsentAsync(consentId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = consentId });

        var result = await _service.WithdrawConsentAsync(consentId);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task WithdrawConsentAsync_ReturnsFalse_WhenApiThrows()
    {
        var consentId = Guid.NewGuid();
        _apiClient.WithdrawContactShareConsentAsync(consentId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Error", 500));

        var result = await _service.WithdrawConsentAsync(consentId);

        await Assert.That(result).IsFalse();
    }

    #endregion

    #region GetOrganizationSharedContactsAsync

    [Test]
    public async Task GetOrganizationSharedContactsAsync_ReturnsMappedViewModels()
    {
        var actorId = Guid.NewGuid();
        var paginatedResult = new PaginatedResultOfSharedContactDto
        {
            Items = new List<SharedContactDto>
            {
                new()
                {
                    ConsentId = Guid.NewGuid(),
                    Email = "user1@example.com",
                    GrantedAt = TestTime.UtcNow,
                    PurposeCode = "ORGANIZER_FUTURE_COMMUNICATIONS"
                }
            },
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 50
        };
        _apiClient.GetOrganizationSharedContactsAsync(actorId, null, null, 1, 50, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(paginatedResult);

        var result = await _service.GetOrganizationSharedContactsAsync(actorId);

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Email).IsEqualTo("user1@example.com");
    }

    [Test]
    public async Task GetOrganizationSharedContactsAsync_ReturnsEmptyList_WhenApiThrows()
    {
        var actorId = Guid.NewGuid();
        _apiClient.GetOrganizationSharedContactsAsync(actorId, null, null, 1, 50, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Error", 500));

        var result = await _service.GetOrganizationSharedContactsAsync(actorId);

        await Assert.That(result).IsEmpty();
    }

    #endregion

    #region ExportSharedContactsAsync

    [Test]
    public async Task ExportSharedContactsAsync_ReturnsBytesAndFileName_WhenApiSucceeds()
    {
        var actorId = Guid.NewGuid();
        var fileContent = System.Text.Encoding.UTF8.GetBytes("Email,GrantedAt\nuser@example.com,2025-01-01");
        var response = new FileContentResult
        {
            FileContents = fileContent,
            FileDownloadName = "shared-contacts-export.csv",
            ContentType = "text/csv"
        };
        _apiClient.ExportOrganizationSharedContactsAsync(actorId, "csv", null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _service.ExportSharedContactsAsync(actorId);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.FileBytes.Length).IsGreaterThan(0);
        await Assert.That(result.Value.FileName).IsEqualTo("shared-contacts-export.csv");
    }

    [Test]
    public async Task ExportSharedContactsAsync_ReturnsNull_WhenApiThrows()
    {
        var actorId = Guid.NewGuid();
        _apiClient.ExportOrganizationSharedContactsAsync(actorId, "csv", null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Error", 500));

        var result = await _service.ExportSharedContactsAsync(actorId);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ExportSharedContactsAsync_TsvFormat_RequestsTsv()
    {
        var actorId = Guid.NewGuid();
        var response = new FileContentResult
        {
            FileContents = System.Text.Encoding.UTF8.GetBytes("data"),
            FileDownloadName = "export.tsv",
            ContentType = "text/tab-separated-values"
        };
        _apiClient.ExportOrganizationSharedContactsAsync(actorId, "tsv", null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _service.ExportSharedContactsAsync(actorId, "tsv");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Value.FileName).IsEqualTo("export.tsv");
    }

    [Test]
    public async Task ExportSharedContactsAsync_UppercaseFormat_NormalizesBeforeApiCall()
    {
        var actorId = Guid.NewGuid();
        var response = new FileContentResult
        {
            FileContents = System.Text.Encoding.UTF8.GetBytes("data"),
            FileDownloadName = "export.tsv",
            ContentType = "text/tab-separated-values"
        };
        _apiClient.ExportOrganizationSharedContactsAsync(actorId, "tsv", null, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(response);

        var result = await _service.ExportSharedContactsAsync(actorId, " TSV ");

        await Assert.That(result).IsNotNull();
        await _apiClient.Received(1).ExportOrganizationSharedContactsAsync(
            actorId,
            "tsv",
            null,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExportSharedContactsAsync_InvalidFormat_ReturnsNullWithoutCallingApi()
    {
        var actorId = Guid.NewGuid();

        var result = await _service.ExportSharedContactsAsync(actorId, "json");

        await Assert.That(result).IsNull();
        await _apiClient.DidNotReceive().ExportOrganizationSharedContactsAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<Guid?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Helpers

    private static ApiException CreateApiException(string message, int statusCode, string response = "")
    {
        return new ApiException(
            message,
            statusCode,
            response,
            new Dictionary<string, IEnumerable<string>>(),
            new InvalidOperationException(message));
    }

    #endregion
}
