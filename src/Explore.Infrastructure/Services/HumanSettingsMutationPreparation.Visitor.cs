using Explore.Application.Contracts.Persistence;

namespace Explore.Infrastructure.Services;

public sealed partial class HumanSettingsMutationPreparation : IHumanVisitorSettingsPreparation
{
    public VisitorSettingsOriginal? CurrentVisitorOriginal =>
        _currentOwner.Value is { Active: true } current ? current.VisitorOriginal : null;

    public IVisitorSettingsOriginalStore? CurrentVisitorStore =>
        _currentOwner.Value is { Active: true } current ? current.VisitorStore : null;

    public void RequireVisitorSource(IVisitorSettingsOriginalSource source)
    {
        if (source is not VisitorSource issued || !ReferenceEquals(issued.Issuer, this))
            throw new UnauthorizedAccessException("The original visitor preparation issuer is required.");
        issued.RequireCustody();
    }

    private sealed class VisitorSource(HumanSettingsMutationPreparation issuer,
        HumanCommitSubject subject, Action custody) : IVisitorSettingsOriginalSource
    {
        internal HumanSettingsMutationPreparation Issuer => issuer;
        public HumanCommitSubject Subject => subject;
        public void RequireCustody() => custody();
    }
}
