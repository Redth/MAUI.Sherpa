using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests;

/// <summary>
/// Published when the set of connected devices, emulators, or simulators changes.
/// </summary>
public record ConnectedDevicesChangedEvent(ConnectedDevicesSnapshot Snapshot) : IEvent;
