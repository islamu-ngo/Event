
using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EmailDispatch.Handlers.Commands;

public sealed class PreviewEmailDeliveryDisableCommandHandler(
    IEmailDeliveryDisableImpactReader impactReader,
    IEmailDeliveryDisableTokenService tokenService,
    IHumanEmailMutationPreparation admission)
    : ICommandHandler<PreviewEmailDeliveryDisableCommand, BaseCommandResponse<EmailDeliveryDisablePreviewDto>>
{
    public async Task<BaseCommandResponse<EmailDeliveryDisablePreviewDto>> ExecuteAsync(
        PreviewEmailDeliveryDisableCommand request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty)
            return BaseCommandResponse.Validation<EmailDeliveryDisablePreviewDto>(["TenantId must not be empty."]);

        var prepared = await admission.PreparePreviewAsync(request.TenantId, cancellationToken);
        // The callback's confirmation is private until the original owner's complete terminal
        // source/time gate and physical settlement finish. This is database work, not SMTP.
        return await admission.ExecutePreparedAsync(prepared, async transactionToken =>
            {
                Guid actorId = await admission.RequireImpactAsync(request.TenantId, transactionToken);
                var snapshot = await impactReader.ReadAsync(request.TenantId, transactionToken);
                if (snapshot is null)
                    return BaseCommandResponse.NotFound<EmailDeliveryDisablePreviewDto>("Email delivery scope was not found.");

                var confirmation = snapshot.CanDisable ? tokenService.Issue(actorId, snapshot) : null;
                var preview = new EmailDeliveryDisablePreviewDto(
                    TenantId: snapshot.TenantId,
                    ExpectedRevision: snapshot.Revision,
                    IsLocked: snapshot.IsLocked,
                    AffectedScopes: snapshot.AffectedScopes.Select(scope => new EmailDeliveryDisableAffectedScopeDto(
                        TenantId: scope.TenantId, Revision: scope.Revision)).ToImmutableArray(),
                    ConfirmationToken: confirmation?.Token,
                    ExpiresAtUtc: confirmation?.ExpiresAtUtc);
                return BaseCommandResponse.Success(preview, "Email delivery disable impact retrieved.");
            }, cancellationToken);
    }
}
