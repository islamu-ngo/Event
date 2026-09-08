namespace Explore.Blazor.Services.Preferences;

using Explore.Blazor.Client.Clients;

public static class BffForwardingResults
{
    public static async Task<IResult> ApiOrFallbackAsync<T>(
        Func<Task<T>> operation,
        T fallback)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (ApiException)
        {
            return Results.Ok(fallback);
        }
    }

    public static async Task<IResult> ApiOrProblemAsync<T>(
        Func<Task<T>> operation,
        string failureDetail,
        string failureTitle)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (ApiException ex)
        {
            return Problem(ex, failureDetail, failureTitle);
        }
    }

    public static async Task<IResult> ApiOrProblemAsync(
        Func<Task> operation,
        string failureDetail,
        string failureTitle)
    {
        try
        {
            await operation();
            return Results.Ok();
        }
        catch (ApiException ex)
        {
            return Problem(ex, failureDetail, failureTitle);
        }
    }

    public static async Task<IResult> JsonStreamOrFallbackAsync<TFallback>(
        HttpResponseMessage response,
        TFallback fallback,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            return Results.Ok(fallback);
        }

        return await JsonContentAsync(response, cancellationToken);
    }

    public static async Task<IResult> JsonStreamOrProblemAsync(
        HttpResponseMessage response,
        string failureDetail,
        string failureTitle,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            return Problem(response, failureDetail, failureTitle);
        }

        return await JsonContentAsync(response, cancellationToken);
    }

    public static IResult OkOrProblem(
        HttpResponseMessage response,
        string failureDetail,
        string failureTitle)
    {
        return response.IsSuccessStatusCode
            ? Results.Ok()
            : Problem(response, failureDetail, failureTitle);
    }

    public static async Task<IResult> ContentOrProblemAsync(
        HttpResponseMessage response,
        string failureDetail,
        string failureTitle,
        CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            return Problem(response, failureDetail, failureTitle);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return Results.Content(payload, response.Content.Headers.ContentType?.MediaType ?? "application/json");
    }

    public static IResult? ProblemOrNull(
        HttpResponseMessage response,
        string failureDetail,
        string failureTitle)
    {
        return response.IsSuccessStatusCode
            ? null
            : Problem(response, failureDetail, failureTitle);
    }

    public static IResult Problem(HttpResponseMessage response, string failureDetail, string failureTitle)
    {
        return Results.Problem(
            detail: failureDetail,
            statusCode: (int)response.StatusCode,
            title: failureTitle);
    }

    public static IResult Problem(ApiException exception, string failureDetail, string failureTitle)
    {
        var statusCode = exception.StatusCode is >= 400 and <= 599
            ? exception.StatusCode
            : StatusCodes.Status502BadGateway;

        return Results.Problem(
            detail: failureDetail,
            statusCode: statusCode,
            title: failureTitle);
    }

    private static async Task<IResult> JsonContentAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return Results.Content(payload, response.Content.Headers.ContentType?.MediaType ?? "application/json");
    }
}
