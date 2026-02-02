using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.ViewModels;
using Moq;

namespace MauiSherpa.Core.Tests.ViewModels;

public class ViewModelBaseTests
{
    [Fact]
    public void SetProperty_ReturnsFalse_WhenValueUnchanged()
    {
        // Arrange
        var vm = new TestViewModel();
        vm.Name = "test";

        // Act - set to same value
        vm.Name = "test";

        // Assert - no change notification for same value
        vm.LastSetPropertyResult.Should().BeFalse();
    }

    [Fact]
    public void SetProperty_ReturnsTrue_WhenValueChanged()
    {
        // Arrange
        var vm = new TestViewModel();
        vm.Name = "test";

        // Act
        vm.Name = "new value";

        // Assert
        vm.LastSetPropertyResult.Should().BeTrue();
        vm.Name.Should().Be("new value");
    }

    [Fact]
    public void SetProperty_RaisesPropertyChanged_WhenValueChanged()
    {
        // Arrange
        var vm = new TestViewModel();
        var propertyChangedRaised = false;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.Name))
                propertyChangedRaised = true;
        };

        // Act
        vm.Name = "new value";

        // Assert
        propertyChangedRaised.Should().BeTrue();
    }

    [Fact]
    public void SetProperty_DoesNotRaisePropertyChanged_WhenValueUnchanged()
    {
        // Arrange
        var vm = new TestViewModel();
        vm.Name = "test";
        var propertyChangedCount = 0;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.Name))
                propertyChangedCount++;
        };

        // Act - set to same value again
        vm.Name = "test";

        // Assert - count should still be 0 (no additional notifications)
        propertyChangedCount.Should().Be(0);
    }

    private class TestViewModel : ViewModelBase
    {
        public TestViewModel() : base(new Mock<IAlertService>().Object, new Mock<ILoggingService>().Object)
        {
        }

        private string _name = string.Empty;
        public bool LastSetPropertyResult { get; private set; }

        public string Name
        {
            get => _name;
            set => LastSetPropertyResult = SetProperty(ref _name, value);
        }
    }
}
