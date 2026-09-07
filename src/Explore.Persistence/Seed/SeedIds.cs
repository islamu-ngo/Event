namespace Explore.Persistence.Seed;

/// <summary>
/// Static deterministic GUIDs for seed data.
/// These should NEVER be changed once used in production to avoid migration issues.
/// Use UUIDv7 format for time-ordered IDs: 018e4e5c-xxxx-7xxx-8xxx-xxxxxxxxxxxx
/// </summary>
public static class SeedIds
{
    // ===== Tenants =====
    /// <summary>
    /// Default tenant ID used as fallback across the system.
    /// </summary>
    public static readonly Guid DefaultTenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");

    // ===== Actors (Development) =====
    public static readonly Guid AdminUserActorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000020");
    public static readonly Guid IslamuOrgActorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000021");
    public static readonly Guid RegularUserActorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000022");
    public static readonly Guid TechOrgActorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000023");
    public static readonly Guid ModeratorUserActorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000024");

    // ===== Users (Development) =====
    public static readonly Guid AdminUserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000030");
    public static readonly Guid RegularUserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000031");
    public static readonly Guid ModeratorUserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000032");
    public static readonly Guid AdminDevelopmentLoginId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000033");
    public static readonly Guid RegularDevelopmentLoginId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000034");
    public static readonly Guid ModeratorDevelopmentLoginId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000035");

    // ===== Organizations (Development) =====
    public static readonly Guid IslamuOrgId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000040");
    public static readonly Guid TechOrgId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000042");

    // ===== Organization Members (Development) =====
    public static readonly Guid AdminIslamuCreatorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000041");
    public static readonly Guid RegularIslamuMemberId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000043");
    public static readonly Guid ModeratorIslamuModId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000044");
    public static readonly Guid AdminTechCoOwnerId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000045");
    public static readonly Guid RegularTechCreatorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000046");

    // ===== Organization Tenant Participations (Development) =====
    public static readonly Guid IslamuOrgTenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000047");
    public static readonly Guid TechOrgTenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000048");

    // ===== Tenant Users And Role Grants (Development) =====
    public static readonly Guid AdminTenantUserRoleGrantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000070");
    public static readonly Guid RegularTenantUserRoleGrantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000071");
    public static readonly Guid ModeratorTenantUserRoleGrantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000072");
    public static readonly Guid AdminTenantUserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000073");
    public static readonly Guid RegularTenantUserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000074");
    public static readonly Guid ModeratorTenantUserId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000075");

    // ===== Storage Objects (Development) =====
    public static readonly Guid DefaultEventImageId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000050");
    public static readonly Guid DefaultProfileImageId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000051");
    public static readonly Guid DefaultOrganizationLogoId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000052");

    // ===== Events (Development) =====
    public static readonly Guid SampleEventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000060");
    public static readonly Guid QuranTafsirWomenEventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000061");
    public static readonly Guid BrothersFiqhEventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000062");
    public static readonly Guid FamilySeerahEventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000063");
    public static readonly Guid SegregatedRamadanEventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000064");
    public static readonly Guid OnlineHadithEventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000065");
    public static readonly Guid ArabicWorkshopEventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000066");
    public static readonly Guid YouthAqeedahEventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000067");
    public static readonly Guid CommunityIftarEventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000068");

    public static readonly Guid[] IslamicEventCatalogIds =
    [
        QuranTafsirWomenEventId,
        BrothersFiqhEventId,
        FamilySeerahEventId,
        SegregatedRamadanEventId,
        OnlineHadithEventId,
        ArabicWorkshopEventId,
        YouthAqeedahEventId,
        CommunityIftarEventId
    ];

    // ===== Categories (Development) =====
    public static readonly Guid IslamicStudiesCategoryId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000100");
    public static readonly Guid QuranCategoryId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000101");
    public static readonly Guid HadithCategoryId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000102");
    public static readonly Guid FiqhCategoryId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000103");
    public static readonly Guid AqeedahCategoryId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000104");
    public static readonly Guid SeerahCategoryId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000105");
    public static readonly Guid ArabicLanguageCategoryId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000106");
    public static readonly Guid CommunityEventsCategoryId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000107");

    // ===== Tags (Development) =====
    public static readonly Guid BeginnerTagId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000200");
    public static readonly Guid IntermediateTagId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000201");
    public static readonly Guid AdvancedTagId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000202");
    public static readonly Guid FreeTagId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000203");
    public static readonly Guid PaidTagId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000204");
    public static readonly Guid OnlineTagId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000205");
    public static readonly Guid InPersonTagId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000206");

