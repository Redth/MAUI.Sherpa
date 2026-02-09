using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;

namespace MauiSherpa.Core.Handlers.Android;

public partial class GetKeystoreSignaturesHandler : IRequestHandler<GetKeystoreSignaturesRequest, KeystoreSignatureInfo>
{
    private readonly IKeystoreService _keystoreService;

    public GetKeystoreSignaturesHandler(IKeystoreService keystoreService)
    {
        _keystoreService = keystoreService;
    }

    [Cache(AbsoluteExpirationSeconds = 3600)]
    public async Task<KeystoreSignatureInfo> Handle(
        GetKeystoreSignaturesRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _keystoreService.GetSignatureHashesAsync(request.KeystorePath, request.Alias, request.Password);
    }
}
