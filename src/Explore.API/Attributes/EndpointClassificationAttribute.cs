using System;

namespace Explore.API.Attributes;

/// <summary>
/// Declares the <see cref="EndpointClass"/> of a controller or a specific action.
/// When applied at the controller level, it acts as the default for every action in that
/// controller. When applied at the action level, it overrides the controller-level default.
/// This attribute is the single source of truth for endpoint classification, and is
/// consumed by the OpenAPI operation transformer (emits <c>x-endpoint-class</c>) and by
/// architecture tests that forbid unclassified public HTTP actions.
/// See <c>docs/GOVERNANCE.md#api-contract-rules</c> for policy details.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    Inherited = true,
    AllowMultiple = false)]
public sealed class EndpointClassificationAttribute : Attribute
{
    /// <summary>
    /// The declared class for the decorated controller or action.
    /// </summary>
    public EndpointClass Class { get; }

    public EndpointClassificationAttribute(EndpointClass @class)
    {
        Class = @class;
    }
}
