using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class GoogleIdentityStateServiceTests
{
    [Fact]
    public void SetSelectedIdentity_PersistsLastNonNullSelection()
    {
        var store = new TestIdentitySelectionStore();
        var sut = new GoogleIdentityStateService(store);
        var identity = new GoogleIdentity("id1", "Project", "project-id", "test@example.com", null, "{}");

        sut.SetSelectedIdentity(identity);
        sut.SetSelectedIdentity(null);

        sut.LastSelectedIdentityId.Should().Be("id1");
        store.GoogleIdentityId.Should().Be("id1");
    }

    [Fact]
    public void Constructor_RestoresLastSelectionId()
    {
        var store = new TestIdentitySelectionStore { GoogleIdentityId = "id2" };

        var sut = new GoogleIdentityStateService(store);

        sut.LastSelectedIdentityId.Should().Be("id2");
        sut.SelectedIdentity.Should().BeNull();
    }

    private sealed class TestIdentitySelectionStore : IIdentitySelectionStore
    {
        public string? AppleIdentityId { get; set; }
        public string? GoogleIdentityId { get; set; }

        public string? GetLastAppleIdentityId() => AppleIdentityId;
        public void SetLastAppleIdentityId(string identityId) => AppleIdentityId = identityId;
        public string? GetLastGoogleIdentityId() => GoogleIdentityId;
        public void SetLastGoogleIdentityId(string identityId) => GoogleIdentityId = identityId;
    }
}
