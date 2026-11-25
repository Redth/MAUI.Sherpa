using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class ToolbarServiceTests
{
    [Fact]
    public void MenuActions_RespectEnabledStateAndPageChanges()
    {
        var service = new ToolbarService();
        service.SetItems(new ToolbarAction("sdk", "Android SDK", "shippingbox", MenuActions: [
            new("sdk-switch", "Switch", "folder"), new("sdk-install", "Install", "plus")]));
        var clicked = new List<string>();
        service.ToolbarItemClicked += clicked.Add;
        service.SetItemEnabled("sdk-install", false);
        service.InvokeToolbarItemClicked("sdk-switch");
        service.InvokeToolbarItemClicked("sdk-install");
        service.ClearItems();
        service.InvokeToolbarItemClicked("sdk-switch");
        clicked.Should().Equal("sdk-switch");
    }

    [Fact]
    public void ScreenshotMode_BlocksNestedExternalActions()
    {
        var service = new ToolbarService(Screenshots.ScreenshotMode.Parse("website-v1", true));
        service.SetItems(new ToolbarAction("sdk", "SDK", "folder", MenuActions: [new("sdk-install", "Install", "plus")]));
        var invoked = false;
        service.ToolbarItemClicked += _ => invoked = true;
        service.InvokeToolbarItemClicked("sdk-install");
        invoked.Should().BeFalse();
        service.CurrentItems[0].MenuActions![0].ToolTip.Should().Contain("Screenshot Mode");
    }
    [Fact]
    public void ScreenshotActionsCannotBeReenabledByPageUpdates()
    {
        var service = new ToolbarService(Screenshots.ScreenshotMode.Parse("website-v1", true));
        service.SetItems(new ToolbarAction("create", "Create", "plus"), new ToolbarAction("refresh", "Refresh", "arrow.clockwise"));
        service.SetItemEnabled("create", true);
        var clicked = new List<string>();
        service.ToolbarItemClicked += clicked.Add;

        service.InvokeToolbarItemClicked("create");
        service.InvokeToolbarItemClicked("refresh");

        clicked.Should().Equal("refresh");
        service.IsItemEnabled("create").Should().BeFalse();
        service.CurrentItems[0].ToolTip.Should().Contain("Screenshot Mode");
    }

    [Fact]
    public void NotifyRouteChanged_UpdatesCurrentRouteBeforeNotifyingSubscribers()
    {
        var service = new ToolbarService();
        var observedRoutes = new List<(string EventRoute, string CurrentRoute)>();
        service.RouteChanged += route => observedRoutes.Add((route, service.CurrentRoute));

        service.NotifyRouteChanged("/devices");
        service.NotifyRouteChanged("/profiling/gcdump");

        observedRoutes.Should().Equal(
            ("/devices", "/devices"),
            ("/profiling/gcdump", "/profiling/gcdump"));
    }

    [Fact]
    public void InvokeToolbarItemClicked_OnlyDispatchesActiveEnabledActions()
    {
        var service = new ToolbarService();
        service.SetItems(
            new ToolbarAction("create", "Create Profile", "plus"),
            new ToolbarAction("refresh", "Refresh", "arrow.clockwise"));
        service.SetItemEnabled("refresh", false);
        var clicked = new List<string>();
        service.ToolbarItemClicked += clicked.Add;

        service.InvokeToolbarItemClicked("create");
        service.InvokeToolbarItemClicked("refresh");
        service.InvokeToolbarItemClicked("export");

        clicked.Should().Equal("create");
    }

    [Fact]
    public void InvokeToolbarItemClicked_AfterPageSwitch_RejectsPreviousPageActions()
    {
        var service = new ToolbarService();
        service.SetItems(new ToolbarAction("create", "Create Profile", "plus"));
        var clicked = new List<string>();
        service.ToolbarItemClicked += clicked.Add;

        service.SetItems(new ToolbarAction("export", "Export", "square.and.arrow.up"));
        service.InvokeToolbarItemClicked("create");
        service.InvokeToolbarItemClicked("export");
        service.ClearItems();
        service.InvokeToolbarItemClicked("export");

        clicked.Should().Equal("export");
    }

    [Fact]
    public void InvokeToolbarItemClicked_ReusedAction_UsesCurrentPageState()
    {
        var service = new ToolbarService();
        service.SetItems(new ToolbarAction("create", "Create Profile", "plus"));
        service.SetItemEnabled("create", false);
        var clicked = new List<string>();
        service.ToolbarItemClicked += clicked.Add;

        service.SetItems(new ToolbarAction("create", "New Secret", "plus"));
        service.InvokeToolbarItemClicked("create");
        service.SetItemEnabled("create", false);
        service.InvokeToolbarItemClicked("create");
        service.SetItemEnabled("create", true);
        service.InvokeToolbarItemClicked("create");

        clicked.Should().Equal("create", "create");
    }

    [Fact]
    public void SetItemEnabled_UnchangedState_DoesNotRequestAnotherToolbarUpdate()
    {
        var service = new ToolbarService();
        service.SetItems(new ToolbarAction("refresh", "Refresh", "arrow.clockwise"));
        var updates = 0;
        service.ToolbarChanged += () => updates++;

        service.SetItemEnabled("refresh", true);
        service.SetItemEnabled("refresh", false);
        service.SetItemEnabled("refresh", false);
        service.SetItemEnabled("refresh", true);
        service.SetItemEnabled("refresh", true);

        updates.Should().Be(2);
    }

    [Fact]
    public void SetItems_ClearsStateFromPreviousPage()
    {
        var service = new ToolbarService();
        service.SetItems(new ToolbarAction("refresh", "Refresh", "arrow.clockwise"));
        service.SetSearch("Search packages...");
        service.NotifySearchTextChanged("android");
        service.SetFilters(new ToolbarFilter("category", "Category", ["All", "Tools"]));
        service.SetItemEnabled("refresh", false);

        service.SetItems(new ToolbarAction("release-notes", "Release Notes", "mountain.2"));

        service.CurrentItems.Should().ContainSingle(item => item.Id == "release-notes");
        service.SearchPlaceholder.Should().BeNull();
        service.SearchText.Should().BeEmpty();
        service.CurrentFilters.Should().BeEmpty();
        service.IsItemEnabled("refresh").Should().BeTrue();
    }
}
