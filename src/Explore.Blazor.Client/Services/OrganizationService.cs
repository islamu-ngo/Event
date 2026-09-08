using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Constants;
using Explore.Blazor.Client.Extensions;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Services.Http;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

/// <summary>
/// Service for managing organization-related operations.
/// Returns clean DTOs, converting from HAL wrapper types internally.
/// </summary>
public interface IOrganizationService
{
    /// <summary>
    /// Creates a new organization.
    /// </summary>
    Task<BaseCommandResponseOfGuid?> CreateOrganizationAsync(CreateOrganizationDto organization);

    /// <summary>
    /// Gets all available approval status types.
    /// </summary>
    Task<ICollection<StatusTypeListDto>> GetStatusTypesAsync();

    /// <summary>
    /// Gets organizations for the current authenticated user.
    /// </summary>
    Task<ICollection<OrganizationListDto>> GetMyOrganizationsAsync();

    /// <summary>
    /// Gets a paginated list of all organizations.
    /// </summary>
    Task<PaginatedResult<OrganizationListDto>> GetOrganizationsPagedAsync(int pageNumber, int pageSize);

    /// <summary>
    /// Gets a paginated list of organizations for the current authenticated user.
    /// </summary>
    Task<PaginatedResult<OrganizationListDto>> GetMyOrganizationsPagedAsync(int pageNumber, int pageSize);

    /// <summary>
    /// Gets organizations for a specific user.
    /// </summary>
    Task<ICollection<OrganizationListDto>> GetOrganizationsByUserAsync(Guid userId);

    /// <summary>
    /// Gets a single organization by ID.
    /// </summary>
    Task<OrganizationDto?> GetOrganizationByIdAsync(Guid id);

    /// <summary>
    /// Updates an existing organization.
    /// </summary>
    Task<BaseCommandResponseOfGuid?> UpdateOrganizationAsync(Guid id, Guid expectedConcurrencyStamp, UpdateOrganizationDto organization);

    Task<ICollection<OrganizationTenantEvidenceDto>> GetTenantEvidenceAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<bool> UploadTenantEvidenceAsync(
        Guid organizationId,
        IBrowserFile file,
        CancellationToken cancellationToken = default);

    Task<bool> ReviewTenantEvidenceAsync(
        Guid organizationId,
        OrganizationTenantEvidenceDto evidence,
        bool approve,
        CancellationToken cancellationToken = default);

    Task<ICollection<OrganizationListDto>> GetOrganizationRequestsAsync();
    Task<bool> ApproveOrganizationAsync(Guid id);
    Task<bool> RejectOrganizationAsync(Guid id);
    Task<bool> RevertToPendingAsync(Guid id);
}

/// <summary>
/// Implementation of organization service using the Event API client.
/// Acts as an Anti-Corruption Layer, converting HAL types to clean DTOs.
/// </summary>
public class OrganizationService : IOrganizationService
{
    private readonly IOrganizationClient _apiClient;
    private readonly IApprovalStatusClient _approvalStatusClient;
    private readonly IOrganizationTenantEvidenceClient _tenantEvidenceClient;
    private readonly IBffClient _bffClient;
    private readonly ILogger<OrganizationService> _logger;

