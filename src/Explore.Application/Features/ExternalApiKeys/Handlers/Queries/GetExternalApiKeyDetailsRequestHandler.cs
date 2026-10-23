using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Features.ExternalApiKeys.Requests.Queries;
using Explore.Application.Lookups;

namespace Explore.Application.Features.ExternalApiKeys.Handlers.Queries;

public class GetExternalApiKeyDetailsRequestHandler : IQueryHandler<GetExternalApiKeyDetailsRequest, ExternalApiKeyListDto?>
{
    private readonly IExternalApiKeyRepository _externalApiKeyRepository;
    private readonly IExternalApiKeyIssuanceAuthority _ownerAuthority;
    private readonly IInstanceAdministratorCommitAuthority _instanceAuthority;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAdminContext _adminContext;
    private readonly IUserContext _userContext;
    private readonly InstanceApiKeyOperations _instanceOperations;
    private readonly IHumanApiKeyManagementPreparation _preparation;
    private readonly ISerializableAttemptOwner _attemptOwner;
    private readonly IHumanActionCommitFinalizer _finalizer;

    public GetExternalApiKeyDetailsRequestHandler(
        IExternalApiKeyRepository externalApiKeyRepository,
        IExternalApiKeyIssuanceAuthority ownerAuthority,
        IInstanceAdministratorCommitAuthority instanceAuthority,
        IUnitOfWork unitOfWork,
        IAdminContext adminContext,
        IUserContext userContext,
        InstanceApiKeyOperations instanceOperations,
        IHumanApiKeyManagementPreparation preparation,
        ISerializableAttemptOwner attemptOwner,
        IHumanActionCommitFinalizer finalizer)
    {
        _externalApiKeyRepository = externalApiKeyRepository;
        _ownerAuthority = ownerAuthority;
        _instanceAuthority = instanceAuthority;
        _unitOfWork = unitOfWork;
        _adminContext = adminContext;
        _userContext = userContext;
        _instanceOperations = instanceOperations;
        _preparation = preparation;
        _attemptOwner = attemptOwner;
        _finalizer = finalizer;
    }

    public async Task<ExternalApiKeyListDto?> QueryAsync(GetExternalApiKeyDetailsRequest request, CancellationToken cancellationToken)
    {
        if (!_userContext.IsAuthenticated
            || await _adminContext.ResolveUserIdAsync(cancellationToken) is not { } currentUserId)
            return null;
        if (request.Id == Guid.Empty)
            return null;
        if (await _externalApiKeyRepository.GetForTenantAsync(request.Id, cancellationToken) is null)
        {
            if (!await _adminContext.IsInstanceAdminAsync(cancellationToken))
                return null;
            var platformKey = (await _instanceOperations.ReadAsync(currentUserId, request.Id, cancellationToken)).SingleOrDefault();
            return platformKey is null ? null : Map(platformKey);
        }
        var prepared = await _preparation.PrepareAsync(request, currentUserId, cancellationToken);
        async Task<ExternalApiKeyListDto?> ReadOwnerAsync(CancellationToken token)
        {
            if (prepared is not null)
                await prepared.AcquireAsync(request, currentUserId, token);
            var before = await _externalApiKeyRepository.GetForTenantAsync(request.Id, token);
            if (before is null || !await ExternalApiKeyManagementAuthority.IsAuthorizedForCommitAsync(
                    before, currentUserId, _ownerAuthority, _instanceAuthority, token))
                return null;
            var externalApiKey = await _externalApiKeyRepository.GetForManagementAsync(request.Id, token);
            if (externalApiKey is null || !ExternalApiKeyManagementAuthority.HasSameOwner(before, externalApiKey))
                return null;
            if (prepared is not null)
                ExternalApiKeyManagementAuthority.RegisterFinalAdmission(before, currentUserId,
                    _externalApiKeyRepository, _ownerAuthority, _instanceAuthority, _finalizer);
            return Map(externalApiKey);
        }
        return prepared is null
            ? await _unitOfWork.ExecuteSerializableAsync(ReadOwnerAsync, cancellationToken)
            : await _attemptOwner.ExecuteAsync(ReadOwnerAsync, cancellationToken);
    }

    private static ExternalApiKeyListDto Map(Explore.Domain.ExternalApiKey externalApiKey) => new()
    {
        Id = externalApiKey.Id,
        Name = externalApiKey.Name,
        Description = externalApiKey.Description,
        KeyId = externalApiKey.KeyId,
        MaskedKeyId = MaskKeyId(externalApiKey.KeyId),
        ExternalApiKeyOwnerTypeId = externalApiKey.ExternalApiKeyOwnerTypeId,
        ExternalApiKeyOwnerTypeCode = NormalizedLookupMetadata.ExternalApiKeyOwnerType(externalApiKey.ExternalApiKeyOwnerTypeId).Code,
        ExternalApiKeyOwnerTypeName = NormalizedLookupMetadata.ExternalApiKeyOwnerType(externalApiKey.ExternalApiKeyOwnerTypeId).Name,
        OwnerId = externalApiKey.OwnerId,
        Scopes = SplitScopes(externalApiKey.Scopes),
        ExternalApiKeyStatusId = externalApiKey.ExternalApiKeyStatusId,
        ExternalApiKeyStatusCode = NormalizedLookupMetadata.ExternalApiKeyStatus(externalApiKey.ExternalApiKeyStatusId).Code,
        ExternalApiKeyStatusName = NormalizedLookupMetadata.ExternalApiKeyStatus(externalApiKey.ExternalApiKeyStatusId).Name,
        ExpiresAt = externalApiKey.ExpiresAt,
        LastUsedAt = externalApiKey.LastUsedAt,
        ExternalApiKeyCreditPeriodId = externalApiKey.ExternalApiKeyCreditPeriodId,
        ExternalApiKeyCreditPeriodCode = NormalizedLookupMetadata.ExternalApiKeyCreditPeriod(externalApiKey.ExternalApiKeyCreditPeriodId).Code,
        ExternalApiKeyCreditPeriodName = NormalizedLookupMetadata.ExternalApiKeyCreditPeriod(externalApiKey.ExternalApiKeyCreditPeriodId).Name,
        CreditLimit = externalApiKey.CreditLimit,
        MaxRolloverCredits = externalApiKey.MaxRolloverCredits
    };

    private static List<string> SplitScopes(string scopes)
    {
        return scopes
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static string MaskKeyId(string keyId)
    {
        return keyId.Length > 4
            ? string.Concat("".PadLeft(keyId.Length - 4, '\u2022'), keyId.AsSpan(keyId.Length - 4))
            : keyId;
    }
}