    // ===== Locations (Development) =====
    public static readonly Guid OnlineLocationId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000300");
    public static readonly Guid BrusselsIslamicCenterLocationId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000301");
    public static readonly Guid AntwerpMasjidLocationId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000302");

    public static Guid RoomId(int roomNumber) => Guid.Parse($"018e4e5c-7f00-7000-8000-0000000003{roomNumber:00}");
    public static Guid EventDayId(int eventNumber, int dayNumber) => Guid.Parse($"018e4e5c-7f00-7000-8000-0000000007{eventNumber}{dayNumber}");
    public static Guid EventSessionId(int eventNumber, int sessionNumber) => Guid.Parse($"018e4e5c-7f00-7000-8000-0000000008{eventNumber}{sessionNumber}");
    public static Guid EventSessionGroupId(int eventNumber, int groupNumber) => Guid.Parse($"018e4e5c-7f00-7000-8000-0000000009{eventNumber}{groupNumber}");
    public static Guid EventAgendaItemId(int eventNumber, int itemNumber) => Guid.Parse($"018e4e5c-7f00-7000-8000-0000000010{eventNumber}{itemNumber}");
    public static Guid EventSessionAgendaItemId(int eventNumber, int sessionNumber, int itemNumber) => Guid.Parse($"018e4e5c-7f00-7000-8000-000000001{eventNumber}{sessionNumber}{itemNumber}");
    public static Guid EventCategoryId(int eventNumber, int categoryNumber) => Guid.Parse($"018e4e5c-7f00-7000-8000-0000000020{eventNumber}{categoryNumber}");
    public static Guid EventTagId(int eventNumber, int tagNumber) => Guid.Parse($"018e4e5c-7f00-7000-8000-0000000021{eventNumber}{tagNumber}");
    public static Guid EventSessionCategoryId(int eventNumber, int sessionNumber, int categoryNumber) => Guid.Parse($"018e4e5c-7f00-7000-8000-000000002{eventNumber}{sessionNumber}{categoryNumber}");
    public static Guid EventSessionTagId(int eventNumber, int sessionNumber, int tagNumber) => Guid.Parse($"018e4e5c-7f00-7000-8000-000000003{eventNumber}{sessionNumber}{tagNumber}");
    public static Guid EventSessionSpeakerId(int eventNumber, int sessionNumber, int speakerNumber) => Guid.Parse($"018e4e5c-7f00-7000-8000-000000004{eventNumber}{sessionNumber}{speakerNumber}");
    public static Guid EventSessionGroupSessionId(int eventNumber, int sessionNumber, int groupNumber) => Guid.Parse($"018e4e5c-7f00-7000-8000-000000005{eventNumber}{sessionNumber}{groupNumber}");

    // ===== Tenant Settings (Development) =====

    // ===== System Settings =====
    public static readonly Guid SystemSettingDeploymentModeId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000500");
    public static readonly Guid SystemSettingMaxSessionsPerEventId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000501");
    public static readonly Guid SystemSettingRequireApprovalId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000502");
    public static readonly Guid SystemSettingIslamicModuleId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000503");
    public static readonly Guid SystemSettingTechModuleId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000504");
    public static readonly Guid SystemSettingTenantSelfServiceRegistrationId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000505");
    public static readonly Guid SystemSettingTenantWhiteLabelingEnabledId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000518");
    public static readonly Guid SystemSettingUserSubmissionEnabledId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000506");
    public static readonly Guid SystemSettingOrganizationVerificationRequiredId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000507");
    public static readonly Guid SystemSettingOrganizationTenantCanOmitVerificationId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000508");
    public static readonly Guid SystemSettingBrandingDisplayNameId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000509");
    public static readonly Guid SystemSettingBrandingLogoUrlId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000510");
    public static readonly Guid SystemSettingBrandingFaviconUrlId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000511");
    public static readonly Guid SystemSettingBrandingCustomCssUrlId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000512");
    public static readonly Guid SystemSettingRoutingDefaultPublicHomePageId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000513");
    public static readonly Guid SystemSettingDomainsInstanceBaseDomainId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000514");
    public static readonly Guid SystemSettingDomainsAllowTenantCustomDomainId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000515");
    public static readonly Guid SystemSettingDomainsTenantSubdomainId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000516");
    public static readonly Guid SystemSettingDomainsTenantCustomDomainId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000517");

