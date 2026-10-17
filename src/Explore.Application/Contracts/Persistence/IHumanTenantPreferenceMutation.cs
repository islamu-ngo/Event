using Explore.Application.Features.Users.Requests.Commands;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Owns one original authenticated subject's prepared preference attempt.</summary>
public interface IHumanTenantPreferencePreparation
{
    IHumanTenantPreferenceMutation? CurrentMutation { get; }
    Task<bool> ExecuteAsync(UpdateUserLastActiveTenantCommand command, CancellationToken cancellationToken);
}

/// <summary>Immutable effects retained by the original preparation, not caller-supplied authority.</summary>
public interface IHumanTenantPreferenceMutation
{
    Guid SubjectId { get; }
    Guid TenantId { get; }
    DateTime UpdatedAt { get; }
    Task RequireCurrentAsync(CancellationToken cancellationToken);
}

/// <summary>Writes only the current registered preparation's exact preference and audit image.</summary>
public interface IHumanTenantPreferenceStore
{
    Task<bool> WriteAsync(IHumanTenantPreferenceMutation mutation, CancellationToken cancellationToken);
    Task RequireCompleteAsync(IHumanTenantPreferenceMutation mutation, CancellationToken cancellationToken);
}
