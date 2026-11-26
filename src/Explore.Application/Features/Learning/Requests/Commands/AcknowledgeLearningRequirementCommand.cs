using Explore.Application.Contracts.Operations;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning.Requests.Commands;

public sealed record AcknowledgeLearningRequirementCommand(
    string RequirementKey,
    LearningAcknowledgeRequirementRequest Input) : ICommand<LearningMutationResult>;
