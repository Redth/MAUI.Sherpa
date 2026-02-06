using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Android;

/// <summary>
/// Request to get Android emulators (AVDs)
/// </summary>
public record GetEmulatorsRequest : IRequest<IReadOnlyList<AvdInfo>>, IContractKey
{
    public string GetKey() => "android:emulators";
}
