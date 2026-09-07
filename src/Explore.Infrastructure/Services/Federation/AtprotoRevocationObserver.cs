namespace Explore.Infrastructure.Services.Federation;

internal sealed class AtprotoRevocationObserver
{
    public bool Attempted { get; private set; }
    public bool Succeeded { get; private set; }

    public void Record(bool succeeded)
    {
        Attempted = true;
        Succeeded = succeeded;
    }
}

internal sealed class AtprotoRevocationObserverHandler(
    AtprotoRevocationObserver observer,
    HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Post)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            observer.Record(response.IsSuccessStatusCode);
            return response;
        }
        catch
        {
            observer.Record(false);
            throw;
        }
    }
}
