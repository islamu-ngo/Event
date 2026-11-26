using Explore.Application.Responses;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning;

/// <summary>Keeps native command failures separate from the private transport representation.</summary>
public sealed record LearningMutationResult(
    BaseCommandResponse<Guid> Response,
    LearningMutationReceiptDto? Receipt);
