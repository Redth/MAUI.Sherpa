using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Android;

/// <summary>
/// Request to get AVD device definitions (hardware profiles)
/// </summary>
public record GetDeviceDefinitionsRequest : IRequest<IReadOnlyList<AvdDeviceDefinition>>, IContractKey
{
    public string GetKey() => "android:devicedefs";
}

/// <summary>
/// Request to get available AVD skins
/// </summary>
public record GetAvdSkinsRequest : IRequest<IReadOnlyList<string>>, IContractKey
{
    public string GetKey() => "android:skins";
}
