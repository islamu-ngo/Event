using Explore.Infrastructure;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class EmailDispatchProcessorSettingsValidatorTests
{
    private readonly EmailDispatchProcessorSettingsValidator _validator = new();

    [Test]
    public async Task ValidateDefaultSettingsReturnsSuccess()
    {
        var result = _validator.Validate(null, new EmailDispatchProcessorSettings());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task ValidateInvalidPollingIntervalReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchProcessorSettings
        {
            PollingIntervalSeconds = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("PollingIntervalSeconds");
    }

    [Test]
    public async Task ValidateInvalidBatchSizeReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchProcessorSettings
        {
            BatchSize = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("BatchSize");
    }

    [Test]
    public async Task ValidateUnsafeFairnessRateAndBackpressureLimitsReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchProcessorSettings
        {
            BatchSize = 10,
            MaxRowsPerTenantPerBatch = 11,
            MaxConcurrentDispatches = 0,
            MaxConcurrentDispatchesPerTenant = 2,
            GlobalSmtpRateLimitPerMinute = 0,
            TenantSmtpRateLimitPerMinute = 0,
            OptionalBacklogHighWatermark = 10,
            OptionalBacklogLowWatermark = 10,
            HealthOldestPendingWarningSeconds = 0,
            HealthTenantBacklogWarningThreshold = 0,
            HealthTenantSampleLimit = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("MaxRowsPerTenantPerBatch");
        await Assert.That(result.FailureMessage).Contains("MaxConcurrentDispatches");
        await Assert.That(result.FailureMessage).Contains("MaxConcurrentDispatchesPerTenant");
        await Assert.That(result.FailureMessage).Contains("GlobalSmtpRateLimitPerMinute");
        await Assert.That(result.FailureMessage).Contains("TenantSmtpRateLimitPerMinute");
        await Assert.That(result.FailureMessage).Contains("OptionalBacklogLowWatermark");
        await Assert.That(result.FailureMessage).Contains("HealthOldestPendingWarningSeconds");
        await Assert.That(result.FailureMessage).Contains("HealthTenantBacklogWarningThreshold");
        await Assert.That(result.FailureMessage).Contains("HealthTenantSampleLimit");
    }

    [Test]
    public async Task ValidateSafeMaximumWorkerLimitsReturnsSuccess()
    {
        var result = _validator.Validate(null, new EmailDispatchProcessorSettings
        {
            BatchSize = 1000,
            MaxRowsPerTenantPerBatch = 1000,
            MaxConcurrentDispatches = 256,
            MaxConcurrentDispatchesPerTenant = 256,
            GlobalSmtpRateLimitPerMinute = 100000,
            TenantSmtpRateLimitPerMinute = 100000,
            OptionalBacklogHighWatermark = 1000000,
            OptionalBacklogLowWatermark = 999999,
            HealthOldestPendingWarningSeconds = 604800,
            HealthTenantBacklogWarningThreshold = 100000
        });

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    [Arguments("BatchSize")]
    [Arguments("MaxRowsPerTenantPerBatch")]
    [Arguments("MaxConcurrentDispatches")]
    [Arguments("MaxConcurrentDispatchesPerTenant")]
    [Arguments("GlobalSmtpRateLimitPerMinute")]
    [Arguments("TenantSmtpRateLimitPerMinute")]
    [Arguments("OptionalBacklogHighWatermark")]
    [Arguments("OptionalBacklogLowWatermark")]
    [Arguments("HealthOldestPendingWarningSeconds")]
    [Arguments("HealthTenantBacklogWarningThreshold")]
    public async Task ValidateWorkerLimitAboveSafeMaximumReturnsFailure(string settingName)
    {
        var settings = new EmailDispatchProcessorSettings();
        switch (settingName)
        {
            case "BatchSize":
                settings.BatchSize = 1001;
                break;
            case "MaxRowsPerTenantPerBatch":
                settings.BatchSize = 1000;
                settings.MaxRowsPerTenantPerBatch = 1001;
                break;
            case "MaxConcurrentDispatches":
                settings.MaxConcurrentDispatches = 257;
                break;
            case "MaxConcurrentDispatchesPerTenant":
                settings.MaxConcurrentDispatches = 256;
                settings.MaxConcurrentDispatchesPerTenant = 257;
                break;
            case "GlobalSmtpRateLimitPerMinute":
                settings.GlobalSmtpRateLimitPerMinute = 100001;
                break;
            case "TenantSmtpRateLimitPerMinute":
                settings.GlobalSmtpRateLimitPerMinute = 100000;
                settings.TenantSmtpRateLimitPerMinute = 100001;
                break;
            case "OptionalBacklogHighWatermark":
                settings.OptionalBacklogHighWatermark = 1000001;
                break;
            case "OptionalBacklogLowWatermark":
                settings.OptionalBacklogLowWatermark = 1000001;
                break;
            case "HealthOldestPendingWarningSeconds":
                settings.HealthOldestPendingWarningSeconds = 604801;
                break;
            case "HealthTenantBacklogWarningThreshold":
                settings.HealthTenantBacklogWarningThreshold = 100001;
                break;
        }

        var result = _validator.Validate(null, settings);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(settingName);
    }

    [Test]
    [Arguments("MaxRowsPerTenantPerBatch")]
    [Arguments("MaxConcurrentDispatchesPerTenant")]
    [Arguments("TenantSmtpRateLimitPerMinute")]
    [Arguments("OptionalBacklogLowWatermark")]
    public async Task ValidateWorkerRelationalLimitReturnsFailure(string settingName)
    {
        var settings = new EmailDispatchProcessorSettings();
        switch (settingName)
        {
            case "MaxRowsPerTenantPerBatch":
                settings.BatchSize = 10;
                settings.MaxRowsPerTenantPerBatch = 11;
                break;
            case "MaxConcurrentDispatchesPerTenant":
                settings.MaxConcurrentDispatches = 8;
                settings.MaxConcurrentDispatchesPerTenant = 9;
                break;
            case "TenantSmtpRateLimitPerMinute":
                settings.GlobalSmtpRateLimitPerMinute = 10;
                settings.TenantSmtpRateLimitPerMinute = 11;
                break;
            case "OptionalBacklogLowWatermark":
                settings.OptionalBacklogHighWatermark = 100;
                settings.OptionalBacklogLowWatermark = 100;
                break;
        }

        var result = _validator.Validate(null, settings);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains(settingName);
    }

    [Test]
    public async Task ValidateInvalidModeReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchProcessorSettings
        {
            Mode = (EmailDispatchProcessorMode)999
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("Mode");
    }

    [Test]
    public async Task ValidateInvalidRetryWindowReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchProcessorSettings
        {
            InitialRetryDelaySeconds = 60,
            MaxRetryDelaySeconds = 10
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("MaxRetryDelaySeconds");
    }

    [Test]
    public async Task ValidateInvalidProcessingLeaseTimeoutReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchProcessorSettings
        {
            ProcessingLeaseTimeoutSeconds = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("ProcessingLeaseTimeoutSeconds");
    }

    [Test]
    public async Task ValidateInvalidDueDispatchHealthThresholdReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchProcessorSettings
        {
            HealthDueDispatchWarningThreshold = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("HealthDueDispatchWarningThreshold");
    }

    [Test]
    public async Task ValidateInvalidStaleProcessingHealthThresholdReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchProcessorSettings
        {
            HealthStaleProcessingWarningThreshold = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("HealthStaleProcessingWarningThreshold");
    }

    [Test]
    public async Task ValidateInvalidDeadLetterHealthThresholdReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchProcessorSettings
        {
            HealthDeadLetterWarningThreshold = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("HealthDeadLetterWarningThreshold");
    }

    [Test]
    public async Task ValidateInvalidUnknownHealthThresholdReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchProcessorSettings
        {
            HealthUnknownWarningThreshold = 0
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("HealthUnknownWarningThreshold");
    }

    [Test]
    public async Task ValidateMissingConsumerIdReturnsFailure()
    {
        var result = _validator.Validate(null, new EmailDispatchProcessorSettings
        {
            ConsumerId = " "
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureMessage).Contains("ConsumerId");
    }
}
