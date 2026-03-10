using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Apple;

/// <summary>
/// Request to get installed Xcode versions from /Applications
/// </summary>
public record GetInstalledXcodesRequest() : IRequest<IReadOnlyList<XcodeInstallation>>, IContractKey
{
    public string GetKey() => "apple:xcode:installed";
}
