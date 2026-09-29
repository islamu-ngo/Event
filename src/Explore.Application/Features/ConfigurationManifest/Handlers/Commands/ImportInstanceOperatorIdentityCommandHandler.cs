namespace Explore.Application.Features.ConfigurationManifest.Handlers.Commands;

using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain.Settings.Documents.Payloads;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using System.Text.Json;

public sealed class ImportInstanceOperatorIdentityCommandHandler(
    InstanceOperatorIdentityService identity,
    IAdminContext admin)
    : ICommandHandler<ImportInstanceOperatorIdentityCommand, BaseCommandResponse<InstanceOperatorIdentitySavedDocument>>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<BaseCommandResponse<InstanceOperatorIdentitySavedDocument>> ExecuteAsync(
        ImportInstanceOperatorIdentityCommand command, CancellationToken cancellationToken)
    {
        if (!await admin.IsInstanceAdminAsync(cancellationToken)
            || await admin.ResolveUserIdAsync(cancellationToken) is not { } actorId
            || actorId == Guid.Empty)
            return BaseCommandResponse.Authorization<InstanceOperatorIdentitySavedDocument>();

        InstanceOperatorIdentitySettings candidate;
        try
        {
            OperatorIdentityManifestJson.Validate(command.Manifest);
            if (command.ExpectedRevisionHash is not { Length: 64 }
                || command.ExpectedRevisionHash.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
                throw new OperatorIdentityManifestException();
            candidate = command.Manifest.Document.Deserialize<InstanceOperatorIdentitySettings>(JsonOptions)
                ?? throw new OperatorIdentityManifestException();
        }
        catch (Exception exception) when (exception is OperatorIdentityManifestException or JsonException)
        {
            return BaseCommandResponse.Failure<InstanceOperatorIdentitySavedDocument>(
                "operator_identity_manifest_invalid", "The operator identity manifest or revision precondition is invalid.");
        }

        return await identity.ImportAsync(candidate, command.ExpectedRevisionHash,
            command.Manifest.ContentDigest, actorId, cancellationToken);
    }
}
