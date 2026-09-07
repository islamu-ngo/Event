using System.Net;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ManagedProviderProvisioning;
using Explore.Application.DTOs.ManagedProviderProvisioning.Validators;
using Explore.Application.DTOs.Management;
using Explore.Application.Exceptions;
using Explore.Application.Features.ManagedProviderProvisioning;
using Explore.Application.Features.ManagedProviderProvisioning.Requests.Commands;
using Explore.Application.Features.Management;
using Explore.Application.Management;
using Explore.Application.Notifications;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Modules;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Documents;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Application.Features.ManagedProviderProvisioning.Handlers.Commands;

public class EnsureManagedProviderClientProvisionedCommandHandler(
    ITenantRepository tenantRepository,
    IUserRepository userRepository,
    IActorRepository actorRepository,
    IUserExternalLoginRepository userExternalLoginRepository,
    ITenantUserRepository tenantUserRepository,
    ITenantUserProfileRepository tenantUserProfileRepository,
    ITenantUserRoleGrantRepository tenantUserRoleGrantRepository,
    IRoleRepository roleRepository,
    IOrganizationRepository organizationRepository,
    IOrganizationTenantRepository organizationTenantRepository,
    IOrganizationMemberRepository organizationMemberRepository,
    IGroupRepository groupRepository,
    IGroupTenantRepository groupTenantRepository,
    IGroupMemberRepository groupMemberRepository,
    IExternalBindingRepository externalBindingRepository,
    ITenantOnboardingStateRepository tenantOnboardingStateRepository,
    IManagedTenantProvisioningOperationRepository managedTenantProvisioningOperationRepository,
    ITenantPlanRepository tenantPlanRepository,
    ITenantCapabilityRepository tenantCapabilityRepository,
    ITenantSettingRepository tenantSettingRepository,
    ITenantSettingsDocumentRepository tenantSettingsDocumentRepository,
    ITenantCreationService tenantCreationService,
    IRecipientNotificationMaterializer recipientNotificationMaterializer,
    IAuditLogRepository auditLogRepository,
    ITenantBrandingSettingsDocumentProvisioningService brandingProvisioningService,
    ITypedSettingsDocumentResolver typedSettingsDocumentResolver,
    IHierarchicalSettingsResolver settingsResolver,
    ManagedTenantProvisioningPreflight managedTenantProvisioningPreflight,
    TenantActivationCapacityPolicy tenantActivationCapacityPolicy,
    IOptions<ManagedControlPlaneOptions> managedControlPlaneOptions,
    ISettingMutationLock mutationLock,
    IUnitOfWork unitOfWork,
    ILogger<EnsureManagedProviderClientProvisionedCommandHandler> logger)
    : IRequestHandler<EnsureManagedProviderClientProvisionedCommand, BaseCommandResponse<ManagedProviderClientProvisioningResultDto>>,
        IManagedProviderClientProvisioner
{
    public async Task<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>> Handle(
        EnsureManagedProviderClientProvisionedCommand request,
        CancellationToken cancellationToken) =>
        await EnsureAsync(
            request.ProvisioningDto,
            request.ManagementRequest,
            request.OperationId,
            request.ExpectedOutboxMessageId,
            cancellationToken);

    public async Task<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>> EnsureAsync(
        ManagedProviderClientProvisioningDto provisioningDto,
        ManagementTenantProvisioningRequestDto? managementRequest,
        Guid? operationId,
        Guid? expectedOutboxMessageId,
        CancellationToken cancellationToken)
    {
        var dto = provisioningDto;

        if (dto.ActivateTenant && dto.DirectoryOperatorIdentity is null)
        {
            return BaseCommandResponse.Failure<ManagedProviderClientProvisioningResultDto>(
                "tenant_directory_operator_identity_incomplete",
                "Tenant directory operator identity is not ready.");
        }

        var validator = new ManagedProviderClientProvisioningDtoValidator();
        var validation = await validator.ValidateAsync(dto, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Validation<ManagedProviderClientProvisioningResultDto>(
                validation.Errors.Select(e => e.ErrorMessage),
                "Managed provider client provisioning failed due to validation errors.");
        }

        var normalizedProviderKey = dto.ProviderKey.Trim();
        var normalizedExternalSystem = dto.ExternalSystem.Trim();
        var normalizedExternalCustomerId = dto.ExternalCustomerId.Trim();
        var normalizedTenantSlug = dto.TenantSlug.Trim().ToLowerInvariant();
        var identityAuthority = dto.ExternalAdmin.IdentityProvider.Trim();
        var normalizedSubject = dto.ExternalAdmin.Subject;
        var normalizedIdentityProvider = ResolveIdentityProvider(identityAuthority, normalizedSubject);
        ProviderAccountKey accountKey = CreateManagedProviderAccountKey(
            normalizedIdentityProvider,
            identityAuthority,
            normalizedSubject);
        ManagedTenantProvisioningOperation? managedOperation = null;
        if (managementRequest is not null)
        {
            if (operationId is null
                || operationId == Guid.Empty
                || expectedOutboxMessageId is null
                || expectedOutboxMessageId == Guid.Empty)
            {
                return Failure(
                    "Managed tenant provisioning operation identity is missing.",
                    "Durable operation and outbox-generation identifiers are required.",
                    "tenant_provisioning_operation_missing");
            }

            managementRequest = ManagedTenantProvisioningRequestCodec.Normalize(managementRequest);
            managedOperation = await managedTenantProvisioningOperationRepository.GetByIdAsNoTrackingAsync(
                operationId.Value,
                cancellationToken);
            string expectedHash = ManagedTenantProvisioningRequestCodec.ComputeHash(managementRequest);
            if (managedOperation is null
                || !string.Equals(managedOperation.RequestHash, expectedHash, StringComparison.Ordinal)
                || !string.Equals(
                    managedOperation.ExternalCustomerReference,
                    managementRequest.ExternalCustomerReference,
                    StringComparison.Ordinal)
                || !string.Equals(managedOperation.TenantSlug, managementRequest.TenantSlug, StringComparison.Ordinal)
                || managedOperation.CurrentOutboxMessageId != expectedOutboxMessageId
                || managedOperation.Status != ManagedTenantProvisioningStatus.Processing)
            {
                return Failure(
                    "Managed tenant provisioning operation does not match its request snapshot.",
                    "The durable operation identity, customer reference, tenant slug, and request hash must match.",
                    "tenant_provisioning_operation_conflict");
            }
        }

        var existingCustomerBinding = await externalBindingRepository.GetByExternalKeyAsync(
            normalizedProviderKey,
            normalizedExternalSystem,
            ExternalBindingTypes.External.ProviderCustomer,
            normalizedExternalCustomerId,
            scopeTenantId: null,
            cancellationToken);

        if (existingCustomerBinding != null)
        {
            return await RehydrateExistingProvisioningResultAsync(
                existingCustomerBinding,
                normalizedProviderKey,
                normalizedExternalSystem,
                normalizedIdentityProvider,
                normalizedSubject,
                managedOperation,
                cancellationToken);
        }

        ManagedTenantProvisioningResolvedBootstrap? resolvedBootstrap = null;
        if (managementRequest is not null)
        {
            ManagedTenantProvisioningPreflightResult preflight =
                await managedTenantProvisioningPreflight.EvaluateAsync(
                    managementRequest,
                    requireProvisionablePlan: true,
                    cancellationToken);
            if (!preflight.Success)
            {
                return Failure(
                    "Managed tenant provisioning policy validation failed.",
                    preflight.Error!,
                    preflight.FailureCode);
            }

            resolvedBootstrap = preflight.Resolved!;
        }

        var existingTenant = await tenantRepository.GetTenantBySlug(normalizedTenantSlug);
        if (existingTenant != null)
        {
            return Failure("A tenant with this slug already exists.", "Tenant slug must be unique across managed provider provisioning requests.");
        }

        var existingLogin = await userExternalLoginRepository.GetByProviderAndKey(accountKey);
        User? existingUser = existingLogin == null ? null : await userRepository.GetById(existingLogin.UserId);
        if (existingLogin != null && existingUser == null)
        {
            return Failure("External admin identity is linked to a missing user.", "The external login points to a user that could not be found.");
        }


        var tenantId = Guid.CreateVersion7();
        var brandingDocumentId = Guid.CreateVersion7();
        var directoryOperatorIdentityDocumentId = Guid.CreateVersion7();
        DateTime tenantOccurredAt = DateTime.UtcNow;
        TenantSettingsDocument brandingSeed =
            TenantBrandingSettingsDocumentDefaults.Create(tenantId, dto.TenantFullName);
        TenantSettingsDocument identitySeed = dto.DirectoryOperatorIdentity is null
            ? TenantDirectoryOperatorIdentityDocumentDefaults.Create(tenantId)
            : TenantDirectoryOperatorIdentityDocumentDefaults.Create(
                tenantId,
                dto.DirectoryOperatorIdentity.ToPayload());
        var userId = existingUser?.Id ?? Guid.CreateVersion7();
        var tenantUserId = Guid.CreateVersion7();
        var tenantUserProfileId = Guid.CreateVersion7();
        var userActorId = Guid.CreateVersion7();
        var userExternalLoginId = existingLogin?.Id ?? Guid.CreateVersion7();
        var tenantUserRoleGrantId = Guid.CreateVersion7();
        var organizerId = dto.Organizer == null ? (Guid?)null : Guid.CreateVersion7();
        var organizerActorId = dto.Organizer == null ? (Guid?)null : Guid.CreateVersion7();
        var organizerParticipationId = dto.Organizer == null ? (Guid?)null : Guid.CreateVersion7();
        var organizerMembershipId = dto.Organizer == null ? (Guid?)null : Guid.CreateVersion7();
        var tenantAdministratorInvitationIntentId = managementRequest?.Administrator.Invitation is null
            ? (Guid?)null
            : Guid.CreateVersion7();
        var tenantAdministratorInvitationEmailId = managementRequest?.Administrator.Invitation is null
            ? (Guid?)null
            : Guid.CreateVersion7();

        async Task<ManagedProviderClientProvisioningResultDto> ProvisionAsync(CancellationToken ct)
        {
            TenantCreationOutcome creation =
                await tenantCreationService.CreateInCurrentTransactionAsync(
                    new TenantCreationRequest(
                        tenantId,
                        dto.TenantFullName,
                        normalizedTenantSlug,
                        dto.ActivateTenant
                            ? (int)TenantStatusEnum.Active
                            : (int)TenantStatusEnum.Provisioning,
                        ActorUserId: null,
                        tenantOccurredAt,
                        new TenantBrandingDocumentSeed(
                            brandingDocumentId,
                            brandingSeed.SchemaVersion,
                            brandingSeed.DefaultsVersion,
                            brandingSeed.PayloadJson),
                        new TenantDirectoryOperatorIdentityDocumentSeed(
                            directoryOperatorIdentityDocumentId,
                            identitySeed.SchemaVersion,
                            identitySeed.DefaultsVersion,
                            identitySeed.PayloadJson)),
                    ct);
            Tenant tenant = creation.Tenant;
            tenant.Description = $"Provisioned from {dto.ExternalSystem.Trim()} customer {dto.ExternalCustomerId.Trim()} by provider {dto.ProviderKey.Trim()}.";
            var user = await EnsureUserAsync(dto.ExternalAdmin, normalizedIdentityProvider, accountKey, existingUser, userId);
            var userActor = await EnsureUserActorAsync(dto.ExternalAdmin, user, userActorId);
            var tenantUser = await EnsureTenantUserAsync(tenant.Id, user.Id, userActor.Id, tenantUserId, user.Id);
            var tenantUserProfile = await EnsureTenantUserProfileAsync(dto.ExternalAdmin, tenant.Id, tenantUser.Id, tenantUserProfileId, user.Id);

            if (existingLogin == null)
            {
                await userExternalLoginRepository.Create(new UserExternalLogin
                {
                    Id = userExternalLoginId,
                    UserId = user.Id,
                    User = null!,
                    AuthenticationProviderId = (int)accountKey.ProviderKind,
                    AuthenticationProvider = null!,
                    ProviderKey = accountKey.Value,
                    ProviderDisplayName = dto.ExternalAdmin.IdentityProvider.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = user.Id
                });
            }

            var tenantUserRoleGrant = await EnsureTenantAdminRoleGrantAsync(tenant.Id, tenantUser.Id, user.Id, tenantUserRoleGrantId);
            var organizerResult = dto.Organizer == null
                ? (OrganizerId: (Guid?)null, ActorId: (Guid?)null, MembershipId: (Guid?)null)
                : await CreateOrganizerAsync(
                    dto.Organizer,
                    tenant.Id,
                    user.Id,
                    organizerId!.Value,
                    organizerActorId!.Value,
                    organizerParticipationId!.Value,
                    organizerMembershipId!.Value,
                    ct);

            await EnsureExternalBindingAsync(
                normalizedProviderKey,
                normalizedExternalSystem,
                ExternalBindingTypes.External.ProviderCustomer,
                normalizedExternalCustomerId,
                ExternalBindingTypes.Internal.Tenant,
                tenant.Id,
                scopeTenantId: null,
                createdBy: user.Id,
                ct);

            if (managedOperation is not null)
            {
                await EnsureExternalBindingAsync(
                    normalizedProviderKey,
                    normalizedExternalSystem,
                    ExternalBindingTypes.External.ManagedTenantProvisioningOperation,
                    managedOperation.Id.ToString("D"),
                    ExternalBindingTypes.Internal.Tenant,
                    tenant.Id,
                    tenant.Id,
                    user.Id,
                    ct);
            }

            await EnsureExternalBindingAsync(
                normalizedProviderKey,
                normalizedIdentityProvider,
                ExternalBindingTypes.External.ExternalAdminUser,
                normalizedSubject,
                ExternalBindingTypes.Internal.User,
                user.Id,
                tenant.Id,
                user.Id,
                ct);

            await EnsureExternalBindingAsync(
                normalizedProviderKey,
                normalizedIdentityProvider,
                ExternalBindingTypes.External.ExternalAdminTenantUser,
                normalizedSubject,
                ExternalBindingTypes.Internal.TenantUser,
                tenantUser.Id,
                tenant.Id,
                user.Id,
                ct);

            await EnsureExternalBindingAsync(
                normalizedProviderKey,
                normalizedIdentityProvider,
                ExternalBindingTypes.External.ExternalAdminTenantUserProfile,
                normalizedSubject,
                ExternalBindingTypes.Internal.TenantUserProfile,
                tenantUserProfile.Id,
                tenant.Id,
                user.Id,
                ct);

            await EnsureExternalBindingAsync(
                normalizedProviderKey,
                normalizedIdentityProvider,
                ExternalBindingTypes.External.ExternalAdminUserActor,
                normalizedSubject,
                ExternalBindingTypes.Internal.Actor,
                userActor.Id,
                tenant.Id,
                user.Id,
                ct);

            await EnsureExternalBindingAsync(
                normalizedProviderKey,
                normalizedIdentityProvider,
                ExternalBindingTypes.External.ExternalAdminUserLogin,
                normalizedSubject,
                ExternalBindingTypes.Internal.UserExternalLogin,
                existingLogin?.Id ?? userExternalLoginId,
                tenant.Id,
                user.Id,
                ct);

            if (dto.Organizer != null && organizerResult.OrganizerId.HasValue)
            {
                var organizerExternalType = dto.Organizer.Kind == ManagedProviderOrganizerKindDto.Group
                    ? ExternalBindingTypes.External.CustomerGroup
                    : ExternalBindingTypes.External.CustomerOrganization;
                var organizerActorExternalType = dto.Organizer.Kind == ManagedProviderOrganizerKindDto.Group
                    ? ExternalBindingTypes.External.CustomerGroupActor
                    : ExternalBindingTypes.External.CustomerOrganizationActor;
                var organizerInternalType = dto.Organizer.Kind == ManagedProviderOrganizerKindDto.Group
                    ? ExternalBindingTypes.Internal.Group
                    : ExternalBindingTypes.Internal.Organization;

                await EnsureExternalBindingAsync(
                    normalizedProviderKey,
                    normalizedExternalSystem,
                    organizerExternalType,
                    normalizedExternalCustomerId,
                    organizerInternalType,
                    organizerResult.OrganizerId.Value,
                    tenant.Id,
                    user.Id,
                    ct);

                if (organizerResult.ActorId.HasValue)
                {
                    await EnsureExternalBindingAsync(
                        normalizedProviderKey,
                        normalizedExternalSystem,
                        organizerActorExternalType,
                        normalizedExternalCustomerId,
                        ExternalBindingTypes.Internal.Actor,
                        organizerResult.ActorId.Value,
                        tenant.Id,
                        user.Id,
                        ct);
                }
            }

            Guid? tenantPlanAssignmentId = null;
            if (managementRequest is not null && resolvedBootstrap is not null)
            {
                tenantPlanAssignmentId = await ApplyManagedBootstrapAsync(
                    managementRequest,
                    resolvedBootstrap,
                    operationId!.Value,
                    managedOperation!.ManagedInstanceId,
                    tenantAdministratorInvitationIntentId,
                    tenantAdministratorInvitationEmailId,
                    ResolvePersistedInvitationEmail(managedOperation),
                    tenant,
                    user,
                    ct);
            }

            var provisioningResult = new ManagedProviderClientProvisioningResultDto
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                TenantUserId = tenantUser.Id,
                TenantUserProfileId = tenantUserProfile.Id,
                UserActorId = userActor.Id,
                UserExternalLoginId = existingLogin?.Id ?? userExternalLoginId,
                TenantUserRoleGrantId = tenantUserRoleGrant.Id,
                OrganizerId = organizerResult.OrganizerId,
                OrganizerActorId = organizerResult.ActorId,
                OrganizerKind = dto.Organizer?.Kind,
                OrganizerMembershipId = organizerResult.MembershipId,
                TenantPlanAssignmentId = tenantPlanAssignmentId
            };

            if (managedOperation is not null)
            {
                bool completed = await managedTenantProvisioningOperationRepository.TryCompleteAsync(
                    managedOperation.Id,
                    expectedOutboxMessageId!.Value,
                    tenant.Id,
                    user.Id,
                    DateTime.UtcNow,
                    ct);
                if (!completed)
                {
                    throw new ConcurrencyConflictException(
                        ConcurrencyConflictException.ConcurrentUpdate,
                        "Managed tenant provisioning generation changed before its side effects could commit.",
                        nameof(ManagedTenantProvisioningOperation),
                        managedOperation.Id.ToString("D"));
                }
            }

            return provisioningResult;
        }

        ManagedProviderClientProvisioningResultDto? result;
        try
        {
            if (managementRequest is null)
            {
                if (dto.ActivateTenant && tenantActivationCapacityPolicy.IsEnforced)
                {
                    (ManagedProviderClientProvisioningResultDto? Result,
                        BaseCommandResponse<ManagedProviderClientProvisioningResultDto>? Failure) ordinaryOutcome =
                        await mutationLock.ExecuteAsync<(
                            ManagedProviderClientProvisioningResultDto? Result,
                            BaseCommandResponse<ManagedProviderClientProvisioningResultDto>? Failure)>(
                            GovernanceSettingKeys.Deployment.Mode,
                            async ct =>
                            {
                                TenantActivationCapacityAssessment capacity =
                                    await tenantActivationCapacityPolicy.EvaluateAsync(
                                        requireMultiTenant: false,
                                        cancellationToken: ct);
                                return capacity.Allowed
                                    ? (await ProvisionAsync(ct), null)
                                    : (null, Failure(
                                        "Tenant activation capacity validation failed.",
                                        capacity.Error!,
                                        capacity.FailureCode));
                            },
                            cancellationToken);
                    if (ordinaryOutcome.Failure is not null)
                    {
                        return ordinaryOutcome.Failure;
                    }

                    result = ordinaryOutcome.Result;
                }
                else
                {
                    result = await unitOfWork.ExecuteInTransactionAsync(ProvisionAsync, cancellationToken);
                }
            }
            else
            {
                (ManagedProviderClientProvisioningResultDto? Result,
                    BaseCommandResponse<ManagedProviderClientProvisioningResultDto>? Failure) outcome =
                    await mutationLock.ExecuteManyAsync<(
                        ManagedProviderClientProvisioningResultDto? Result,
                        BaseCommandResponse<ManagedProviderClientProvisioningResultDto>? Failure)>(
                    BuildManagedMutationKeys(resolvedBootstrap!),
                    async ct =>
                    {
                        ManagedTenantProvisioningOperation? currentOperation =
                            await managedTenantProvisioningOperationRepository.GetByIdAsNoTrackingAsync(
                                operationId!.Value,
                                ct);
                        if (!IsCurrentGeneration(
                                currentOperation,
                                managedOperation!,
                                expectedOutboxMessageId!.Value))
                        {
                            return (null, Failure(
                                "Managed tenant provisioning generation is stale.",
                                "The operation was retried, cancelled, or completed before tenant mutation began.",
                                "tenant_provisioning_generation_stale"));
                        }

                        managedOperation = currentOperation;
                        TenantActivationCapacityAssessment capacity = await tenantActivationCapacityPolicy.EvaluateAsync(
                            requireMultiTenant: true,
                            excludedReservationOperationId: operationId,
                            cancellationToken: ct);
                        if (!capacity.Allowed)
                        {
                            return (null, Failure(
                                "Managed tenant provisioning capacity validation failed.",
                                capacity.Error!,
                                capacity.FailureCode));
                        }

                        ManagedTenantProvisioningPreflightResult recheck =
                            await managedTenantProvisioningPreflight.EvaluateAsync(
                                managementRequest,
                                requireProvisionablePlan: true,
                                ct);
                        if (!recheck.Success)
                        {
                            return (null, Failure(
                                "Managed tenant provisioning policy validation failed.",
                                recheck.Error!,
                                recheck.FailureCode));
                        }

                        resolvedBootstrap = recheck.Resolved!;
                        return (await ProvisionAsync(ct), null);
                    },
                    cancellationToken);

                if (outcome.Failure is not null)
                {
                    return outcome.Failure;
                }

                result = outcome.Result;
            }
        }
        catch (TenantDirectoryOperatorIdentityReadinessException exception)
        {
            return BaseCommandResponse.Failure<ManagedProviderClientProvisioningResultDto>(
                exception.FailureCode,
                exception.Message,
                exception.ReasonCodes);
        }

        if (result is null)
        {
            return Failure(
                "Managed tenant provisioning is unavailable in SingleTenant mode.",
                "The persisted deployment mode must remain MultiTenant until tenant creation commits.",
                "tenant_provisioning_requires_multi_tenant");
        }

        if (managementRequest is not null)
        {
            settingsResolver.InvalidateCache(SettingScope.Tenant, result.TenantId);
            typedSettingsDocumentResolver.InvalidateTenantDocumentCache(
                result.TenantId,
                SettingsDocumentKeys.Tenant.Branding);
        }

        return BaseCommandResponse.Success(
            result,
            "Managed provider client provisioned successfully.");
    }

    private async Task<User> EnsureUserAsync(
        ManagedProviderExternalAdminDto admin,
        string normalizedIdentityProvider,
        ProviderAccountKey accountKey,
        User? existingUser,
        Guid userId)
    {
        if (existingUser != null)
        {
            if (admin.EmailVerified && existingUser.EmailVerified != true)
            {
                existingUser.EmailVerified = true;
                existingUser.UpdatedAt = DateTime.UtcNow;
                await userRepository.Update(existingUser);
            }

            return existingUser;
        }

        return await userRepository.Create(new User
        {
            Id = userId,
            Pii = new UserPii
            {
                Email = admin.Email.Trim(),
                FirstName = admin.FirstName.Trim(),
                LastName = admin.LastName.Trim()
            },
            EmailVerified = admin.EmailVerified,
            CreatedAt = DateTime.UtcNow
        });
    }

    private async Task<Actor> EnsureUserActorAsync(ManagedProviderExternalAdminDto admin, User user, Guid userActorId)
    {
        var existingUserActor = await actorRepository.GetActorByUserId(user.Id);
        if (existingUserActor != null)
        {
            return existingUserActor;
        }

        var userActor = await actorRepository.Create(new Actor
        {
            Id = userActorId,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id,
            Pii = new ActorPii
            {
                DisplayName = ResolveDisplayName(admin)
            },
            CreatedAt = DateTime.UtcNow,
            CreatedBy = user.Id
        });

        return userActor;
    }

    private async Task<TenantUser> EnsureTenantUserAsync(Guid tenantId, Guid userId, Guid actorId, Guid tenantUserId, Guid createdBy)
    {
        var existing = await tenantUserRepository.GetByTenantAndUserAsync(tenantId, userId);
        if (existing != null)
        {
            return existing;
        }

        return await tenantUserRepository.Create(new TenantUser
        {
            Id = tenantUserId,
            TenantId = tenantId,
            Tenant = null!,
            UserId = userId,
            User = null!,
            ActorId = actorId,
            Actor = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        });
    }

    private async Task<TenantUserProfile> EnsureTenantUserProfileAsync(
        ManagedProviderExternalAdminDto admin,
        Guid tenantId,
        Guid tenantUserId,
        Guid tenantUserProfileId,
        Guid createdBy)
    {
        var existing = await tenantUserProfileRepository.GetByTenantUserAsync(tenantUserId);
        if (existing != null)
        {
            return existing;
        }

        return await tenantUserProfileRepository.Create(new TenantUserProfile
        {
            Id = tenantUserProfileId,
            TenantId = tenantId,
            Tenant = null!,
            TenantUserId = tenantUserId,
            TenantUser = null!,
            DisplayNameOverride = ResolveDisplayName(admin),
            ContactEmailOverride = admin.Email.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        });
    }

    private async Task<TenantUserRoleGrant> EnsureTenantAdminRoleGrantAsync(Guid tenantId, Guid tenantUserId, Guid userId, Guid tenantUserRoleGrantId)
    {
        var existing = await tenantUserRoleGrantRepository.GetByTenantAndUser(tenantId, userId);
        if (existing != null)
        {
            return existing;
        }

        var tenantAdminRole = await roleRepository.GetByMasterCodeAsync("tenant.admin")
            ?? await roleRepository.GetByIdAsync((int)RoleEnum.TenantAdmin);

        if (tenantAdminRole == null)
        {
            logger.LogWarning("tenant.admin role not found during managed provider provisioning.");
            tenantAdminRole = new Role
            {
                Id = (int)RoleEnum.TenantAdmin,
                MasterCode = "tenant.admin",
                FullName = "Tenant Administrator",
                Scope = RoleScopeEnum.Tenant
            };
        }

        return await tenantUserRoleGrantRepository.Create(new TenantUserRoleGrant
        {
            Id = tenantUserRoleGrantId,
            TenantId = tenantId,
            Tenant = null!,
            TenantUserId = tenantUserId,
            TenantUser = null!,
            RoleId = tenantAdminRole.Id,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = DateTime.UtcNow,
            GrantedBy = userId
        });
    }

    private async Task<(Guid? OrganizerId, Guid? ActorId, Guid? MembershipId)> CreateOrganizerAsync(
        ManagedProviderOrganizerDto organizer,
        Guid tenantId,
        Guid adminUserId,
        Guid organizerId,
        Guid organizerActorId,
        Guid organizerParticipationId,
        Guid organizerMembershipId,
        CancellationToken cancellationToken)
    {
        return organizer.Kind == ManagedProviderOrganizerKindDto.Group
            ? await CreateGroupOrganizerAsync(
                organizer,
                tenantId,
                adminUserId,
                organizerId,
                organizerActorId,
                organizerParticipationId,
                organizerMembershipId)
            : await CreateOrganizationOrganizerAsync(
                organizer,
                tenantId,
                adminUserId,
                organizerId,
                organizerActorId,
                organizerParticipationId,
                organizerMembershipId,
                cancellationToken);
    }

    private async Task<(Guid? OrganizerId, Guid? ActorId, Guid? MembershipId)> CreateOrganizationOrganizerAsync(
        ManagedProviderOrganizerDto organizer,
        Guid tenantId,
        Guid adminUserId,
        Guid organizationId,
        Guid actorId,
        Guid participationId,
        Guid membershipId,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.Create(new Organization
        {
            Id = organizationId,
            Pii = new OrganizationPii
            {
                FullName = organizer.FullName.Trim(),
                Email = organizer.Email?.Trim(),
                Country = organizer.Country?.Trim(),
                City = organizer.City?.Trim(),
                Address = organizer.Address?.Trim(),
                Postcode = organizer.Postcode?.Trim()
            },
            WebsiteUrl = organizer.WebsiteUrl?.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = adminUserId
        });

        var actor = await actorRepository.Create(new Actor
        {
            Id = actorId,
            ActorTypeId = (int)ActorTypeEnum.Organization,
            ActorType = null!,
            OrganizationId = organization.Id,
            Pii = new ActorPii
            {
                DisplayName = organization.FullName
            },
            CreatedAt = DateTime.UtcNow,
            CreatedBy = adminUserId
        });

        var participation = await organizationTenantRepository.Create(new OrganizationTenant
        {
            Id = participationId,
            TenantId = tenantId,
            Tenant = null!,
            OrganizationId = organization.Id,
            Organization = organization,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!,
            IsVisible = true,
            IsOrganizerEligible = true,
            ApprovedAt = DateTime.UtcNow,
            ApprovedBy = adminUserId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = adminUserId
        });

        var existingMembership = await organizationMemberRepository.GetByOrganizationAndUser(organization.Id, adminUserId);
        var membership = existingMembership ?? await organizationMemberRepository.Create(new OrganizationMember
        {
            Id = membershipId,
            OrganizationTenantId = participation.Id,
            OrganizationTenant = participation,
            UserId = adminUserId,
            User = null!,
            RoleId = (int)RoleEnum.OrgAdmin,
            Role = null!,
            TenantId = tenantId,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = adminUserId
        });

        return (organization.Id, actor.Id, membership.Id);
    }

    private async Task<(Guid? OrganizerId, Guid? ActorId, Guid? MembershipId)> CreateGroupOrganizerAsync(
        ManagedProviderOrganizerDto organizer,
        Guid tenantId,
        Guid adminUserId,
        Guid groupId,
        Guid actorId,
        Guid participationId,
        Guid membershipId)
    {
        var group = await groupRepository.Create(new Group
        {
            Id = groupId,
            FullName = organizer.FullName.Trim(),
            Description = organizer.Description?.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = adminUserId
        });

        var actor = await actorRepository.Create(new Actor
        {
            Id = actorId,
            ActorTypeId = (int)ActorTypeEnum.Group,
            ActorType = null!,
            GroupId = group.Id,
            Pii = new ActorPii
            {
                DisplayName = group.FullName
            },
            CreatedAt = DateTime.UtcNow,
            CreatedBy = adminUserId
        });

        var participation = await groupTenantRepository.Create(new GroupTenant
        {
            Id = participationId,
            TenantId = tenantId,
            Tenant = null!,
            GroupId = group.Id,
            Group = group,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!,
            IsVisible = true,
            IsOrganizerEligible = true,
            ApprovedAt = DateTime.UtcNow,
            ApprovedBy = adminUserId,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = adminUserId
        });

        var existingMembership = await groupMemberRepository.GetByGroupAndUser(group.Id, adminUserId);
        var membership = existingMembership ?? await groupMemberRepository.Create(new GroupMember
        {
            Id = membershipId,
            GroupTenantId = participation.Id,
            GroupTenant = participation,
            UserId = adminUserId,
            User = null!,
            RoleId = (int)RoleEnum.GroupAdmin,
            Role = null!,
            TenantId = tenantId,
            Tenant = null!,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = adminUserId
        });

        return (group.Id, actor.Id, membership.Id);
    }

    private async Task<Guid> ApplyManagedBootstrapAsync(
        ManagementTenantProvisioningRequestDto request,
        ManagedTenantProvisioningResolvedBootstrap bootstrap,
        Guid operationId,
        Guid managedInstanceId,
        Guid? tenantAdministratorInvitationIntentId,
        Guid? tenantAdministratorInvitationEmailId,
        string? tenantAdministratorInvitationEmail,
        Tenant tenant,
        User administrator,
        CancellationToken cancellationToken)
    {
        DateTime now = DateTime.UtcNow;
        await tenantOnboardingStateRepository.Create(new TenantOnboardingState
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = null!,
            IsCompleted = false,
            CurrentStep = 0,
            TotalSteps = 4,
            CompletedStepsJson = "[]",
            CreatedAt = now
        });

        var assignment = await tenantPlanRepository.CreateAssignmentAsync(
            new TenantPlanAssignment
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = null!,
                TenantPlanId = bootstrap.Plan.Id,
                TenantPlan = null!,
                TenantPlanVersionId = bootstrap.PlanVersion.Id,
                TenantPlanVersion = null!,
                TenantPlanAssignmentStatusId = (int)TenantPlanAssignmentStatusEnum.Active,
                TenantPlanAssignmentStatus = null!,
                AssignedByUserId = administrator.Id,
                AssignedAt = now,
                CreatedAt = now,
                CreatedBy = null
            },
            cancellationToken);

        await tenantSettingRepository.UpsertManyForTenantAsync(
            tenant.Id,
            bootstrap.Settings,
            administrator.Id,
            cancellationToken);

        foreach (ModuleDefinition module in bootstrap.Modules)
        {
            await tenantCapabilityRepository.Create(new TenantCapability
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                Tenant = null!,
                ModuleId = module.Id,
                Module = null!,
                IsEnabled = true,
                EnabledAt = now,
                EnabledBy = null,
                CreatedAt = now,
                CreatedBy = null
            }, cancellationToken);
        }

        var brandingDocument = await brandingProvisioningService.EnsureTenantBrandingDocumentAsync(
            tenant.Id,
            request.TenantName,
            cancellationToken);
        brandingDocument.UpdatePayload(
            brandingDocument.SchemaVersion,
            brandingDocument.DefaultsVersion,
            JsonSerializer.Serialize(bootstrap.Branding, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        brandingDocument.CreatedBy = null;
        brandingDocument.UpdatedAt = now;
        brandingDocument.UpdatedBy = null;
        await tenantSettingsDocumentRepository.Update(brandingDocument);

        if (request.Administrator.Invitation is not null)
        {
            Uri signInUrl = managedControlPlaneOptions.Value.TenantAdministratorSignInUrl
                ?? throw new InvalidOperationException("Tenant administrator sign-in URL is unavailable after preflight.");
            string encodedUrl = WebUtility.HtmlEncode(signInUrl.AbsoluteUri);
            Guid intentId = tenantAdministratorInvitationIntentId
                ?? throw new InvalidOperationException("Tenant administrator invitation intent identity is missing.");
            Guid emailId = tenantAdministratorInvitationEmailId
                ?? throw new InvalidOperationException("Tenant administrator invitation email identity is missing.");
            string recipientEmail = tenantAdministratorInvitationEmail
                ?? throw new InvalidOperationException("Persisted tenant administrator invitation authority is missing.");
            if (!string.Equals(
                    recipientEmail,
                    request.Administrator.Invitation.Email,
                    StringComparison.Ordinal)
                || !string.Equals(recipientEmail, administrator.Pii.Email, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Persisted tenant administrator invitation authority does not match the provisioned recipient.");
            }

            await recipientNotificationMaterializer.MaterializeInCurrentTransactionAsync(
                new RecipientNotificationMaterialization(
                    IntentId: intentId,
                    Intent: new NotificationIntentDraft(
                        Explore.Application.Notifications.NotificationCategory.ProductLifecycle,
                        TenantId: tenant.Id,
                        RecipientKind: nameof(NotificationRecipientKindEnum.TenantAdmin),
                        TemplateKey: "tenant-administrator-invitation",
                        SafePayloadReference: operationId.ToString("D"),
                        DeduplicationKey: $"managed-tenant-provisioning:{operationId:D}:tenant-administrator-invitation",
                        CorrelationId: operationId.ToString("D"),
                        UserId: administrator.Id),
                    DeliveryPolicy: NotificationDeliveryPolicyEnum.TenantAdministrationRequired,
                    DisclosureLevel: "generic",
                    InApp: null,
                    Email: new EmailDispatchOutbox
                    {
                        Id = emailId,
                        TenantId = tenant.Id,
                        Kind = EmailDispatchKind.TenantAdministratorInvitation,
                        SourceType = "managed_tenant_provisioning",
                        SourceId = operationId,
                        RecipientUserId = administrator.Id,
                        RecipientAddressSource = RecipientAddressSource.ManagedTenantAdministratorInvitation,
                        ManagedTenantProvisioningOperationId = operationId,
                        RecipientEmail = recipientEmail,
                        Subject = $"Administrator invitation for {tenant.FullName}",
                        PlainTextBody = $"Assalamu alaykum,\n\nYou have been invited to administer {tenant.FullName}. Sign in with this verified email address: {signInUrl.AbsoluteUri}\n\nEvent Platform",
                        HtmlBody = $"<p>Assalamu alaykum,</p><p>You have been invited to administer {WebUtility.HtmlEncode(tenant.FullName)}.</p><p><a href=\"{encodedUrl}\">Sign in to Event</a> with this verified email address.</p><p>Event Platform</p>",
                        CorrelationId = operationId.ToString("D"),
                        CreatedAt = now,
                        CreatedBy = null
                    },
                    IncludeEmailChannel: true,
                    EmailRequired: true,
                    LinkAllowed: true),
                cancellationToken);
        }

        await auditLogRepository.Create(new AuditLog
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = null!,
            EntityType = nameof(Tenant),
            EntityId = tenant.Id.ToString("D"),
            Action = "ManagedTenantProvisioned",
            NewValues = JsonSerializer.Serialize(new
            {
                managedInstanceId,
                operationId,
                planVersionId = bootstrap.PlanVersion.Id,
                modules = bootstrap.Modules.Select(module => module.ModuleKey).Order(StringComparer.Ordinal),
                invitationRequested = request.Administrator.Invitation is not null
            }),
            AffectedColumns = JsonSerializer.Serialize(new[]
            {
                "Tenant",
                "TenantAdministrator",
                "TenantPlanAssignment",
                "TenantCapabilities",
                "TenantSettings",
                "TenantOnboardingState"
            }),
            ActorId = null,
            Timestamp = now
        });

        return assignment.Id;
    }

    private async Task<BaseCommandResponse<ManagedProviderClientProvisioningResultDto>> RehydrateExistingProvisioningResultAsync(
        ExternalBinding existingCustomerBinding,
        string normalizedProviderKey,
        string normalizedExternalSystem,
        string normalizedIdentityProvider,
        string normalizedSubject,
        ManagedTenantProvisioningOperation? managedOperation,
        CancellationToken cancellationToken)
    {
        if (existingCustomerBinding.InternalType != ExternalBindingTypes.Internal.Tenant)
        {
            return Failure(
                "External provider customer binding is invalid.",
                "The provider customer binding does not point to an Event tenant.");
        }

        var tenant = await tenantRepository.GetById(existingCustomerBinding.InternalId);
        if (tenant == null)
        {
            return Failure(
                "External provider customer binding points to a missing tenant.",
                "The existing provider customer binding could not be rehydrated.");
        }

        if (managedOperation is not null
            && !string.Equals(tenant.Slug, managedOperation.TenantSlug, StringComparison.Ordinal))
        {
            return Failure(
                "Existing provider customer tenant does not match this managed provisioning operation.",
                "The customer reference is already bound to a tenant with a different slug.",
                "tenant_provisioning_customer_conflict");
        }

        if (managedOperation is not null)
        {
            ExternalBinding? operationBinding = await externalBindingRepository.GetByExternalKeyAsync(
                normalizedProviderKey,
                normalizedExternalSystem,
                ExternalBindingTypes.External.ManagedTenantProvisioningOperation,
                managedOperation.Id.ToString("D"),
                tenant.Id,
                cancellationToken);
            if (operationBinding is null
                || operationBinding.InternalType != ExternalBindingTypes.Internal.Tenant
                || operationBinding.InternalId != tenant.Id)
            {
                return Failure(
                    "Existing provider customer binding lacks managed operation provenance.",
                    "The tenant was not created by this durable managed provisioning operation.",
                    "tenant_provisioning_operation_provenance_missing");
            }
        }

        var userBinding = await externalBindingRepository.GetByExternalKeyAsync(
            normalizedProviderKey,
            normalizedIdentityProvider,
            ExternalBindingTypes.External.ExternalAdminUser,
            normalizedSubject,
            tenant.Id,
            cancellationToken);
        if (userBinding == null || userBinding.InternalType != ExternalBindingTypes.Internal.User)
        {
            return Failure(
                "External admin user binding is missing.",
                "The provider customer was already provisioned, but the external admin user binding is incomplete.");
        }

        var user = await userRepository.GetById(userBinding.InternalId);
        if (user == null)
        {
            return Failure(
                "External admin user binding points to a missing user.",
                "The existing external admin user binding could not be rehydrated.");
        }

        var actorBinding = await externalBindingRepository.GetByExternalKeyAsync(
            normalizedProviderKey,
            normalizedIdentityProvider,
            ExternalBindingTypes.External.ExternalAdminUserActor,
            normalizedSubject,
            tenant.Id,
            cancellationToken);
        var tenantUserBinding = await externalBindingRepository.GetByExternalKeyAsync(
            normalizedProviderKey,
            normalizedIdentityProvider,
            ExternalBindingTypes.External.ExternalAdminTenantUser,
            normalizedSubject,
            tenant.Id,
            cancellationToken);
        var tenantUserProfileBinding = await externalBindingRepository.GetByExternalKeyAsync(
            normalizedProviderKey,
            normalizedIdentityProvider,
            ExternalBindingTypes.External.ExternalAdminTenantUserProfile,
            normalizedSubject,
            tenant.Id,
            cancellationToken);
        var loginBinding = await externalBindingRepository.GetByExternalKeyAsync(
            normalizedProviderKey,
            normalizedIdentityProvider,
            ExternalBindingTypes.External.ExternalAdminUserLogin,
            normalizedSubject,
            tenant.Id,
            cancellationToken);
        var tenantUserRoleGrant = await tenantUserRoleGrantRepository.GetByTenantAndUser(tenant.Id, user.Id);

        if (actorBinding == null || tenantUserBinding == null || tenantUserProfileBinding == null || loginBinding == null || tenantUserRoleGrant == null)
        {
            return Failure(
                "Managed provider provisioning binding is incomplete.",
                "The existing provider customer binding is missing the tenant user state, user actor, external login, or tenant-admin role grant.");
        }

        return BaseCommandResponse.Success(
            new ManagedProviderClientProvisioningResultDto
            {
                TenantId = tenant.Id,
                UserId = user.Id,
                TenantUserId = tenantUserBinding.InternalId,
                TenantUserProfileId = tenantUserProfileBinding.InternalId,
                UserActorId = actorBinding.InternalId,
                UserExternalLoginId = loginBinding.InternalId,
                TenantUserRoleGrantId = tenantUserRoleGrant.Id
            },
            "Managed provider client already provisioned.");
    }

    private async Task<ExternalBinding> EnsureExternalBindingAsync(
        string providerKey,
        string externalSystem,
        string externalType,
        string externalId,
        string internalType,
        Guid internalId,
        Guid? scopeTenantId,
        Guid createdBy,
        CancellationToken cancellationToken)
    {
        var existing = await externalBindingRepository.GetByExternalKeyAsync(
            providerKey,
            externalSystem,
            externalType,
            externalId,
            scopeTenantId,
            cancellationToken);
        if (existing != null)
        {
            if (existing.InternalType != internalType || existing.InternalId != internalId)
            {
                throw new InvalidOperationException("External binding already points to a different internal record.");
            }

            existing.LastSeenAt = DateTime.UtcNow;
            await externalBindingRepository.Update(existing);
            return existing;
        }

        return await externalBindingRepository.Create(new ExternalBinding
        {
            Id = Guid.CreateVersion7(),
            ProviderKey = providerKey,
            ExternalSystem = externalSystem,
            ExternalType = externalType,
            ExternalId = externalId,
            InternalType = internalType,
            InternalId = internalId,
            ScopeTenantId = scopeTenantId,
            ExternalBindingStatusId = (int)ExternalBindingStatusEnum.Active,
            LastSeenAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        });
    }

    private static BaseCommandResponse<ManagedProviderClientProvisioningResultDto> Failure(
        string message,
        string error,
        string? failureCode = null) => failureCode is null
            ? BaseCommandResponse.Validation<ManagedProviderClientProvisioningResultDto>([error], message)
            : BaseCommandResponse.Failure<ManagedProviderClientProvisioningResultDto>(failureCode, message, [error]);

    private static string[] BuildManagedMutationKeys(
        ManagedTenantProvisioningResolvedBootstrap bootstrap)
    {
        IEnumerable<string> keys = bootstrap.Settings.Select(setting => setting.SettingKey)
            .Concat([
                GovernanceSettingKeys.Deployment.Mode,
                GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes,
                GovernanceSettingKeys.Tenants.WhiteLabelingEnabled,
                GovernanceSettingKeys.Branding.DisplayName,
                GovernanceSettingKeys.Branding.LogoUrl,
                GovernanceSettingKeys.Branding.FaviconUrl,
                GovernanceSettingKeys.Branding.CustomCssUrl,
                GovernanceSettingKeys.Domains.AllowTenantCustomDomain,
                ManagedTenantProvisioningPreflight.DomainNamespaceMutationKey
            ]);
        return keys.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool SupportsVerifiedEmailMatch(string identityProvider) =>
        identityProvider.Equals("keycloak", StringComparison.OrdinalIgnoreCase)
        || identityProvider.Equals("google", StringComparison.OrdinalIgnoreCase);

    private static bool IsCurrentGeneration(
        ManagedTenantProvisioningOperation? current,
        ManagedTenantProvisioningOperation expected,
        Guid expectedOutboxMessageId) =>
        current is not null
        && current.Id == expected.Id
        && current.CurrentOutboxMessageId == expectedOutboxMessageId
        && current.Status == ManagedTenantProvisioningStatus.Processing
        && string.Equals(current.RequestHash, expected.RequestHash, StringComparison.Ordinal)
        && string.Equals(
            current.ExternalCustomerReference,
            expected.ExternalCustomerReference,
            StringComparison.Ordinal)
        && string.Equals(current.TenantSlug, expected.TenantSlug, StringComparison.Ordinal);

    private static string? ResolvePersistedInvitationEmail(ManagedTenantProvisioningOperation operation)
    {
        if (string.IsNullOrWhiteSpace(operation.RequestJson))
        {
            return null;
        }

        return ManagedTenantProvisioningRequestCodec.Deserialize(operation.RequestJson)
            .Administrator.Invitation?.Email;
    }

    private static string ResolveIdentityProvider(string authority, string subject)
    {
        if (authority.Equals("atproto", StringComparison.OrdinalIgnoreCase))
        {
            _ = Explore.Domain.ValueObjects.AtprotoDid.Parse(subject);
            return "atproto";
        }

        if (authority.Equals("managed-invitation", StringComparison.OrdinalIgnoreCase))
        {
            return "managed-invitation";
        }

        if (!Uri.TryCreate(authority, UriKind.Absolute, out Uri? issuer)
            || (issuer.Scheme != Uri.UriSchemeHttps && issuer.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "Managed external identity requires an absolute OIDC issuer authority.");
        }

        return issuer.Host.Equals("accounts.google.com", StringComparison.OrdinalIgnoreCase)
            ? "google"
            : "keycloak";
    }

    private static ProviderAccountKey CreateManagedProviderAccountKey(
        string provider,
        string authority,
        string subject) =>
        provider == "atproto"
            ? PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(
                Explore.Domain.ValueObjects.AtprotoDid.Parse(subject))
            : PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
                provider == "managed-invitation"
                    ? "https://control-plane.invalid/managed-invitation"
                    : authority,
                subject);

    private static string ResolveDisplayName(ManagedProviderExternalAdminDto admin)
    {
        if (!string.IsNullOrWhiteSpace(admin.DisplayName)) return admin.DisplayName.Trim();

        var fullName = $"{admin.FirstName.Trim()} {admin.LastName.Trim()}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? admin.Email.Trim() : fullName;
    }

}
