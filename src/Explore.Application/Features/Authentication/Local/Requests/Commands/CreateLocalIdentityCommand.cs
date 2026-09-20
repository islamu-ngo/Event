
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.Authentication.Local.Requests.Commands;

public sealed record CreateLocalIdentityCommand : ICommand<LocalCredentialIssueCommandResponse>
{
    public CreateLocalIdentityCommand(Guid operationId, string email, string firstName, string lastName)
    {
        OperationId = operationId;
        Email = email;
        FirstName = firstName;
        LastName = lastName;
    }

    public Guid OperationId { get; }
    public string Email { get; }
    public string FirstName { get; }
    public string LastName { get; }

    public override string ToString() => nameof(CreateLocalIdentityCommand);
}
