using Shiny.Mediator;

namespace MauiSherpa.Core.Requests.Apple;

/// <summary>
/// Request to get recently used bundle ID identifiers for a specific Apple identity, sorted by most recent first
/// </summary>
public record GetRecentBundleIdsRequest(string IdentityId) : IRequest<IReadOnlyList<string>>, IContractKey
{
    public string GetKey() => $"apple:recent-bundleids:{IdentityId}";
}
