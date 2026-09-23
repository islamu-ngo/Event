using System.Diagnostics;
using OpenTelemetry;

namespace Explore.ServiceDefaults;

/// <summary>Removes file capability identifiers from server and outbound HTTP spans before export.</summary>
internal sealed class EventResourceTraceProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        if (activity.Kind is not (ActivityKind.Server or ActivityKind.Client)) return;
        string? target = activity.GetTagItem("url.path") as string
            ?? activity.GetTagItem("url.full") as string
            ?? activity.GetTagItem("http.target") as string
            ?? activity.GetTagItem("http.url") as string;
        if (target is null) return;
        string path = !target.StartsWith('/') && Uri.TryCreate(target, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath : target;
        int query = path.IndexOfAny(['?', '#']);
        if (query >= 0) path = path[..query];
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3) return;
        int identity;
        if (segments[0].Equals("api", StringComparison.OrdinalIgnoreCase))
        {
            if (segments[1].Equals("eventresource", StringComparison.OrdinalIgnoreCase))
                identity = 2;
            else if (segments[1].Equals("event", StringComparison.OrdinalIgnoreCase)
                && segments.Length >= 4 && segments[3].Equals("resources", StringComparison.OrdinalIgnoreCase))
                identity = 2;
            else if (segments[1].Equals("storageobject", StringComparison.OrdinalIgnoreCase))
                identity = segments[2].Equals("upload-sessions", StringComparison.OrdinalIgnoreCase)
                    ? segments.Length >= 4 ? 3 : -1 : 2;
            else return;
        }
        else if (segments[0].Equals("bff", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("event-resources", StringComparison.OrdinalIgnoreCase))
            identity = 2;
        else if (segments[0].Equals("bff", StringComparison.OrdinalIgnoreCase)
            && segments[1].Equals("storage", StringComparison.OrdinalIgnoreCase)
            && segments[2].Equals("upload-proxy", StringComparison.OrdinalIgnoreCase))
            identity = -1;
        else return;

        if (identity >= 0 && !segments[identity].StartsWith('{'))
            segments[identity] = "{id}";
        string route = "/" + string.Join('/', segments);
        activity.DisplayName = $"{activity.GetTagItem("http.request.method") ?? activity.GetTagItem("http.method") ?? "HTTP"} {route}";
        activity.SetTag("url.path", route);
        activity.SetTag("http.target", route);
        activity.SetTag("url.full", null);
        activity.SetTag("url.query", null);
        activity.SetTag("http.url", null);
    }
}
