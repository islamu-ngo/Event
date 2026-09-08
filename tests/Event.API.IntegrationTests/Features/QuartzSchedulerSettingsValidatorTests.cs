using Explore.API.Configuration;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

public sealed class QuartzSchedulerSettingsValidatorTests
{
    private readonly QuartzSchedulerSettingsValidator _validator = new();

    [Test]
    public async Task ValidateDefaultSettingsReturnsSuccess()
    {
        var result = _validator.Validate(null, new QuartzSchedulerSettings());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ValidateRejectsInvalidSchedulerShape()
    {
        var result = _validator.Validate(null, new QuartzSchedulerSettings
        {
            SchedulerName = " ",
            InstanceId = " ",
            MaxConcurrency = 0,
            TablePrefix = " "
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("SchedulerName");
        await Assert.That(result.FailureMessage).Contains("InstanceId");
        await Assert.That(result.FailureMessage).Contains("MaxConcurrency");
        await Assert.That(result.FailureMessage).Contains("TablePrefix");
    }

    [Test]
    public async Task ValidateRejectsTablePrefixThatIsUnsafeToInlineIntoDdl()
    {
        var result = _validator.Validate(null, new QuartzSchedulerSettings
        {
            TablePrefix = "QRTZ_\"; DROP TABLE users; --"
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("TablePrefix");
    }

    [Test]
    public async Task ValidateRejectsStatusEndpointWithoutSafeAuthorization()
    {
        var result = _validator.Validate(null, new QuartzSchedulerSettings
        {
            StatusEndpointEnabled = true,
            StatusEndpointPath = "admin/scheduler",
            StatusEndpointAuthorizationPolicy = "AllowAnonymous"
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("StatusEndpointPath");
        await Assert.That(result.FailureMessage).Contains("StatusEndpointAuthorizationPolicy");
    }

    [Test]
    public async Task ValidateRejectsRootStatusEndpointPath()
    {
        var result = _validator.Validate(null, new QuartzSchedulerSettings
        {
            StatusEndpointEnabled = true,
            StatusEndpointPath = "/"
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("StatusEndpointPath");
    }

    [Test]
    public async Task ValidateRejectsClusteringWithoutPersistentStore()
    {
        var result = _validator.Validate(null, new QuartzSchedulerSettings
        {
            ClusteringEnabled = true,
            UsePersistentStore = false
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("UsePersistentStore");
    }

    [Test]
    public async Task ValidateRejectsClusteringWithSharedInstanceIdentity()
    {
        var result = _validator.Validate(null, new QuartzSchedulerSettings
        {
            ClusteringEnabled = true,
            InstanceId = "fixed-node",
            ClusterCheckinIntervalSeconds = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("InstanceId");
        await Assert.That(result.FailureMessage).Contains("ClusterCheckinIntervalSeconds");
    }

    [Test]
    public async Task ValidateAcceptsClusteringWithAutoInstanceIdentity()
    {
        var result = _validator.Validate(null, new QuartzSchedulerSettings
        {
            ClusteringEnabled = true,
            InstanceId = QuartzSchedulerSettings.AutoInstanceId
        });

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ValidateRejectsSchemaValidationWithoutPersistentStore()
    {
        var result = _validator.Validate(null, new QuartzSchedulerSettings
        {
            UsePersistentStore = false
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("ValidateSchemaOnStartup");
        await Assert.That(result.FailureMessage).Contains("UsePersistentStore");
    }

    [Test]
    public async Task ValidateAcceptsInMemoryStoreWhenSchemaValidationIsTurnedOffWithIt()
    {
        var result = _validator.Validate(null, new QuartzSchedulerSettings
        {
            UsePersistentStore = false,
            ValidateSchemaOnStartup = false
        });

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task DefaultSettingsValidateTheSchedulerSchemaOnStartup()
    {
        var settings = new QuartzSchedulerSettings();

        await Assert.That(settings.ValidateSchemaOnStartup).IsTrue();
    }

    [Test]
    public async Task ValidateRejectsAdminApiWithoutScheduler()
    {
        var result = _validator.Validate(null, new QuartzSchedulerSettings
        {
            Enabled = false,
            AdminApiEnabled = true
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("AdminApiEnabled");
    }

    [Test]
    public async Task ValidateRejectsDashboardWithoutScheduler()
    {
        var result = _validator.Validate(null, new QuartzSchedulerSettings
        {
            Enabled = false,
            DashboardEnabled = true
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("DashboardEnabled");
    }

    [Test]
    public async Task ValidateRejectsDashboardWithUnroutablePath()
    {
        var result = _validator.Validate(null, new QuartzSchedulerSettings
        {
            DashboardEnabled = true,
            DashboardPath = "/"
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("DashboardPath");
    }

    [Test]
    public async Task ValidateRejectsAnonymousDashboardAuthorization()
    {
        var result = _validator.Validate(null, new QuartzSchedulerSettings
        {
            DashboardEnabled = true,
            DashboardAuthorizationPolicy = "AllowAnonymous"
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("DashboardAuthorizationPolicy");
    }

    [Test]
    public async Task ValidateAcceptsEnabledOperatorSurfaces()
    {
        var result = _validator.Validate(null, new QuartzSchedulerSettings
        {
            AdminApiEnabled = true,
            AdminApiReadOnly = false,
            DashboardEnabled = true,
            DashboardReadOnly = false
        });

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task DefaultSettingsKeepOperatorSurfacesDisabledAndReadOnly()
    {
        var settings = new QuartzSchedulerSettings();

        await Assert.That(settings.AdminApiEnabled).IsFalse();
        await Assert.That(settings.DashboardEnabled).IsFalse();
        await Assert.That(settings.AdminApiReadOnly).IsTrue();
        await Assert.That(settings.DashboardReadOnly).IsTrue();
        await Assert.That(settings.DashboardPath).IsEqualTo(QuartzSchedulerSettings.DefaultDashboardPath);
    }
}
