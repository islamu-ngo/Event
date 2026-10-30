using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Learning;
using System.Text.Json;

namespace Explore.Infrastructure.Services;

public sealed class LearningGuidancePolicyReader : ILearningGuidancePolicyReader
{
    public ResolvedLearningGuidancePolicy Resolve(LearningSourceFacts sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(sources.PolicySourceStamp)
            || sources.GuidanceDocument is { } document && document.TenantId != sources.TenantId)
            throw new UnauthorizedAccessException("Guidance requires the original selected tenant's sources.");
        if (sources.EffectiveSettings.TryGetValue("learning.guidance.allow_tenant_edits", out var control))
        {
            using var value = JsonDocument.Parse(control.Value);
            bool allowed = value.RootElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new InvalidOperationException("The captured instance guidance control is malformed.")
            };
            if (allowed != sources.AllowTenantEdits)
                throw new UnauthorizedAccessException("The captured instance guidance control is inconsistent.");
        }
        return new(LearningGuidancePolicyCodec.Decode(sources.GuidanceDocument),
            LearningGuidancePolicyCodec.SchemaVersion, LearningGuidancePolicyCodec.DefaultsVersion,
            sources.GuidanceDocument?.ConcurrencyStamp, sources.PolicySourceStamp, sources.AllowTenantEdits);
    }
}
