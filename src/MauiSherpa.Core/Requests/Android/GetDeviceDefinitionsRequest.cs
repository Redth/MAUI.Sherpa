using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Android;

/// <summary>
/// Request to get AVD device definitions (hardware profiles)
/// </summary>
public record GetDeviceDefinitionsRequest : IRequest<IReadOnlyList<AvdDeviceDefinition>>;

/// <summary>
/// Request to get available AVD skins
/// </summary>
public record GetAvdSkinsRequest : IRequest<IReadOnlyList<string>>;
