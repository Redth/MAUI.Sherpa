using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.ViewModels;
using Moq;
using Shiny.Mediator;

namespace MauiSherpa.Core.Tests.ViewModels;

public class AndroidSdkViewModelTests
{
    private readonly Mock<IAndroidSdkService> _mockSdkService;
    private readonly Mock<IAndroidSdkSettingsService> _mockSdkSettings;
    private readonly Mock<IMediator> _mockMediator;
    private readonly Mock<IAlertService> _mockAlertService;
    private readonly Mock<ILoggingService> _mockLoggingService;
    private readonly AndroidSdkViewModel _viewModel;

    public AndroidSdkViewModelTests()
    {
        _mockSdkService = new Mock<IAndroidSdkService>();
        _mockSdkSettings = new Mock<IAndroidSdkSettingsService>();
        _mockMediator = new Mock<IMediator>();
        _mockAlertService = new Mock<IAlertService>();
        _mockLoggingService = new Mock<ILoggingService>();

        _viewModel = new AndroidSdkViewModel(
            _mockSdkService.Object,
            _mockSdkSettings.Object,
            _mockMediator.Object,
            _mockAlertService.Object,
            _mockLoggingService.Object);
    }

    [Fact]
    public void Title_ReturnsExpectedValue()
    {
        // Assert
        _viewModel.Title.Should().Be("Android SDK Management");
    }

    [Fact]
    public void InitialState_IsCorrect()
    {
        // Assert
        _viewModel.IsLoading.Should().BeFalse();
        _viewModel.StatusMessage.Should().Be("Ready");
        _viewModel.SdkPath.Should().BeNull();
        _viewModel.IsSdkInstalled.Should().BeFalse();
        _viewModel.InstalledPackages.Should().BeEmpty();
        _viewModel.AvailablePackages.Should().BeEmpty();
    }

    [Fact]
    public void SetProperty_RaisesPropertyChanged_ForStatusMessage()
    {
        // Arrange
        var propertyChangedRaised = false;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(_viewModel.StatusMessage))
                propertyChangedRaised = true;
        };

        // Act
        _viewModel.StatusMessage = "New Status";

        // Assert
        propertyChangedRaised.Should().BeTrue();
        _viewModel.StatusMessage.Should().Be("New Status");
    }

    [Fact]
    public void SetProperty_RaisesPropertyChanged_ForSdkPath()
    {
        // Arrange
        var propertyChangedRaised = false;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(_viewModel.SdkPath))
                propertyChangedRaised = true;
        };

        // Act
        _viewModel.SdkPath = "/new/sdk/path";

        // Assert
        propertyChangedRaised.Should().BeTrue();
        _viewModel.SdkPath.Should().Be("/new/sdk/path");
    }

    [Fact]
    public void SetProperty_RaisesPropertyChanged_ForIsLoading()
    {
        // Arrange
        var propertyChangedRaised = false;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(_viewModel.IsLoading))
                propertyChangedRaised = true;
        };

        // Act
        _viewModel.IsLoading = true;

        // Assert
        propertyChangedRaised.Should().BeTrue();
        _viewModel.IsLoading.Should().BeTrue();
    }

    [Fact]
    public void SetProperty_RaisesPropertyChanged_ForIsSdkInstalled()
    {
        // Arrange
        var propertyChangedRaised = false;
        _viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(_viewModel.IsSdkInstalled))
                propertyChangedRaised = true;
        };

        // Act
        _viewModel.IsSdkInstalled = true;

        // Assert
        propertyChangedRaised.Should().BeTrue();
        _viewModel.IsSdkInstalled.Should().BeTrue();
    }

    [Fact]
    public void InstalledPackages_CanBeSet()
    {
        // Arrange
        var packages = new List<SdkPackageInfo>
        {
            new("platforms;android-34", "Android SDK Platform 34", "34.0.0", "platforms", true)
        };

        // Act
        _viewModel.InstalledPackages = packages;

        // Assert
        _viewModel.InstalledPackages.Should().BeEquivalentTo(packages);
    }

    [Fact]
    public void AvailablePackages_CanBeSet()
    {
        // Arrange
        var packages = new List<SdkPackageInfo>
        {
            new("platforms;android-35", "Android SDK Platform 35", "35.0.0", "platforms", false)
        };

        // Act
        _viewModel.AvailablePackages = packages;

        // Assert
        _viewModel.AvailablePackages.Should().BeEquivalentTo(packages);
    }
}
