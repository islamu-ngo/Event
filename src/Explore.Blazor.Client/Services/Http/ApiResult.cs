using System.Net;
using Explore.Blazor.Client.Exceptions;

namespace Explore.Blazor.Client.Services.Http;

public sealed record ApiResult<T>
{
    private ApiResult(T? value, ApiProblemException? problem, Exception? exception)
    {
        Value = value;
        Problem = problem;
        Exception = exception;
    }

    public bool IsSuccess => Problem is null && Exception is null;

    public T? Value { get; }

    public ApiProblemException? Problem { get; }

    public Exception? Exception { get; }

    public HttpStatusCode? StatusCode => Problem?.StatusCode;

    public string? ErrorMessage => Problem?.Message ?? Exception?.Message;

    public static ApiResult<T> Success(T value) => new(value, problem: null, exception: null);

    public static ApiResult<T> Failure(ApiProblemException problem) => new(default, problem, exception: null);

    public static ApiResult<T> Failure(Exception exception) => new(default, problem: null, exception);
}

public sealed record ApiResult
{
    private ApiResult(ApiProblemException? problem, Exception? exception)
    {
        Problem = problem;
        Exception = exception;
    }

    public bool IsSuccess => Problem is null && Exception is null;

    public ApiProblemException? Problem { get; }

    public Exception? Exception { get; }

    public HttpStatusCode? StatusCode => Problem?.StatusCode;

    public string? ErrorMessage => Problem?.Message ?? Exception?.Message;

    public static ApiResult Success() => new(problem: null, exception: null);

    public static ApiResult Failure(ApiProblemException problem) => new(problem, exception: null);

    public static ApiResult Failure(Exception exception) => new(problem: null, exception);
}
