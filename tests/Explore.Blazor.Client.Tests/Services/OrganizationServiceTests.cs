using System.Net;
using System.Net.Http.Json;
using Explore.Blazor.Client.Constants;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Services.Http;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Tests.Services;

/// <summary>
/// Unit tests for OrganizationService.
/// </summary>
/// <remarks>
/// These tests verify:
/// - HAL collection/resource conversion to DTOs
/// - Read-operation fallback behavior (empty/null)
/// - Write-operation re-throw behavior for API failures
/// - Pagination usage with ApiConstants.FirstPage and ApiConstants.DefaultPageSize
/// </remarks>
public class OrganizationServiceTests
{
    private readonly IOrganizationClient _apiClient;
    private readonly IApprovalStatusClient _approvalStatusClient;
    private readonly IOrganizationTenantEvidenceClient _tenantEvidenceClient;
    private readonly IBffClient _bffClient;
    private readonly Microsoft.Extensions.Logging.ILogger<OrganizationService> _logger;
    private readonly OrganizationService _service;

    public OrganizationServiceTests()
    {
        _apiClient = Substitute.For<IOrganizationClient>();
        _approvalStatusClient = Substitute.For<IApprovalStatusClient>();
        _tenantEvidenceClient = Substitute.For<IOrganizationTenantEvidenceClient>();
        _bffClient = Substitute.For<IBffClient>();
        _logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<OrganizationService>>();
        _service = new OrganizationService(
            _apiClient,
            _approvalStatusClient,
            _tenantEvidenceClient,
            _bffClient,
            _logger);
    }

    // ========== GetMyOrganizationsAsync ==========

    #region GetMyOrganizationsAsync Tests

