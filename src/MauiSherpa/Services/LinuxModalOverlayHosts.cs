namespace MauiSherpa.Services;

public sealed class LinuxModalOverlayHosts
{
    private readonly object _gate = new();
    private readonly List<(object Host, SynchronizationContext? Context)> _hosts = [];

    public event Action? Changed;

    public void Register(object host)
    {
        lock (_gate)
            _hosts.Add((host, SynchronizationContext.Current));
    }

    public void Unregister(object host)
    {
        lock (_gate)
            _hosts.RemoveAll(entry => ReferenceEquals(entry.Host, host));
        Changed?.Invoke();
    }

    public object? CaptureOwner()
    {
        lock (_gate)
        {
            var context = SynchronizationContext.Current;
            if (context != null)
            {
                for (var i = _hosts.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(_hosts[i].Context, context))
                        return _hosts[i].Host;
                }
            }

            return _hosts.Count > 0 ? _hosts[^1].Host : null;
        }
    }

    public bool Owns(object host, object? owner, bool isMainHost)
    {
        lock (_gate)
        {
            if (owner == null)
                return isMainHost;

            if (_hosts.Any(entry => ReferenceEquals(entry.Host, owner)))
                return ReferenceEquals(host, owner);

            return _hosts.Count > 0 && ReferenceEquals(host, _hosts[^1].Host);
        }
    }
}
