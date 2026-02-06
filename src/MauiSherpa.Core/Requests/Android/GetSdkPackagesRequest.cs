using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Android;

/// <summary>
/// Request to get installed Android SDK packages
/// </summary>
public record GetInstalledPackagesRequest : IRequest<IReadOnlyList<SdkPackageInfo>>, IContractKey
{
    public string GetKey() => "android:packages:installed";
}

/// <summary>
/// Request to get available Android SDK packages
/// </summary>
public record GetAvailablePackagesRequest : IRequest<IReadOnlyList<SdkPackageInfo>>, IContractKey
{
    public string GetKey() => "android:packages:available";
}
