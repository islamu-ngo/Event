using System.Net;
using System.Text;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Scheduling;
using Explore.Blazor.Client.Pages.Admin.Instance.Components;
using Explore.Blazor.Client.Services.Scheduling;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin.Instance;

public sealed class InstanceSchedulerSectionTests
{
    [Test]
    [Arguments(429, false)]
    [Arguments(429, true)]
    [Arguments(503, false)]
    [Arguments(503, true)]
    public async Task RejectedReadLeavesCircuitUsableAndManualRefreshRecovers(int statusCode, bool rejectJobs)
    {
        using var context = new BlazorTestContext();
        using var handler = new SchedulerResponseHandler((HttpStatusCode)statusCode, rejectJobs);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.example.test/") };
        context.Services.AddSingleton<ISchedulerAdminService>(
            new SchedulerAdminApiAdapter(new SchedulerAdminClient(http)));

        var cut = context.RenderMudComponent<InstanceSchedulerSection>();
        await Assert.That(cut.FindComponents<MudAlert>()
            .Any(alert => alert.Instance.Severity == Severity.Error)).IsTrue();
        var refresh = cut.FindComponents<MudButton>()
            .Single(button => button.Instance.StartIcon == Icons.Material.Filled.Refresh);
        await Assert.That(refresh.Instance.Disabled).IsFalse();

        handler.RejectRead = false;
        await refresh.Find("button").ClickAsync(new MouseEventArgs());

        await Assert.That(cut.FindComponents<MudAlert>()
            .Any(alert => alert.Instance.Severity == Severity.Error)).IsFalse();
        await Assert.That(cut.Markup).Contains("qa-scheduler-instance");
        await Assert.That(handler.MutationRequested).IsFalse();
    }

    private sealed class SchedulerResponseHandler(HttpStatusCode statusCode, bool rejectJobs) : HttpMessageHandler
    {
        public bool RejectRead { get; set; } = true;
        public bool MutationRequested { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            MutationRequested |= request.Method != HttpMethod.Get;
            string path = request.RequestUri!.AbsolutePath;
            if (RejectRead && path == (rejectJobs ? "/api/admin/scheduler/jobs" : "/api/admin/scheduler"))
            {
                return Task.FromResult(new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/problem+json")
                });
            }

            string body = path switch
            {
                "/api/admin/scheduler" => """{"available":true,"instanceId":"qa-scheduler-instance","state":"running"}""",
                "/api/admin/scheduler/jobs" => """{"_embedded":{"items":[]}}""",
                _ => throw new InvalidOperationException("Unexpected scheduler read route.")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/hal+json")
            });
        }
    }
}
