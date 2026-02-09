using Shiny.Mediator;
using Shiny.Mediator.Caching;
using MauiSherpa.Core.Interfaces;
using MauiSherpa.Core.Requests.Android;

namespace MauiSherpa.Core.Handlers.Android;

public partial class GetKeystoresHandler : IRequestHandler<GetKeystoresRequest, IReadOnlyList<AndroidKeystore>>
{
    private readonly IKeystoreService _keystoreService;

    public GetKeystoresHandler(IKeystoreService keystoreService)
    {
        _keystoreService = keystoreService;
    }

    [Cache(AbsoluteExpirationSeconds = 300)]
    [OfflineAvailable]
    public async Task<IReadOnlyList<AndroidKeystore>> Handle(
        GetKeystoresRequest request,
        IMediatorContext context,
        CancellationToken ct)
    {
        return await _keystoreService.ListKeystoresAsync();
    }
}
