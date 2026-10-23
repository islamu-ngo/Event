using Explore.Application.DTOs.Onboarding;
using Explore.Domain.Secrets;

namespace Explore.Application.Features.InstanceOnboarding.Services;

public static class AuthenticationCredentialCutoverReceiptMapper
{
    public static AuthenticationCredentialCutoverDto Map(
        AuthenticationCredentialCutover operation,
        AuthenticationCredentialCutoverHead? head) =>
        new()
        {
            OperationId = operation.Id,
            InstanceId = operation.InstanceId,
            CreatedBy = operation.CreatedBy,
            CreatedAt = operation.CreatedAt,
            Deadline = operation.Deadline,
            UpdatedAt = operation.UpdatedAt,
            DeploymentRevision = operation.DeploymentRevision,
            Authority = operation.Authority.ToString(),
            AuthorityProfileRevision = operation.AuthorityProfileRevision,
            ConcurrencyStamp = operation.ConcurrencyStamp,
            CurrentHeadStamp = head?.InstanceId == operation.InstanceId
                && head.CurrentOperationId == operation.Id ? head.ConcurrencyStamp : null,
            State = operation.State.ToString(),
            RequiredReplacements = operation.RequiredReplacements.Select(member =>
                new AuthenticationCredentialReplacementDto(member.ReplicaId,
                    member.PreviousProcessBootId, member.ProcessBootId, member.Consumer,
                    member.Generation)).ToArray(),
            Activations = operation.Activations.Select(report =>
                new AuthenticationCredentialActivationDto(report.InstanceId, report.OperationId,
                    report.DeploymentRevision, report.ReplicaId, report.ProcessBootId,
                    report.Consumer, report.Generation)).ToArray(),
            FenceAttested = operation.Fence is not null,
            CleanedSettingCount = operation.CleanedSettingCount,
            CleanedBindingCount = operation.CleanedBindingCount
        };
}
