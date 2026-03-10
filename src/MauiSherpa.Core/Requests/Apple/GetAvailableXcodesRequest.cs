using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Apple;

/// <summary>
/// Request to get available Xcode releases from xcodereleases.com
/// </summary>
public record GetAvailableXcodesRequest() : IRequest<IReadOnlyList<XcodeRelease>>, IContractKey
{
    public string GetKey() => "apple:xcode:available";
}