    public OrganizationService(
        IOrganizationClient apiClient,
        IApprovalStatusClient approvalStatusClient,
        IOrganizationTenantEvidenceClient tenantEvidenceClient,
        IBffClient bffClient,
        ILogger<OrganizationService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _approvalStatusClient = approvalStatusClient ?? throw new ArgumentNullException(nameof(approvalStatusClient));
        _tenantEvidenceClient = tenantEvidenceClient ?? throw new ArgumentNullException(nameof(tenantEvidenceClient));
        _bffClient = bffClient ?? throw new ArgumentNullException(nameof(bffClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<BaseCommandResponseOfGuid?> CreateOrganizationAsync(CreateOrganizationDto organization)
    {
        try
        {
            return await _apiClient.CreateOrganizationAsync(organization);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[OrganizationService.CreateOrganizationAsync] API error creating organization. StatusCode: {StatusCode}", ex.StatusCode);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ICollection<StatusTypeListDto>> GetStatusTypesAsync()
    {
        try
        {
            return await _approvalStatusClient.GetApprovalStatusOptionsAsync() ?? new List<StatusTypeListDto>();
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[OrganizationService.GetStatusTypesAsync] API error fetching status types. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<StatusTypeListDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrganizationService.GetStatusTypesAsync] Unexpected error fetching status types");
            return new List<StatusTypeListDto>();
        }
    }

    /// <inheritdoc />
    public async Task<ICollection<OrganizationListDto>> GetMyOrganizationsAsync()
    {
        try
        {
            var result = await _apiClient.GetMyOrganizationsAsync(pageNumber: ApiConstants.FirstPage, pageSize: ApiConstants.DefaultPageSize);
            return result?.GetItems() ?? new List<OrganizationListDto>();
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[OrganizationService.GetMyOrganizationsAsync] API error fetching my organizations. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<OrganizationListDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrganizationService.GetMyOrganizationsAsync] Unexpected error fetching my organizations");
            return new List<OrganizationListDto>();
        }
    }

    /// <inheritdoc />
    public async Task<PaginatedResult<OrganizationListDto>> GetOrganizationsPagedAsync(int pageNumber, int pageSize)
    {
        try
        {
            var result = await _apiClient.GetOrganizationsAsync(pageNumber, pageSize);
            return result.ToPaginatedResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrganizationService.GetOrganizationsPagedAsync] Error fetching paged organizations (page {PageNumber}, size {PageSize})", pageNumber, pageSize);
            return PaginatedResult<OrganizationListDto>.Empty(pageNumber, pageSize);
        }
    }

    /// <inheritdoc />
    public async Task<PaginatedResult<OrganizationListDto>> GetMyOrganizationsPagedAsync(int pageNumber, int pageSize)
    {
        try
        {
            var result = await _apiClient.GetMyOrganizationsAsync(pageNumber, pageSize);
            return result.ToPaginatedResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrganizationService.GetMyOrganizationsPagedAsync] Error fetching my paged organizations (page {PageNumber}, size {PageSize})", pageNumber, pageSize);
            return PaginatedResult<OrganizationListDto>.Empty(pageNumber, pageSize);
        }
    }

    /// <inheritdoc />
    public async Task<ICollection<OrganizationListDto>> GetOrganizationsByUserAsync(Guid userId)
    {
        try
        {
            // Note: This endpoint may not exist or may need HAL conversion
            // For now, we'll try the direct approach
            var result = await _apiClient.GetMyOrganizationsAsync(pageNumber: ApiConstants.FirstPage, pageSize: ApiConstants.DefaultPageSize);
            return result?.GetItems() ?? new List<OrganizationListDto>();
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[OrganizationService.GetOrganizationsByUserAsync] API error fetching organizations for user {UserId}. StatusCode: {StatusCode}", userId, ex.StatusCode);
            return new List<OrganizationListDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrganizationService.GetOrganizationsByUserAsync] Unexpected error fetching organizations for user {UserId}", userId);
            return new List<OrganizationListDto>();
        }
    }

