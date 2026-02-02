using FluentAssertions;
using MauiSherpa.Core.ViewModels;

namespace MauiSherpa.Core.Tests.ViewModels;

public class DashboardViewModelTests
{
    [Fact]
    public void Title_ReturnsExpectedValue()
    {
        // Arrange
        var viewModel = new DashboardViewModel();

        // Assert
        viewModel.Title.Should().Be("Dashboard");
    }

    [Fact]
    public void WelcomeMessage_ReturnsExpectedValue()
    {
        // Arrange
        var viewModel = new DashboardViewModel();

        // Assert
        viewModel.WelcomeMessage.Should().Be("Let .NET MAUI Sherpa guide your development environment needs!");
    }
}
