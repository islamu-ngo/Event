using Explore.Blazor.Client.Exceptions;
using Explore.Blazor.Client.Services.Http;
using Refit;

namespace Explore.Blazor.Client.Extensions;

public static class RefitApiResponseExtensions
{
    /// <summary>
    /// Converts a Refit IApiResponse to the application's ApiResult type.
    /// Safely handles API ProblemDetails extraction on failure.
    /// </summary>
    public static ApiResult<T> ToApiResult<T>(this IApiResponse<T> response, string serviceName = "HTTP API")
    {
        if (response.IsSuccessStatusCode && response.Content is not null)
        {
            return ApiResult<T>.Success(response.Content);
        }

        if (response.HasResponseError(out var responseError))
        {
            var problemException = ApiProblemException.FromRefitException(responseError, serviceName);
            return ApiResult<T>.Failure(problemException);
        }

        if (response.HasRequestError(out var requestError))
        {
            return ApiResult<T>.Failure(requestError);
        }

        if (response.Error is not null)
        {
            return ApiResult<T>.Failure(response.Error);
        }

        if (response.StatusCode is not { } statusCode)
        {
            return ApiResult<T>.Failure(new HttpRequestException(response.ReasonPhrase ?? "API request failed before a response was received."));
        }

        // Fallback for non-success without an ApiException (rare in Refit unless configured differently)
        var fallbackProblem = new ApiProblemException(
            statusCode,
            response.ReasonPhrase ?? "API Request Failed",
            serviceName: serviceName);

        return ApiResult<T>.Failure(fallbackProblem);
    }

    /// <summary>
    /// Converts a Refit IApiResponse (no content) to the application's ApiResult type.
    /// </summary>
    public static ApiResult ToApiResult(this IApiResponse response, string serviceName = "HTTP API")
    {
        if (response.IsSuccessStatusCode)
        {
            return ApiResult.Success();
        }

        if (response.HasResponseError(out var responseError))
        {
            var problemException = ApiProblemException.FromRefitException(responseError, serviceName);
            return ApiResult.Failure(problemException);
        }

        if (response.HasRequestError(out var requestError))
        {
            return ApiResult.Failure(requestError);
        }

        if (response.Error is not null)
        {
            return ApiResult.Failure(response.Error);
        }

        if (response.StatusCode is not { } statusCode)
        {
            return ApiResult.Failure(new HttpRequestException(response.ReasonPhrase ?? "API request failed before a response was received."));
        }

        var fallbackProblem = new ApiProblemException(
            statusCode,
            response.ReasonPhrase ?? "API Request Failed",
            serviceName: serviceName);

        return ApiResult.Failure(fallbackProblem);
    }
}
