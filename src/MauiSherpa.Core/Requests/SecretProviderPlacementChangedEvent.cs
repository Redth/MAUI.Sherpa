using MauiSherpa.Core.Interfaces;
using Shiny.Mediator;

namespace MauiSherpa.Core.Requests;

public sealed record SecretProviderPlacementChangedEvent(
    SecretItemRef Item,
    ProviderPlacementState Placement) : IEvent;
