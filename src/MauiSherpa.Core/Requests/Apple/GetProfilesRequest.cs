using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Apple;

/// <summary>
/// Request to get provisioning profiles for an Apple identity
/// </summary>
public record GetProfilesRequest(string IdentityId) : IRequest<IReadOnlyList<AppleProfile>>, IContractKey
{
    public string GetKey() => $"apple:profiles:{IdentityId}";
}
