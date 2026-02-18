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
}
