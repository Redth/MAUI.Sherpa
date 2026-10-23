using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class ToolbarServiceTests
{
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
