namespace Explore.Application.Contracts.Infrastructure;

public interface IEmailDispatchTransport
{
    Task DeclareTopologyAsync(CancellationToken cancellationToken = default);

    Task<EmailDispatchPublishResult> PublishDispatchPointerAsync(
        EmailDispatchPointer pointer,
        CancellationToken cancellationToken = default);

    Task<EmailDispatchTransportHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
}
