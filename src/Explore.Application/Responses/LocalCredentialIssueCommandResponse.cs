
using Explore.Application.Features.Authentication.Local.Models;

namespace Explore.Application.Responses;

public sealed record LocalCredentialIssueCommandResponse : BaseCommandResponse<Guid>
{
    private LocalCredentialIssueCommandResponse(BaseCommandResponse<Guid> state, LocalCredentialIssueDto? issue)
        : base(state, true)
    {
        Issue = issue;
    }

    public LocalCredentialIssueDto? Issue { get; }

    public static LocalCredentialIssueCommandResponse Issued(LocalCredentialIssueDto issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        if (issue.Outcome != LocalCredentialIssueOutcome.Issued)
        {
            throw new ArgumentException("An issuance result is required.", nameof(issue));
        }
        return new LocalCredentialIssueCommandResponse(
            state: BaseCommandResponse.Success(id: issue.Operation.Receipt.OperationId), issue: issue);
    }

    public static LocalCredentialIssueCommandResponse Replayed(LocalCredentialIssueDto issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        if (issue.Outcome != LocalCredentialIssueOutcome.Replayed)
        {
            throw new ArgumentException("A safe replay result is required.", nameof(issue));
        }
        return new LocalCredentialIssueCommandResponse(
            state: BaseCommandResponse.Success(id: issue.Operation.Receipt.OperationId), issue: issue);
    }

    public static LocalCredentialIssueCommandResponse Failure(BaseCommandResponse<Guid> failure) =>
        new(state: BaseCommandResponse.RequireFailure(failure), issue: null);

    public override string ToString() => nameof(LocalCredentialIssueCommandResponse);
}
