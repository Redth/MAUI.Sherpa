using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Apple;

/// <summary>
/// Request to get bundle IDs for an Apple identity
/// </summary>
public record GetBundleIdsRequest(string IdentityId) : IRequest<IReadOnlyList<AppleBundleId>>, IContractKey
{
    public string GetKey() => $"apple:bundleids:{IdentityId}";
}
