namespace Explore.Blazor.Client.Tests.Services;

public sealed class FooterAdminServiceTests
{
    private readonly IFooterClient _apiClient = Substitute.For<IFooterClient>();
    private readonly ILogger<FooterAdminService> _logger = Substitute.For<ILogger<FooterAdminService>>();

    [Test]
    public async Task GetLinkGroupsAsync_ReturnsGroups_WhenApiSucceeds()
    {
        ICollection<FooterLinkGroupListDto> groups =
        [
            new() { Id = Guid.NewGuid(), Title = "Main", IsActive = true }
        ];
        _apiClient.GetFooterLinkGroupsAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(groups);
        var service = CreateService();

        var result = await service.GetLinkGroupsAsync();

        await Assert.That(result.Count).IsEqualTo(1);
        await Assert.That(result[0].Title).IsEqualTo("Main");
        await _apiClient.Received(1).GetFooterLinkGroupsAsync(
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetTenantFooterSettingsAsync_ReturnsAuthoritativeHalResource()
    {
        var settings = new HalResourceOfTenantFooterSettingsDto
        {
            Enabled = true,
            Template = "compact",
            LockTenantTemplate = true,
            _links = new Dictionary<string, HalLink>
            {
                ["edit"] = new() { Href = "/api/footer/settings" }
            }
        };
        _apiClient.GetTenantFooterSettingsAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(settings);
        var service = CreateService();

        var result = await service.GetTenantFooterSettingsAsync();

        await Assert.That(result).IsSameReferenceAs(settings);
        await Assert.That(result!.Enabled).IsTrue();
        await Assert.That(result.Template).IsEqualTo("compact");
        await Assert.That(result.LockTenantTemplate).IsTrue();
        await Assert.That(result._links!.ContainsKey("edit")).IsTrue();
        await _apiClient.DidNotReceive().GetFooterConfigAsync(
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CreateLinkGroupAsync_ReturnsFailureResponse_WhenApiRejectsRequest()
    {
        _apiClient.CreateFooterLinkGroupAsync(
                Arg.Any<CreateFooterLinkGroupRequest>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<BaseCommandResponseOfGuid>>(_ => throw new ApiException(
                "Bad Request",
                400,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                null));
        var service = CreateService();
        var request = new CreateFooterLinkGroupRequest { Title = "Main" };

        var result = await service.CreateLinkGroupAsync(request);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Message).IsEqualTo("API error (400).");
        await Assert.That(result.Errors).Contains("API error (400).");
        await _apiClient.Received(1).CreateFooterLinkGroupAsync(
            request,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateLinkAsync_ForwardsIdAndRequestAndReturnsSuccess()
    {
        var expected = new BaseCommandResponseOfGuid
        {
            Success = true,
            Message = "updated",
            Id = Guid.NewGuid()
        };
        _apiClient.UpdateFooterLinkAsync(
                Arg.Any<Guid>(),
                Arg.Any<PatchFooterLinkDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = CreateService();
        var linkId = Guid.NewGuid();
        var request = new PatchFooterLinkDto
        {
            Label = new() { Value = "Docs" },
            Url = new() { Value = "/docs" },
            IsActive = new() { Value = true }
        };

        var result = await service.UpdateLinkAsync(linkId, request);

        await Assert.That(result).IsSameReferenceAs(expected);
        await Assert.That(result.Success).IsTrue();
        await _apiClient.Received(1).UpdateFooterLinkAsync(
            linkId,
            request,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task DeleteLinkAsync_ReturnsFalse_WhenApiThrows()
    {
        _apiClient.DeleteFooterLinkAsync(
                Arg.Any<Guid>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<bool>>(_ => throw new HttpRequestException("network failed"));
        var service = CreateService();

        var result = await service.DeleteLinkAsync(Guid.NewGuid());

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task PatchTenantFooterSettingsAsync_ForwardsGroupedRequestAndReturnsSuccess()
    {
        var expected = new BaseCommandResponseOfGuid
        {
            Success = true,
            Message = "updated",
            Id = Guid.NewGuid()
        };
        _apiClient.PatchTenantFooterSettingsAsync(
                Arg.Any<PatchTenantFooterSettingsDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var service = CreateService();
        var request = new PatchTenantFooterSettingsDto
        {
            General = new PatchTenantFooterGeneralDto
            {
                Enabled = new OptionalUpdateOfboolean { HasValue = true, Value = false },
                ShowCookieSettingsLink = new OptionalUpdateOfboolean { HasValue = true, Value = true }
            }
        };

        var result = await service.PatchTenantFooterSettingsAsync(request);

        await Assert.That(result).IsSameReferenceAs(expected);
        await Assert.That(result.Success).IsTrue();
        await Assert.That(request.Template).IsNull();
        await Assert.That(request.Description).IsNull();
        await Assert.That(request.SocialLinks).IsNull();
        await Assert.That(request.Copyright).IsNull();
        await _apiClient.Received(1).PatchTenantFooterSettingsAsync(
            request,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LinkCrudAndReorder_NeverInvokeSettingsPatch()
    {
        var success = new BaseCommandResponseOfGuid { Success = true };
        _apiClient.CreateFooterLinkGroupAsync(
                Arg.Any<CreateFooterLinkGroupRequest>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(success);
        _apiClient.UpdateFooterLinkGroupAsync(
                Arg.Any<Guid>(), Arg.Any<PatchFooterLinkGroupDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(success);
        _apiClient.DeleteFooterLinkGroupAsync(
                Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _apiClient.ReorderFooterLinkGroupsAsync(
                Arg.Any<IEnumerable<Guid>>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(success);
        _apiClient.CreateFooterLinkAsync(
                Arg.Any<Guid>(), Arg.Any<CreateFooterLinkRequest>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(success);
        _apiClient.UpdateFooterLinkAsync(
                Arg.Any<Guid>(), Arg.Any<PatchFooterLinkDto>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(success);
        _apiClient.DeleteFooterLinkAsync(
                Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var service = CreateService();
        var id = Guid.NewGuid();

        await service.CreateLinkGroupAsync(new CreateFooterLinkGroupRequest { Title = "Group" });
        await service.UpdateLinkGroupAsync(id, new PatchFooterLinkGroupDto { Title = new() { Value = "Updated" } });
        await service.DeleteLinkGroupAsync(id);
        await service.ReorderLinkGroupsAsync([id]);
        await service.CreateLinkAsync(id, new CreateFooterLinkRequest { Label = "Docs", Url = "/docs" });
        await service.UpdateLinkAsync(id, new PatchFooterLinkDto
        {
            Label = new() { Value = "Help" },
            Url = new() { Value = "/help" }
        });
        await service.DeleteLinkAsync(id);

        await _apiClient.DidNotReceive().PatchTenantFooterSettingsAsync(
            Arg.Any<PatchTenantFooterSettingsDto>(),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CancelledReadsCommandsAndDeletes_PropagateCancellation()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        _apiClient.GetTenantFooterSettingsAsync(null, null, source.Token)
            .Returns<Task<HalResourceOfTenantFooterSettingsDto>>(_ => throw new OperationCanceledException(source.Token));
        _apiClient.CreateFooterLinkGroupAsync(
                Arg.Any<CreateFooterLinkGroupRequest>(), null, null, source.Token)
            .Returns<Task<BaseCommandResponseOfGuid>>(_ => throw new OperationCanceledException(source.Token));
        _apiClient.DeleteFooterLinkAsync(Arg.Any<Guid>(), null, null, source.Token)
            .Returns<Task<bool>>(_ => throw new OperationCanceledException(source.Token));
        var service = CreateService();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.GetTenantFooterSettingsAsync(source.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.CreateLinkGroupAsync(new CreateFooterLinkGroupRequest { Title = "Group" }, source.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await service.DeleteLinkAsync(Guid.NewGuid(), source.Token));
    }

    private FooterAdminService CreateService() => new(_apiClient, _logger);
}
