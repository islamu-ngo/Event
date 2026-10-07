using Explore.Domain.Secrets;

namespace Explore.Application.DTOs.Onboarding;

public sealed record AuthenticationCredentialReplacementDto(
    Guid ReplicaId,
    Guid PreviousProcessBootId,
    Guid ProcessBootId,
    AuthenticationCredentialCutover.CredentialConsumer Consumer,
    Guid Generation);

public sealed record PrepareAuthenticationCredentialCutoverInputDto
{
    private readonly IReadOnlyList<AuthenticationCredentialReplacementDto> replacements = [];

    public Guid DeploymentRevision { get; init; }
    public IReadOnlyList<AuthenticationCredentialReplacementDto> RequiredReplacements
    {
        get => replacements;
        init => replacements = value is null ? [] : Array.AsReadOnly(value.ToArray());
    }
    public Guid? ExpectedCurrentOperationId { get; init; }
    public Guid? ExpectedHeadStamp { get; init; }
    public Guid? ExpectedPredecessorStamp { get; init; }
}

public sealed record AuthenticationCredentialActivationInputDto(
    Guid OperationId,
    Guid DeploymentRevision,
    Guid ReplicaId,
    Guid ProcessBootId,
    AuthenticationCredentialCutover.CredentialConsumer Consumer,
    Guid Generation,
    Guid ExpectedHeadStamp,
    Guid ExpectedOperationStamp);

public sealed record AuthenticationCredentialFenceInputDto(
    Guid OperationId,
    Guid DeploymentRevision,
    Guid ExpectedHeadStamp,
    Guid ExpectedOperationStamp,
    bool OldProcessesTerminated,
    bool OldWritersFenced,
    bool OldSessionsFenced);

public sealed record AuthenticationCredentialCleanupInputDto
{
    private readonly IReadOnlyList<Guid>? settingIds;
    private readonly IReadOnlyList<Guid>? bindingIds;
    public Guid OperationId { get; init; }
    public Guid DeploymentRevision { get; init; }
    public Guid ExpectedHeadStamp { get; init; }
    public Guid ExpectedOperationStamp { get; init; }
    public IReadOnlyList<Guid>? ApprovedSettingIds
    {
        get => settingIds;
        init => settingIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
    public IReadOnlyList<Guid>? ApprovedBindingIds
    {
        get => bindingIds;
        init => bindingIds = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}

public sealed record AuthenticationCredentialCleanupTargetDto(Guid Id, string SettingKey);

public sealed record AuthenticationCredentialCleanupInventoryDto
{
    private readonly IReadOnlyList<AuthenticationCredentialCleanupTargetDto> settings = [];
    private readonly IReadOnlyList<AuthenticationCredentialCleanupTargetDto> bindings = [];
    public Guid OperationId { get; init; }
    public Guid DeploymentRevision { get; init; }
    public Guid HeadStamp { get; init; }
    public Guid OperationStamp { get; init; }
    public IReadOnlyList<AuthenticationCredentialCleanupTargetDto> Settings
    {
        get => settings;
        init => settings = Array.AsReadOnly(value.ToArray());
    }
    public IReadOnlyList<AuthenticationCredentialCleanupTargetDto> Bindings
    {
        get => bindings;
        init => bindings = Array.AsReadOnly(value.ToArray());
    }
}

public sealed record AuthenticationCredentialActivationDto(
    Guid InstanceId,
    Guid OperationId,
    Guid DeploymentRevision,
    Guid ReplicaId,
    Guid ProcessBootId,
    AuthenticationCredentialCutover.CredentialConsumer Consumer,
    Guid Generation);

/// <summary>Value-free retained evidence, not credential material or provider health.</summary>
public sealed record AuthenticationCredentialCutoverDto
{
    private readonly IReadOnlyList<AuthenticationCredentialReplacementDto> replacements = [];
    private readonly IReadOnlyList<AuthenticationCredentialActivationDto> activations = [];

    public Guid OperationId { get; init; }
    public Guid InstanceId { get; init; }
    public Guid CreatedBy { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset Deadline { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public Guid DeploymentRevision { get; init; }
    public string Authority { get; init; } = string.Empty;
    public Guid AuthorityProfileRevision { get; init; }
    public Guid ConcurrencyStamp { get; init; }
    public Guid? CurrentHeadStamp { get; init; }
    public string State { get; init; } = string.Empty;
    public IReadOnlyList<AuthenticationCredentialReplacementDto> RequiredReplacements
    {
        get => replacements;
        init => replacements = Array.AsReadOnly(value.ToArray());
    }
    public IReadOnlyList<AuthenticationCredentialActivationDto> Activations
    {
        get => activations;
        init => activations = Array.AsReadOnly(value.ToArray());
    }
    public bool FenceAttested { get; init; }
    public int? CleanedSettingCount { get; init; }
    public int? CleanedBindingCount { get; init; }
}
