
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Identity;

namespace Explore.Application.Features.Authentication.Local.Models;

public enum LocalCredentialIssueOutcome
{
    Issued = 1,
    Replayed = 2
}

public sealed record LocalCredentialIssueDto
{
    private LocalCredentialIssueDto(
        LocalCredentialIssueOutcome outcome, LocalCredentialOperationStatus operation, string? temporaryPassword)
    {
        Outcome = outcome;
        Operation = operation;
        TemporaryPassword = temporaryPassword;
    }

    public LocalCredentialIssueOutcome Outcome { get; }
    public LocalCredentialOperationStatus Operation { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemporaryPassword { get; }

    public static LocalCredentialIssueDto Issued(LocalCredentialOperationStatus operation, string temporaryPassword)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryPassword);
        if (!operation.IsCurrent || operation.CredentialState != LocalCredentialState.ChangeRequired
            || operation.Receipt.Stage != LocalCredentialOperationStage.ChangeRequired)
        {
            throw new ArgumentException("Issuance requires a current change-required operation.", nameof(operation));
        }
        return new LocalCredentialIssueDto(
            outcome: LocalCredentialIssueOutcome.Issued, operation: operation, temporaryPassword: temporaryPassword);
    }

    public static LocalCredentialIssueDto Replayed(LocalCredentialOperationStatus operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return new LocalCredentialIssueDto(
            outcome: LocalCredentialIssueOutcome.Replayed, operation: operation, temporaryPassword: null);
    }

    public override string ToString() => nameof(LocalCredentialIssueDto);
}
