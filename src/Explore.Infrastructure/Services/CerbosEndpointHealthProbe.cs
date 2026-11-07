using Explore.Application.Utilities;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Services;

/// <summary>Performs the external Cerbos health exchange independently of configuration custody.</summary>
public interface ICerbosEndpointHealthProbe
{
    Task<bool> VerifyAsync(string grpcEndpoint, CancellationToken cancellationToken = default);
}

public sealed class CerbosEndpointHealthProbe(ILogger<CerbosEndpointHealthProbe> logger) : ICerbosEndpointHealthProbe
{
    public async Task<bool> VerifyAsync(string grpcEndpoint, CancellationToken cancellationToken = default)
    {
        var normalizedEndpoint = GrpcEndpointNormalizer.Normalize(grpcEndpoint);
        if (!GrpcEndpointNormalizer.IsValid(normalizedEndpoint))
            return false;
        try
        {
            using var channel = GrpcChannel.ForAddress(
                normalizedEndpoint, CerbosGrpcChannelOptionsFactory.Create());
            var client = new Health.HealthClient(channel);
            var response = await client.CheckAsync(
                new HealthCheckRequest(),
                deadline: DateTime.UtcNow.AddSeconds(5),
                cancellationToken: cancellationToken);
            bool healthy = response.Status == HealthCheckResponse.Types.ServingStatus.Serving;
            logger.LogInformation("Cerbos gRPC health check completed with status {Status}", response.Status);
            return healthy;
        }
        catch (RpcException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (RpcException exception)
        {
            logger.LogWarning("Cerbos gRPC health check failed. GrpcStatusCode={GrpcStatusCode}", exception.StatusCode);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning("Cerbos gRPC health check failed. FailureType={FailureType}", exception.GetType().Name);
            return false;
        }
    }
}
