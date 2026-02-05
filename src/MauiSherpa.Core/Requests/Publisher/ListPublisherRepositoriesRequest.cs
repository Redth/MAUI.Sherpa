using Shiny.Mediator;
using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Requests.Publisher;

/// <summary>
/// Request to list available repositories from a secrets publisher
/// </summary>
public record ListPublisherRepositoriesRequest(string PublisherId) : IRequest<IReadOnlyList<PublisherRepository>>;
