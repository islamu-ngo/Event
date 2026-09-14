using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;

namespace Explore.Application.Features.Federation.Atproto.Requests.Commands;

public sealed record ReconcileAtprotoPdsSnapshotsCommand(
    AtprotoJetstreamClaim Claim,
    IReadOnlyCollection<string> AllowedDids,
    DateTime SnapshotStartedAt,
    string? LastCompletedFingerprint = null) : ICommand<AtprotoPdsRecoveryResult>;
