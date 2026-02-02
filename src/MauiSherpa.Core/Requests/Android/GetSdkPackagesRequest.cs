using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Android;

/// <summary>
/// Request to get installed Android SDK packages
/// </summary>
public record GetInstalledPackagesRequest : IRequest<IReadOnlyList<SdkPackageInfo>>;

/// <summary>
/// Request to get available Android SDK packages
/// </summary>
public record GetAvailablePackagesRequest : IRequest<IReadOnlyList<SdkPackageInfo>>;
