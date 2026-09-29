namespace Explore.Application.Features.ConfigurationManifest.Handlers.Queries;

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Services;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Contracts.Services;
using System.Text.Json;

public sealed class ExportInstanceOperatorIdentityQueryHandler(
    InstanceOperatorIdentityService identity, IAdminContext admin)
    : IQueryHandler<ExportInstanceOperatorIdentityQuery, BaseCommandResponse<OperatorIdentityManifest>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BaseCommandResponse<OperatorIdentityManifest>> QueryAsync(
        ExportInstanceOperatorIdentityQuery query, CancellationToken cancellationToken)
    {
        if (!await admin.IsInstanceAdminAsync(cancellationToken))
            return BaseCommandResponse.Authorization<OperatorIdentityManifest>();
        InstanceOperatorIdentityDocument current = await identity.GetCurrentAsync(cancellationToken);
        if (current.Settings is null || !current.PaidCommerce.IsReady || current.Settings.Revision is null)
            return BaseCommandResponse.Failure<OperatorIdentityManifest>(
                current.PaidCommerce.FailureCode ?? InstanceOperatorIdentityFailureCodes.IntegrityError,
                "A complete persisted operator identity is required for export.");
        return BaseCommandResponse.Success(OperatorIdentityManifestJson.Create(
            JsonSerializer.SerializeToElement(current.Settings, JsonOptions)));
    }
}
