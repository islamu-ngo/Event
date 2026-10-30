using Explore.Application.Contracts.Operations;
using ISLAMU.Wire.Contracts.Learning;

namespace Explore.Application.Features.Learning.Requests.Queries;

public sealed record GetLearningTourPlanQuery(
    string Family,
    string ContextKey = "self",
    Guid? EventContextId = null) : IQuery<LearningTourPlanDto>;
