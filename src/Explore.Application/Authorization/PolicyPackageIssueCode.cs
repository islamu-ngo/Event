namespace Explore.Application.Authorization;

public enum PolicyPackageIssueCode
{
    None = 0,
    AdminApiNotConfigured = 1,
    AdminApiAuthenticationFailed = 2,
    AdminApiUnavailable = 3,
    PackageMismatch = 4,
    PackageStatusUnknown = 5,
    ReloadFailed = 6,
    PdpUnreachable = 7,
    PublishFailed = 8,
    PackageUnavailable = 9
}
