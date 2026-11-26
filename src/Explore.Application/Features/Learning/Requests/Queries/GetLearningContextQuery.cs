using Explore.Application.Contracts.Operations;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning.Requests.Queries;

public sealed record GetLearningContextQuery(
    string ContextKey = "self",
    Guid? EventContextId = null) : IQuery<LearningContextDto>;
