using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Apple;

/// <summary>
/// Request to get certificates for an Apple identity
/// </summary>
public record GetCertificatesRequest(string IdentityId) : IRequest<IReadOnlyList<AppleCertificate>>, IContractKey
{
    public string GetKey() => $"apple:certs:{IdentityId}";
}
