using Explore.Application.Onboarding;

namespace Explore.Application.Contracts.Services;

public interface IInstanceBootstrapAuditLogger
{
    void Log(InstanceBootstrapAuditEvent auditEvent);
}