    [Test]
    public async Task GetMyOrganizationsAsync_ReturnsOrganizations_WhenApiSucceeds()
    {
        // Arrange
        var organizations = ComponentDataBuilder.OrganizationListDto.Generate(2);
        var halResponse = CreateOrgCollectionResponse(organizations);

        _apiClient.GetMyOrganizationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        var result = await _service.GetMyOrganizationsAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result.First().FullName).IsEqualTo(organizations.First().FullName);
    }

    [Test]
    public async Task GetMyOrganizationsAsync_ReturnsEmptyList_WhenApiThrows()
    {
        // Arrange
        _apiClient.GetMyOrganizationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("API Error", 500));

        // Act
        var result = await _service.GetMyOrganizationsAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetMyOrganizationsAsync_ReturnsEmptyList_WhenApiReturnsNull()
    {
        // Arrange
        _apiClient.GetMyOrganizationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((HalCollectionResourceOfOrganizationListDto?)null);

        // Act
        var result = await _service.GetMyOrganizationsAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetMyOrganizationsAsync_CallsApiWithCorrectPagination()
    {
        // Arrange
        var halResponse = CreateOrgCollectionResponse(new List<OrganizationListDto>());
        _apiClient.GetMyOrganizationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        await _service.GetMyOrganizationsAsync();

        // Assert
        await _apiClient.Received(1).GetMyOrganizationsAsync(
            ApiConstants.FirstPage,
            ApiConstants.DefaultPageSize,
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    #endregion

    // ========== GetOrganizationsByUserAsync ==========

    #region GetOrganizationsByUserAsync Tests

    [Test]
    public async Task GetOrganizationsByUserAsync_ReturnsOrganizations_WhenApiSucceeds()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var organizations = ComponentDataBuilder.OrganizationListDto.Generate(3);
        var halResponse = CreateOrgCollectionResponse(organizations);

        _apiClient.GetMyOrganizationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        var result = await _service.GetOrganizationsByUserAsync(userId);

        // Assert
        await Assert.That(result.Count).IsEqualTo(3);
        await Assert.That(result.First().Id).IsEqualTo(organizations.First().Id);
    }

    [Test]
    public async Task GetOrganizationsByUserAsync_ReturnsEmptyList_WhenApiThrows()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _apiClient.GetMyOrganizationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Forbidden", 403));

        // Act
        var result = await _service.GetOrganizationsByUserAsync(userId);

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetOrganizationsByUserAsync_CallsApiWithCorrectPagination()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var halResponse = CreateOrgCollectionResponse(new List<OrganizationListDto>());

        _apiClient.GetMyOrganizationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        await _service.GetOrganizationsByUserAsync(userId);

        // Assert
        await _apiClient.Received(1).GetMyOrganizationsAsync(
            ApiConstants.FirstPage,
            ApiConstants.DefaultPageSize,
            Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetOrganizationsByUserAsync_ReturnsEmptyList_WhenApiReturnsNull()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _apiClient.GetMyOrganizationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((HalCollectionResourceOfOrganizationListDto?)null);

        // Act
        var result = await _service.GetOrganizationsByUserAsync(userId);

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetOrganizationsByUserAsync_ReturnsEmptyList_WhenApiThrowsServerError()
    {
        // Arrange
        var userId = Guid.NewGuid();
        _apiClient.GetMyOrganizationsAsync(Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        // Act
        var result = await _service.GetOrganizationsByUserAsync(userId);

        // Assert
        await Assert.That(result).IsEmpty();
    }

    #endregion

    // ========== GetOrganizationByIdAsync ==========

    #region GetOrganizationByIdAsync Tests

    [Test]
    public async Task GetOrganizationByIdAsync_ReturnsOrganization_WhenFound()
    {
        // Arrange
        var organizationId = Guid.NewGuid();
        var organization = ComponentDataBuilder.OrganizationDto.Generate() with { Id = organizationId };
        var halResponse = CreateOrgResourceResponse(organization);

        _apiClient.GetOrganizationByIdAsync(organizationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(halResponse);

        // Act
        var result = await _service.GetOrganizationByIdAsync(organizationId);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo(organizationId);
        await Assert.That(result.FullName).IsEqualTo(organization.FullName);
    }

    [Test]
    public async Task GetOrganizationByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Arrange
        var organizationId = Guid.NewGuid();
        _apiClient.GetOrganizationByIdAsync(organizationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Not Found", 404));

        // Act
        var result = await _service.GetOrganizationByIdAsync(organizationId);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetOrganizationByIdAsync_ReturnsNull_WhenApiThrowsException()
    {
        // Arrange
        var organizationId = Guid.NewGuid();
        _apiClient.GetOrganizationByIdAsync(organizationId, Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server Error", 500));

        // Act
        var result = await _service.GetOrganizationByIdAsync(organizationId);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    // ========== CreateOrganizationAsync ==========

    #region CreateOrganizationAsync Tests

    [Test]
    public async Task CreateOrganizationAsync_ReturnsResponse_WhenSuccess()
    {
        // Arrange
        var createDto = ComponentDataBuilder.CreateOrganizationDto.Generate();
        var expectedResponse = ComponentDataBuilder.SuccessResponse();

        _apiClient.CreateOrganizationAsync(Arg.Any<CreateOrganizationDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        // Act
        var result = await _service.CreateOrganizationAsync(createDto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
    }

    [Test]
    public async Task CreateOrganizationAsync_Throws_WhenApiThrowsApiException()
    {
        // Arrange
        var createDto = ComponentDataBuilder.CreateOrganizationDto.Generate();
        _apiClient.CreateOrganizationAsync(Arg.Any<CreateOrganizationDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Server error", 500));

        // Act & Assert
        await Assert.ThrowsAsync<ApiException>(async () => await _service.CreateOrganizationAsync(createDto));
    }

    #endregion

    // ========== UpdateOrganizationAsync ==========

    #region UpdateOrganizationAsync Tests

    [Test]
    public async Task UpdateOrganizationAsync_ReturnsResponse_WhenSuccess()
    {
        // Arrange
        var organizationId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        var updateDto = new UpdateOrganizationDto
        {
            FullName = new UpdateOrganizationFullNameDto { Value = "Updated Organization" },
            Email = new UpdateOrganizationEmailDto { Value = "updated@example.com" }
        };
        var expectedResponse = ComponentDataBuilder.SuccessResponse(organizationId);

        _apiClient.UpdateOrganizationAsync(Arg.Any<Guid>(), Arg.Any<UpdateOrganizationDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(expectedResponse);

        // Act
        var result = await _service.UpdateOrganizationAsync(organizationId, concurrencyStamp, updateDto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsTrue();
        await Assert.That(result.Id).IsEqualTo(organizationId);
        await _apiClient.Received(1).UpdateOrganizationAsync(
            organizationId,
            updateDto,
            $"\"{concurrencyStamp:D}\"",
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateOrganizationAsync_Throws_WhenApiThrows()
    {
        // Arrange
        var organizationId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        var updateDto = new UpdateOrganizationDto
        {
            FullName = new UpdateOrganizationFullNameDto { Value = "Updated Organization" },
            Email = new UpdateOrganizationEmailDto { Value = "updated@example.com" }
        };

        _apiClient.UpdateOrganizationAsync(Arg.Any<Guid>(), Arg.Any<UpdateOrganizationDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Bad Request", 400));

        // Act & Assert
        await Assert.ThrowsAsync<ApiException>(async () => await _service.UpdateOrganizationAsync(organizationId, concurrencyStamp, updateDto));
    }

    [Test]
    public async Task UpdateOrganizationAsync_ReturnsFailure_WhenConcurrencyStampIsEmpty()
    {
        // Arrange
        var updateDto = new UpdateOrganizationDto
        {
            FullName = new UpdateOrganizationFullNameDto { Value = "Updated Organization" }
        };

        // Act
        var result = await _service.UpdateOrganizationAsync(Guid.NewGuid(), Guid.Empty, updateDto);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Success).IsFalse();
        await _apiClient.DidNotReceive().UpdateOrganizationAsync(
            Arg.Any<Guid>(),
            Arg.Any<UpdateOrganizationDto>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    // ========== GetStatusTypesAsync ==========

    #region GetStatusTypesAsync Tests

    [Test]
    public async Task GetStatusTypesAsync_ReturnsStatusTypes_WhenSuccess()
    {
        // Arrange
        var statuses = new List<StatusTypeListDto>
        {
            new() { Id = 1, FullName = "Pending", MasterCode = "PEND" },
            new() { Id = 2, FullName = "Approved", MasterCode = "APPR" }
        };
        _approvalStatusClient.GetApprovalStatusOptionsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(statuses);

        // Act
        var result = await _service.GetStatusTypesAsync();

        // Assert
        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result.First().FullName).IsEqualTo("Pending");
    }

    [Test]
    public async Task GetStatusTypesAsync_ReturnsEmptyList_WhenApiReturnsNull()
    {
        // Arrange
        _approvalStatusClient.GetApprovalStatusOptionsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((ICollection<StatusTypeListDto>?)null);

        // Act
        var result = await _service.GetStatusTypesAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetStatusTypesAsync_ReturnsEmptyList_WhenApiThrows_Exception()
    {
        // Arrange
        _approvalStatusClient.GetApprovalStatusOptionsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Internal Server Error", 500));

        // Act
        var result = await _service.GetStatusTypesAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task GetStatusTypesAsync_ReturnsEmptyList_WhenApiReturnsUnauthorized()
    {
        // Arrange
        _approvalStatusClient.GetApprovalStatusOptionsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException("Unauthorized", 401));

        // Act
        var result = await _service.GetStatusTypesAsync();

        // Assert
        await Assert.That(result).IsEmpty();
    }

    #endregion

    [Test]
    public async Task UploadTenantEvidenceAsync_UsesBoundPdfSessionBeforeSubmittingStorageObject()
    {
        var organizationId = Guid.NewGuid();
        var storageObjectId = Guid.NewGuid();
        var file = Substitute.For<IBrowserFile>();
        file.Name.Returns("legitimacy.pdf");
        file.ContentType.Returns("application/pdf");
        file.Size.Returns(4);
        file.OpenReadStream(4, Arg.Any<CancellationToken>())
            .Returns(new MemoryStream([1, 2, 3, 4]));

        _bffClient.PostAsync(
                $"/bff/organizations/{organizationId:D}/legitimacy-evidence/upload-session",
                Arg.Any<BffStorageUploadSessionRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BffStorageUploadSessionResponse
                {
                    UploadSessionId = "session-1"
                })
            });
        _bffClient.PostMultipartAsync(
                "/bff/storage/upload-proxy",
                Arg.Any<MultipartFormDataContent>(),
                Arg.Any<CancellationToken>())
            .Returns(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new BffStorageUploadProxyResponse
                {
                    StorageObjectId = storageObjectId
                })
            });
        _tenantEvidenceClient.SubmitOrganizationTenantEvidenceAsync(
                organizationId,
                Arg.Is<SubmitOrganizationTenantEvidenceDto>(dto =>
                    dto.DocumentStorageObjectId == storageObjectId),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var result = await _service.UploadTenantEvidenceAsync(organizationId, file);

        await Assert.That(result).IsTrue();
        await _bffClient.Received(1).PostMultipartAsync(
            "/bff/storage/upload-proxy",
            Arg.Any<MultipartFormDataContent>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ReviewTenantEvidenceAsync_SendsTypedDecisionAndConcurrencyStamp()
    {
        var organizationId = Guid.NewGuid();
        var evidenceId = Guid.NewGuid();
        var concurrencyStamp = Guid.NewGuid();
        var evidence = new OrganizationTenantEvidenceDto
        {
            Id = evidenceId,
            ConcurrencyStamp = concurrencyStamp,
            DocumentDisplayName = "legitimacy.pdf"
        };
        _tenantEvidenceClient.ReviewOrganizationTenantEvidenceAsync(
                organizationId,
                evidenceId,
                Arg.Is<ReviewOrganizationTenantEvidenceDto>(dto =>
                    dto.Decision == OrganizationTenantEvidenceReviewDecisionDto.Approve
                    && dto.ExpectedConcurrencyStamp == concurrencyStamp),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var result = await _service.ReviewTenantEvidenceAsync(
            organizationId,
            evidence,
            approve: true);

        await Assert.That(result).IsTrue();
    }

    #region Organization Approval Tests

    [Test]
    public async Task GetOrganizationRequestsAsync_Success_ReturnsItems()
    {
        var items = new List<OrganizationListDto>
        {
            new() { Id = Guid.NewGuid(), FullName = "Pending Org 1", ApprovalStatusId = ApprovalStatusId.Pending }
        };
        _apiClient.GetOrganizationsAsync(pageNumber: ApiConstants.FirstPage, pageSize: ApiConstants.DefaultPageSize)
            .Returns(CreateOrgCollectionResponse(items));

        var result = await _service.GetOrganizationRequestsAsync();

        await Assert.That(result).IsNotEmpty();
        await Assert.That(result.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetOrganizationRequestsAsync_ApiException_ReturnsEmpty()
    {
        _apiClient.GetOrganizationsAsync(pageNumber: ApiConstants.FirstPage, pageSize: ApiConstants.DefaultPageSize)
            .ThrowsAsync(CreateApiException("Internal Error", 500));

        var result = await _service.GetOrganizationRequestsAsync();

        await Assert.That(result).IsEmpty();
    }

    [Test]
    public async Task ApproveOrganizationAsync_Success_ReturnsTrue()
    {
        var orgId = Guid.NewGuid();
        _apiClient.UpdateOrganizationApprovalStatusAsync(orgId, Arg.Is<UpdateOrganizationApprovalStatusDto>(d => d.ApprovalStatusId == ApprovalStatusId.Approved))
            .Returns(Task.CompletedTask);

        var result = await _service.ApproveOrganizationAsync(orgId);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task RejectOrganizationAsync_Success_ReturnsTrue()
    {
        var orgId = Guid.NewGuid();
        _apiClient.UpdateOrganizationApprovalStatusAsync(orgId, Arg.Is<UpdateOrganizationApprovalStatusDto>(d => d.ApprovalStatusId == ApprovalStatusId.Rejected))
            .Returns(Task.CompletedTask);

        var result = await _service.RejectOrganizationAsync(orgId);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task RevertToPendingAsync_Success_ReturnsTrue()
    {
        var orgId = Guid.NewGuid();
        _apiClient.UpdateOrganizationApprovalStatusAsync(orgId, Arg.Is<UpdateOrganizationApprovalStatusDto>(d => d.ApprovalStatusId == ApprovalStatusId.Pending))
            .Returns(Task.CompletedTask);

        var result = await _service.RevertToPendingAsync(orgId);

        await Assert.That(result).IsTrue();
    }

    #endregion

    // ========== HAL Response Helpers ==========

    #region HAL Response Helpers

    private static HalCollectionResourceOfOrganizationListDto CreateOrgCollectionResponse(
        IList<OrganizationListDto> items)
    {
        return new HalCollectionResourceOfOrganizationListDto
        {
            _embedded = new HalCollectionEmbeddedOfOrganizationListDto
            {
                Items = items.Select(ToHalResource).ToList()
            }
        };
    }

    private static HalResourceOfOrganizationListDto ToHalResource(OrganizationListDto item)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(item);
        return System.Text.Json.JsonSerializer.Deserialize<HalResourceOfOrganizationListDto>(json)
               ?? new HalResourceOfOrganizationListDto();
    }

    private static HalResourceOfOrganizationDto CreateOrgResourceResponse(OrganizationDto dto)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        return System.Text.Json.JsonSerializer.Deserialize<HalResourceOfOrganizationDto>(json)
               ?? new HalResourceOfOrganizationDto();
    }

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
