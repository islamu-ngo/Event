using Explore.Application.Contracts.Operations;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning.Requests.Commands;

public sealed record ClaimLearningTourCommand(LearningClaimTourRequest Input) : ICommand<LearningMutationResult>;
