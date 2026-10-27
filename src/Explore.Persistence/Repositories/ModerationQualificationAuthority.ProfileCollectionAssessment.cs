using Explore.Domain;

namespace Explore.Persistence.Repositories;

internal sealed partial class ModerationQualificationAuthority
{
    /// <summary>
    /// Only the exact retained assessment's nominated correction actors, not general
    /// non-admin source publication. Current parent mandates are not needed to withdraw.
    /// The caller retains identity/privacy and exact original row fences through commit.
    /// </summary>
    internal async Task<ModerationSourceAuthority?> AcquireProfileAssessmentWithdrawalAsync(
        ModerationProfileCollectionAssessmentAdoption retained, CancellationToken token)
    {
        if (!context.IdentityFenceOwnsTransaction) return null;
        var authority = await AcquireAsync(retained.Source.Binding.Jurisdiction,
            false, false, null, null, token);
        if (authority is null) return null;
        var procedure = retained.Source.ProcedureSource;
        return procedure.HostBindingId == authority.Binding.Id
            && procedure.TrustDomainId == authority.Binding.TrustDomainId
            && procedure.TenantId == authority.Tenant.Id
            && retained.Source.Binding.Target.Origin == authority.Instance
            && (authority.UserId == procedure.OwnerHumanId
                || authority.UserId == procedure.OwnerControllerHumanId
                || authority.UserId == procedure.ReviewerHumanId
                || authority.UserId == procedure.ReviewerControllerHumanId)
            ? authority : null;
    }
}
