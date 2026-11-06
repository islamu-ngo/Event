using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Local.Handlers.Commands;

public sealed class RegisterLocalVisitorCommandHandler(ILocalVisitorEnrollmentService enrollment)
    : ICommandHandler<RegisterLocalVisitorCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(
        RegisterLocalVisitorCommand request, CancellationToken cancellationToken = default)
    {
        string? digest = LocalVisitorBrowserProof.ComputeDigest(request.BrowserProof);
        if (digest is null)
            return BaseCommandResponse.Failure<Guid>(FailureCodes.LocalEnrollmentProofInvalid);
        try
        {
            Guid operation = await enrollment.RegisterAsync(request.Request, digest, cancellationToken)
                .ConfigureAwait(false);
            return BaseCommandResponse.Success(id: operation);
        }
        catch (LocalVisitorEnrollmentRejectedException)
        {
            return BaseCommandResponse.Validation<Guid>(errors: ["The visitor credential was rejected."],
                id: request.Request.OperationId);
        }
        catch (TenantEnrollmentClosedException)
        {
            return BaseCommandResponse.Failure(FailureCodes.LocalEnrollmentClosed, id: request.Request.OperationId);
        }
        catch (UnauthorizedAccessException)
        {
            return BaseCommandResponse.Failure<Guid>(FailureCodes.LocalEnrollmentProofInvalid);
        }
    }
}
