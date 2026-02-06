using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Apple;

/// <summary>
/// Request to get registered devices for an Apple identity
/// </summary>
public record GetAppleDevicesRequest(string IdentityId) : IRequest<IReadOnlyList<AppleDevice>>, IContractKey
{
    public string GetKey() => $"apple:devices:{IdentityId}";
}
