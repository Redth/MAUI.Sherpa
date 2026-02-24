using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests;

/// <summary>
/// Request to get the current snapshot of all connected devices.
/// </summary>
public record GetConnectedDevicesRequest : IRequest<ConnectedDevicesSnapshot>;
