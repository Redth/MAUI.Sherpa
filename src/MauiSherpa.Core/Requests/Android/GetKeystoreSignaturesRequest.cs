using Shiny.Mediator;

namespace MauiSherpa.Core.Requests.Android;

public record GetKeystoreSignaturesRequest(
    string KeystorePath,
    string Alias,
    string Password
) : IRequest<Interfaces.KeystoreSignatureInfo>, IContractKey
{
    public string GetKey() => $"android:keystoresig:{KeystorePath}:{Alias}";
}
