using System.Diagnostics;
using Explore.API.Middleware;
using OpenTelemetry;

namespace Explore.API.Telemetry;

/// <summary>Removes inherited private trace context and bounds API HTTP spans before export.</summary>
internal sealed class ApiDiagnosticTraceProcessor : BaseProcessor<Activity>
{
    public override void OnEnd(Activity activity)
    {
        // Baggage enumeration includes live ancestors; clearing only the child leaves
        // inherited values visible to exporters. Trace IDs remain available for correlation.
        for (Activity? current = activity; current is not null; current = current.Parent)
        {
            foreach (var baggage in current.Baggage.ToArray())
                current.SetBaggage(baggage.Key, null);
            current.TraceStateString = null;
        }

        bool server = activity.Kind == ActivityKind.Server
            && activity.Source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal);
        bool client = activity.Kind == ActivityKind.Client && activity.Source.Name == "System.Net.Http";
        if (!server && !client)
            return;

        string route = client ? "http-dependency"
            : activity.GetTagItem("http.route") is string template
                ? "/" + template.Trim('/')
                : SafeRouteMetadata.UnresolvedRouteClassification;
        string method = (activity.GetTagItem("http.request.method") ?? activity.GetTagItem("http.method")) switch
        {
            string supported when supported is "GET" or "POST" or "PUT" or "PATCH" or "DELETE"
                or "HEAD" or "OPTIONS" or "TRACE" or "CONNECT" => supported,
            _ => "OTHER"
        };
        foreach (var tag in activity.TagObjects.ToArray())
        {
            if (tag.Key is not ("http.response.status_code" or "http.status_code")
                && !(server && tag.Key == "http.route"))
                activity.SetTag(tag.Key, null);
        }
        activity.SetStatus(activity.Status);
        activity.SetTag("http.request.method", method);
        activity.SetTag("url.path", route);
        activity.SetTag("http.target", route);
        activity.DisplayName = $"{method} {route}";
    }
}
