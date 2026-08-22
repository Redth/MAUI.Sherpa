using FluentAssertions;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Services;

namespace MauiSherpa.Core.Tests.Services;

public class AppleIdentityStateServiceTests
{
    [Fact]
    public void SetSelectedIdentity_SameIdDifferentCredentials_UpdatesSelection()
    {
        var sut = new AppleIdentityStateService();
        var events = 0;
        sut.OnSelectionChanged += () => events++;

        var original = new AppleIdentity("id1", "Team", "KEY1", "ISS1", null, "p8-old");
        var updated = original with { KeyId = "KEY2", IssuerId = "ISS2", P8KeyContent = "p8-new" };

        sut.SetSelectedIdentity(original);
        sut.SetSelectedIdentity(updated);

        sut.SelectedIdentity.Should().Be(updated);
        events.Should().Be(2);
    }

    [Fact]
    public void SetSelectedIdentity_EquivalentIdentity_DoesNotRaiseEvent()
    {
        var sut = new AppleIdentityStateService();
        var events = 0;
        sut.OnSelectionChanged += () => events++;

        var identity = new AppleIdentity("id1", "Team", "KEY1", "ISS1", null, "p8");
        var equivalent = new AppleIdentity("id1", "Team", "KEY1", "ISS1", null, "p8");

        sut.SetSelectedIdentity(identity);
        sut.SetSelectedIdentity(equivalent);

        events.Should().Be(1);
    }

    [Fact]
    public void SetSelectedIdentity_PersistsLastNonNullSelection()
    {
        var store = new TestIdentitySelectionStore();
        var sut = new AppleIdentityStateService(store);
        var identity = new AppleIdentity("id1", "Team", "KEY1", "ISS1", null, "p8");

        sut.SetSelectedIdentity(identity);
        sut.SetSelectedIdentity(null);

        sut.LastSelectedIdentityId.Should().Be("id1");
        store.AppleIdentityId.Should().Be("id1");
    }

    [Fact]
    public void Constructor_RestoresLastSelectionId()
    {
        var store = new TestIdentitySelectionStore { AppleIdentityId = "id2" };

        var sut = new AppleIdentityStateService(store);

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
