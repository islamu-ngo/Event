using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Domain.Services.Discovery;

namespace Explore.Application.Features.Events.Discovery;

public static class EventDiscoveryCriteria
{
    /// <summary>Hashes public matching and ranking, excluding navigation and the trusted operation clock.</summary>
    public static string Digest(GetEventListRequest criteria)
    {
        var normalized = criteria with
        {
            PageNumber = 1,
            PageSize = 20,
            OperationNow = null,
            SortBy = string.IsNullOrWhiteSpace(criteria.SortBy) ? "date" : criteria.SortBy.Trim().ToLowerInvariant()
        };
        JsonNode node = JsonSerializer.SerializeToNode(normalized)!;
        node.AsObject()["discoveryRankContractVersion"] = EventDiscoveryRank.ContractVersion;
        string canonical = Canonicalize(node)!.ToJsonString();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static JsonNode? Canonicalize(JsonNode? node)
    {
        if (node is JsonObject properties)
        {
            var ordered = new JsonObject();
            foreach (var property in properties.OrderBy(property => property.Key, StringComparer.Ordinal))
                ordered.Add(property.Key, Canonicalize(property.Value));
            return ordered;
        }
        if (node is JsonArray values)
        {
            var ordered = new JsonArray();
            foreach (var value in values.Select(Canonicalize)
                         .GroupBy(value => value?.ToJsonString(), StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
                ordered.Add(value.First());
            return ordered;
        }
        return node?.DeepClone();
    }
}