    /// <inheritdoc />
    public async Task<OrganizationDto?> GetOrganizationByIdAsync(Guid id)
    {
        try
        {
            var result = await _apiClient.GetOrganizationByIdAsync(id);
            return result?.ToDto();
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("[OrganizationService.GetOrganizationByIdAsync] Organization not found. OrganizationId: {OrganizationId}", id);
            return null;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[OrganizationService.GetOrganizationByIdAsync] API error fetching organization. OrganizationId: {OrganizationId}, StatusCode: {StatusCode}", id, ex.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrganizationService.GetOrganizationByIdAsync] Unexpected error fetching organization. OrganizationId: {OrganizationId}", id);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<BaseCommandResponseOfGuid?> UpdateOrganizationAsync(Guid id, Guid expectedConcurrencyStamp, UpdateOrganizationDto organization)
    {
        try
        {
            if (id == Guid.Empty || expectedConcurrencyStamp == Guid.Empty)
            {
                return new BaseCommandResponseOfGuid
                {
                    Success = false,
                    Message = "Organization ID and concurrency stamp are required."
                };
            }

            return await _apiClient.UpdateOrganizationAsync(id, organization, $"\"{expectedConcurrencyStamp:D}\"");
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[OrganizationService.UpdateOrganizationAsync] API error updating organization. OrganizationId: {OrganizationId}, StatusCode: {StatusCode}", id, ex.StatusCode);
            throw;
        }
    }

    public async Task<ICollection<OrganizationTenantEvidenceDto>> GetTenantEvidenceAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _tenantEvidenceClient.GetOrganizationTenantEvidenceCollectionAsync(
                organizationId,
                cancellationToken: cancellationToken);
            return result.GetItems();
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(
                "Organization evidence could not be loaded. StatusCode={StatusCode}",
                ex.StatusCode);
            throw new InvalidOperationException("Organization evidence could not be loaded.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "Organization evidence could not be loaded. FailureType={FailureType}",
                ex.GetType().Name);
            throw new InvalidOperationException("Organization evidence could not be loaded.");
        }
    }

    public async Task<bool> UploadTenantEvidenceAsync(
        Guid organizationId,
        IBrowserFile file,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty ||
            file.Size <= 0 ||
            !file.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            using var sessionResponse = await _bffClient.PostAsync(
                $"/bff/organizations/{organizationId:D}/legitimacy-evidence/upload-session",
                new BffStorageUploadSessionRequest
                {
                    FileName = file.Name,
                    ContentType = "application/pdf",
                    ExpectedSizeBytes = file.Size
                },
                cancellationToken);
            if (!sessionResponse.IsSuccessStatusCode)
            {
                return false;
            }

            var session = await sessionResponse.ReadJsonOrDefaultAsync<BffStorageUploadSessionResponse>(
                cancellationToken);
            if (string.IsNullOrWhiteSpace(session?.UploadSessionId))
            {
                return false;
            }

            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(session.UploadSessionId), "uploadSessionId");
            content.Add(new StringContent("application/pdf"), "contentType");
            using var stream = file.OpenReadStream(file.Size, cancellationToken);
            using var document = new StreamContent(stream);
            document.Headers.ContentType = new("application/pdf");
            content.Add(document, "file", file.Name);

            using var uploadResponse = await _bffClient.PostMultipartAsync(
                "/bff/storage/upload-proxy",
                content,
                cancellationToken);
            if (!uploadResponse.IsSuccessStatusCode)
            {
                return false;
            }

            var upload = await uploadResponse.ReadJsonOrDefaultAsync<BffStorageUploadProxyResponse>(
                cancellationToken);
            if (upload?.StorageObjectId is not { } storageObjectId || storageObjectId == Guid.Empty)
            {
                return false;
            }

            var submitted = await _tenantEvidenceClient.SubmitOrganizationTenantEvidenceAsync(
                organizationId,
                new SubmitOrganizationTenantEvidenceDto
                {
                    DocumentStorageObjectId = storageObjectId
                },
                cancellationToken: cancellationToken);
            return submitted?.Success == true;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(
                "Organization evidence could not be submitted. StatusCode={StatusCode}",
                ex.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "Organization evidence could not be submitted. FailureType={FailureType}",
                ex.GetType().Name);
            return false;
        }
    }

    public async Task<bool> ReviewTenantEvidenceAsync(
        Guid organizationId,
        OrganizationTenantEvidenceDto evidence,
        bool approve,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty ||
            evidence.Id is not { } evidenceId ||
            evidenceId == Guid.Empty ||
            evidence.ConcurrencyStamp is not { } concurrencyStamp ||
            concurrencyStamp == Guid.Empty)
        {
            return false;
        }

        try
        {
            var response = await _tenantEvidenceClient.ReviewOrganizationTenantEvidenceAsync(
                organizationId,
                evidenceId,
                new ReviewOrganizationTenantEvidenceDto
                {
                    Decision = approve
                        ? OrganizationTenantEvidenceReviewDecisionDto.Approve
                        : OrganizationTenantEvidenceReviewDecisionDto.Reject,
                    ExpectedConcurrencyStamp = concurrencyStamp
                },
                cancellationToken: cancellationToken);
            return response?.Success == true;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(
                "Organization evidence could not be reviewed. StatusCode={StatusCode}",
                ex.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "Organization evidence could not be reviewed. FailureType={FailureType}",
                ex.GetType().Name);
            return false;
        }
    }

    public async Task<ICollection<OrganizationListDto>> GetOrganizationRequestsAsync()
    {
        try
        {
            var result = await _apiClient.GetOrganizationsAsync(
                pageNumber: ApiConstants.FirstPage,
                pageSize: ApiConstants.DefaultPageSize);
            return result?.GetItems() ?? new List<OrganizationListDto>();
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[OrganizationService.GetOrganizationRequestsAsync] API error fetching organization requests. StatusCode: {StatusCode}", ex.StatusCode);
            return new List<OrganizationListDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrganizationService.GetOrganizationRequestsAsync] Unexpected error fetching organization requests");
            return new List<OrganizationListDto>();
        }
    }

    public async Task<bool> ApproveOrganizationAsync(Guid id)
    {
        try
        {
            var updateDto = new UpdateOrganizationApprovalStatusDto { ApprovalStatusId = ApprovalStatusId.Approved };
            await _apiClient.UpdateOrganizationApprovalStatusAsync(id, updateDto);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode is 204 or 200)
        {
            return true;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[OrganizationService.ApproveOrganizationAsync] API error approving organization. OrganizationId: {OrganizationId}, StatusCode: {StatusCode}", id, ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrganizationService.ApproveOrganizationAsync] Unexpected error approving organization. OrganizationId: {OrganizationId}", id);
            return false;
        }
    }

    public async Task<bool> RejectOrganizationAsync(Guid id)
    {
        try
        {
            var updateDto = new UpdateOrganizationApprovalStatusDto { ApprovalStatusId = ApprovalStatusId.Rejected };
            await _apiClient.UpdateOrganizationApprovalStatusAsync(id, updateDto);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode is 204 or 200)
        {
            return true;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[OrganizationService.RejectOrganizationAsync] API error rejecting organization. OrganizationId: {OrganizationId}, StatusCode: {StatusCode}", id, ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrganizationService.RejectOrganizationAsync] Unexpected error rejecting organization. OrganizationId: {OrganizationId}", id);
            return false;
        }
    }

    public async Task<bool> RevertToPendingAsync(Guid id)
    {
        try
        {
            var updateDto = new UpdateOrganizationApprovalStatusDto { ApprovalStatusId = ApprovalStatusId.Pending };
            await _apiClient.UpdateOrganizationApprovalStatusAsync(id, updateDto);
            return true;
        }
        catch (ApiException ex) when (ex.StatusCode is 204 or 200)
        {
            return true;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "[OrganizationService.RevertToPendingAsync] API error reverting organization to pending. OrganizationId: {OrganizationId}, StatusCode: {StatusCode}", id, ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OrganizationService.RevertToPendingAsync] Unexpected error reverting organization to pending. OrganizationId: {OrganizationId}", id);
            return false;
        }
    }
}