    // ===== System Settings — Email / SMTP =====
    public static readonly Guid SystemSettingEmailSmtpHostId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000520");
    public static readonly Guid SystemSettingEmailSmtpPortId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000521");
    public static readonly Guid SystemSettingEmailSmtpSecurityId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000524");
    public static readonly Guid SystemSettingEmailFromAddressId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000525");
    public static readonly Guid SystemSettingEmailFromNameId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000526");
    public static readonly Guid SystemSettingEmailSmtpTimeoutId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000527");
    public static readonly Guid SystemSettingEmailSmtpSkipCertValidationId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000528");

    // ===== System Settings — Object Storage / S3 =====
    public static readonly Guid SystemSettingStorageProviderId = Guid.Parse("018e4e5c-7f00-7000-8000-00000000052c");
    public static readonly Guid SystemSettingStorageDefaultMaxUploadBytesId = Guid.Parse("018e4e5c-7f00-7000-8000-00000000052d");
    public static readonly Guid SystemSettingStorageDefaultTenantQuotaBytesId = Guid.Parse("018e4e5c-7f00-7000-8000-00000000052e");
    public static readonly Guid SystemSettingStorageInstanceMaxUploadBytesId = Guid.Parse("018e4e5c-7f00-7000-8000-00000000052f");
    public static readonly Guid SystemSettingS3EndpointId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000530");
    public static readonly Guid SystemSettingS3PublicEndpointId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000531");
    public static readonly Guid SystemSettingS3BucketNameId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000532");
    public static readonly Guid SystemSettingS3RegionId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000535");
    public static readonly Guid SystemSettingS3ForcePathStyleId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000536");
    public static readonly Guid SystemSettingS3UploadUrlExpirationMinutesId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000537");

    // ===== System Settings — Analytics =====
    public static readonly Guid SystemSettingAnalyticsProviderId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000540");
    public static readonly Guid SystemSettingAnalyticsEnabledId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000541");
    public static readonly Guid SystemSettingAnalyticsApiKeyId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000542");
    public static readonly Guid SystemSettingAnalyticsEndpointUrlId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000543");

    // ===== System Settings — Entity Submission & Self-Registration =====
    public static readonly Guid SystemSettingOrgSubmissionEnabledId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000550");
    public static readonly Guid SystemSettingGroupSubmissionEnabledId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000551");
    public static readonly Guid SystemSettingOrgSelfRegistrationEnabledId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000552");
    public static readonly Guid SystemSettingGroupSelfRegistrationEnabledId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000553");
    public static readonly Guid SystemSettingEventReportingIntakeEnabledId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000554");

    // ===== System Settings — Localization / TMS =====
    public static readonly Guid SystemSettingLocalizationDefaultLanguageId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000560");
    public static readonly Guid SystemSettingLocalizationTmsProviderId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000561");
    public static readonly Guid SystemSettingLocalizationTmsApiUrlId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000562");
    public static readonly Guid SystemSettingLocalizationTmsProjectIdId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000563");
    public static readonly Guid SystemSettingLocalizationTmsComponentId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000564");
    public static readonly Guid SystemSettingLocalizationEnabledLanguagesId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000565");
    public static readonly Guid SystemSettingLocalizationFallbackLanguageId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000566");
    public static readonly Guid SystemSettingLocalizationClientPickerEnabledId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000567");
    public static readonly Guid SystemSettingLocalizationForceOfflineModeId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000568");

    public static readonly Guid SystemSettingSupportAccessEnabledId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000570");
    public static readonly Guid SystemSettingSupportAccessMaxReadOnlyMinutesId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000571");
    public static readonly Guid SystemSettingSupportAccessMaxWriteMinutesId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000572");
    public static readonly Guid SystemSettingSupportAccessAllowWriteModeId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000573");
    public static readonly Guid SystemSettingSupportAccessRequireTicketReferenceId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000574");
    public static readonly Guid SystemSettingSupportAccessOneActiveSessionPerActorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000575");
    public static readonly Guid SystemSettingAtprotoEventsEnabledId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000580");
    public static readonly Guid SystemSettingAtprotoEventValidationProfileId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000581");
    public static readonly Guid SystemSettingAtprotoEventsBackfillEnabledId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000582");
    public static readonly Guid SystemSettingAtprotoEventsBackfillModeId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000583");

    // ===== Module Definitions =====
    public static readonly Guid ModuleCoreId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000600");
    public static readonly Guid ModuleIslamicId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000601");
    public static readonly Guid ModuleTechId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000602");

    // ===== Tenant Capabilities (Development) =====
    public static readonly Guid DefaultTenantCoreCapabilityId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000610");
    public static readonly Guid DefaultTenantIslamicCapabilityId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000611");
}
