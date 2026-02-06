using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Android;

/// <summary>
/// Request to get connected Android devices
/// </summary>
public record GetAndroidDevicesRequest : IRequest<IReadOnlyList<DeviceInfo>>, IContractKey
{
    public string GetKey() => "android:devices";
}
