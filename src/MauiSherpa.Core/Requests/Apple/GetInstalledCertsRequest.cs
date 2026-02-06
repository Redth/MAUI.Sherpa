using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Apple;

/// <summary>
/// Request to get installed Apple root/intermediate certificates from keychain
/// </summary>
public record GetInstalledCertsRequest : IRequest<IReadOnlyList<InstalledCertInfo>>, IContractKey
{
    public string GetKey() => "local:applecerts";
}
