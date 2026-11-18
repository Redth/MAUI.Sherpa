using MauiSherpa.Services;

namespace MauiSherpa.Core.Tests.Services;

public class LinuxModalOverlayHostsTests
{
    [Fact]
    public void UsesTheCallingRendererAndFallsBackWhenItsHostCloses()
    {
        var hosts = new LinuxModalOverlayHosts();
        var main = new object();
        var modal = new object();
        var mainContext = new SynchronizationContext();
        var modalContext = new SynchronizationContext();
        var previous = SynchronizationContext.Current;

        try
        {
            SynchronizationContext.SetSynchronizationContext(mainContext);
            hosts.Register(main);
            SynchronizationContext.SetSynchronizationContext(modalContext);
            hosts.Register(modal);

            Assert.Same(modal, hosts.CaptureOwner());
            SynchronizationContext.SetSynchronizationContext(mainContext);
            Assert.Same(main, hosts.CaptureOwner());
            Assert.False(hosts.Owns(main, modal, isMainHost: true));
            Assert.True(hosts.Owns(modal, modal, isMainHost: false));

            hosts.Unregister(modal);
            Assert.True(hosts.Owns(main, modal, isMainHost: true));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void UsesTheMostRecentHostForBackgroundCallers()
    {
        var hosts = new LinuxModalOverlayHosts();
        var main = new object();
        var modal = new object();
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            hosts.Register(main);
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            hosts.Register(modal);
            SynchronizationContext.SetSynchronizationContext(null);

            Assert.Same(modal, hosts.CaptureOwner());
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}
