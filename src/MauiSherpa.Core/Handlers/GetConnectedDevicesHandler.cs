using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests;

namespace MauiSherpa.Core.Handlers;

/// <summary>
/// Returns the current device snapshot from the monitor service (no caching — service is the source of truth).
/// </summary>
[MediatorSingleton]
public class GetConnectedDevicesHandler : IRequestHandler<GetConnectedDevicesRequest, ConnectedDevicesSnapshot>
{
    private readonly IDeviceMonitorService _monitor;

    public GetConnectedDevicesHandler(IDeviceMonitorService monitor)
    {
        _monitor = monitor;
    }

    public Task<ConnectedDevicesSnapshot> Handle(
        GetConnectedDevicesRequest request,
        IMediatorContext context,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_monitor.Current);
    }
}
