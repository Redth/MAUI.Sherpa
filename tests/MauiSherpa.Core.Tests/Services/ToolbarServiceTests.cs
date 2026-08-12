using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class ToolbarServiceTests
{
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
