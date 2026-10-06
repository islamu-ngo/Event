using System.Text.Json.Serialization;

namespace Explore.Application.Models;

/// <summary>Describes acknowledged disclosure without assigning a success outcome to failed commands.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExternalApiKeyDisclosureStatus>))]
public enum ExternalApiKeyDisclosureStatus
{
    /// <summary>The acknowledged winner receives newly issued credential material.</summary>
    Issued = 1,
    /// <summary>An authorized replay receives metadata but cannot redisclose the credential.</summary>
    PreviouslyIssued = 2
}
