// ABOUTME: Expresses supervised Local reset intent against a previously observed current operation.
// ABOUTME: Keeps the initiating administrator server-resolved and preserves a bounded audit reason.

using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Authentication.Local.Requests.Commands;

public sealed record ResetLocalCredentialCommand : IRequest<LocalCredentialIssueCommandResponse>
{
    public ResetLocalCredentialCommand(
        Guid operationId, Guid localSubjectId, Guid expectedCurrentOperationId,
        Guid expectedCurrentOperationConcurrencyStamp, string reason)
    {
        OperationId = operationId;
        LocalSubjectId = localSubjectId;
        ExpectedCurrentOperationId = expectedCurrentOperationId;
        ExpectedCurrentOperationConcurrencyStamp = expectedCurrentOperationConcurrencyStamp;
        Reason = reason;
    }

    public Guid OperationId { get; }
    public Guid LocalSubjectId { get; }
    public Guid ExpectedCurrentOperationId { get; }
    public Guid ExpectedCurrentOperationConcurrencyStamp { get; }
    public string Reason { get; }

    public override string ToString() => nameof(ResetLocalCredentialCommand);
}
