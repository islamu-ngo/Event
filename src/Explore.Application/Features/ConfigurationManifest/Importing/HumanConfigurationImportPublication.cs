using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;

namespace Explore.Application.Features.ConfigurationManifest.Importing;

/// <summary>
/// An accepted slice of the original parsed human import. Only its section compiler
/// constructs it; possession does not replace the original scoped import issuer.
/// </summary>
public sealed class HumanConfigurationImportPublication
{
    internal HumanConfigurationImportPublication(Guid operationId, HumanCommitSubject subject,
        ConfigurationImportTarget target, string artifactDigest, Guid? tenantId,
        ImmutableArray<PublicationPolicySettingMutation> mutations,
        IHumanConfigurationImportPublicationStore store)
    {
        OperationId = operationId;
        Subject = subject;
        Target = target;
        ArtifactDigest = artifactDigest;
        TenantId = tenantId;
        Mutations = mutations;
        Store = store;
    }

    public Guid OperationId { get; }
    public HumanCommitSubject Subject { get; }
    public ConfigurationImportTarget Target { get; }
    public string ArtifactDigest { get; }
    public Guid? TenantId { get; }
    public ImmutableArray<PublicationPolicySettingMutation> Mutations { get; }
    public DateTime OccurredAt => Store.OccurredAt;
    public IHumanConfigurationImportPublicationStore Store { get; }
    public override string ToString() => nameof(HumanConfigurationImportPublication);
}

/// <summary>
/// Captures data only from the actual selected import owner. No new owner, credential
/// or transaction may be selected by this factory, and observation grants no write.
/// </summary>
public interface IHumanConfigurationImportPublicationFactory
{
    Task<IHumanConfigurationImportPublicationStore> ObservePublicationAsync(
        HumanConfigurationImportAuthority authority, Guid operationId, string artifactDigest,
        ImmutableArray<HumanConfigurationImportSettingEffect> effects, DateTime occurredAt,
        CancellationToken cancellationToken);
}

/// <summary>
/// Original data observation, exact preimage writes and private completion on one
/// physical attempt. Public implementations are not evidence to the actual issuer.
/// </summary>
public interface IHumanConfigurationImportPublicationStore
{
    DateTime OccurredAt { get; }
    Task RequireCurrentAsync(CancellationToken cancellationToken);
    Task CompileAsync(ImmutableArray<HumanConfigurationImportPublication> publications, CancellationToken cancellationToken);
    Task<PublicationPolicyMutationSnapshot> ReadAsync(HumanConfigurationImportPublication publication,
        CancellationToken cancellationToken);
    Task<CoordinatedSettingMutationWriteResult> WriteAsync(HumanConfigurationImportPublication publication,
        CancellationToken cancellationToken);
    Task RequireCompletedAsync(CancellationToken cancellationToken);
}
