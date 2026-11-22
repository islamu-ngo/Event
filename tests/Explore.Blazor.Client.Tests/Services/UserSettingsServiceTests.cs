using Microsoft.JSInterop;
using NSubstitute.Extensions;
using Wire = ISLAMU.Wire.Contracts.Announcements;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class UserSettingsServiceTests
{
    [Test]
    [Arguments("PublicExperiencePreferences", "public_experience_preferences")]
    [Arguments("AiAssistantPreferences", "ai_assistant_preferences")]
    [Arguments("UiShellPreferences", "ui_shell_preferences")]
    [Arguments("AtprotoFederation", "federation")]
    [Arguments("event-list", "event_list")]
    [Arguments("home-discovery", "home_discovery")]
    public async Task AnonymousCategoriesUseRegisteredPreferenceNamespaces(string category, string expected)
    {
        await Assert.That(UserSettingsService.CategoryToKeyPrefix(category)).IsEqualTo(expected);
    }

    [Test]
    public void UnknownAnonymousCategoryDoesNotInventAStorageNamespace()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UserSettingsService.CategoryToKeyPrefix("unregistered-category"));
    }

    [Test]
    public async Task AccountChangeCannotReturnThePreviousAccountsCachedPreferences()
    {
        var first = new SettingGroupResponseDto
        {
            Category = PreferenceCategory,
            Settings = [new EffectiveSettingDto
            {
                Key = DismissalKey, Value = "4"
            }]
        };
        var second = new SettingGroupResponseDto
        {
            Category = PreferenceCategory,
            Settings = [new EffectiveSettingDto
            {
                Key = DismissalKey, Value = "9"
            }]
        };
        var apiClient = Substitute.For<ISettingsClient>();
        apiClient.GetUserSettingsAsync(PreferenceCategory, Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(first, second);
        var authentication = new ChangingAuthenticationState();
        authentication.SetUser(Guid.CreateVersion7());
        await using var service = new UserSettingsService(apiClient, authentication,
            Substitute.For<IJSRuntime>(), Substitute.For<ILogger<UserSettingsService>>(),
            Substitute.For<IPublicExperienceService>());

        var original = await service.GetSettingsAsync(PreferenceCategory);
        authentication.SetUser(Guid.CreateVersion7());
        var changed = await service.GetSettingsAsync(PreferenceCategory);

        await Assert.That(original!.Settings.Single().Value).IsEqualTo("4");
        await Assert.That(changed!.Settings.Single().Value).IsEqualTo("9");
    }

    private sealed class ChangingAuthenticationState : AuthenticationStateProvider
    {
        private AuthenticationState _state = new(new ClaimsPrincipal(new ClaimsIdentity()));

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_state);

        internal void SetUser(Guid id)
        {
            _state = new(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, id.ToString("D"))], "TestAuth")));
            NotifyAuthenticationStateChanged(Task.FromResult(_state));
        }

        internal void SetAnonymous()
        {
            _state = new(new ClaimsPrincipal(new ClaimsIdentity()));
            NotifyAuthenticationStateChanged(Task.FromResult(_state));
        }
    }

    [Test]
    public async Task LogoutCannotReuseAnAuthenticatedPreferenceAsAnonymousHistory()
    {
        var apiClient = Substitute.For<ISettingsClient>();
        apiClient.GetUserSettingsAsync(PreferenceCategory, Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SettingGroupResponseDto
            {
                Category = PreferenceCategory,
                Settings = [new EffectiveSettingDto
                {
                    Key = DismissalKey, Value = "4"
                }]
            });
        var authentication = new ChangingAuthenticationState();
        authentication.SetUser(Guid.CreateVersion7());
        await using var service = new UserSettingsService(apiClient, authentication,
            Substitute.For<IJSRuntime>(), Substitute.For<ILogger<UserSettingsService>>(),
            Substitute.For<IPublicExperienceService>());
        await service.GetSettingsAsync(PreferenceCategory);

        authentication.SetAnonymous();
        var anonymous = await service.GetSettingsAsync(PreferenceCategory);

        await Assert.That(anonymous).IsNull();
    }

    [Test]
    public async Task AResponseFromThePreviousAccountCannotPopulateTheNewAccountsCache()
    {
        var response = new TaskCompletionSource<SettingGroupResponseDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var apiClient = Substitute.For<ISettingsClient>();
        apiClient.GetUserSettingsAsync(PreferenceCategory, Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requested.TrySetResult();
                return response.Task;
            });
        var authentication = new ChangingAuthenticationState();
        authentication.SetUser(Guid.CreateVersion7());
        await using var service = new UserSettingsService(apiClient, authentication,
            Substitute.For<IJSRuntime>(), Substitute.For<ILogger<UserSettingsService>>(),
            Substitute.For<IPublicExperienceService>());
        Task<SettingGroupResponseDto?> pending = service.GetSettingsAsync(PreferenceCategory);
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        authentication.SetUser(Guid.CreateVersion7());
        response.SetResult(new SettingGroupResponseDto
        {
            Category = PreferenceCategory,
            Settings = [new EffectiveSettingDto
            {
                Key = DismissalKey, Value = "4"
            }]
        });

        await Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(10))).IsNull();
    }

    [Test]
    public async Task GetSettingsAsync_UsesGeneratedClient_WhenAuthenticated()
    {
        var expected = new SettingGroupResponseDto
        {
            Category = "AiAssistantPreferences",
            Settings =
            [
                new EffectiveSettingDto
                {
                    Key = "ai_assistant_preferences.show_navbar_button",
                    Value = "true",
                    SettingValueTypeId = 1,
                    SettingValueTypeCode = "Boolean",
                    SettingValueTypeName = "Boolean",
                    Source = SettingSource.UserPreference,
                    IsLocked = false,
                    CanEdit = true
                }
            ]
        };
        var apiClient = Substitute.For<ISettingsClient>();
        apiClient.GetUserSettingsAsync(
                "AiAssistantPreferences",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var authState = Substitute.For<AuthenticationStateProvider>();
        authState.GetAuthenticationStateAsync().Returns(
            new AuthenticationState(new ClaimsPrincipal(
                new ClaimsIdentity(authenticationType: "TestAuth"))));
        await using var service = new UserSettingsService(
            apiClient,
            authState,
            Substitute.For<IJSRuntime>(),
            Substitute.For<ILogger<UserSettingsService>>(),
            Substitute.For<IPublicExperienceService>());

        var result = await service.GetSettingsAsync("AiAssistantPreferences");

        await Assert.That(result).IsSameReferenceAs(expected);
        var setting = result!.Settings.Single();
        await Assert.That(setting.Key).IsEqualTo("ai_assistant_preferences.show_navbar_button");
        await Assert.That(setting.Source).IsEqualTo(SettingSource.UserPreference);
    }

    [Test]
    public async Task AnonymousPreferenceRoundTripsOnlyWithinItsVerifiedTenant()
    {
        await using var browser = new AnonymousPreferences();
        var originalTenant = browser.TenantId;
        var otherTenantId = Guid.CreateVersion7();
        browser.Values[("", DismissalKey)] = "99";

        await Assert.That(await browser.Service.GetSettingsAsync(PreferenceCategory)).IsNull();
        var saved = await browser.Service.UpdateSettingsBatchAsync(PreferenceCategory,
            new Dictionary<string, string> { [DismissalKey] = "4" });
        var original = await browser.Service.GetSettingsAsync(PreferenceCategory);
        browser.ChangeTenant(otherTenantId);
        var other = await browser.Service.GetSettingsAsync(PreferenceCategory);
        browser.ChangeTenant(originalTenant);
        var restored = await browser.Service.GetSettingsAsync(PreferenceCategory);

        await Assert.That(saved!.Success).IsEqualTo(true);
        await Assert.That(saved.Results.Single().Applied).IsEqualTo(true);
        await Assert.That(original!.Settings.Single().Value).IsEqualTo("4");
        await Assert.That(other).IsNull();
        await Assert.That(restored!.Settings.Single().Value).IsEqualTo("4");
    }

    [Test]
    public async Task AnonymousResetClearsOnlyTheCanonicalCategoryAndCurrentTenant()
    {
        await using var browser = new AnonymousPreferences();
        var originalTenant = browser.TenantId;
        var otherTenantId = Guid.CreateVersion7();
        await browser.Service.UpdateSettingAsync(DismissalKey, "4");
        await browser.Service.UpdateSettingAsync("ai_assistant_preferences.show_navbar_button", "true");
        browser.ChangeTenant(otherTenantId);
        await browser.Service.UpdateSettingAsync(DismissalKey, "9");
        browser.ChangeTenant(originalTenant);

        bool reset = await browser.Service.ResetAllAsync(PreferenceCategory);
        var dismissed = await browser.Service.GetSettingsAsync(PreferenceCategory);
        var otherCategory = await browser.Service.GetSettingsAsync("AiAssistantPreferences");
        browser.ChangeTenant(otherTenantId);
        var otherTenant = await browser.Service.GetSettingsAsync(PreferenceCategory);

        await Assert.That(reset).IsTrue();
        await Assert.That(dismissed).IsNull();
        await Assert.That(otherCategory!.Settings.Single().Value).IsEqualTo("true");
        await Assert.That(otherTenant!.Settings.Single().Value).IsEqualTo("9");
    }

    [Test]
    public async Task AnonymousSingleResetRemovesOnlyTheSelectedKey()
    {
        await using var browser = new AnonymousPreferences();
        await browser.Service.UpdateSettingAsync("event_list.page_size", "24");
        await browser.Service.UpdateSettingAsync("event_list.browse_mode", "pagination");

        bool reset = await browser.Service.ResetSettingAsync("event_list.page_size");
        var remaining = await browser.Service.GetSettingsAsync("event-list");

        await Assert.That(reset).IsTrue();
        await Assert.That(remaining!.Settings.Single().Key).IsEqualTo("event_list.browse_mode");
    }

    [Test]
    public async Task MissingPublicTenantNeverReadsOrWritesUnscopedHistory()
    {
        await using var browser = new AnonymousPreferences();
        browser.Values[("", DismissalKey)] = "99";
        browser.ChangeTenant(Guid.Empty);

        await Assert.That(await browser.Service.GetSettingsAsync(PreferenceCategory)).IsNull();
        await Assert.That(await browser.Service.UpdateSettingAsync(DismissalKey, "4")).IsFalse();
        await Assert.That(await browser.Service.ResetSettingAsync(DismissalKey)).IsFalse();
        await Assert.That(await browser.Service.ResetAllAsync(PreferenceCategory)).IsFalse();
        await Assert.That(browser.Values[("", DismissalKey)]).IsEqualTo("99");
    }

    [Test]
    public async Task AnonymousBatchCannotWriteKeysOutsideItsRegisteredCategory()
    {
        await using var browser = new AnonymousPreferences();

        var result = await browser.Service.UpdateSettingsBatchAsync(PreferenceCategory,
            new Dictionary<string, string> { ["ai_assistant_preferences.show_navbar_button"] = "true" });

        await Assert.That(result).IsNull();
        await Assert.That(browser.Values.Count).IsEqualTo(0);
        await Assert.That(await browser.Service.ResetAllAsync("unregistered-category")).IsFalse();
    }

    [Test]
    public async Task BrowserWriteFailureDoesNotReportAnAppliedPreference()
    {
        await using var browser = new AnonymousPreferences();
        browser.RejectWrites = true;

        var result = await browser.Service.UpdateSettingsBatchAsync(PreferenceCategory,
            new Dictionary<string, string> { [DismissalKey] = "4" });

        await Assert.That(result!.Success).IsEqualTo(false);
        await Assert.That(result.Results.Single().Applied).IsEqualTo(false);
        await Assert.That(await browser.Service.UpdateSettingAsync(DismissalKey, "4")).IsFalse();
        await Assert.That(await browser.Service.GetSettingsAsync(PreferenceCategory)).IsNull();
    }

    [Test]
    public async Task BrowserStorageExceptionDoesNotReportDurableSuccess()
    {
        await using var browser = new AnonymousPreferences();
        browser.ThrowOnWrite = true;

        var result = await browser.Service.UpdateSettingsBatchAsync(PreferenceCategory,
            new Dictionary<string, string> { [DismissalKey] = "4" });

        await Assert.That(result).IsNull();
        await Assert.That(await browser.Service.UpdateSettingAsync(DismissalKey, "4")).IsFalse();
    }

    [Test]
    public async Task SigningInDoesNotMergeAnonymousPreferencesIntoAccountPreferences()
    {
        await using var browser = new AnonymousPreferences();
        await browser.Service.UpdateSettingAsync(DismissalKey, "4");
        var accountValues = new Dictionary<string, string>();
        browser.Api.GetUserSettingsAsync(PreferenceCategory, Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => new SettingGroupResponseDto
            {
                Category = PreferenceCategory,
                Settings = accountValues.Select(pair => new EffectiveSettingDto
                    { Key = pair.Key, Value = pair.Value }).ToList()
            });
        browser.Api.UpdateUserSettingsBatchAsync(PreferenceCategory, Arg.Any<UpdateSettingBatchDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var values = call.Arg<UpdateSettingBatchDto>().Values;
                foreach (var pair in values)
                    accountValues[pair.Key] = pair.Value;
                return new BatchUpdateResponseDto
                {
                    Success = true,
                    Results = values.Select(pair => new SettingUpdateResultDto
                        { Key = pair.Key, Applied = true }).ToList()
                };
            });
        browser.Api.UpdateUserSettingAsync(Arg.Any<string>(), Arg.Any<UpdateSettingValueDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                accountValues[call.ArgAt<string>(0)] = call.Arg<UpdateSettingValueDto>().Value;
                return new BaseCommandResponseOfGuid { Success = true };
            });
        browser.Authentication.SetUser(Guid.CreateVersion7());

        var account = await browser.Service.GetSettingsAsync(PreferenceCategory);
        browser.Authentication.SetAnonymous();
        var anonymous = await browser.Service.GetSettingsAsync(PreferenceCategory);

        await Assert.That(account!.Settings.Count).IsEqualTo(0);
        await Assert.That(anonymous!.Settings.Single().Value).IsEqualTo("4");
    }

    [Test]
    public async Task TenantChangeDiscardsAnInFlightBrowserRead()
    {
        await using var browser = new AnonymousPreferences();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<Dictionary<string, string>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.Module.Configure().InvokeAsync<Dictionary<string, string>?>("getAll", Arg.Any<CancellationToken>(),
                Arg.Any<object?[]?>())
            .Returns(_ =>
            {
                started.TrySetResult();
                return new ValueTask<Dictionary<string, string>?>(response.Task);
            });
        var pending = browser.Service.GetSettingsAsync(PreferenceCategory);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        browser.ChangeTenant(Guid.CreateVersion7());
        response.SetResult(new Dictionary<string, string> { [DismissalKey] = "4" });

        await Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(10))).IsNull();
    }

    [Test]
    public async Task AccountChangeDiscardsAnInFlightBatchSaveResult()
    {
        await using var browser = new AnonymousPreferences();
        browser.Authentication.SetUser(Guid.CreateVersion7());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<BatchUpdateResponseDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.Api.UpdateUserSettingsBatchAsync(PreferenceCategory, Arg.Any<UpdateSettingBatchDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.TrySetResult();
                return response.Task;
            });
        var pending = browser.Service.UpdateSettingsBatchAsync(PreferenceCategory,
            new Dictionary<string, string> { [DismissalKey] = "4" });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        browser.Authentication.SetUser(Guid.CreateVersion7());
        response.SetResult(new BatchUpdateResponseDto
        {
            Success = true, Results = [new SettingUpdateResultDto { Key = DismissalKey, Applied = true }]
        });

        await Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(10))).IsNull();
    }

    [Test]
    public async Task AuthenticatedSingleMutationHonorsTheServerFailureResult()
    {
        await using var browser = new AnonymousPreferences();
        browser.Authentication.SetUser(Guid.CreateVersion7());
        browser.Api.UpdateUserSettingAsync(DismissalKey, Arg.Any<UpdateSettingValueDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = false });
        browser.Api.ResetUserSettingAsync(DismissalKey, Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = false });

        await Assert.That(await browser.Service.UpdateSettingAsync(DismissalKey, "4")).IsFalse();
        await Assert.That(await browser.Service.ResetSettingAsync(DismissalKey)).IsFalse();
    }

    [Test]
    public async Task TenantChangeInvalidatesTheAuthenticatedPreferenceCache()
    {
        await using var browser = new AnonymousPreferences();
        browser.Authentication.SetUser(Guid.CreateVersion7());
        var value = "4";
        browser.Api.GetUserSettingsAsync(PreferenceCategory, Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => new SettingGroupResponseDto
            {
                Category = PreferenceCategory,
                Settings = [new EffectiveSettingDto { Key = DismissalKey, Value = value }]
            });
        await browser.Service.GetSettingsAsync(PreferenceCategory);

        value = "9";
        browser.ChangeTenant(Guid.CreateVersion7());
        var result = await browser.Service.GetSettingsAsync(PreferenceCategory);

        await Assert.That(result!.Settings.Single().Value).IsEqualTo("9");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ContextChangeStopsRemainingAnonymousBatchWrites(bool accountChange)
    {
        await using var browser = new AnonymousPreferences();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        const string firstKey = "event_list.page_size";
        const string secondKey = "event_list.browse_mode";
        browser.Module.Configure().InvokeAsync<bool>("set", Arg.Any<CancellationToken>(), Arg.Any<object?[]?>())
            .Returns(call =>
            {
                var args = call.ArgAt<object?[]>(2);
                var key = (string)args[1]!;
                browser.Values[((string)args[0]!, key)] = (string)args[2]!;
                if (key != firstKey)
                    return new ValueTask<bool>(true);
                started.TrySetResult();
                return new ValueTask<bool>(response.Task);
            });
        var pending = browser.Service.UpdateSettingsBatchAsync("event-list",
            new Dictionary<string, string> { [firstKey] = "24", [secondKey] = "pagination" });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        if (accountChange)
            browser.Authentication.SetUser(Guid.CreateVersion7());
        else
            browser.ChangeTenant(Guid.CreateVersion7());
        response.SetResult(true);

        await Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(10))).IsNull();
        await Assert.That(browser.Values.Keys.Any(key => key.Key == secondKey)).IsFalse();
        await Assert.That(browser.Values.Single().Value).IsEqualTo("24");
    }

    [Test]
    [Arguments("batch-update")]
    [Arguments("single-update")]
    [Arguments("single-reset")]
    [Arguments("category-reset")]
    public async Task MutationCompletionInvalidatesPreferencesReadWhileSaveWasPending(string operation)
    {
        await using var browser = new AnonymousPreferences();
        browser.Authentication.SetUser(Guid.CreateVersion7());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string currentValue = "4";
        browser.Api.GetUserSettingsAsync(PreferenceCategory, Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => new SettingGroupResponseDto
            {
                Category = PreferenceCategory,
                Settings = [new EffectiveSettingDto { Key = DismissalKey, Value = currentValue }]
            });
        async Task<BaseCommandResponseOfGuid> MutateSingle()
        {
            started.TrySetResult();
            await commit.Task;
            currentValue = "9";
            return new BaseCommandResponseOfGuid { Success = true };
        }
        browser.Api.UpdateUserSettingAsync(DismissalKey, Arg.Any<UpdateSettingValueDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => MutateSingle());
        browser.Api.ResetUserSettingAsync(DismissalKey, Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => MutateSingle());
        browser.Api.UpdateUserSettingsBatchAsync(PreferenceCategory, Arg.Any<UpdateSettingBatchDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                await MutateSingle();
                return new BatchUpdateResponseDto
                {
                    Success = true, Results = [new SettingUpdateResultDto { Key = DismissalKey, Applied = true }]
                };
            });
        async Task<bool> StartMutation() => operation switch
        {
            "batch-update" => (await browser.Service.UpdateSettingsBatchAsync(PreferenceCategory,
                new Dictionary<string, string> { [DismissalKey] = "9" }))?.Success == true,
            "single-update" => await browser.Service.UpdateSettingAsync(DismissalKey, "9"),
            "single-reset" => await browser.Service.ResetSettingAsync(DismissalKey),
            "category-reset" => await browser.Service.ResetAllAsync(PreferenceCategory),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        Task<bool> mutation = StartMutation();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var during = await browser.Service.GetSettingsAsync(PreferenceCategory);

        commit.SetResult();
        bool saved = await mutation.WaitAsync(TimeSpan.FromSeconds(10));
        var after = await browser.Service.GetSettingsAsync(PreferenceCategory);

        await Assert.That(during!.Settings.Single().Value).IsEqualTo("4");
        await Assert.That(saved).IsTrue();
        await Assert.That(after!.Settings.Single().Value).IsEqualTo("9");
    }

    [Test]
    public async Task MutationCompletionDiscardsAnOlderReadThatReturnsAfterCommit()
    {
        await using var browser = new AnonymousPreferences();
        browser.Authentication.SetUser(Guid.CreateVersion7());
        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldRead = new TaskCompletionSource<SettingGroupResponseDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.Api.UpdateUserSettingAsync(DismissalKey, Arg.Any<UpdateSettingValueDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                saving.TrySetResult();
                await commit.Task;
                return new BaseCommandResponseOfGuid { Success = true };
            });
        browser.Api.GetUserSettingsAsync(PreferenceCategory, Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                reading.TrySetResult();
                return oldRead.Task;
            }, _ => Task.FromResult(new SettingGroupResponseDto
            {
                Category = PreferenceCategory,
                Settings = [new EffectiveSettingDto { Key = DismissalKey, Value = "9" }]
            }));
        var mutation = browser.Service.UpdateSettingAsync(DismissalKey, "9");
        await saving.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var pendingRead = browser.Service.GetSettingsAsync(PreferenceCategory);
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(10));

        commit.SetResult();
        await Assert.That(await mutation.WaitAsync(TimeSpan.FromSeconds(10))).IsTrue();
        oldRead.SetResult(new SettingGroupResponseDto
        {
            Category = PreferenceCategory,
            Settings = [new EffectiveSettingDto { Key = DismissalKey, Value = "4" }]
        });

        await Assert.That(await pendingRead.WaitAsync(TimeSpan.FromSeconds(10))).IsNull();
        var current = await browser.Service.GetSettingsAsync(PreferenceCategory);
        await Assert.That(current!.Settings.Single().Value).IsEqualTo("9");
    }

    [Test]
    public async Task ProductionTenantRefreshInvalidatesAuthenticatedPreferenceCache()
    {
        var publicApi = Substitute.For<IPublicExperienceClient>();
        var current = new PublicExperienceSettingsDto
        {
            TenantId = Guid.CreateVersion7(),
            DirectoryOperator = new TenantDirectoryOperatorPublicDto
                { PublicName = "Directory", LegalName = "Directory legal name" }
        };
        Guid? originalTenant = current.TenantId;
        publicApi.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(current));
        var experience = new PublicExperienceService(publicApi, Substitute.For<ILogger<PublicExperienceService>>());
        await experience.GetSettingsAsync();
        var authentication = new ChangingAuthenticationState();
        authentication.SetUser(Guid.CreateVersion7());
        var api = Substitute.For<ISettingsClient>();
        api.GetUserSettingsAsync(PreferenceCategory, Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => new SettingGroupResponseDto
            {
                Category = PreferenceCategory,
                Settings = [new EffectiveSettingDto
                {
                    Key = DismissalKey, Value = current.TenantId == originalTenant ? "4" : "9"
                }]
            });
        await using var service = new UserSettingsService(api, authentication,
            Substitute.For<IJSRuntime>(), Substitute.For<ILogger<UserSettingsService>>(), experience);
        var original = await service.GetSettingsAsync(PreferenceCategory);

        current = current with { TenantId = Guid.CreateVersion7() };
        await experience.GetSettingsAsync();
        var refreshed = await service.GetSettingsAsync(PreferenceCategory);

        await Assert.That(original!.Settings.Single().Value).IsEqualTo("4");
        await Assert.That(refreshed!.Settings.Single().Value).IsEqualTo("9");
    }

    [Test]
    public async Task DisposalDuringImportDisposesTheLateModuleOnceAndRejectsFurtherWork()
    {
        await using var browser = new AnonymousPreferences();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var imported = new TaskCompletionSource<IJSObjectReference>(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.Runtime.Configure().InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>())
            .Returns(_ =>
            {
                started.TrySetResult();
                return new ValueTask<IJSObjectReference>(imported.Task);
            });
        int disposals = 0;
        bool storageUsed = false;
        browser.Module.Configure().DisposeAsync().Returns(_ =>
        {
            Interlocked.Increment(ref disposals);
            return ValueTask.CompletedTask;
        });
        browser.Module.Configure().InvokeAsync<Dictionary<string, string>?>("getAll", Arg.Any<CancellationToken>(),
                Arg.Any<object?[]?>())
            .Returns(_ =>
            {
                storageUsed = true;
                return new ValueTask<Dictionary<string, string>?>(new Dictionary<string, string>
                    { [DismissalKey] = "4" });
            });
        var read = browser.Service.GetSettingsAsync(PreferenceCategory);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Task disposal = browser.Service.DisposeAsync().AsTask();
        Task repeatedDisposal = browser.Service.DisposeAsync().AsTask();
        await Assert.That(disposal.IsCompleted).IsFalse();
        imported.SetResult(browser.Module);
        await Task.WhenAll(disposal, repeatedDisposal).WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(await read.WaitAsync(TimeSpan.FromSeconds(10))).IsNull();
        await Assert.That(storageUsed).IsFalse();
        await Assert.That(disposals).IsEqualTo(1);
        await Assert.That(await browser.Service.GetSettingsAsync(PreferenceCategory)).IsNull();
        await Assert.That(await browser.Service.UpdateSettingsBatchAsync(PreferenceCategory,
            new Dictionary<string, string> { [DismissalKey] = "4" })).IsNull();
        await Assert.That(await browser.Service.UpdateSettingsBatchAsync(PreferenceCategory,
            new Dictionary<string, string>())).IsNull();
        await Assert.That(await browser.Service.UpdateSettingAsync(DismissalKey, "4")).IsFalse();
        await Assert.That(await browser.Service.ResetSettingAsync(DismissalKey)).IsFalse();
        await Assert.That(await browser.Service.ResetAllAsync(PreferenceCategory)).IsFalse();
        await browser.Service.DisposeAsync();
        await Assert.That(disposals).IsEqualTo(1);
    }

    [Test]
    public async Task ConcurrentAnonymousReadsShareThePendingModuleImport()
    {
        await using var browser = new AnonymousPreferences();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var imported = new TaskCompletionSource<IJSObjectReference>(TaskCreationOptions.RunContinuationsAsynchronously);
        int imports = 0;
        browser.Runtime.Configure().InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref imports);
                started.TrySetResult();
                return new ValueTask<IJSObjectReference>(imported.Task);
            });
        browser.Values[(browser.TenantId.ToString("N"), DismissalKey)] = "4";
        var first = browser.Service.GetSettingsAsync(PreferenceCategory);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = browser.Service.GetSettingsAsync(PreferenceCategory);

        imported.SetResult(browser.Module);
        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(imports).IsEqualTo(1);
        await Assert.That(results.All(result => result?.Settings.Single().Value == "4")).IsTrue();
    }

    [Test]
    public async Task FailedPrerenderImportDoesNotPreventLaterInteractiveAcquisition()
    {
        await using var browser = new AnonymousPreferences();
        browser.Runtime.Configure().InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>())
            .Returns(new ValueTask<IJSObjectReference>(Task.FromException<IJSObjectReference>(
                    new InvalidOperationException("Interop unavailable during prerender"))),
                new ValueTask<IJSObjectReference>(browser.Module));
        browser.Values[(browser.TenantId.ToString("N"), DismissalKey)] = "4";

        var prerender = await browser.Service.GetSettingsAsync(PreferenceCategory);
        var interactive = await browser.Service.GetSettingsAsync(PreferenceCategory);

        await Assert.That(prerender).IsNull();
        await Assert.That(interactive!.Settings.Single().Value).IsEqualTo("4");
    }

    [Test]
    public async Task DisconnectedLateModuleDisposalCompletesWithoutReleasingTwice()
    {
        await using var browser = new AnonymousPreferences();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var imported = new TaskCompletionSource<IJSObjectReference>(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.Runtime.Configure().InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>())
            .Returns(_ =>
            {
                started.TrySetResult();
                return new ValueTask<IJSObjectReference>(imported.Task);
            });
        int disposals = 0;
        browser.Module.Configure().DisposeAsync().Returns(_ =>
        {
            Interlocked.Increment(ref disposals);
            return new ValueTask(Task.FromException(new JSDisconnectedException("Circuit disconnected")));
        });
        var read = browser.Service.GetSettingsAsync(PreferenceCategory);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var disposal = browser.Service.DisposeAsync().AsTask();

        imported.SetResult(browser.Module);
        await disposal.WaitAsync(TimeSpan.FromSeconds(10));
        await browser.Service.DisposeAsync();

        await Assert.That(await read.WaitAsync(TimeSpan.FromSeconds(10))).IsNull();
        await Assert.That(disposals).IsEqualTo(1);
    }

    [Test]
    public async Task AnonymousEditionMarkerUsesOnlyTheCurrentTenantAndIgnoresRetiredHistory()
    {
        await using var browser = new AnonymousPreferences();
        var tenant = browser.TenantId;
        const string retired = "public_experience_preferences.announcement_bar.dismissed_revision";
        browser.Values[(tenant.ToString("N"), retired)] = "7";
        var marker = AnnouncementTestData.AnonymousMarker(tenant);

        var absent = await browser.Service.GetAnonymousAnnouncementMarkerAsync(tenant);
        await Assert.That(absent.Success).IsTrue();
        await Assert.That(absent.Marker).IsNull();
        await Assert.That(await browser.Service.SaveAnonymousAnnouncementMarkerAsync(tenant, marker)).IsTrue();
        browser.ChangeTenant(AnnouncementTestData.OtherTenant);
        var otherTenant = await browser.Service.GetAnonymousAnnouncementMarkerAsync(browser.TenantId);
        await Assert.That(otherTenant.Success).IsTrue();
        await Assert.That(otherTenant.Marker).IsNull();
        browser.ChangeTenant(tenant);

        var retained = await browser.Service.GetAnonymousAnnouncementMarkerAsync(tenant);
        await Assert.That(retained.Success).IsTrue();
        await Assert.That(retained.Marker).IsEqualTo(marker);
        await Assert.That(await browser.Service.GetSettingsAsync("PublicExperiencePreferences")).IsNull();
        await Assert.That(browser.Values[(tenant.ToString("N"), retired)]).IsEqualTo("7");
    }

    [Test]
    public async Task AuthenticationDuringAnonymousImportCannotFallThroughToAGenericServerMutation()
    {
        await using var browser = new AnonymousPreferences();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var imported = new TaskCompletionSource<IJSObjectReference>(TaskCreationOptions.RunContinuationsAsynchronously);
        browser.Runtime.Configure().InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>())
            .Returns(_ => { started.TrySetResult(); return new ValueTask<IJSObjectReference>(imported.Task); });
        bool serverWrite = false;
        browser.Api.UpdateUserSettingAsync(Arg.Any<string>(), Arg.Any<UpdateSettingValueDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { serverWrite = true; return new BaseCommandResponseOfGuid { Success = true }; });

        var pending = browser.Service.SaveAnonymousAnnouncementMarkerAsync(
            browser.TenantId, AnnouncementTestData.AnonymousMarker(browser.TenantId));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        browser.Authentication.SetUser(AnnouncementTestData.OtherTenant);
        imported.SetResult(browser.Module);

        await Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(10))).IsFalse();
        await Assert.That(serverWrite).IsFalse();
        await Assert.That(browser.AnnouncementMarkers.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AnonymousEditionStorageFailureDoesNotClaimDurableSuccess(bool exception)
    {
        await using var browser = new AnonymousPreferences();
        browser.RejectWrites = !exception;
        browser.ThrowOnWrite = exception;

        var applied = await browser.Service.SaveAnonymousAnnouncementMarkerAsync(
            browser.TenantId, AnnouncementTestData.AnonymousMarker(browser.TenantId));

        await Assert.That(applied).IsFalse();
        await Assert.That(browser.AnnouncementMarkers.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GenericAndAnnouncementReadsShareImportAndDisposeItsLateModuleExactlyOnce()
    {
        await using var browser = new AnonymousPreferences();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var imported = new TaskCompletionSource<IJSObjectReference>(TaskCreationOptions.RunContinuationsAsynchronously);
        int imports = 0, disposals = 0;
        browser.Runtime.Configure().InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>())
            .Returns(_ =>
            {
                imports++;
                started.TrySetResult();
                return new ValueTask<IJSObjectReference>(imported.Task);
            });
        browser.Module.Configure().DisposeAsync().Returns(_ =>
        {
            disposals++;
            return ValueTask.CompletedTask;
        });

        var generic = browser.Service.GetSettingsAsync(PreferenceCategory);
        var announcement = browser.Service.GetAnonymousAnnouncementMarkerAsync(browser.TenantId);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var disposing = browser.Service.DisposeAsync().AsTask();
        imported.SetResult(browser.Module);
        await disposing.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(await generic.WaitAsync(TimeSpan.FromSeconds(10))).IsNull();
        var unavailable = await announcement.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.That(unavailable.Success).IsFalse();
        await Assert.That(unavailable.Marker).IsNull();
        await Assert.That(imports).IsEqualTo(1);
        await Assert.That(disposals).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AnnouncementKeysNeverUseGenericClientMutationPorts(bool authenticated)
    {
        await using var browser = new AnonymousPreferences();
        if (authenticated)
            browser.Authentication.SetUser(AnnouncementTestData.OtherTenant);
        bool serverWrite = false;
        browser.Api.UpdateUserSettingAsync(Arg.Any<string>(), Arg.Any<UpdateSettingValueDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { serverWrite = true; return new BaseCommandResponseOfGuid { Success = true }; });
        browser.Api.UpdateUserSettingsBatchAsync(Arg.Any<string>(), Arg.Any<UpdateSettingBatchDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { serverWrite = true; return new BatchUpdateResponseDto { Success = true }; });
        browser.Api.ResetUserSettingAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => { serverWrite = true; return new BaseCommandResponseOfGuid { Success = true }; });

        await Assert.That(await browser.Service.UpdateSettingAsync(
            "public_experience_preferences.announcement_bar.dismissed_revision", "7")).IsFalse();
        await Assert.That(await browser.Service.UpdateSettingsBatchAsync("PublicExperiencePreferences",
            new Dictionary<string, string> { ["public_experience_preferences.announcement_bar.dismissed_edition"] = "{}" }))
            .IsNull();
        await Assert.That(await browser.Service.ResetSettingAsync(
            "public_experience_preferences.announcement_bar.dismissed_edition")).IsFalse();
        await Assert.That(serverWrite).IsFalse();
        await Assert.That(browser.AnnouncementMarkers.Count).IsEqualTo(0);
        await Assert.That(browser.Values.Count).IsEqualTo(0);
    }

    private const string PreferenceCategory = "EventList";
    private const string DismissalKey = "event_list.page_size";

    private sealed class AnonymousPreferences : IAsyncDisposable
    {
        internal Guid TenantId { get; private set; } = Guid.CreateVersion7();
        internal bool RejectWrites { get; set; }
        internal bool ThrowOnWrite { get; set; }
        internal Dictionary<(string Tenant, string Key), string> Values { get; } = [];
        internal Dictionary<string, Wire.AnnouncementDismissalMarkerDto> AnnouncementMarkers { get; } = [];
        internal IPublicExperienceService Experience { get; } = Substitute.For<IPublicExperienceService>();
        internal ChangingAuthenticationState Authentication { get; } = new();
        internal ISettingsClient Api { get; } = Substitute.For<ISettingsClient>();
        internal IJSObjectReference Module { get; } = Substitute.For<IJSObjectReference>();
        internal IJSRuntime Runtime { get; } = Substitute.For<IJSRuntime>();
        internal UserSettingsService Service { get; }

        internal AnonymousPreferences()
        {
            Experience.GetCachedSettingsAsync().Returns(_ =>
                Task.FromResult<PublicExperienceSettingsDto?>(new() { TenantId = TenantId }));
            Runtime.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>())
                .Returns(new ValueTask<IJSObjectReference>(Module));
            Module.InvokeAsync<Wire.AnnouncementDismissalMarkerDto?>("getAnnouncement", Arg.Any<CancellationToken>(),
                    Arg.Any<object?[]?>())
                .Returns(call =>
                {
                    var args = call.ArgAt<object?[]>(2);
                    return new ValueTask<Wire.AnnouncementDismissalMarkerDto?>(
                        AnnouncementMarkers.GetValueOrDefault((string)args[0]!));
                });
            Module.InvokeAsync<bool>("setAnnouncement", Arg.Any<CancellationToken>(), Arg.Any<object?[]?>())
                .Returns(call =>
                {
                    if (ThrowOnWrite)
                        throw new JSException("Storage unavailable");
                    var args = call.ArgAt<object?[]>(2);
                    if (!RejectWrites)
                        AnnouncementMarkers[(string)args[0]!] = (Wire.AnnouncementDismissalMarkerDto)args[1]!;
                    return new ValueTask<bool>(!RejectWrites);
                });
            Module.InvokeAsync<Dictionary<string, string>?>("getAll", Arg.Any<CancellationToken>(),
                    Arg.Any<object?[]?>())
                .Returns(call =>
                {
                    var args = call.ArgAt<object?[]>(2);
                    var tenant = (string)args[0]!;
                    var prefix = (string)args[1]! + ".";
                    var values = Values.Where(pair => pair.Key.Tenant == tenant
                            && pair.Key.Key.StartsWith(prefix, StringComparison.Ordinal))
                        .ToDictionary(pair => pair.Key.Key, pair => pair.Value);
                    return new ValueTask<Dictionary<string, string>?>(values.Count == 0 ? null : values);
                });
            Module.InvokeAsync<bool>("set", Arg.Any<CancellationToken>(), Arg.Any<object?[]?>())
                .Returns(call =>
                {
                    if (ThrowOnWrite)
                        throw new JSException("Storage unavailable");
                    var args = call.ArgAt<object?[]>(2);
                    if (!RejectWrites)
                        Values[((string)args[0]!, (string)args[1]!)] = (string)args[2]!;
                    return new ValueTask<bool>(!RejectWrites);
                });
            Module.InvokeAsync<bool>("remove", Arg.Any<CancellationToken>(), Arg.Any<object?[]?>())
                .Returns(call =>
                {
                    var args = call.ArgAt<object?[]>(2);
                    Values.Remove(((string)args[0]!, (string)args[1]!));
                    return new ValueTask<bool>(true);
                });
            Module.InvokeAsync<int>("clearPrefix", Arg.Any<CancellationToken>(), Arg.Any<object?[]?>())
                .Returns(call =>
                {
                    var args = call.ArgAt<object?[]>(2);
                    var keys = Values.Keys.Where(key => key.Tenant == (string)args[0]!
                        && key.Key.StartsWith((string)args[1]! + ".", StringComparison.Ordinal)).ToArray();
                    foreach (var key in keys)
                        Values.Remove(key);
                    return new ValueTask<int>(keys.Length);
                });
            Service = new UserSettingsService(Api, Authentication, Runtime,
                Substitute.For<ILogger<UserSettingsService>>(), Experience);
        }

        internal void ChangeTenant(Guid tenantId)
        {
            TenantId = tenantId;
            Experience.SettingsChanged += Raise.Event<Action>();
        }

        public ValueTask DisposeAsync() => Service.DisposeAsync();
    }
}
