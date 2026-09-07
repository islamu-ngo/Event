using Explore.Blazor.Client.Exceptions;
using Explore.Blazor.Client.Extensions;

namespace Explore.Blazor.Client.Services.Http;

public interface IApiClientExecutor
{
    Task<ApiResult<T>> ReadJsonAsync<T>(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
        string serviceName,
        CancellationToken cancellationToken = default);

    Task<ApiResult> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
        string serviceName,
        CancellationToken cancellationToken = default);
}

public sealed class ApiClientExecutor : IApiClientExecutor
{
    public async Task<ApiResult<T>> ReadJsonAsync<T>(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await sendAsync(cancellationToken);
            var value = await response.ReadJsonOrThrowAsync<T>(serviceName, cancellationToken);
            return ApiResult<T>.Success(value);
        }
        catch (ApiProblemException ex)
        {
            return ApiResult<T>.Failure(ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ApiResult<T>.Failure(ex);
        }
    }

    public async Task<ApiResult> SendAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> sendAsync,
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await sendAsync(cancellationToken);
            await response.EnsureSuccessOrThrowAsync(serviceName, cancellationToken);
            return ApiResult.Success();
        }
        catch (ApiProblemException ex)
        {
            return ApiResult.Failure(ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ApiResult.Failure(ex);
        }
    }
}
