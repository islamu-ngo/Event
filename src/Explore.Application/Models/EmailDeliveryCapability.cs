// ABOUTME: Safe computed email capability without SMTP coordinates or credential material.
// ABOUTME: Reports transport ownership and explicit intent separately from authentication authority.

using Explore.Domain.Enums;

namespace Explore.Application.Models;

public sealed record EmailDeliveryCapability(
    EmailDeliveryState State,
    bool Enabled,
    SecretScope Scope,
    Guid? TenantId,
    string ReasonCode);
