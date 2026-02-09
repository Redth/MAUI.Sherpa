using Shiny.Mediator;

namespace MauiSherpa.Core.Requests.Android;

public record GetKeystoresRequest : IRequest<IReadOnlyList<Interfaces.AndroidKeystore>>, IContractKey
{
    public string GetKey() => "android:keystores";
}
