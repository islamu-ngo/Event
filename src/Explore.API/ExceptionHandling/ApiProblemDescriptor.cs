namespace Explore.API.ExceptionHandling;

internal sealed record ApiValidationProblemDescriptor(
    string ErrorKey,
    string Title,
    string FallbackDetail);

internal sealed record ApiNotFoundProblemDescriptor(
    string Title,
    string Detail,
    string Code = ApiProblemCodes.ResourceNotFound);
