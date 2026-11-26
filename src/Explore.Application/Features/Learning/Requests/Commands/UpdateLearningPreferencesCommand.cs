using Explore.Application.Contracts.Operations;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning.Requests.Commands;

public sealed record UpdateLearningPreferencesCommand(LearningUpdatePreferencesRequest Input) : ICommand<LearningMutationResult>;
